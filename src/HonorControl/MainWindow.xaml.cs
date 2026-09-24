using System.ComponentModel;
using System.Globalization;
using HonorControl.Services;
using HonorControl.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using WinRT.Interop;
using Color = Windows.UI.Color;

namespace HonorControl;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly AppSettingsService settingsService = new();
    private readonly AppWindow appWindow;
    private readonly TrayIconController trayIcon;
    private bool themeSelectorIsLoading;
    private bool settingsAreLoading;
    private bool controlSelectionIsSyncing;
    private bool closeToTray;
    private bool hasShownTrayHint;
    private bool explicitExit;

    public MainWindow()
    {
        InitializeComponent();
        if (NavigationHost.SettingsItem is NavigationViewItem settingsItem) settingsItem.Content = "设置";

        Title = "Honor Control";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();

        IntPtr windowHandle = WindowNative.GetWindowHandle(this);
        AppDiagnostics.Write($"[window] Native window created. Hwnd=0x{windowHandle.ToInt64():X}.");
        appWindow = ConfigureWindow(windowHandle);
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "HonorControl.ico");
        trayIcon = new TrayIconController(windowHandle, DispatcherQueue, iconPath);
        trayIcon.OpenRequested += ShowFromTray;
        trayIcon.ExitRequested += ExitApplication;
        appWindow.Closing += AppWindow_Closing;
        Closed += MainWindow_Closed;

        viewModel = new MainViewModel();
        AppRoot.DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        AppRoot.ActualThemeChanged += AppRoot_ActualThemeChanged;
        AppRoot.Loaded += AppRoot_Loaded;
    }

    private AppWindow ConfigureWindow(IntPtr windowHandle)
    {
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        AppWindow configuredWindow = AppWindow.GetFromWindowId(windowId);
        RectInt32 workArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min(1080, Math.Max(760, workArea.Width - 96));
        int height = Math.Min(760, Math.Max(600, workArea.Height - 96));
        int x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        int y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
        configuredWindow.MoveAndResize(new RectInt32(x, y, width, height));
        configuredWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        configuredWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        return configuredWindow;
    }

    private async void AppRoot_Loaded(object sender, RoutedEventArgs e)
    {
        AppDiagnostics.Write("[window] Root loaded.");
        LoadPreferences();
        SyncControlsFromViewModel();
        UpdateAlertVisibility();
        await viewModel.RefreshAsync();
        SyncControlsFromViewModel();
        UpdateAlertVisibility();
        AppDiagnostics.Write("[window] Initial device refresh completed.");
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ConnectionSeverity)
            or nameof(MainViewModel.OperationSeverity)
            or nameof(MainViewModel.HasOperationStatus))
        {
            UpdateAlertVisibility();
        }

        if (e.PropertyName is nameof(MainViewModel.CustomChargeStart)
            or nameof(MainViewModel.CustomChargeEnd)
            or nameof(MainViewModel.SelectedChargePresetIndex)
            or nameof(MainViewModel.SelectedPerformanceMode))
        {
            SyncControlsFromViewModel();
        }
    }

    private void UpdateAlertVisibility()
    {
        ConnectionAlert.IsOpen = viewModel.ConnectionSeverity is InfoBarSeverity.Warning or InfoBarSeverity.Error;
        OperationAlert.IsOpen = viewModel.HasOperationStatus;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await viewModel.RefreshAsync();

    private async void ApplySelectedChargeMode_Click(object sender, RoutedEventArgs e) => await viewModel.ApplySelectedChargeModeAsync();

    private void ChargeModeOption_Checked(object sender, RoutedEventArgs e)
    {
        if (controlSelectionIsSyncing || viewModel is null || sender is not RadioButton { Tag: string tag }) return;
        if (int.TryParse(tag, out int preset)) viewModel.SelectChargePreset(preset);
    }

    private void PerformanceModeOption_Checked(object sender, RoutedEventArgs e)
    {
        if (controlSelectionIsSyncing || viewModel is null || sender is not RadioButton { Tag: string tag }) return;
        if (int.TryParse(tag, out int mode)) viewModel.SelectPerformanceMode(mode);
    }

    private void CustomChargeNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (controlSelectionIsSyncing || viewModel is null) return;
        string value = double.IsNaN(args.NewValue)
            ? string.Empty
            : args.NewValue.ToString("0.################", CultureInfo.InvariantCulture);
        if (ReferenceEquals(sender, CustomChargeStartBox)) viewModel.CustomChargeStart = value;
        if (ReferenceEquals(sender, CustomChargeEndBox)) viewModel.CustomChargeEnd = value;
    }

    private void SyncControlsFromViewModel()
    {
        if (viewModel is null) return;
        controlSelectionIsSyncing = true;
        try
        {
            ChargeProtectOption.IsChecked = viewModel.SelectedChargePresetIndex == 0;
            ChargeFullOption.IsChecked = viewModel.SelectedChargePresetIndex == 1;
            bool customChargeSelected = viewModel.SelectedChargePresetIndex == -1;
            ChargeCustomOption.IsChecked = customChargeSelected;
            CustomChargeEditor.Visibility = customChargeSelected ? Visibility.Visible : Visibility.Collapsed;
            SmartPerformanceOption.IsChecked = viewModel.SelectedPerformanceMode == 1;
            HighPerformanceOption.IsChecked = viewModel.SelectedPerformanceMode == 2;
            CustomChargeStartBox.Value = ParseNumberBoxValue(viewModel.CustomChargeStart);
            CustomChargeEndBox.Value = ParseNumberBoxValue(viewModel.CustomChargeEnd);
        }
        finally
        {
            controlSelectionIsSyncing = false;
        }
    }

    private static double ParseNumberBoxValue(string value) =>
        double.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out double result) ? result : double.NaN;

    private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavigateTo("settings");
            return;
        }
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string destination)
        {
            NavigateTo(destination);
        }
    }

    private void NavigateTo(string destination)
    {
        ChargeView.Visibility = destination == "charge" ? Visibility.Visible : Visibility.Collapsed;
        PerformanceView.Visibility = destination == "performance" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = destination == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ApplyPerformanceMode_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = AppRoot.XamlRoot,
            Title = "应用" + viewModel.SelectedPerformanceModeLabel,
            Content = viewModel.SelectedPerformanceMode == 2
                ? "将把固件切换到高能模式，并启用 Honor Performance 电源方案。功耗、温度和风扇噪声可能明显上升。"
                : "将把固件切换到智能模式，并恢复 Windows 平衡电源方案。",
            PrimaryButtonText = "继续应用",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await viewModel.SetPerformanceModeAsync();
        }
        else
        {
            viewModel.SetCancelledStatus();
        }
    }

    private async void ShowDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        TextBox details = new()
        {
            Text = viewModel.DiagnosticDetails,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            MinWidth = 560,
            Height = 340
        };
        ContentDialog dialog = new()
        {
            XamlRoot = AppRoot.XamlRoot,
            Title = "诊断信息",
            Content = details,
            PrimaryButtonText = "复制",
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            CopyText(viewModel.DiagnosticDetails);
        }
    }

    private static void CopyText(string text)
    {
        DataPackage package = new();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private void LoadPreferences()
    {
        AppSettingsSnapshot settings = settingsService.Load();
        ApplyThemePreference(settings.Theme, false);
        hasShownTrayHint = settings.HasShownTrayHint;

        settingsAreLoading = true;
        closeToTray = trayIcon.IsAvailable && settings.CloseToTray;
        CloseToTrayToggle.IsOn = closeToTray;
        CloseToTrayToggle.IsEnabled = trayIcon.IsAvailable;
        settingsAreLoading = false;

        if (!trayIcon.IsAvailable)
        {
            TraySettingsHint.Text = "系统托盘不可用；关闭窗口将直接退出。";
            if (!string.IsNullOrWhiteSpace(trayIcon.LastError)) ToolTipService.SetToolTip(TraySettingsHint, trayIcon.LastError);
        }
        else if (!string.IsNullOrWhiteSpace(settingsService.LastError))
        {
            TraySettingsHint.Text = "无法读取已保存的设置：" + settingsService.LastError;
        }
        else if (!string.IsNullOrWhiteSpace(trayIcon.LastError))
        {
            TraySettingsHint.Text = "系统托盘可用，但任务栏重启后可能需要重新启动应用。";
            ToolTipService.SetToolTip(TraySettingsHint, trayIcon.LastError);
        }
        else
        {
            TraySettingsHint.Text = string.Empty;
        }
    }

    private void CloseToTrayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (settingsAreLoading) return;
        closeToTray = trayIcon.IsAvailable && CloseToTrayToggle.IsOn;
        settingsService.SaveCloseToTray(closeToTray);
        TraySettingsHint.Text = string.IsNullOrWhiteSpace(settingsService.LastError)
            ? string.Empty
            : "设置保存失败：" + settingsService.LastError;
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        AppDiagnostics.Write($"[window] Closing requested. ExplicitExit={explicitExit}; SystemEnding={trayIcon.SystemEnding}; CloseToTray={closeToTray}; TrayAvailable={trayIcon.IsAvailable}.");
        if (explicitExit || trayIcon.SystemEnding || !closeToTray || !trayIcon.IsAvailable)
        {
            explicitExit = true;
            return;
        }

        args.Cancel = true;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            if (explicitExit) return;
            sender.Hide();
            AppDiagnostics.Write("[window] Hidden to system tray.");
        }))
        {
            args.Cancel = false;
            explicitExit = true;
            AppDiagnostics.Write("[window] Failed to queue tray hide; allowing the application to close.");
            return;
        }
        if (!hasShownTrayHint)
        {
            trayIcon.ShowNotification("Honor Control 仍在运行", "双击托盘图标可重新打开，右键菜单可以完全退出。");
            hasShownTrayHint = true;
            settingsService.MarkTrayHintShown();
        }
    }

    private void ShowFromTray()
    {
        AppDiagnostics.Write("[window] Restore requested from system tray.");
        if (appWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }
        appWindow.Show();
        Activate();
    }

    private void ExitApplication()
    {
        if (explicitExit) return;
        explicitExit = true;
        AppDiagnostics.Write("[window] Explicit exit requested from system tray.");
        trayIcon.Dispose();
        Application.Current.Exit();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        AppDiagnostics.Write("[window] Native window closed; exiting application.");
        explicitExit = true;
        trayIcon.Dispose();
        Application.Current.Exit();
    }

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (themeSelectorIsLoading || ThemeSelector.SelectedItem is not ComboBoxItem item || item.Tag is not string value) return;
        if (!Enum.TryParse(value, true, out AppThemePreference preference)) return;
        ApplyThemePreference(preference, true);
    }

    private void ApplyThemePreference(AppThemePreference preference, bool save)
    {
        themeSelectorIsLoading = true;
        ThemeSelector.SelectedIndex = preference switch
        {
            AppThemePreference.Light => 1,
            AppThemePreference.Dark => 2,
            _ => 0
        };
        themeSelectorIsLoading = false;

        AppRoot.RequestedTheme = preference switch
        {
            AppThemePreference.Light => ElementTheme.Light,
            AppThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        UpdateTitleBarColors(AppRoot.ActualTheme);

        if (save)
        {
            settingsService.SaveTheme(preference);
            ThemeSettingsHint.Text = string.IsNullOrWhiteSpace(settingsService.LastError)
                ? "颜色会立即应用。"
                : "主题已应用，但保存失败：" + settingsService.LastError;
        }
        else if (!string.IsNullOrWhiteSpace(settingsService.LastError))
        {
            ThemeSettingsHint.Text = "无法读取主题设置：" + settingsService.LastError;
        }
    }

    private void AppRoot_ActualThemeChanged(FrameworkElement sender, object args) => UpdateTitleBarColors(AppRoot.ActualTheme);

    private void UpdateTitleBarColors(ElementTheme theme)
    {
        Color foreground = theme == ElementTheme.Dark ? Colors.White : Colors.Black;
        appWindow.TitleBar.ButtonForegroundColor = foreground;
        appWindow.TitleBar.ButtonInactiveForegroundColor = foreground;
        appWindow.TitleBar.ButtonHoverBackgroundColor = theme == ElementTheme.Dark
            ? Color.FromArgb(32, 255, 255, 255)
            : Color.FromArgb(20, 0, 0, 0);
        appWindow.TitleBar.ButtonPressedBackgroundColor = theme == ElementTheme.Dark
            ? Color.FromArgb(48, 255, 255, 255)
            : Color.FromArgb(32, 0, 0, 0);
    }
}
