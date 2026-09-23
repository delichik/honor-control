using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using HonorControl.Models;
using HonorControl.Services;

namespace HonorControl.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly OemWmiClient client = new();
    private readonly SystemPowerService systemPower = new();
    private string chargeStatus = "正在检查充电策略…";
    private string connectionTitle = "正在检查设备接口";
    private string deviceSummary = "请稍候。";
    private string powerSummary = "电源状态未知";
    private string chargeAvailabilityText = "检查中";
    private string performanceRequirementText = "正在检查性能模式前置条件。";
    private string pendingChangeTitle = "尚未选择性能模式";
    private string pendingChangeDetail = "完成设备检查后可选择并应用模式。";
    private string operationTitle = "准备就绪";
    private string operationStatus = "正在验证荣耀硬件接口和当前状态。";
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
    private int selectedPerformanceMode = 1;

    public ObservableCollection<ActivityRecord> ActivityRecords { get; } = new();

    public string ChargeStatus { get => chargeStatus; private set { chargeStatus = value; OnPropertyChanged(); } }
    public string ConnectionTitle { get => connectionTitle; private set { connectionTitle = value; OnPropertyChanged(); } }
    public string DeviceSummary { get => deviceSummary; private set { deviceSummary = value; OnPropertyChanged(); } }
    public string PowerSummary { get => powerSummary; private set { powerSummary = value; OnPropertyChanged(); } }
    public string ChargeAvailabilityText { get => chargeAvailabilityText; private set { chargeAvailabilityText = value; OnPropertyChanged(); } }
    public string PerformanceRequirementText { get => performanceRequirementText; private set { performanceRequirementText = value; OnPropertyChanged(); } }
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
    public bool CanConfigurePerformance => performanceSupported && isOnAcPower == true && batteryPercent.HasValue && batteryPercent.Value >= 20 && !IsBusy;
    public bool CanApplyPerformance => CanConfigurePerformance;
    public string SelectedPerformanceModeLabel => selectedPerformanceMode == 2 ? "高能模式" : "智能模式";

    public async Task RefreshAsync()
    {
        BeginOperation("正在检查设备", "正在读取电源状态、充电策略和性能接口。", InfoBarSeverity.Informational);
        try
        {
            SystemPowerSnapshot power = await Task.Run(systemPower.GetSnapshot);
            ApplyPowerSnapshot(power);

            Exception? chargeError = null;
            Exception? performanceError = null;
            ChargeThreshold? threshold = null;
            PerformanceStatus? performance = null;
            try { threshold = await Task.Run(client.GetChargeThreshold); }
            catch (Exception exception) { chargeError = exception; }
            try { performance = await Task.Run(client.GetPerformanceStatus); }
            catch (Exception exception) { performanceError = exception; }

            chargeSupported = threshold != null;
            performanceSupported = performance != null;
            if (threshold != null)
            {
                ChargeStatus = FormatChargeThreshold(threshold);
                CustomChargeStart = threshold.Start.ToString();
                CustomChargeEnd = threshold.End.ToString();
                ChargeAvailabilityText = "荣耀充电阈值接口可用。写入后会自动回读验证。";
                ChargeAvailabilitySeverity = InfoBarSeverity.Success;
            }
            else
            {
                ChargeStatus = "无法读取充电阈值。";
                ChargeAvailabilityText = "充电接口不可用：" + (chargeError?.Message ?? "未知错误");
                ChargeAvailabilitySeverity = InfoBarSeverity.Error;
            }

            UpdatePerformanceRequirements();
            UpdateDeviceSummary(chargeError, performanceError);
            DiagnosticDetails = BuildDiagnostics(power, performance, chargeError, performanceError);
            FinishOperation(chargeSupported || performanceSupported ? "设备检查完成" : "无法连接设备接口",
                chargeSupported || performanceSupported ? "已读取可用接口。你可以根据当前状态选择操作。" : "请确认管理员权限、机型支持和 BIOS 状态。",
                chargeSupported || performanceSupported ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        }
        catch (Exception exception)
        {
            chargeSupported = false;
            performanceSupported = false;
            ConnectionTitle = "设备检查失败";
            ConnectionSeverity = InfoBarSeverity.Error;
            DeviceSummary = "无法读取系统电源或荣耀接口状态。";
            FinishOperation("设备检查失败", exception.Message, InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task SetSmartChargeAsync() => SetChargeThresholdAsync(40, 70, "已选择电池保护", "正在下发 40%–70% 充电阈值。", "智能充电已写入并验证：{0}%–{1}%。");
    public Task DisableChargeLimitAsync() => SetChargeThresholdAsync(0, 100, "已选择充满模式", "正在下发 0%–100% 充电阈值。", "充电限制已关闭并验证：{0}%–{1}%。");

    public async Task SetCustomChargeAsync()
    {
        if (!TryGetCustomThreshold(out int start, out int end)) return;
        await SetChargeThresholdAsync(start, end, "正在应用自定义阈值", "正在下发自定义充电阈值。", "自定义阈值已写入并验证：{0}%–{1}%。");
    }

    public async Task SetPerformanceModeAsync()
    {
        if (!CanApplyPerformance) return;
        string label = SelectedPerformanceModeLabel;
        BeginOperation("正在应用" + label, "1/3 正在向 BIOS 下发模式命令。", InfoBarSeverity.Informational);
        try
        {
            PerformanceStatus status = await Task.Run(() => client.SetPerformanceMode(selectedPerformanceMode));
            DiagnosticDetails = "目标接口：root\\wmi / OemWMIMethod / ACPI\\PNP0C14\\HWMI_0\n性能原始返回：" + status.ModeQuery + "；支持信息：" + status.SupportQuery;
            FinishOperation("性能模式已应用", "3/3 BIOS 已确认，已重新读取模式状态。请在实际负载下确认功耗和风扇表现。", InfoBarSeverity.Success);
            AddActivity("性能模式写入成功：" + label, false);
        }
        catch (Exception exception)
        {
            FinishOperation("性能模式设置失败", exception.Message, InfoBarSeverity.Error);
            AddActivity("性能模式写入失败：" + exception.Message, true);
        }
        finally { IsBusy = false; }
    }

    public void SelectPerformanceMode(int mode)
    {
        if (mode is < 1 or > 2 || !CanConfigurePerformance) return;
        selectedPerformanceMode = mode;
        PendingChangeTitle = "待应用：" + SelectedPerformanceModeLabel;
        PendingChangeDetail = mode == 2 ? "切换到高能策略；功耗、温度和风扇噪声会提高。" : "切换到均衡策略，适合日常办公和轻负载。";
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
            FinishOperation("充电策略已更新", "2/2 已收到 BIOS 成功状态并回读验证。", InfoBarSeverity.Success);
            AddActivity(string.Format(successFormat, threshold.Start, threshold.End), false);
        }
        catch (Exception exception)
        {
            FinishOperation("充电策略设置失败", exception.Message, InfoBarSeverity.Error);
            AddActivity("充电策略写入失败：" + exception.Message, true);
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

    private void UpdatePerformanceRequirements()
    {
        if (!performanceSupported)
        {
            PerformanceRequirementText = "此设备未返回可用的性能模式接口。";
            PerformanceRequirementSeverity = InfoBarSeverity.Error;
            PendingChangeTitle = "性能模式不可用";
            PendingChangeDetail = "请在诊断详情中查看接口错误。";
        }
        else if (isOnAcPower != true)
        {
            PerformanceRequirementText = isOnAcPower == false ? "请接通 AC 电源后再切换性能模式，避免 BIOS 自动回退。" : "无法确认 AC 供电状态；为避免自动回退，暂不允许切换性能模式。";
            PerformanceRequirementSeverity = InfoBarSeverity.Warning;
            PendingChangeTitle = "等待接通 AC 电源";
            PendingChangeDetail = "检查电源连接后刷新状态。";
        }
        else if (!batteryPercent.HasValue || batteryPercent.Value < 20)
        {
            PerformanceRequirementText = batteryPercent.HasValue ? "当前电量为 " + batteryPercent.Value + "%；性能模式要求电量至少 20%。" : "无法读取当前电量；为避免低电量切换，暂不允许写入性能模式。";
            PerformanceRequirementSeverity = InfoBarSeverity.Warning;
            PendingChangeTitle = "等待电池状态满足条件";
            PendingChangeDetail = "请确保电量至少为 20% 后刷新状态。";
        }
        else
        {
            PerformanceRequirementText = "已满足 AC 供电和电量前置条件。高能模式会提高功耗、温度和风扇噪声。";
            PerformanceRequirementSeverity = InfoBarSeverity.Success;
            PendingChangeTitle = "待应用：" + SelectedPerformanceModeLabel;
            PendingChangeDetail = selectedPerformanceMode == 2 ? "切换到高能策略；功耗、温度和风扇噪声会提高。" : "切换到均衡策略，适合日常办公和轻负载。";
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
            DeviceSummary = "未读取到可用响应。请确认管理员权限、机型支持和 BIOS 状态。";
        }
    }

    private static string BuildDiagnostics(SystemPowerSnapshot power, PerformanceStatus? performance, Exception? chargeError, Exception? performanceError)
    {
        string powerState = power.IsOnAcPower == true ? "AC 在线" : power.IsOnAcPower == false ? "AC 离线" : "AC 状态未知";
        string charge = chargeError == null ? "充电接口：可用" : "充电接口错误：" + chargeError.Message;
        string mode = performance == null ? "性能接口错误：" + (performanceError?.Message ?? "未知") : "性能原始返回：" + performance.ModeQuery + "；支持信息：" + performance.SupportQuery;
        return "目标接口：root\\wmi / OemWMIMethod / ACPI\\PNP0C14\\HWMI_0\n" + powerState + "\n" + charge + "\n" + mode;
    }

    private void ValidateCustomThreshold()
    {
        if (TryGetCustomThreshold(out int start, out int end, false))
        {
            CustomChargeValidation = $"输入有效：低于 {start}% 恢复充电，达到 {end}% 停止充电。";
            CustomChargeValidationSeverity = InfoBarSeverity.Success;
        }
    }

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
        OperationTitle = title;
        OperationStatus = detail;
        OperationSeverity = severity;
    }

    private void FinishOperation(string title, string detail, InfoBarSeverity severity)
    {
        OperationTitle = title;
        OperationStatus = detail;
        OperationSeverity = severity;
    }

    private void NotifyControlStateChanged()
    {
        OnPropertyChanged(nameof(IsNotBusy));
        OnPropertyChanged(nameof(CanConfigureCharge));
        OnPropertyChanged(nameof(CanConfigurePerformance));
        OnPropertyChanged(nameof(CanApplyPerformance));
    }

    private void AddActivity(string message, bool isError)
    {
        ActivityRecords.Insert(0, new ActivityRecord(message, isError));
        if (ActivityRecords.Count > 8) ActivityRecords.RemoveAt(ActivityRecords.Count - 1);
    }

    private static string FormatChargeThreshold(ChargeThreshold threshold)
    {
        string state = threshold.Start == 0 && threshold.End == 100 ? "充满模式" : threshold.Start == 40 && threshold.End == 70 ? "电池保护已开启" : "当前为自定义阈值";
        return $"{state}：低于 {threshold.Start}% 恢复充电，达到 {threshold.End}% 停止充电。";
    }
}
