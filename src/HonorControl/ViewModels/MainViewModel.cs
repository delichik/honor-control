using System.Collections.ObjectModel;
using System.Text;
using Microsoft.UI.Xaml.Controls;
using HonorControl.Models;
using HonorControl.Services;

namespace HonorControl.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly OemWmiClient client = new();
    private readonly SystemPowerService systemPower = new();
    private readonly PowerSchemeService powerSchemes = new();
    private string chargeStatus = "正在检查充电策略…";
    private string connectionTitle = "正在检查设备接口";
    private string deviceSummary = "请稍候。";
    private string powerSummary = "电源状态未知";
    private string chargeAvailabilityText = "检查中";
    private string performanceRequirementText = "正在检查性能模式前置条件。";
    private string currentPerformanceModeText = "当前模式未知";
    private string currentPowerPlanText = "Windows 电源方案未知";
    private string pendingChangeTitle = "尚未选择性能模式";
    private string pendingChangeDetail = "完成设备检查后可选择并应用模式。";
    private string operationTitle = "准备就绪";
    private string operationStatus = string.Empty;
    private string diagnosticDetails = "尚未获得诊断信息。";
    private string customChargeStart = "40";
    private string customChargeEnd = "70";
    private string customChargeValidation = "输入将在点击“应用自定义值”后校验。";
    private InfoBarSeverity connectionSeverity = InfoBarSeverity.Informational;
    private InfoBarSeverity chargeAvailabilitySeverity = InfoBarSeverity.Informational;
    private InfoBarSeverity performanceRequirementSeverity = InfoBarSeverity.Informational;
    private InfoBarSeverity operationSeverity = InfoBarSeverity.Informational;
    private InfoBarSeverity customChargeValidationSeverity = InfoBarSeverity.Informational;
    private bool isBusy;
    private bool chargeSupported;
    private bool performanceSupported;
    private bool? isOnAcPower;
    private int? batteryPercent;
    private int? currentPerformanceMode;
    private bool balancedPowerPlanAvailable;
    private bool honorPerformancePlanAvailable;
    private bool hasOperationStatus;
    private int selectedChargePreset;
    private int selectedPerformanceMode = 1;

    public ObservableCollection<ActivityRecord> ActivityRecords { get; } = new();

    public string ChargeStatus { get => chargeStatus; private set { chargeStatus = value; OnPropertyChanged(); } }
    public string ConnectionTitle { get => connectionTitle; private set { connectionTitle = value; OnPropertyChanged(); } }
    public string DeviceSummary { get => deviceSummary; private set { deviceSummary = value; OnPropertyChanged(); } }
    public string PowerSummary { get => powerSummary; private set { powerSummary = value; OnPropertyChanged(); } }
    public string ChargeAvailabilityText { get => chargeAvailabilityText; private set { chargeAvailabilityText = value; OnPropertyChanged(); } }
    public string PerformanceRequirementText { get => performanceRequirementText; private set { performanceRequirementText = value; OnPropertyChanged(); } }
    public string CurrentPerformanceModeText { get => currentPerformanceModeText; private set { currentPerformanceModeText = value; OnPropertyChanged(); } }
    public string CurrentPowerPlanText { get => currentPowerPlanText; private set { currentPowerPlanText = value; OnPropertyChanged(); } }
    public string PendingChangeTitle { get => pendingChangeTitle; private set { pendingChangeTitle = value; OnPropertyChanged(); } }
    public string PendingChangeDetail { get => pendingChangeDetail; private set { pendingChangeDetail = value; OnPropertyChanged(); } }
    public string OperationTitle { get => operationTitle; private set { operationTitle = value; OnPropertyChanged(); } }
    public string OperationStatus { get => operationStatus; private set { operationStatus = value; OnPropertyChanged(); } }
    public string DiagnosticDetails { get => diagnosticDetails; private set { diagnosticDetails = value; OnPropertyChanged(); } }
    public InfoBarSeverity ConnectionSeverity { get => connectionSeverity; private set { connectionSeverity = value; OnPropertyChanged(); } }
    public InfoBarSeverity ChargeAvailabilitySeverity { get => chargeAvailabilitySeverity; private set { chargeAvailabilitySeverity = value; OnPropertyChanged(); } }
    public InfoBarSeverity PerformanceRequirementSeverity { get => performanceRequirementSeverity; private set { performanceRequirementSeverity = value; OnPropertyChanged(); } }
    public InfoBarSeverity OperationSeverity { get => operationSeverity; private set { operationSeverity = value; OnPropertyChanged(); } }
    public InfoBarSeverity CustomChargeValidationSeverity { get => customChargeValidationSeverity; private set { customChargeValidationSeverity = value; OnPropertyChanged(); } }
    public bool HasOperationStatus { get => hasOperationStatus; private set { hasOperationStatus = value; OnPropertyChanged(); } }

    public string CustomChargeStart
    {
        get => customChargeStart;
        set { customChargeStart = value; ValidateCustomThreshold(); OnPropertyChanged(); }
    }

    public string CustomChargeEnd
    {
        get => customChargeEnd;
        set { customChargeEnd = value; ValidateCustomThreshold(); OnPropertyChanged(); }
    }

    public string CustomChargeValidation { get => customChargeValidation; private set { customChargeValidation = value; OnPropertyChanged(); } }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            isBusy = value;
            OnPropertyChanged();
            NotifyControlStateChanged();
        }
    }

    public bool IsNotBusy => !IsBusy;
    public bool CanConfigureCharge => chargeSupported && !IsBusy;
    public bool CanApplySelectedChargePreset => CanConfigureCharge && selectedChargePreset is 1 or 2;
    public bool CanApplySelectedChargeMode => CanConfigureCharge && (selectedChargePreset is 1 or 2 || selectedChargePreset == 0 && IsCustomThresholdValid());
    public bool CanConfigurePerformance => performanceSupported && currentPerformanceMode is 1 or 2 && isOnAcPower == true && batteryPercent.HasValue && batteryPercent.Value >= 20 && !IsBusy;
    public bool CanSelectHighPerformance => CanConfigurePerformance && honorPerformancePlanAvailable;
    public bool CanApplyPerformance => CanConfigurePerformance && (selectedPerformanceMode == 1 ? balancedPowerPlanAvailable : honorPerformancePlanAvailable);
    public int SelectedChargePresetIndex
    {
        get => selectedChargePreset - 1;
        set => SelectChargePreset(value + 1);
    }
    public string SelectedChargePresetLabel => selectedChargePreset == 1 ? "电池保护 · 40%–70%" : selectedChargePreset == 2 ? "完整续航 · 0%–100%" : "自定义充电阈值";
    public string SelectedChargePresetDetail => selectedChargePreset == 1 ? "适合长期接通电源使用，减少电池长期满电停留。" : selectedChargePreset == 2 ? "允许充至 100%，适合即将移动使用的场景。" : CustomChargeValidation;
    public string ChargeModeHint => selectedChargePreset == 0 ? CustomChargeValidation : "选择后应用到设备。";
    public string ChargeActionLabel => selectedChargePreset == 0 ? "应用自定义阈值" : "应用充电模式";
    public int SelectedPerformanceMode => selectedPerformanceMode;
    public string SelectedPerformanceModeLabel => selectedPerformanceMode == 2 ? "高能模式" : "智能模式";

    public async Task RefreshAsync()
    {
        ConnectionTitle = "正在检查设备接口";
        DeviceSummary = "正在连接硬件控制接口。";
        ConnectionSeverity = InfoBarSeverity.Informational;
        ChargeStatus = "正在读取充电阈值…";
        ChargeAvailabilityText = "正在读取设备状态。";
        PerformanceRequirementText = "正在检查切换条件。";
        BeginOperation("正在刷新", "正在读取电源、充电和性能状态。", InfoBarSeverity.Informational);
        try
        {
            SystemPowerSnapshot power = await Task.Run(systemPower.GetSnapshot);
            ApplyPowerSnapshot(power);

            Exception? chargeError = null;
            Exception? performanceError = null;
            Exception? powerSchemeError = null;
            ChargeThreshold? threshold = null;
            PerformanceStatus? performance = null;
            PowerSchemeStatus? schemes = null;
            try { threshold = await Task.Run(client.GetChargeThreshold); }
            catch (Exception exception) { chargeError = exception; }
            try { performance = await Task.Run(client.GetPerformanceStatus); }
            catch (Exception exception) { performanceError = exception; }
            try { schemes = await Task.Run(powerSchemes.GetStatus); }
            catch (Exception exception) { powerSchemeError = exception; }

            chargeSupported = threshold != null;
            performanceSupported = performance != null;
            ApplyPerformanceState(performance, schemes);
            if (threshold != null)
            {
                ChargeStatus = FormatChargeThreshold(threshold);
                CustomChargeStart = threshold.Start.ToString();
                CustomChargeEnd = threshold.End.ToString();
                SelectChargePreset(PresetFromThreshold(threshold));
                ChargeAvailabilityText = "可用 · 更改后会自动验证实际值";
                ChargeAvailabilitySeverity = InfoBarSeverity.Success;
            }
            else
            {
                ChargeStatus = "无法读取充电阈值。";
                ChargeAvailabilityText = "无法读取充电策略，可在设置中查看诊断。";
                ChargeAvailabilitySeverity = InfoBarSeverity.Error;
            }

            UpdatePerformanceRequirements();
            UpdateDeviceSummary(chargeError, performanceError);
            DiagnosticDetails = BuildDiagnostics(power, threshold, performance, schemes, chargeError, performanceError, powerSchemeError);
            FinishOperation(chargeSupported || performanceSupported ? "设备检查完成" : "无法连接设备接口",
                chargeSupported || performanceSupported ? "状态已更新。" : "硬件控制接口没有响应，可在设置中查看诊断。",
                chargeSupported || performanceSupported ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        }
        catch (Exception exception)
        {
            chargeSupported = false;
            performanceSupported = false;
            currentPerformanceMode = null;
            balancedPowerPlanAvailable = false;
            honorPerformancePlanAvailable = false;
            CurrentPerformanceModeText = "当前模式未知";
            CurrentPowerPlanText = "Windows 电源方案未知";
            ConnectionTitle = "设备检查失败";
            ConnectionSeverity = InfoBarSeverity.Error;
            DeviceSummary = "无法读取系统电源或荣耀接口状态。";
            ChargeAvailabilityText = "无法读取充电策略，可在设置中查看诊断。";
            ChargeAvailabilitySeverity = InfoBarSeverity.Error;
            PerformanceRequirementText = "无法读取性能控制状态，可在设置中查看诊断。";
            PerformanceRequirementSeverity = InfoBarSeverity.Error;
            DiagnosticDetails = BuildOperationFailureDiagnostics("设备检查", exception);
            FinishOperation("刷新失败", "无法读取设备状态，可在设置中查看诊断。", InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task SetSmartChargeAsync() => SetChargeThresholdAsync(40, 70, "已选择电池保护", "正在下发 40%–70% 充电阈值。", "电池保护已写入并验证：{0}%–{1}%。");
    public Task DisableChargeLimitAsync() => SetChargeThresholdAsync(0, 100, "已选择完整续航", "正在下发 0%–100% 充电阈值。", "完整续航已写入并验证：{0}%–{1}%。");

    public Task ApplySelectedChargePresetAsync()
    {
        if (!CanApplySelectedChargePreset) return Task.CompletedTask;
        return selectedChargePreset == 1 ? SetSmartChargeAsync() : DisableChargeLimitAsync();
    }

    public Task ApplySelectedChargeModeAsync() => selectedChargePreset == 0
        ? SetCustomChargeAsync()
        : ApplySelectedChargePresetAsync();

    public void SelectChargePreset(int preset)
    {
        if (preset is < 0 or > 2 || selectedChargePreset == preset) return;
        selectedChargePreset = preset;
        OnPropertyChanged(nameof(SelectedChargePresetIndex));
        OnPropertyChanged(nameof(SelectedChargePresetLabel));
        OnPropertyChanged(nameof(SelectedChargePresetDetail));
        OnPropertyChanged(nameof(CanApplySelectedChargePreset));
        OnPropertyChanged(nameof(CanApplySelectedChargeMode));
        OnPropertyChanged(nameof(ChargeModeHint));
        OnPropertyChanged(nameof(ChargeActionLabel));
    }

    public async Task SetCustomChargeAsync()
    {
        if (!TryGetCustomThreshold(out int start, out int end)) return;
        await SetChargeThresholdAsync(start, end, "正在应用自定义阈值", "正在下发自定义充电阈值。", "自定义阈值已写入并验证：{0}%–{1}%。");
    }

    public async Task SetPerformanceModeAsync()
    {
        if (!CanApplyPerformance) return;
        int targetMode = selectedPerformanceMode;
        string label = targetMode == 2 ? "高能模式" : "智能模式";
        bool applyStarted = false;
        BeginOperation("正在复核" + label, "正在重新读取供电、固件模式与 Windows 电源方案。", InfoBarSeverity.Informational);
        try
        {
            var preflight = await Task.Run(() => (
                Power: systemPower.GetSnapshot(),
                Status: client.GetPerformanceStatus(),
                Schemes: powerSchemes.GetStatus()));
            if (!CanWritePerformance(preflight.Power, preflight.Status, preflight.Schemes, targetMode, out string reason))
            {
                ApplyPowerSnapshot(preflight.Power);
                ApplyPerformanceState(preflight.Status, preflight.Schemes);
                UpdatePerformanceRequirements();
                FinishOperation("性能模式未写入", reason, InfoBarSeverity.Warning);
                AddActivity("性能模式写入已取消：" + reason, true);
                return;
            }

            OperationStatus = "正在同步固件模式与 Windows 电源方案。";
            applyStarted = true;
            var applied = await Task.Run(() => ApplyPerformanceMode(preflight.Status, preflight.Schemes, targetMode));
            ApplyPowerSnapshot(preflight.Power);
            ApplyPerformanceState(applied.Status, applied.Schemes);
            UpdatePerformanceRequirements();
            DiagnosticDetails = BuildPerformanceDiagnostics(applied.Status, applied.Schemes);
            FinishOperation("性能模式已更新", "已通过 0x0E04 回读确认" + label + "，并验证 Windows 电源方案。", InfoBarSeverity.Success);
            AddActivity("性能模式与 Windows 电源方案已同步：“" + label + "”。", false);
        }
        catch (Exception exception)
        {
            string diagnostics = BuildOperationFailureDiagnostics(applyStarted ? "性能模式同步" : "性能模式写入前复核", exception);
            if (applyStarted)
            {
                try
                {
                    var finalState = await Task.Run(() => (
                        Status: client.GetPerformanceStatus(),
                        Schemes: powerSchemes.GetStatus()));
                    ApplyPerformanceState(finalState.Status, finalState.Schemes);
                    UpdatePerformanceRequirements();
                    diagnostics += "\n\n失败后的实际状态：\n" + BuildPerformanceDiagnostics(finalState.Status, finalState.Schemes);
                    FinishOperation("性能模式同步失败", "已尝试回滚并重新读取实际状态，请在设置中核对诊断。", InfoBarSeverity.Error);
                }
                catch (Exception refreshException)
                {
                    diagnostics += "\n\n失败后的状态回查也失败：\n" + FormatExceptionDiagnostics(refreshException);
                    FinishOperation("性能模式同步失败", "已尝试回滚，但无法确认最终状态；请刷新后再核对。", InfoBarSeverity.Error);
                }
                AddActivity("性能模式同步失败；已尝试回滚，请核对当前状态。", true);
            }
            else
            {
                FinishOperation("性能模式未写入", "写入前复核失败，没有执行更改；可在设置中查看诊断。", InfoBarSeverity.Error);
                AddActivity("性能模式写入前复核失败；未执行更改。", true);
            }
            DiagnosticDetails = diagnostics;
        }
        finally { IsBusy = false; }
    }

    public void SelectPerformanceMode(int mode)
    {
        if (mode is < 1 or > 2 || !CanConfigurePerformance || mode == 2 && !honorPerformancePlanAvailable) return;
        selectedPerformanceMode = mode;
        OnPropertyChanged(nameof(SelectedPerformanceMode));
        OnPropertyChanged(nameof(SelectedPerformanceModeLabel));
        PendingChangeTitle = "待应用：" + SelectedPerformanceModeLabel;
        PendingChangeDetail = mode == 2
            ? "同步固件高能状态与 Honor Performance 电源方案。"
            : "同步固件智能状态与 Windows 平衡电源方案。";
        OnPropertyChanged(nameof(CanApplyPerformance));
    }

    public void SetCancelledStatus() => FinishOperation("已取消性能模式写入", "当前 BIOS 配置未改变。", InfoBarSeverity.Informational);

    private async Task SetChargeThresholdAsync(int start, int end, string title, string detail, string successFormat)
    {
        if (!CanConfigureCharge) return;
        BeginOperation(title, "1/2 " + detail, InfoBarSeverity.Informational);
        try
        {
            ChargeThreshold threshold = await Task.Run(() => client.SetChargeThreshold(start, end));
            ChargeStatus = FormatChargeThreshold(threshold);
            CustomChargeStart = threshold.Start.ToString();
            CustomChargeEnd = threshold.End.ToString();
            SelectChargePreset(PresetFromThreshold(threshold));
            if (threshold.Start != start || threshold.End != end)
            {
                FinishOperation("设备采用了不同的充电值", "请求 " + start + "%–" + end + "%；实际为 " + threshold.Start + "%–" + threshold.End + "% 。", InfoBarSeverity.Warning);
                AddActivity("充电策略回读不符：请求 " + start + "%–" + end + "%；实际 " + threshold.Start + "%–" + threshold.End + "% 。", true);
                return;
            }
            FinishOperation("充电策略已更新", "已从设备回读并确认 " + threshold.Start + "%–" + threshold.End + "% 。", InfoBarSeverity.Success);
            AddActivity(string.Format(successFormat, threshold.Start, threshold.End), false);
        }
        catch (Exception exception)
        {
            DiagnosticDetails = BuildOperationFailureDiagnostics("充电策略设置", exception);
            FinishOperation("充电策略设置失败", "设备没有接受请求，可在设置中查看诊断。", InfoBarSeverity.Error);
            AddActivity("充电策略写入失败；请查看诊断页。", true);
        }
        finally { IsBusy = false; }
    }

    private void ApplyPowerSnapshot(SystemPowerSnapshot power)
    {
        isOnAcPower = power.IsOnAcPower;
        batteryPercent = power.BatteryPercent;
        string battery = power.BatteryPercent.HasValue ? $"电池 {power.BatteryPercent.Value}%" : "电池电量未知";
        PowerSummary = power.IsOnAcPower == true ? "已接通 AC · " + battery : power.IsOnAcPower == false ? "未接通 AC · " + battery : "AC 状态未知 · " + battery;
    }

    private void ApplyPerformanceState(PerformanceStatus? performance, PowerSchemeStatus? schemes)
    {
        currentPerformanceMode = performance?.CurrentMode;
        CurrentPerformanceModeText = currentPerformanceMode switch
        {
            1 => "当前模式 · 智能",
            2 => "当前模式 · 高能",
            _ => "当前模式 · 无法识别"
        };

        balancedPowerPlanAvailable = schemes?.Balanced != null;
        honorPerformancePlanAvailable = schemes?.HonorPerformance != null;
        CurrentPowerPlanText = schemes == null
            ? "Windows 电源方案不可用"
            : "Windows 电源方案 · " + schemes.Active.Name;

        if (currentPerformanceMode is 1 or 2)
        {
            selectedPerformanceMode = currentPerformanceMode.Value;
            OnPropertyChanged(nameof(SelectedPerformanceMode));
            OnPropertyChanged(nameof(SelectedPerformanceModeLabel));
        }
        NotifyControlStateChanged();
    }

    private void UpdatePerformanceRequirements()
    {
        if (!performanceSupported)
        {
            PerformanceRequirementText = "此设备暂时无法使用性能模式。";
            PerformanceRequirementSeverity = InfoBarSeverity.Error;
            PendingChangeTitle = "性能模式不可用";
            PendingChangeDetail = "可在设置中查看诊断信息。";
        }
        else if (currentPerformanceMode is not (1 or 2))
        {
            PerformanceRequirementText = "固件返回了无法识别的性能模式，已阻止写入。";
            PerformanceRequirementSeverity = InfoBarSeverity.Error;
            PendingChangeTitle = "当前模式无法识别";
            PendingChangeDetail = "可在设置中查看 0x0E04 原始返回。";
        }
        else if (isOnAcPower != true)
        {
            PerformanceRequirementText = isOnAcPower == false ? "请接通电源后再切换模式。" : "无法确认供电状态，暂时不能切换。";
            PerformanceRequirementSeverity = InfoBarSeverity.Warning;
            PendingChangeTitle = "等待接通 AC 电源";
            PendingChangeDetail = "检查电源连接后刷新状态。";
        }
        else if (!batteryPercent.HasValue || batteryPercent.Value < 20)
        {
            PerformanceRequirementText = batteryPercent.HasValue ? "当前电量 " + batteryPercent.Value + "% · 至少需要 20%" : "无法读取电量，暂时不能切换。";
            PerformanceRequirementSeverity = InfoBarSeverity.Warning;
            PendingChangeTitle = "等待电池状态满足条件";
            PendingChangeDetail = "请确保电量至少为 20% 后刷新状态。";
        }
        else if (!balancedPowerPlanAvailable)
        {
            PerformanceRequirementText = "未找到 Windows 平衡电源方案，已阻止模式切换。";
            PerformanceRequirementSeverity = InfoBarSeverity.Error;
            PendingChangeTitle = "缺少平衡电源方案";
            PendingChangeDetail = "恢复 Windows 默认电源方案后再刷新。";
        }
        else if (!honorPerformancePlanAvailable && selectedPerformanceMode == 2)
        {
            selectedPerformanceMode = 1;
            OnPropertyChanged(nameof(SelectedPerformanceMode));
            OnPropertyChanged(nameof(SelectedPerformanceModeLabel));
            PerformanceRequirementText = "未安装 Honor Performance 电源方案，可使用智能模式。";
            PerformanceRequirementSeverity = InfoBarSeverity.Warning;
            PendingChangeTitle = "待应用：智能模式";
            PendingChangeDetail = "同步固件智能状态与 Windows 平衡电源方案。";
            OnPropertyChanged(nameof(CanApplyPerformance));
        }
        else
        {
            PerformanceRequirementText = honorPerformancePlanAvailable
                ? "已满足智能与高能模式切换条件"
                : "已满足智能模式切换条件；未安装 Honor Performance 方案";
            PerformanceRequirementSeverity = InfoBarSeverity.Success;
            PendingChangeTitle = "待应用：" + SelectedPerformanceModeLabel;
            PendingChangeDetail = selectedPerformanceMode == 2
                ? "同步固件高能状态与 Honor Performance 电源方案。"
                : "同步固件智能状态与 Windows 平衡电源方案。";
        }
    }

    private void UpdateDeviceSummary(Exception? chargeError, Exception? performanceError)
    {
        if (chargeSupported || performanceSupported)
        {
            ConnectionTitle = "荣耀硬件接口已连接";
            ConnectionSeverity = InfoBarSeverity.Success;
            DeviceSummary = $"充电控制：{(chargeSupported ? "可用" : "不可用")}；性能控制：{(performanceSupported ? "可用" : "不可用")}。";
        }
        else
        {
            ConnectionTitle = "荣耀硬件接口不可用";
            ConnectionSeverity = InfoBarSeverity.Error;
            DeviceSummary = "无法连接硬件控制接口，可在设置中查看诊断信息。";
        }
    }

    private (PerformanceStatus Status, PowerSchemeStatus Schemes) ApplyPerformanceMode(
        PerformanceStatus previousStatus,
        PowerSchemeStatus previousSchemes,
        int mode)
    {
        PowerSchemeInfo targetScheme = previousSchemes.GetTarget(mode)
            ?? throw new InvalidOperationException(mode == 2
                ? "未找到 Honor Performance 电源方案，未执行任何写入。"
                : "未找到 Windows 平衡电源方案，未执行任何写入。");

        int previousMode = previousStatus.CurrentMode;
        Guid previousSchemeId = previousSchemes.Active.Id;
        bool firmwareWriteAttempted = previousMode != mode;
        bool schemeWriteAttempted = previousSchemeId != targetScheme.Id;

        try
        {
            PerformanceStatus status = firmwareWriteAttempted
                ? client.SetPerformanceMode(mode)
                : client.GetPerformanceStatus();
            if (schemeWriteAttempted) powerSchemes.SetActiveForMode(mode);
            PowerSchemeStatus schemes = powerSchemes.GetStatus();
            if (schemes.Active.Id != targetScheme.Id)
                throw new InvalidOperationException($"Windows 电源方案回读不符：请求 {targetScheme.Id}，实际为 {schemes.Active.Id}。");
            return (status, schemes);
        }
        catch (Exception exception)
        {
            List<string> rollback = new();
            if (firmwareWriteAttempted && previousMode is 1 or 2)
            {
                try
                {
                    client.SetPerformanceMode(previousMode);
                    rollback.Add("固件模式已恢复");
                }
                catch (Exception rollbackException)
                {
                    rollback.Add("固件模式恢复失败：" + rollbackException.Message);
                }
            }
            if (schemeWriteAttempted)
            {
                try
                {
                    powerSchemes.SetActive(previousSchemeId);
                    rollback.Add("Windows 电源方案已恢复");
                }
                catch (Exception rollbackException)
                {
                    rollback.Add("Windows 电源方案恢复失败：" + rollbackException.Message);
                }
            }

            string rollbackSummary = rollback.Count == 0 ? "未发生需要回滚的更改" : string.Join("；", rollback);
            throw new InvalidOperationException("性能模式同步失败；回滚结果：" + rollbackSummary + "。", exception);
        }
    }

    private static string BuildDiagnostics(
        SystemPowerSnapshot power,
        ChargeThreshold? threshold,
        PerformanceStatus? performance,
        PowerSchemeStatus? schemes,
        Exception? chargeError,
        Exception? performanceError,
        Exception? powerSchemeError)
    {
        string powerState = power.IsOnAcPower == true ? "AC 在线" : power.IsOnAcPower == false ? "AC 离线" : "AC 状态未知";
        string charge = threshold != null
            ? $"充电接口：可用；0x1103 回读阈值={threshold.Start}%–{threshold.End}%"
            : "充电接口错误：\n" + (chargeError == null ? "未知" : FormatExceptionDiagnostics(chargeError));
        string mode = performance == null
            ? "性能接口错误：\n" + (performanceError == null ? "未知" : FormatExceptionDiagnostics(performanceError))
            : BuildPerformanceDiagnostics(performance, schemes);
        string planError = powerSchemeError == null ? string.Empty : "\n\nWindows 电源方案错误：\n" + FormatExceptionDiagnostics(powerSchemeError);
        return "目标接口：root\\wmi / OemWMIMethod\n候选实例优先级：HWMI_0 → HWMI_1 → 其他活动实例\n" + powerState + "\n\n" + charge + "\n\n" + mode + planError;
    }

    private static string BuildPerformanceDiagnostics(PerformanceStatus performance, PowerSchemeStatus? schemes)
    {
        string currentMode = performance.CurrentMode switch { 1 => "智能", 2 => "高能", _ => "未知" };
        string plan = schemes == null
            ? "不可用"
            : schemes.Active.Name + " (" + schemes.Active.Id + ")";
        return "性能模式：" + currentMode
            + "；0x0E04=" + performance.ModeStateQuery
            + "；0x0802 遥测=" + performance.TelemetryQuery
            + "；0x3C06 支持掩码=0x" + performance.SupportMask.ToString("X2")
            + "，原始=" + performance.SupportQuery
            + "（HUNTER=" + (performance.SupportsHunterMode ? "支持" : "不支持") + "）"
            + "；0x0902 适配器电压=" + performance.AdapterVoltageMillivolts + "mV，原始=" + performance.AdapterQuery
            + "；Windows 电源方案=" + plan;
    }

    private static string BuildOperationFailureDiagnostics(string operation, Exception exception)
    {
        return operation + "失败\n" + FormatExceptionDiagnostics(exception);
    }

    private static string FormatExceptionDiagnostics(Exception exception)
    {
        StringBuilder details = new();
        Exception? current = exception;
        int depth = 0;
        while (current != null)
        {
            if (depth > 0) details.AppendLine().Append("内部异常 ").Append(depth).AppendLine("：");
            details.Append(current.GetType().FullName)
                .Append(" (HRESULT 0x")
                .Append(current.HResult.ToString("X8"))
                .AppendLine(")")
                .Append(current.Message);
            current = current.InnerException;
            depth++;
        }
        return details.ToString();
    }

    private static bool CanWritePerformance(
        SystemPowerSnapshot power,
        PerformanceStatus status,
        PowerSchemeStatus schemes,
        int mode,
        out string reason)
    {
        if (status.CurrentMode is not (1 or 2))
        {
            reason = "写入前无法通过 0x0E04 识别当前性能模式。";
            return false;
        }
        if (power.IsOnAcPower != true)
        {
            reason = "写入前复核发现 AC 电源未连接或状态未知。";
            return false;
        }
        if (!power.BatteryPercent.HasValue || power.BatteryPercent.Value < 20)
        {
            reason = "写入前复核发现电量低于 20% 或无法读取。";
            return false;
        }
        if (schemes.GetTarget(mode) == null)
        {
            reason = mode == 2
                ? "未找到 Honor Performance 电源方案，已阻止高能模式写入。"
                : "未找到 Windows 平衡电源方案，已阻止智能模式写入。";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private void ValidateCustomThreshold()
    {
        if (TryGetCustomThreshold(out int start, out int end, false))
        {
            CustomChargeValidation = $"输入有效：低于 {start}% 恢复充电，达到 {end}% 停止充电。";
            CustomChargeValidationSeverity = InfoBarSeverity.Success;
        }
        else
        {
            CustomChargeValidation = "请输入 0–100 的整数，且开始值不得大于停止值。";
            CustomChargeValidationSeverity = InfoBarSeverity.Error;
        }
        OnPropertyChanged(nameof(SelectedChargePresetDetail));
        OnPropertyChanged(nameof(ChargeModeHint));
        OnPropertyChanged(nameof(CanApplySelectedChargeMode));
    }

    private bool IsCustomThresholdValid() => TryGetCustomThreshold(out _, out _, false);

    private bool TryGetCustomThreshold(out int start, out int end, bool updateMessage = true)
    {
        start = 0;
        end = 0;
        bool startParsed = int.TryParse(CustomChargeStart, out start);
        bool endParsed = int.TryParse(CustomChargeEnd, out end);
        bool valid = startParsed && endParsed && start is >= 0 and <= 100 && end is >= 0 and <= 100 && start <= end;
        if (!valid && updateMessage)
        {
            CustomChargeValidation = "请输入 0–100 的整数，且开始值不得大于停止值。";
            CustomChargeValidationSeverity = InfoBarSeverity.Error;
        }
        return valid;
    }

    private void BeginOperation(string title, string detail, InfoBarSeverity severity)
    {
        IsBusy = true;
        HasOperationStatus = true;
        OperationTitle = title;
        OperationStatus = detail;
        OperationSeverity = severity;
    }

    private void FinishOperation(string title, string detail, InfoBarSeverity severity)
    {
        HasOperationStatus = true;
        OperationTitle = title;
        OperationStatus = detail;
        OperationSeverity = severity;
    }

    private void NotifyControlStateChanged()
    {
        OnPropertyChanged(nameof(IsNotBusy));
        OnPropertyChanged(nameof(CanConfigureCharge));
        OnPropertyChanged(nameof(CanApplySelectedChargePreset));
        OnPropertyChanged(nameof(CanApplySelectedChargeMode));
        OnPropertyChanged(nameof(CanConfigurePerformance));
        OnPropertyChanged(nameof(CanSelectHighPerformance));
        OnPropertyChanged(nameof(CanApplyPerformance));
    }

    private void AddActivity(string message, bool isError)
    {
        ActivityRecords.Insert(0, new ActivityRecord(message, isError));
        if (ActivityRecords.Count > 8) ActivityRecords.RemoveAt(ActivityRecords.Count - 1);
    }

    private static string FormatChargeThreshold(ChargeThreshold threshold)
    {
        string state = threshold.Start == 0 && threshold.End == 100 ? "完整续航" : threshold.Start == 40 && threshold.End == 70 ? "电池保护已开启" : "当前为自定义阈值";
        return $"{state}：低于 {threshold.Start}% 恢复充电，达到 {threshold.End}% 停止充电。";
    }

    private static int PresetFromThreshold(ChargeThreshold threshold)
    {
        if (threshold.Start == 40 && threshold.End == 70) return 1;
        if (threshold.Start == 0 && threshold.End == 100) return 2;
        return 0;
    }
}
