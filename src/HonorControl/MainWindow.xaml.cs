using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using WinRT.Interop;
using HonorControl.Services;
using HonorControl.ViewModels;
using Color = Windows.UI.Color;

namespace HonorControl;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly AppSettingsService settingsService = new();
    private bool themeSelectorIsLoading;

    public MainWindow()
    {
        InitializeComponent();
        if (NavigationHost.SettingsItem is NavigationViewItem settingsItem) settingsItem.Content = "设置";
        Title = "荣耀控制中心";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        ConfigureWindow();

        viewModel = new MainViewModel();
        AppRoot.DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        AppRoot.ActualThemeChanged += AppRoot_ActualThemeChanged;
        AppRoot.Loaded += async (_, _) =>
        {
            LoadThemePreference();
            UpdateAlertVisibility();
            await viewModel.RefreshAsync();
            UpdateAlertVisibility();
        };
    }

    private void ConfigureWindow()
    {
        IntPtr handle = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(handle);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
        RectInt32 workArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min(1180, Math.Max(720, workArea.Width - 64));
        int height = Math.Min(780, Math.Max(560, workArea.Height - 64));
        appWindow.Resize(new SizeInt32(width, height));
        appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ConnectionSeverity) or nameof(MainViewModel.OperationSeverity))
        {
            UpdateAlertVisibility();
        }
    }

    private void UpdateAlertVisibility()
    {
        ConnectionAlert.IsOpen = viewModel.ConnectionSeverity is InfoBarSeverity.Warning or InfoBarSeverity.Error;
        OperationAlert.IsOpen = viewModel.OperationSeverity is InfoBarSeverity.Warning or InfoBarSeverity.Error;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await viewModel.RefreshAsync();
    private async void ApplyCustomCharge_Click(object sender, RoutedEventArgs e) => await viewModel.SetCustomChargeAsync();
    private async void ApplySelectedChargePreset_Click(object sender, RoutedEventArgs e) => await viewModel.ApplySelectedChargePresetAsync();

    private void ChargeModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (viewModel is null || ChargeModeSelector.SelectedIndex < 0) return;
        viewModel.SelectChargePreset(ChargeModeSelector.SelectedIndex + 1);
    }

    private void OpenCharge_Click(object sender, RoutedEventArgs e) => NavigationHost.SelectedItem = ChargeNavigationItem;
    private void OpenPerformance_Click(object sender, RoutedEventArgs e) => NavigationHost.SelectedItem = PerformanceNavigationItem;

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        try
        {
            DataPackage package = new();
            package.SetText(viewModel.DiagnosticDetails);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            button.Content = "已复制";
        }
        catch (Exception exception)
        {
            button.Content = "复制失败";
            ToolTipService.SetToolTip(button, exception.Message);
        }
    }

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
        OverviewView.Visibility = destination == "overview" ? Visibility.Visible : Visibility.Collapsed;
        ChargeView.Visibility = destination == "charge" ? Visibility.Visible : Visibility.Collapsed;
        PerformanceView.Visibility = destination == "performance" ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsView.Visibility = destination == "diagnostics" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = destination == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PerformanceModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (viewModel is null || PerformanceModeSelector.SelectedItem is not RadioButton option) return;
        viewModel.SelectPerformanceMode(Convert.ToInt32(option.Tag));
    }

    private async void ApplyPerformanceMode_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = AppRoot.XamlRoot,
            Title = "确认性能模式写入",
            Content = "将写入 " + viewModel.SelectedPerformanceModeLabel + "。该 BIOS SET 命令仍属实验性操作，是否继续？",
            PrimaryButtonText = "继续写入",
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

    private void LoadThemePreference()
    {
        ApplyThemePreference(settingsService.LoadTheme(), false);
        if (!string.IsNullOrWhiteSpace(settingsService.LastError))
        {
            ThemeSettingsHint.Text = "无法读取保存的主题偏好：" + settingsService.LastError;
        }
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
                ? "主题偏好已保存，并立即应用。"
                : "主题已应用，但保存失败：" + settingsService.LastError;
        }
    }

    private void AppRoot_ActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdateTitleBarColors(AppRoot.ActualTheme);
    }

    private void UpdateTitleBarColors(ElementTheme theme)
    {
        IntPtr handle = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(handle);
        AppWindowTitleBar titleBar = AppWindow.GetFromWindowId(windowId).TitleBar;
        Color foreground = theme == ElementTheme.Dark ? Colors.White : Colors.Black;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = theme == ElementTheme.Dark
            ? Color.FromArgb(32, 255, 255, 255)
            : Color.FromArgb(20, 0, 0, 0);
        titleBar.ButtonPressedBackgroundColor = theme == ElementTheme.Dark
            ? Color.FromArgb(48, 255, 255, 255)
            : Color.FromArgb(32, 0, 0, 0);
    }
}
