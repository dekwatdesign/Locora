using Locora.Infrastructure.Hosting;
using Locora.Infrastructure.Configuration;
using Locora.Infrastructure.Services;
using Locora.Supervisor.Configuration;
using Locora.Supervisor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
        services.AddSingleton<IPostConfigureOptions<ManagedServicesOptions>, VersionAwareManagedServicesPostConfigure>();
        services.AddOptions<ProjectDiscoveryOptions>()
            .Bind(context.Configuration.GetSection(ProjectDiscoveryOptions.SectionName));
        services.AddOptions<PackageSourcesOptions>()
            .Bind(context.Configuration.GetSection(PackageSourcesOptions.SectionName));
        services.AddOptions<PackagesLockOptions>()
            .Bind(context.Configuration.GetSection(PackagesLockOptions.SectionName));
        services.AddSingleton<PortProbe>();
        services.AddSingleton<PackageSourceRegistry>();
        services.AddSingleton<PackageDownloadManager>();
        services.AddSingleton<StackProfileRegistry>();
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
