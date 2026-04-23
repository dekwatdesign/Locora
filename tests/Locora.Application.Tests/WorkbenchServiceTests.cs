using Locora.App.Contracts;
using Locora.Application.Abstractions;
using Locora.Application.Services;
using Locora.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Locora.Application.Tests;

public sealed class WorkbenchServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_WhenSupervisorThrows_ReturnsOfflineSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "locora-application-tests", Guid.NewGuid().ToString("N"));
        var client = new FakeSupervisorClient
        {
            SnapshotException = new IOException("pipe unavailable")
        };

        var service = new WorkbenchService(
            client,
            new TestEnvironmentPaths(root),
            NullLogger<WorkbenchService>.Instance);

        var snapshot = await service.GetSnapshotAsync();

        Assert.False(snapshot.IsSupervisorReachable);
        Assert.Equal("Bootstrap", snapshot.ActiveProfile);
        Assert.Equal(Path.GetFullPath(root), snapshot.EnvironmentRoot);
        Assert.Contains(snapshot.Services, service => service.Key == "nginx" && service.State == ServiceState.Unknown);
        Assert.Contains(snapshot.Issues, issue => issue.Title == "Supervisor offline" && issue.Description.Contains("pipe unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SelectRuntimeVersionAsync_ForwardsPackageAndVersionAsSelectionKey()
    {
        var client = new FakeSupervisorClient
        {
            Snapshot = TestSnapshots.CreateDto()
        };

        var service = new WorkbenchService(
            client,
            new TestEnvironmentPaths(Path.Combine(Path.GetTempPath(), "locora-application-tests")),
            NullLogger<WorkbenchService>.Instance);

        var snapshot = await service.SelectRuntimeVersionAsync("php", "8.4.7");

        Assert.Equal("php|8.4.7", client.SelectedRuntimeSelectionKey);
        Assert.True(snapshot.IsSupervisorReachable);
        Assert.Contains(snapshot.Services, service => service.Key == "nginx" && service.State == ServiceState.Running);
    }

    private sealed class FakeSupervisorClient : ISupervisorClient
    {
        public EnvironmentSnapshotDto? Snapshot { get; init; }

        public Exception? SnapshotException { get; init; }

        public string? SelectedRuntimeSelectionKey { get; private set; }

        public Task<EnvironmentSnapshotDto> GetEnvironmentSnapshotAsync(CancellationToken cancellationToken = default)
        {
            if (SnapshotException is not null)
            {
                return Task.FromException<EnvironmentSnapshotDto>(SnapshotException);
            }

            return Task.FromResult(Snapshot ?? TestSnapshots.CreateDto());
        }

        public Task StartAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StartServiceAsync(string serviceKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopServiceAsync(string serviceKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StartServicePresetAsync(string presetKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopServicePresetAsync(string presetKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ApplyHostsPreviewAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackHostsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RestartSupervisorElevatedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ValidateNginxConfigAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RepairRuntimeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RepairDomainsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RepairServiceAsync(string serviceKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SyncPackageDownloadsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ExtractPackageArchivesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task InstallOrUpdatePackagesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RemovePackageInstallAsync(string packageId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SelectRuntimeVersionAsync(string selectionKey, CancellationToken cancellationToken = default)
        {
            SelectedRuntimeSelectionKey = selectionKey;
            return Task.CompletedTask;
        }

        public Task SelectStackProfileAsync(string profileKey, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveActiveEnvironmentAsync(string profileName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ExportStackProfilesAsync(string targetPath, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ImportStackProfilesAsync(string sourcePath, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RepairLocalSslAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task GenerateLocalSslAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task TrustLocalSslCaAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackLocalSslTrustAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestEnvironmentPaths : IEnvironmentPaths
    {
        public TestEnvironmentPaths(string appRoot)
        {
            AppRoot = Path.GetFullPath(appRoot);
        }

        public string AppRoot { get; }

        public string UserRoot => Path.Combine(AppRoot, "usr");

        public string CacheRoot => Path.Combine(UserRoot, "cache");

        public string ConfigRoot => Path.Combine(UserRoot, "config");

        public string ConfigBackupRoot => Path.Combine(UserRoot, "backups", "config");

        public string ProfilesRoot => Path.Combine(UserRoot, "profiles");

        public string AliasesRoot => Path.Combine(UserRoot, "aliases");

        public string ShellIntegrationRoot => Path.Combine(UserRoot, "shell");

        public string UserEnvironmentApplyScriptFile => Path.Combine(ShellIntegrationRoot, "install-user-environment.ps1");

        public string UserEnvironmentRemoveScriptFile => Path.Combine(ShellIntegrationRoot, "uninstall-user-environment.ps1");

        public string UserEnvironmentManifestFile => Path.Combine(ShellIntegrationRoot, "user-environment-manifest.json");

        public string UserEnvironmentBackupRoot => Path.Combine(ShellIntegrationRoot, "environment-backups");

        public string AppUpdateRoot => Path.Combine(UserRoot, "updates");

        public string AppUpdateDownloadRoot => Path.Combine(AppUpdateRoot, "downloads");

        public string AppUpdateManifestCacheFile => Path.Combine(AppUpdateRoot, "latest-release.json");

        public string AppUpdatePlanFile => Path.Combine(AppUpdateRoot, "update-plan.json");

        public string AppUpdateManifestExampleFile => Path.Combine(AppUpdateRoot, "release-manifest.example.json");

        public string PortableDistributionRoot => Path.Combine(UserRoot, "distribution");

        public string PortableDistributionArtifactsRoot => Path.Combine(PortableDistributionRoot, "artifacts");

        public string PortableDistributionScriptFile => Path.Combine(PortableDistributionRoot, "build-portable-distribution.ps1");

        public string PortableDistributionPlanFile => Path.Combine(PortableDistributionRoot, "portable-distribution-plan.json");

        public string PortableDistributionReadmeFile => Path.Combine(PortableDistributionRoot, "README.md");

        public string PortableDistributionManifestTemplateFile => Path.Combine(PortableDistributionRoot, "release-manifest.template.json");

        public string LogsRoot => Path.Combine(UserRoot, "logs");

        public string ProjectRoot => Path.Combine(AppRoot, "www");

        public string DataRoot => Path.Combine(AppRoot, "data");

        public string BinRoot => Path.Combine(AppRoot, "bin");

        public string TempRoot => Path.Combine(AppRoot, "temp");

        public string AppSettingsFile => Path.Combine(ConfigRoot, "appsettings.json");

        public string SupervisorSettingsFile => Path.Combine(ConfigRoot, "supervisor.json");

        public string ServicesSettingsFile => Path.Combine(ConfigRoot, "services.json");

        public string ProjectsSettingsFile => Path.Combine(ConfigRoot, "projects.json");

        public string ProfilesSettingsFile => Path.Combine(ConfigRoot, "profiles.json");

        public string PackageSourcesSettingsFile => Path.Combine(ConfigRoot, "sources.json");

        public string PackagesLockSettingsFile => Path.Combine(ConfigRoot, "packages.lock.json");

        public string ProjectPinsSettingsFile => Path.Combine(ConfigRoot, "project-pins.json");

        public string OnboardingSettingsFile => Path.Combine(ConfigRoot, "onboarding.json");

        public string TerminalCommandsSettingsFile => Path.Combine(ConfigRoot, "terminal-commands.json");

        public string CustomToolsSettingsFile => Path.Combine(ConfigRoot, "custom-tools.json");

        public string LocalTunnelsSettingsFile => Path.Combine(ConfigRoot, "local-tunnels.json");

        public string ShellContextMenuInstallFile => Path.Combine(ShellIntegrationRoot, "install-context-menu.reg");

        public string ShellContextMenuUninstallFile => Path.Combine(ShellIntegrationRoot, "uninstall-context-menu.reg");

        public string PackageManifestsRoot => Path.Combine(AppRoot, "package-manifests");

        public string PackageCacheRoot => Path.Combine(CacheRoot, "packages");

        public string GetLogFilePath(string processName) => Path.Combine(LogsRoot, $"{processName}.log.jsonl");

        public string GetServiceOutputLogPath(string serviceKey) => Path.Combine(LogsRoot, $"{serviceKey}.stdout.log");

        public string GetServiceErrorLogPath(string serviceKey) => Path.Combine(LogsRoot, $"{serviceKey}.stderr.log");
    }

    private static class TestSnapshots
    {
        public static EnvironmentSnapshotDto CreateDto()
        {
            return new EnvironmentSnapshotDto(
                EnvironmentRoot: "D:\\Locora",
                ActiveProfile: "Default",
                Services:
                [
                    new ServiceStatusDto(
                        Key: "nginx",
                        DisplayName: "Nginx",
                        Version: "1.27",
                        Port: 80,
                        State: "Running",
                        AutoStart: true,
                        Note: "ready")
                ],
                ServicePresets: [],
                Projects: [],
                Issues: [],
                ValidationResults: [],
                PortDiagnostics: [],
                PermissionDiagnostics: [],
                PackageSources: [],
                PackageRegistry: new PackageRegistrySummaryDto(0, 0, 0, 0, 0, "empty", "empty"),
                PackageDownloads: [],
                PackageDownloadsSummary: new PackageDownloadSummaryDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "empty", "empty"),
                RuntimePackages: [],
                RuntimePackageSummary: new RuntimePackageSummaryDto(0, 0, 0, 0, 0, "empty", "empty"),
                ToolPackages: [],
                ToolPackageSummary: new ToolPackageSummaryDto(0, 0, 0, 0, 0, "empty", "empty"),
                StackProfiles: [],
                StackProfileSummary: new StackProfileSummaryDto(0, 0, 0, "default", "Default", "empty", "empty"),
                SslStatus: new SslStatusDto(
                    AuthorityName: "Locora Test CA",
                    CaCertificatePath: "ca.cer",
                    CertificatesRoot: "certs",
                    CaExists: false,
                    IsCurrentUserTrusted: false,
                    TrustSupported: true,
                    ProjectCertificateCount: 0,
                    HttpsProjectCount: 0,
                    MissingProjectCertificateCount: 0,
                    ExpiredProjectCertificateCount: 0,
                    ExpiringProjectCertificateCount: 0,
                    Summary: "empty",
                    Details: "empty",
                    LastGeneratedAt: null,
                    CaExpiresAt: null,
                    CertificateDiagnostics: []),
                GeneratedAt: DateTimeOffset.UtcNow,
                IsSupervisorElevated: true,
                SupervisorProcessId: 1234);
        }
    }
}
