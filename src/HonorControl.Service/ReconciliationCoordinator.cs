using System.Diagnostics;
using Microsoft.Win32;
using HonorControl.Contracts;
using HonorControl.Models;
using HonorControl.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HonorControl.Service;

internal sealed class ReconciliationCoordinator : BackgroundService
{
    private readonly ConfigurationStore configuration;
    private readonly ILogger<ReconciliationCoordinator> logger;
    private readonly OemWmiClient wmi = new();
    private readonly SystemPowerService power = new();
    private readonly PowerSchemeService schemes = new();
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly SemaphoreSlim cycle = new(1, 1);
    private ActualState actual = new(ServiceError: "服务正在读取设备状态。");
    private long requestedRevision;
    private long completedRevision;
    private int chargeFailures;
    private int performanceFailures;

    public ReconciliationCoordinator(ConfigurationStore configuration, ILogger<ReconciliationCoordinator> logger)
    {
        this.configuration = configuration;
        this.logger = logger;
    }

    public ServiceSnapshot Snapshot() => new(configuration.Load(), Volatile.Read(ref actual), configuration.LoadTrayPolicy());

    public DesiredConfiguration Update(string command, DesiredConfiguration input)
    {
        DesiredConfiguration updated = configuration.Update(current => command switch
        {
            "SetCharge" => current with { ChargeStart = input.ChargeStart, ChargeEnd = input.ChargeEnd },
            "SetPerformance" => current with { PerformanceMode = input.PerformanceMode },
            "SetAutoReconcile" => current with { AutoReconcile = input.AutoReconcile },
            _ => throw new InvalidOperationException("未知的配置命令。")
        });
        Interlocked.Exchange(ref chargeFailures, 0);
        Interlocked.Exchange(ref performanceFailures, 0);
        Interlocked.Increment(ref requestedRevision);
        Signal();
        return updated;
    }

    /// <summary>
    /// 写入托盘策略。
    /// 托盘图标由独立进程注册（服务在会话 0 里做不到这件事），服务只负责按这里保存的策略
    /// 决定要不要拉起它——策略的唯一权威在服务端，面板写、托盘进程只读。
    /// </summary>
    public TrayPolicyMode UpdateTrayPolicy(TrayPolicyMode policy) => configuration.UpdateTrayPolicy(policy);

