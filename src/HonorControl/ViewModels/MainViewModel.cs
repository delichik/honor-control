using HonorControl.Contracts;
using HonorControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HonorControl.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ServiceClient service = new();
    private ServiceSnapshot? snapshot;
    private bool loaded;
    private bool busy;
    private bool refreshing;
    private int selectedChargePreset = 1;
    private int selectedPerformanceMode = 1;
    private string customChargeStart = "40";
    private string customChargeEnd = "70";
    private string operationTitle = string.Empty;
    private string operationStatus = string.Empty;
    private InfoBarSeverity operationSeverity = InfoBarSeverity.Informational;
    private bool hasOperationStatus;
    private string? connectionError;

    public bool IsBusy => busy;
    public bool IsRefreshing => refreshing;
    public bool HasOperationStatus => hasOperationStatus;
    public string OperationTitle => operationTitle;
    public string OperationStatus => operationStatus;
    public InfoBarSeverity OperationSeverity => operationSeverity;
    public bool AutoReconcileEnabled => snapshot?.Desired.AutoReconcile == true;
    public bool IsServiceAvailable => snapshot != null && connectionError == null;
    public string AutoReconcileHint => snapshot == null
        ? "无法连接后台服务。"
        : AutoReconcileEnabled ? "服务将在开机后和运行中持续校正配置。" : "关闭后，服务仅在保存配置时尝试应用。";

    public string ConnectionTitle => connectionError != null ? (snapshot == null ? "后台服务不可用" : "后台服务不可用 · 显示上次状态")
        : snapshot?.Actual.ServiceError != null ? "后台服务读取失败"
        : snapshot?.Actual.PcManagerOpen == true ? "荣耀电脑管家已开启 · 服务只读"
        : snapshot == null ? "正在连接后台服务" : "后台服务已连接";
    public string DeviceSummary => connectionError ?? (snapshot?.Actual.PcManagerOpen == true
        ? "当前只回读设备状态；已保存的配置将在电脑管家关闭后继续应用。"
        : snapshot?.Actual.ServiceError ?? "硬件状态由后台服务读取和维护。");
    public InfoBarSeverity ConnectionSeverity => connectionError != null ? InfoBarSeverity.Error
        : snapshot?.Actual.ServiceError != null ? InfoBarSeverity.Error
        : snapshot?.Actual.PcManagerOpen == true ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
    public double BatteryProgress => snapshot?.Actual.BatteryPercent is int percent and >= 0 and <= 100 ? percent : 0;
    public Visibility BatteryKnownVisibility => snapshot?.Actual.BatteryPercent is >= 0 and <= 100 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BatteryUnknownVisibility => BatteryKnownVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    public string BatteryPercentText => BatteryKnownVisibility == Visibility.Visible ? $"{snapshot!.Actual.BatteryPercent}%" : "—";
    public string PowerStateText => snapshot?.Actual.IsOnAcPower switch
    {
        true => "已接通电源",
        false => "电池供电",
        _ => "供电状态未知"
    };
    public string BatteryAccessibleText => $"实际电量：{(BatteryKnownVisibility == Visibility.Visible ? BatteryPercentText : "未知")}；{PowerStateText}";
    public int? ActualChargeStart => HasActualChargeRange ? snapshot!.Actual.ChargeStart : null;
    public int? ActualChargeEnd => HasActualChargeRange ? snapshot!.Actual.ChargeEnd : null;
    private bool HasActualChargeRange => snapshot?.Actual is { ChargeStart: int start, ChargeEnd: int end }
        && start is >= 0 and <= 100 && end is >= 0 and <= 100 && start < end;
    public string ChargeRangeText => HasActualChargeRange ? $"{ActualChargeStart}–{ActualChargeEnd}%" : "—";
    public string ChargeUnavailableText => snapshot?.Actual.ChargeError ?? (snapshot == null ? "正在读取" : "阈值未知");
    public Visibility ChargeUnavailableVisibility => HasActualChargeRange && string.IsNullOrWhiteSpace(snapshot?.Actual.ChargeError)
        ? Visibility.Collapsed : Visibility.Visible;
    public string ChargeAccessibleText => HasActualChargeRange
        ? $"设备实际阈值：恢复充电 {ActualChargeStart}%，停止充电 {ActualChargeEnd}%"
          + (string.IsNullOrWhiteSpace(snapshot?.Actual.ChargeError) ? string.Empty : "；" + snapshot?.Actual.ChargeError)
        : "设备实际阈值不可用：" + ChargeUnavailableText;
    public Visibility ActualSmartVisibility => snapshot?.Actual.PerformanceMode == 1 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ActualHighVisibility => snapshot?.Actual.PerformanceMode == 2 ? Visibility.Visible : Visibility.Collapsed;
    public double ActualSmartOpacity => snapshot?.Actual.PerformanceMode == 1 ? 1 : 0.55;
    public double ActualHighOpacity => snapshot?.Actual.PerformanceMode == 2 ? 1 : 0.55;
    public string ActualPerformanceModeName => snapshot?.Actual.PerformanceMode switch { 1 => "智能", 2 => "高能", _ => "未知" };
    public string ActualPowerPlanText => "Windows 电源方案 · " + (snapshot?.Actual.PowerSchemeName ?? "未知");
    public string PerformanceReadoutError => snapshot?.Actual.PerformanceError
        ?? (snapshot?.Actual.PerformanceMode is 1 or 2 ? string.Empty : "模式未知");
    public Visibility PerformanceErrorVisibility => string.IsNullOrEmpty(PerformanceReadoutError) ? Visibility.Collapsed : Visibility.Visible;
    public string PerformanceAccessibleText => $"设备实际性能模式：{ActualPerformanceModeName}；{ActualPowerPlanText}"
        + (string.IsNullOrEmpty(PerformanceReadoutError) ? string.Empty : "；" + PerformanceReadoutError);
    public string ChargeDesiredStatus => snapshot?.Desired is { ChargeStart: int start, ChargeEnd: int end }
        ? $"已保存：{start}%–{end}%" : "尚未保存";
    public string PerformanceDesiredStatus => snapshot?.Desired.PerformanceMode switch
    {
        1 => "已保存：智能",
        2 => "已保存：高能",
        _ => "尚未保存"
    };
    public string PerformanceRequirementText => !IsServiceAvailable ? "后台服务不可用，无法保存配置。"
        : snapshot?.Actual.PcManagerOpen == true
        ? "电脑管家运行期间服务只读；保存的配置会等待其关闭。"
        : snapshot?.Actual.PerformanceError is { } error ? "性能状态：" + error
        : selectedPerformanceMode == 2 && snapshot?.Actual.HighPerformanceSupported != true
        ? "设备尚未报告高能模式能力；配置可保存，服务当前不会写入。"
        : selectedPerformanceMode == 2 && snapshot?.Actual.HonorPerformancePlanAvailable != true
        ? "Honor Performance 电源方案不可用；配置可保存，服务当前不会写入。"
        : selectedPerformanceMode == 1 && snapshot?.Actual.BalancedPlanAvailable != true
        ? "Windows 平衡电源方案不可用；配置可保存，服务当前不会写入。"
        : snapshot?.Actual.IsOnAcPower != true ? "保存后，服务会等待 AC 供电再应用。"
        : snapshot?.Actual.BatteryPercent is not >= 20 ? "保存后，服务会等待电量达到 20% 再应用。"
        : string.Empty;
    public Visibility ChargeHintVisibility => string.IsNullOrEmpty(ChargeModeHint) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PerformanceHintVisibility => string.IsNullOrEmpty(PerformanceRequirementText) ? Visibility.Collapsed : Visibility.Visible;
    public string DiagnosticDetails => snapshot == null ? connectionError ?? "尚未收到服务状态。"
        : $"服务检查时间：{snapshot.Actual.CheckedAt:O}\n"
          + $"期望阈值：{snapshot.Desired.ChargeStart}%–{snapshot.Desired.ChargeEnd}%；期望性能模式：{snapshot.Desired.PerformanceMode}\n"
          + $"实际阈值：{snapshot.Actual.ChargeStart}%–{snapshot.Actual.ChargeEnd}%；实际性能模式：{snapshot.Actual.PerformanceMode}\n"
          + $"电脑管家运行：{snapshot.Actual.PcManagerOpen}；开机自动维护：{snapshot.Desired.AutoReconcile}\n"
          + $"充电错误：{snapshot.Actual.ChargeError ?? "无"}\n性能错误：{snapshot.Actual.PerformanceError ?? "无"}\n服务错误：{snapshot.Actual.ServiceError ?? "无"}";

    public string CustomChargeStart
    {
        get => customChargeStart;
        set { customChargeStart = value; OnPropertyChanged(); OnPropertyChanged(nameof(ChargeModeHint)); OnPropertyChanged(nameof(ChargeHintVisibility)); OnPropertyChanged(nameof(CanApplySelectedChargeMode)); }
    }
    public string CustomChargeEnd
    {
        get => customChargeEnd;
        set { customChargeEnd = value; OnPropertyChanged(); OnPropertyChanged(nameof(ChargeModeHint)); OnPropertyChanged(nameof(ChargeHintVisibility)); OnPropertyChanged(nameof(CanApplySelectedChargeMode)); }
    }
    public int SelectedChargePresetIndex => selectedChargePreset - 1;
    public string ChargeModeHint => selectedChargePreset == 0 && !TryGetCustomThreshold(out _, out _)
        ? "恢复值须低于停止值。" : string.Empty;
    public bool CanConfigureCharge => IsServiceAvailable && !busy;
    public bool CanApplySelectedChargeMode => CanConfigureCharge
        && (selectedChargePreset is 1 or 2 || selectedChargePreset == 0 && TryGetCustomThreshold(out _, out _));
    public int SelectedPerformanceMode => selectedPerformanceMode;
    public string SelectedPerformanceModeLabel => selectedPerformanceMode == 2 ? "高能模式" : "智能模式";
    public bool CanConfigurePerformance => IsServiceAvailable && !busy;
    public bool CanSelectHighPerformance => CanConfigurePerformance;
    public bool CanApplyPerformance => CanConfigurePerformance;

    public async Task RefreshAsync(bool preserveSelection = false)
    {
        if (busy || refreshing) return;
        refreshing = true;
        OnPropertyChanged(nameof(IsRefreshing));
        try
        {
            ServiceSnapshot latest = await service.GetStateAsync();
            connectionError = null;
            ApplySnapshot(latest, preserveSelection && loaded);
            loaded = true;
        }
        catch (Exception exception)
        {
            connectionError = exception.Message;
            RaiseState();
        }
        finally
        {
            refreshing = false;
            OnPropertyChanged(nameof(IsRefreshing));
        }
    }

    public void SelectChargePreset(int preset)
    {
        if (preset is < 0 or > 2 || preset == selectedChargePreset) return;
        selectedChargePreset = preset;
        OnPropertyChanged(nameof(SelectedChargePresetIndex));
        OnPropertyChanged(nameof(ChargeModeHint));
        OnPropertyChanged(nameof(ChargeHintVisibility));
        OnPropertyChanged(nameof(CanApplySelectedChargeMode));
    }

    public void SelectPerformanceMode(int mode)
    {
        if (mode is not (1 or 2) || mode == 2 && !CanSelectHighPerformance) return;
        selectedPerformanceMode = mode;
        OnPropertyChanged(nameof(SelectedPerformanceMode));
        OnPropertyChanged(nameof(SelectedPerformanceModeLabel));
        OnPropertyChanged(nameof(CanApplyPerformance));
        OnPropertyChanged(nameof(PerformanceRequirementText));
        OnPropertyChanged(nameof(PerformanceHintVisibility));
    }

    public async Task ApplySelectedChargeModeAsync()
    {
        if (!CanApplySelectedChargeMode) return;
        int start = selectedChargePreset == 1 ? 40 : selectedChargePreset == 2 ? 0 : int.Parse(customChargeStart);
        int end = selectedChargePreset == 1 ? 70 : selectedChargePreset == 2 ? 100 : int.Parse(customChargeEnd);
        await SaveAsync(() => service.SaveChargeAsync(start, end), "充电配置已保存");
    }

    public async Task SetPerformanceModeAsync()
    {
        if (!CanApplyPerformance) return;
        await SaveAsync(() => service.SavePerformanceAsync(selectedPerformanceMode), "性能配置已保存");
    }

    public async Task SetAutoReconcileAsync(bool enabled) =>
        await SaveAsync(() => service.SetAutoReconcileAsync(enabled), "开机维护设置已保存");

    public void SetCancelledStatus() => SetOperation("已取消", "配置没有改变。", InfoBarSeverity.Informational);

    private async Task SaveAsync(Func<Task<ServiceSnapshot>> save, string title)
    {
        if (busy) return;
        busy = true;
        RaiseState();
        try
        {
            ServiceSnapshot updated = await save();
            connectionError = null;
            ApplySnapshot(updated, true);
            SetOperation(title, updated.Actual.PcManagerOpen
                ? "电脑管家运行中，服务只读；配置将在其关闭后应用。"
                : "后台服务已收到配置，将回读并校正实际状态。", InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            SetOperation("配置保存失败", exception.Message, InfoBarSeverity.Error);
        }
        finally { busy = false; RaiseState(); }
    }

    private void ApplySnapshot(ServiceSnapshot latest, bool preserveSelection)
    {
        snapshot = latest;
        if (!preserveSelection)
        {
            int start = latest.Desired.ChargeStart ?? latest.Actual.ChargeStart ?? 40;
            int end = latest.Desired.ChargeEnd ?? latest.Actual.ChargeEnd ?? 70;
            customChargeStart = start.ToString();
            customChargeEnd = end.ToString();
            selectedChargePreset = start == 40 && end == 70 ? 1 : start == 0 && end == 100 ? 2 : 0;
            selectedPerformanceMode = latest.Desired.PerformanceMode ?? latest.Actual.PerformanceMode ?? 1;
        }
        RaiseState();
    }

    private bool TryGetCustomThreshold(out int start, out int end)
    {
        bool startParsed = int.TryParse(customChargeStart, out start);
        bool endParsed = int.TryParse(customChargeEnd, out end);
        return startParsed && endParsed && start is >= 0 and <= 100
            && end is >= 0 and <= 100 && start < end;
    }

    private void SetOperation(string title, string message, InfoBarSeverity severity)
    {
        operationTitle = title;
        operationStatus = message;
        operationSeverity = severity;
        hasOperationStatus = true;
        OnPropertyChanged(nameof(OperationTitle));
        OnPropertyChanged(nameof(OperationStatus));
        OnPropertyChanged(nameof(OperationSeverity));
        OnPropertyChanged(nameof(HasOperationStatus));
    }

    private void RaiseState()
    {
        foreach (string name in new[]
        {
            nameof(IsBusy), nameof(ConnectionTitle), nameof(ConnectionSeverity), nameof(DeviceSummary),
            nameof(BatteryProgress), nameof(BatteryKnownVisibility), nameof(BatteryUnknownVisibility),
            nameof(BatteryPercentText), nameof(PowerStateText), nameof(BatteryAccessibleText),
            nameof(ActualChargeStart), nameof(ActualChargeEnd), nameof(ChargeRangeText),
            nameof(ChargeUnavailableText), nameof(ChargeUnavailableVisibility), nameof(ChargeAccessibleText),
            nameof(ActualSmartVisibility), nameof(ActualHighVisibility), nameof(ActualSmartOpacity), nameof(ActualHighOpacity), nameof(ActualPerformanceModeName),
            nameof(ActualPowerPlanText), nameof(PerformanceReadoutError), nameof(PerformanceErrorVisibility), nameof(PerformanceAccessibleText),
            nameof(ChargeDesiredStatus), nameof(PerformanceDesiredStatus),
            nameof(PerformanceRequirementText), nameof(PerformanceHintVisibility), nameof(ChargeHintVisibility),
            nameof(DiagnosticDetails), nameof(AutoReconcileEnabled),
            nameof(AutoReconcileHint), nameof(IsServiceAvailable), nameof(CanConfigureCharge), nameof(CanApplySelectedChargeMode),
            nameof(CanConfigurePerformance), nameof(CanSelectHighPerformance), nameof(CanApplyPerformance),
            nameof(CustomChargeStart), nameof(CustomChargeEnd), nameof(SelectedChargePresetIndex),
            nameof(SelectedPerformanceMode), nameof(SelectedPerformanceModeLabel)
        }) OnPropertyChanged(name);
    }
}
