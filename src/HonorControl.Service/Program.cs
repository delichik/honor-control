using HonorControl.Service;
using HonorControl.Service.Telemetry;
using HonorControl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "HonorControlService")
    .ConfigureServices(services =>
    {
        services.AddSingleton<ConfigurationStore>();
        services.AddSingleton<WindowsPowerSettingsService>();
        services.AddSingleton<ReconciliationCoordinator>();
        services.AddHostedService(provider => provider.GetRequiredService<ReconciliationCoordinator>());

        // 遥测采样器与历史存储：面板的实时读数与监控曲线都从这里来。
        // 采样在后台跑，客户端轮询只读缓存，不会因为面板刷新而打 WMI。
        services.AddSingleton<HistoryStore>();
        services.AddSingleton<TelemetrySampler>();
        services.AddHostedService(provider => provider.GetRequiredService<TelemetrySampler>());

        services.AddHostedService<PipeServer>();
    })
    .Build()
    .Run();
