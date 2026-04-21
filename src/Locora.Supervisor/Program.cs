using Locora.Infrastructure.Hosting;
using Locora.Infrastructure.Services;
using Locora.Supervisor.Configuration;
using Locora.Supervisor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using var singleInstanceLease = SingleInstanceLease.Acquire("Locora.Supervisor", TimeSpan.FromSeconds(15));
if (!singleInstanceLease.OwnsMutex)
{
    UserFacingAlerts.ShowDuplicateInstance("Locora Supervisor", singleInstanceLease.RootPath, singleInstanceLease.TimedOut);
    return;
}

var host = LocoraHostBuilder.BuildHost(
    "supervisor",
    (context, services) =>
    {
        services.AddOptions<SupervisorSettings>()
            .Bind(context.Configuration.GetSection(SupervisorSettings.SectionName));
        services.AddOptions<ManagedServicesOptions>()
            .Bind(context.Configuration.GetSection(ManagedServicesOptions.SectionName));
        services.AddOptions<ProjectDiscoveryOptions>()
            .Bind(context.Configuration.GetSection(ProjectDiscoveryOptions.SectionName));
        services.AddSingleton<PortProbe>();
        services.AddSingleton<ManagedServiceFactory>();
        services.AddSingleton<ManagedServiceRegistry>();
        services.AddSingleton<ServiceConfigurationWriter>();
        services.AddSingleton<ProjectDiscoveryService>();
        services.AddSingleton<LocalSslService>();
        services.AddSingleton<ProjectConfigurationWriter>();
        services.AddSingleton<HostsFileManager>();
        services.AddSingleton<ElevationService>();
        services.AddSingleton<SupervisorPrivilegeService>();
        services.AddSingleton<PortDiagnosticsService>();
        services.AddSingleton<PermissionDiagnosticsService>();
        services.AddSingleton<NginxConfigValidator>();
        services.AddSingleton<RuntimeRepairService>();
        services.AddSingleton<SupervisorStateStore>();
        services.AddHostedService<SupervisorConfigBootstrapper>();
        services.AddHostedService<ProjectConfigBootstrapper>();
        services.AddHostedService<SupervisorRuntimeBootstrapper>();
        services.AddHostedService<SupervisorPipeServer>();
    });

await host.RunAsync();
