using HonorControl.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "HonorControlService")
    .ConfigureServices(services =>
    {
        services.AddSingleton<ConfigurationStore>();
        services.AddSingleton<ReconciliationCoordinator>();
        services.AddHostedService(provider => provider.GetRequiredService<ReconciliationCoordinator>());
        services.AddHostedService<PipeServer>();
    })
    .Build()
    .Run();
