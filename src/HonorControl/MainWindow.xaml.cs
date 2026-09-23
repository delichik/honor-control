using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;
using HonorControl.ViewModels;

namespace HonorControl;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();
        Title = "荣耀控制中心";
        SystemBackdrop = new MicaBackdrop();
        ConfigureWindowSize();

        viewModel = new MainViewModel();
        RootGrid.DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.RefreshAsync();
    }

    private void ConfigureWindowSize()
    {
        IntPtr handle = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(handle);
        AppWindow.GetFromWindowId(windowId).Resize(new SizeInt32(980, 760));
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await viewModel.RefreshAsync();
    private async void EnableSmartCharge_Click(object sender, RoutedEventArgs e) => await viewModel.SetSmartChargeAsync();
    private async void DisableSmartCharge_Click(object sender, RoutedEventArgs e) => await viewModel.DisableChargeLimitAsync();
    private async void ApplyCustomCharge_Click(object sender, RoutedEventArgs e) => await viewModel.SetCustomChargeAsync();

    private void PerformanceModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (viewModel is null || PerformanceModeSelector.SelectedItem is not RadioButton option) return;
        viewModel.SelectPerformanceMode(Convert.ToInt32(option.Tag));
    }

    private async void ApplyPerformanceMode_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "确认性能模式写入",
            Content = "将写入 " + viewModel.SelectedPerformanceModeLabel + "。该 BIOS SET 命令尚未写入实测，是否继续？",
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
        RootGrid.RequestedTheme = ThemeSwitch.IsOn ? ElementTheme.Dark : ElementTheme.Light;
    }
}