    private void Signal()
    {
        try { wake.Release(); }
        catch (SemaphoreFullException) { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task managerWatcher = WatchManagerAsync(stoppingToken);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Run(() => Reconcile(), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Reconciliation cycle failed");
                    Volatile.Write(ref actual, Volatile.Read(ref actual) with
                    {
                        ServiceError = exception.Message,
                        CheckedAt = DateTimeOffset.UtcNow
                    });
                }

                try { await wake.WaitAsync(TimeSpan.FromSeconds(20), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
        finally { await managerWatcher; }
    }

    private async Task WatchManagerAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool open = IsPcManagerOpen();
                ActualState current = Volatile.Read(ref actual);
                if (current.PcManagerOpen != open)
                {
                    ActualState updated;
                    do
                    {
                        current = Volatile.Read(ref actual);
                        updated = current with { PcManagerOpen = open };
                    } while (!ReferenceEquals(Interlocked.CompareExchange(ref actual, updated, current), current));
                    Signal();
                }
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Cannot inspect Honor PC Manager processes");
                ActualState current;
                ActualState updated;
                do
                {
                    current = Volatile.Read(ref actual);
                    updated = current with
                    {
                        PcManagerOpen = true,
                        ServiceError = "无法确认荣耀电脑管家进程，服务暂时只读。"
                    };
                } while (!ReferenceEquals(Interlocked.CompareExchange(ref actual, updated, current), current));
                try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    private void Reconcile()
    {
        cycle.Wait();
        try
        {
            long revision = Interlocked.Read(ref requestedRevision);
            DesiredConfiguration desired = configuration.Load();
            bool managerOpen = IsPcManagerOpen();
            bool supportedDevice = IsHonorComputer();
            SystemPowerSnapshot supply = power.GetSnapshot();
            ChargeThreshold? threshold = null;
            PerformanceStatus? performance = null;
            PowerSchemeStatus? powerSchemes = null;
            string? chargeError = null;
            string? performanceError = null;
            bool chargeAttempted = false;
            bool performanceAttempted = false;

            try { threshold = wmi.GetChargeThreshold(); }
            catch (Exception exception) { chargeError = exception.Message; }
            try { performance = wmi.GetPerformanceStatus(); }
            catch (Exception exception) { performanceError = exception.Message; }
            try { powerSchemes = schemes.GetStatus(); }
            catch (Exception exception) { performanceError = Join(performanceError, exception.Message); }

            bool shouldApply = desired.AutoReconcile || revision > Interlocked.Read(ref completedRevision);
            if (!supportedDevice)
            {
                chargeError = Join(chargeError, "此设备不是受支持的荣耀电脑，服务只读。");
            }
            else if (managerOpen)
            {
                if (shouldApply) logger.LogInformation("Honor PC Manager is open; hardware writes are suspended");
            }
            else if (shouldApply)
            {
                if (desired.ChargeStart is int start && desired.ChargeEnd is int end && threshold != null
                    && (threshold.Start != start || threshold.End != end))
                {
                    if (chargeFailures >= 3) chargeError = Join(chargeError, "重复校正失败，已暂停充电策略写入；请重新保存配置后重试。");
                    else
                    {
                        try
                        {
                            ThrowIfPcManagerOpen();
                            chargeAttempted = true;
                            ChargeThreshold verified = wmi.SetChargeThreshold(start, end);
                            if (verified.Start != start || verified.End != end)
                                throw new InvalidOperationException($"充电阈值回读不符：请求 {start}%–{end}%，实际 {verified.Start}%–{verified.End}%。");
                            threshold = verified;
                            chargeFailures = 0;
                        }
                        catch (Exception exception)
                        {
                            if (chargeAttempted) chargeFailures++;
                            chargeError = Join(chargeError, exception.Message);
                            logger.LogWarning(exception, "Charge reconciliation failed");
                        }
                    }
                }

                if (desired.PerformanceMode is int mode && performance != null && powerSchemes != null)
                {
                    PowerSchemeInfo? target = powerSchemes.GetTarget(mode);
                    if (performance.CurrentMode != mode || target != null && powerSchemes.Active.Id != target.Id)
                    {
                        if (performanceFailures >= 3) performanceError = Join(performanceError, "重复校正失败，已暂停性能模式写入；请重新保存配置后重试。");
                        else
                        {
                            try
                            {
                                ValidatePerformancePreconditions(supply, performance, target, mode);
                                ThrowIfPcManagerOpen();
                                performanceAttempted = true;
                                (performance, powerSchemes) = ApplyPerformance(performance, powerSchemes, mode);
                                performanceFailures = 0;
                            }
                            catch (Exception exception)
                            {
                                if (performanceAttempted) performanceFailures++;
                                performanceError = Join(performanceError, exception.Message);
                                logger.LogWarning(exception, "Performance reconciliation failed");
                            }
                        }
                    }
                }
            }

            if (chargeAttempted && chargeError != null)
            {
                try { threshold = wmi.GetChargeThreshold(); }
                catch (Exception exception) { chargeError = Join(chargeError, "失败后回读：" + exception.Message); }
            }
            if (performanceAttempted && performanceError != null)
            {
                try { performance = wmi.GetPerformanceStatus(); }
                catch (Exception exception) { performanceError = Join(performanceError, "失败后回读：" + exception.Message); }
                try { powerSchemes = schemes.GetStatus(); }
                catch (Exception exception) { performanceError = Join(performanceError, "电源方案回读：" + exception.Message); }
            }
            managerOpen = IsPcManagerOpen();
            if (!managerOpen && chargeError == null && performanceError == null)
                Interlocked.Exchange(ref completedRevision, revision);

            Volatile.Write(ref actual, new ActualState(
                threshold?.Start, threshold?.End, performance?.CurrentMode,
                powerSchemes?.Active.Name, supply.IsOnAcPower, supply.BatteryPercent,
                managerOpen, powerSchemes?.Balanced != null, powerSchemes?.HonorPerformance != null,
                performance?.SupportsHunterMode == true,
                chargeError, performanceError, null, DateTimeOffset.UtcNow));
        }
        finally { cycle.Release(); }
    }

    private (PerformanceStatus, PowerSchemeStatus) ApplyPerformance(
        PerformanceStatus previous, PowerSchemeStatus previousSchemes, int mode)
    {
        PowerSchemeInfo target = previousSchemes.GetTarget(mode)!;
        bool firmwareChanged = previous.CurrentMode != mode;
        bool schemeChanged = previousSchemes.Active.Id != target.Id;
        try
        {
            PerformanceStatus verified = firmwareChanged ? wmi.SetPerformanceMode(mode) : previous;
            ThrowIfPcManagerOpen();
            if (schemeChanged) schemes.SetActiveForMode(mode);
            PowerSchemeStatus latest = schemes.GetStatus();
            if (latest.Active.Id != target.Id)
                throw new InvalidOperationException("Windows 电源方案回读不符。");
            return (verified, latest);
        }
        catch
        {
            if (!IsPcManagerOpen())
            {
                if (firmwareChanged && previous.CurrentMode is 1 or 2)
                {
                    try { wmi.SetPerformanceMode(previous.CurrentMode); }
                    catch (Exception exception) { logger.LogError(exception, "Firmware rollback failed"); }
                }
                if (schemeChanged)
                {
                    try { schemes.SetActive(previousSchemes.Active.Id); }
                    catch (Exception exception) { logger.LogError(exception, "Power scheme rollback failed"); }
                }
            }
            throw;
        }
    }

    private static void ValidatePerformancePreconditions(SystemPowerSnapshot supply, PerformanceStatus status, PowerSchemeInfo? target, int mode)
    {
        if (status.CurrentMode is not (1 or 2)) throw new InvalidOperationException("固件性能模式无法识别。");
        if (supply.IsOnAcPower != true) throw new InvalidOperationException("未确认 AC 供电。");
        if (supply.BatteryPercent is not >= 20) throw new InvalidOperationException("电量不足 20% 或无法读取。");
        if (target == null) throw new InvalidOperationException("目标 Windows 电源方案不存在。");
        if (mode == 2 && !status.SupportsHunterMode) throw new InvalidOperationException("设备未报告高能模式能力。");
    }

    private static string Join(string? existing, string next) =>
        string.IsNullOrWhiteSpace(existing) ? next : existing + "；" + next;

    private static void ThrowIfPcManagerOpen()
    {
        if (IsPcManagerOpen()) throw new InvalidOperationException("荣耀电脑管家已打开，已停止写入。");
    }

    private static bool IsPcManagerOpen()
    {
        // Background services such as PCManagerMainService are long-lived even when the UI is closed.
        foreach (string name in new[] { "PCManager", "PCManagerTray", "MBAMessageCenter" })
        {
            Process[] processes = Process.GetProcessesByName(name);
            try { if (processes.Length != 0) return true; }
            finally { foreach (Process process in processes) process.Dispose(); }
        }
        return false;
    }

    private static bool IsHonorComputer()
    {
        using RegistryKey? bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        string? manufacturer = bios?.GetValue("SystemManufacturer") as string;
        return manufacturer?.Contains("HONOR", StringComparison.OrdinalIgnoreCase) == true;
    }
}
