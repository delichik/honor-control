using System.ComponentModel;
using HonorControl.Services;
using HonorControl.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using WinRT.Interop;
using Color = Windows.UI.Color;

namespace HonorControl;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly AppSettingsService settingsService = new();
    private readonly AppWindow appWindow;
    private readonly TrayIconController trayIcon;
    private readonly DispatcherTimer autoRefreshTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private Storyboard? pageTransition;
    private FrameworkElement? currentPage;
    private int? renderedChargeStart = -1;
    private int? renderedChargeEnd = -1;
    private bool? overviewCardsStacked;
    private bool? powerCardsStacked;
    private bool themeSelectorIsLoading;
    private bool settingsAreLoading;
    private bool autoReconcileLoading;
    private bool controlSelectionIsSyncing;
    private bool closeToTray;
    private bool hasShownTrayHint;
    private bool explicitExit;
    private bool rootLoaded;
    private bool isWindowVisible = true;

    public MainWindow()
    {
        InitializeComponent();

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
        PopulateCustomChargeOptions();
        AppRoot.DataContext = viewModel;
        currentPage = OverviewView;
        MainNavigation.SelectedItem = OverviewNavigationItem;
        if (MainNavigation.SettingsItem is NavigationViewItem settingsItem) settingsItem.Content = "设置";
        UpdateChargeTrack();
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        AppRoot.ActualThemeChanged += AppRoot_ActualThemeChanged;
        AppRoot.Loaded += AppRoot_Loaded;
        Activated += MainWindow_Activated;
        autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
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
        SyncAutoReconcileToggle();
        SyncControlsFromViewModel();
        UpdateAlertVisibility();
        rootLoaded = true;
        autoRefreshTimer.Start();
        AppDiagnostics.Write("[window] Initial device refresh completed.");
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ConnectionSeverity))
            ConnectionAlert.IsOpen = viewModel.ConnectionSeverity is InfoBarSeverity.Warning or InfoBarSeverity.Error;
        if (e.PropertyName is nameof(MainViewModel.OperationSeverity) or nameof(MainViewModel.HasOperationStatus))
            OperationAlert.IsOpen = viewModel.HasOperationStatus;

        if (e.PropertyName is nameof(MainViewModel.CustomChargeStart)
            or nameof(MainViewModel.CustomChargeEnd)
            or nameof(MainViewModel.SelectedChargePresetIndex)
            or nameof(MainViewModel.SelectedPerformanceMode))
        {
            SyncControlsFromViewModel();
        }
        if (e.PropertyName == nameof(MainViewModel.AutoReconcileEnabled)) SyncAutoReconcileToggle();
        if (e.PropertyName is nameof(MainViewModel.ActualChargeStart) or nameof(MainViewModel.ActualChargeEnd)) UpdateChargeTrack();
    }

    private void UpdateAlertVisibility()
    {
        ConnectionAlert.IsOpen = viewModel.ConnectionSeverity is InfoBarSeverity.Warning or InfoBarSeverity.Error;
        OperationAlert.IsOpen = viewModel.HasOperationStatus;
    }

    private async void AutoRefreshTimer_Tick(object? sender, object e)
    {
        if (!rootLoaded || !isWindowVisible || viewModel.IsBusy || viewModel.IsRefreshing
            || appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }) return;
        await viewModel.RefreshAsync(true);
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (!rootLoaded || !isWindowVisible || viewModel.IsBusy || viewModel.IsRefreshing || args.WindowActivationState == WindowActivationState.Deactivated) return;
        await viewModel.RefreshAsync(true);
    }

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

    private void PopulateCustomChargeOptions()
    {
        for (int value = 0; value <= 100; value++)
        {
            CustomChargeStartBox.Items.Add(new ComboBoxItem { Content = $"{value}%" });
            CustomChargeEndBox.Items.Add(new ComboBoxItem { Content = $"{value}%" });
        }
    }

    private void CustomChargeSelection_Changed(object sender, SelectionChangedEventArgs args)
    {
        if (controlSelectionIsSyncing || viewModel is null || sender is not ComboBox selector || selector.SelectedIndex < 0) return;
        if (ReferenceEquals(selector, CustomChargeStartBox)) viewModel.CustomChargeStart = selector.SelectedIndex.ToString();
        if (ReferenceEquals(selector, CustomChargeEndBox)) viewModel.CustomChargeEnd = selector.SelectedIndex.ToString();
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
            CustomChargeStartBox.SelectedIndex = ParseChargePercent(viewModel.CustomChargeStart);
            CustomChargeEndBox.SelectedIndex = ParseChargePercent(viewModel.CustomChargeEnd);
            int start = CustomChargeStartBox.SelectedIndex;
            int end = CustomChargeEndBox.SelectedIndex;
            for (int value = 0; value <= 100; value++)
            {
                ((ComboBoxItem)CustomChargeStartBox.Items[value]).IsEnabled = end < 0 || value < end;
                ((ComboBoxItem)CustomChargeEndBox.Items[value]).IsEnabled = start < 0 || value > start;
            }
        }
        finally
        {
            controlSelectionIsSyncing = false;
        }
    }

    private static int ParseChargePercent(string value) =>
        int.TryParse(value, out int result) && result is >= 0 and <= 100 ? result : -1;

    private void PageViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Bound the page to the actual viewport before centering it.
        // MaxWidth alone can leave the scroll content arranged beyond the window.
        if (sender is ScrollViewer { Content: Grid { Children.Count: > 0 } container }
            && container.Children[0] is FrameworkElement page)
        {
            page.Width = Math.Min(1000, Math.Max(0, e.NewSize.Width));
        }
    }

    private void OverviewStatusGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool stacked = e.NewSize.Width < 650;
        if (overviewCardsStacked == stacked) return;
        overviewCardsStacked = stacked;
        OverviewStatusGrid.RowSpacing = stacked ? 14 : 0;
        Grid.SetColumnSpan(BatteryStatusCard, stacked ? 3 : 1);
        Grid.SetColumn(ChargeStatusCard, stacked ? 0 : 1);
        Grid.SetRow(ChargeStatusCard, stacked ? 1 : 0);
        Grid.SetColumnSpan(ChargeStatusCard, stacked ? 3 : 1);
        Grid.SetColumn(PerformanceStatusCard, stacked ? 0 : 2);
        Grid.SetRow(PerformanceStatusCard, stacked ? 2 : 0);
        Grid.SetColumnSpan(PerformanceStatusCard, stacked ? 3 : 1);
    }

    private void PowerSettingsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool stacked = e.NewSize.Width < 650;
        if (powerCardsStacked == stacked) return;
        powerCardsStacked = stacked;
        PowerSettingsGrid.RowSpacing = stacked ? 16 : 0;
        Grid.SetColumnSpan(ChargeSettingsCard, stacked ? 2 : 1);
        Grid.SetColumn(PerformanceSettingsCard, stacked ? 0 : 1);
        Grid.SetRow(PerformanceSettingsCard, stacked ? 1 : 0);
        Grid.SetColumnSpan(PerformanceSettingsCard, stacked ? 2 : 1);
    }

    private void MainNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (currentPage is null) return;
        FrameworkElement next = args.IsSettingsSelected ? SettingsView
            : ReferenceEquals(args.SelectedItem, PowerNavigationItem) ? PowerSettingsView : OverviewView;
        if (ReferenceEquals(currentPage, next)) return;
        ShowPage(currentPage, next);
        currentPage = next;
    }

    private void UpdateChargeTrack()
    {
        int? start = viewModel.ActualChargeStart;
        int? end = viewModel.ActualChargeEnd;
        if (start == renderedChargeStart && end == renderedChargeEnd) return;
        renderedChargeStart = start;
        renderedChargeEnd = end;
        ChargeRangeFill.Visibility = start.HasValue && end.HasValue ? Visibility.Visible : Visibility.Collapsed;
        ChargeTrackSegments.ColumnDefinitions[0].Width = new GridLength(start ?? 0, GridUnitType.Star);
        ChargeTrackSegments.ColumnDefinitions[1].Width = new GridLength(start.HasValue && end.HasValue ? end.Value - start.Value : 0, GridUnitType.Star);
        ChargeTrackSegments.ColumnDefinitions[2].Width = new GridLength(end.HasValue ? 100 - end.Value : 100, GridUnitType.Star);
    }

    private void ShowPage(FrameworkElement current, FrameworkElement next)
    {
        pageTransition?.Stop();
        current.Opacity = 1;
        if (current.RenderTransform is TranslateTransform previousTranslation) previousTranslation.Y = 0;
        current.Visibility = Visibility.Collapsed;
        next.Visibility = Visibility.Visible;
        TranslateTransform translation = next.RenderTransform as TranslateTransform ?? new TranslateTransform();
        next.RenderTransform = translation;
        next.Opacity = 1;
        translation.Y = 0;
        if (!new UISettings().AnimationsEnabled) return;

        CubicEase easing = new() { EasingMode = EasingMode.EaseOut };
        DoubleAnimation fade = new()
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = easing
        };
        Storyboard.SetTarget(fade, next);
        Storyboard.SetTargetProperty(fade, "Opacity");

        DoubleAnimation slide = new()
        {
            From = 10,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(slide, translation);
        Storyboard.SetTargetProperty(slide, "Y");

        pageTransition = new Storyboard();
        pageTransition.Children.Add(fade);
        pageTransition.Children.Add(slide);
        pageTransition.Begin();
    }

    private async void ApplyPerformanceMode_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = AppRoot.XamlRoot,
            Title = "保存" + viewModel.SelectedPerformanceModeLabel + "配置",
            Content = viewModel.SelectedPerformanceMode == 2
                ? "后台服务将在条件满足且荣耀电脑管家未运行时同步固件与 Honor Performance 电源方案。功耗、温度和噪声可能上升。"
                : "后台服务将在条件满足且荣耀电脑管家未运行时同步固件与 Windows 平衡电源方案。",
            PrimaryButtonText = "保存配置",
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

    private void SyncAutoReconcileToggle()
    {
        autoReconcileLoading = true;
        AutoReconcileToggle.IsOn = viewModel.AutoReconcileEnabled;
        autoReconcileLoading = false;
    }

    private async void AutoReconcileToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (autoReconcileLoading || viewModel is null || !viewModel.IsServiceAvailable) return;
        await viewModel.SetAutoReconcileAsync(AutoReconcileToggle.IsOn);
        SyncAutoReconcileToggle();
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
            isWindowVisible = false;
            autoRefreshTimer.Stop();
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
        isWindowVisible = true;
        autoRefreshTimer.Start();
        appWindow.Show();
        Activate();
    }

    public void ShowFromExternalRequest() => ShowFromTray();

    private void ExitApplication()
    {
        if (explicitExit) return;
        explicitExit = true;
        autoRefreshTimer.Stop();
        AppDiagnostics.Write("[window] Explicit exit requested from system tray.");
        trayIcon.Dispose();
        Application.Current.Exit();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        AppDiagnostics.Write("[window] Native window closed; exiting application.");
        explicitExit = true;
        autoRefreshTimer.Stop();
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
                ? string.Empty
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
