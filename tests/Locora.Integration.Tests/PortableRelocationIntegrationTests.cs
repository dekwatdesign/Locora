using System.Diagnostics;
using Locora.Infrastructure.Configuration;
using Locora.Infrastructure.Hosting;
using Locora.Infrastructure.Services;
using Locora.Supervisor.Configuration;
using Locora.Supervisor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Locora.Integration.Tests;

public sealed class PortableRelocationIntegrationTests : IDisposable
{
    private readonly string? _previousRoot = Environment.GetEnvironmentVariable("LOCORA_ROOT");
    private readonly string _originalRoot = Path.Combine(Path.GetTempPath(), "locora relocation source", Guid.NewGuid().ToString("N"));
    private readonly string _relocatedFolder = Path.Combine(Path.GetTempPath(), "locora relocation target", Guid.NewGuid().ToString("N"));
    private char? _mappedDriveLetter;

    [Fact]
    public async Task PortableRelocationValidation_RemainsValidAfterMoveToNewDrive()
    {
        Environment.SetEnvironmentVariable("LOCORA_ROOT", _originalRoot);

        using (var initialHost = BuildSupervisorValidationHost())
        {
            await initialHost.StartAsync();
            var stateStore = initialHost.Services.GetRequiredService<SupervisorStateStore>();
            var initialSnapshot = await stateStore.GetSnapshotAsync();
            var initialValidation = initialSnapshot.ValidationResults.Single(validation =>
                validation.Key.Equals("portable_relocation_readiness", StringComparison.OrdinalIgnoreCase));

            Assert.True(initialValidation.IsValid, initialValidation.Details);
            Assert.Contains(initialSnapshot.Projects, project => project.Name == "welcome");

            await initialHost.StopAsync();
        }

        CopyDirectory(_originalRoot, _relocatedFolder);

        var driveLetter = FindAvailableDriveLetter();
        MapSubstDrive(driveLetter, _relocatedFolder);
        _mappedDriveLetter = driveLetter;

        var relocatedRoot = $"{driveLetter}:\\";
        Environment.SetEnvironmentVariable("LOCORA_ROOT", relocatedRoot);

        using var relocatedHost = BuildSupervisorValidationHost();
        await relocatedHost.StartAsync();

        var relocatedStateStore = relocatedHost.Services.GetRequiredService<SupervisorStateStore>();
        var relocatedSnapshot = await relocatedStateStore.GetSnapshotAsync();
        var relocatedValidation = relocatedSnapshot.ValidationResults.Single(validation =>
            validation.Key.Equals("portable_relocation_readiness", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(Path.GetFullPath(relocatedRoot), relocatedSnapshot.EnvironmentRoot);
        Assert.True(relocatedValidation.IsValid, relocatedValidation.Details);
        Assert.Contains(
            relocatedSnapshot.Projects,
            project => project.Name == "welcome" &&
                project.Path.StartsWith(Path.GetFullPath(relocatedRoot), StringComparison.OrdinalIgnoreCase));

        await relocatedHost.StopAsync();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LOCORA_ROOT", _previousRoot);

        if (_mappedDriveLetter is char driveLetter)
        {
            try
            {
                RunSubstCommand($"{driveLetter}: /d");
            }
            catch (InvalidOperationException)
            {
            }
        }

        TryDeleteDirectory(_relocatedFolder);
        TryDeleteDirectory(_originalRoot);
    }

    private static IHost BuildSupervisorValidationHost()
    {
        return LocoraHostBuilder.BuildHost(
            "integration-relocation",
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
            });
    }

    private static char FindAvailableDriveLetter()
    {
        var usedDriveLetters = DriveInfo.GetDrives()
            .Select(drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();

        for (var code = 'Z'; code >= 'P'; code--)
        {
            if (!usedDriveLetters.Contains(code))
            {
                return code;
            }
        }

        throw new InvalidOperationException("No drive letters are available for portable relocation validation.");
    }

    private static void MapSubstDrive(char driveLetter, string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        RunSubstCommand($"{driveLetter}: \"{targetPath}\"");
    }

    private static void RunSubstCommand(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c subst {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start the subst process.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var message = string.Join(
                Environment.NewLine,
                new[] { output, error }.Where(value => !string.IsNullOrWhiteSpace(value)));
            throw new InvalidOperationException($"subst {arguments} failed with exit code {process.ExitCode}.{Environment.NewLine}{message}".TrimEnd());
        }
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);

        foreach (var directory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, file);
            var destinationPath = Path.Combine(destinationRoot, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
