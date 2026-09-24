using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using WinRT.Interop;
using HonorControl.ViewModels;
using Color = Windows.UI.Color;

namespace HonorControl;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();
        Title = "荣耀控制中心";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        ConfigureWindow();

        viewModel = new MainViewModel();
        AppRoot.DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        AppRoot.Loaded += async (_, _) =>
        {
            ThemeSwitch.IsOn = AppRoot.ActualTheme == ElementTheme.Dark;
            UpdateTitleBarColors(AppRoot.ActualTheme);
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
    private async void EnableSmartCharge_Click(object sender, RoutedEventArgs e) => await viewModel.SetSmartChargeAsync();
    private async void DisableSmartCharge_Click(object sender, RoutedEventArgs e) => await viewModel.DisableChargeLimitAsync();
    private async void ApplyCustomCharge_Click(object sender, RoutedEventArgs e) => await viewModel.SetCustomChargeAsync();

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
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string destination) return;
        bool showDiagnostics = destination == "diagnostics";
        OverviewView.Visibility = showDiagnostics ? Visibility.Collapsed : Visibility.Visible;
        DiagnosticsView.Visibility = showDiagnostics ? Visibility.Visible : Visibility.Collapsed;
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

    private void ThemeSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        ElementTheme theme = ThemeSwitch.IsOn ? ElementTheme.Dark : ElementTheme.Light;
        AppRoot.RequestedTheme = theme;
        UpdateTitleBarColors(theme);
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
