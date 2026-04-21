using Locora.Application.Abstractions;
using Locora.Infrastructure.Configuration;
using Locora.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Locora.Infrastructure.Services;

public sealed class AppDataBootstrapper : IHostedService
{
    private readonly IEnvironmentPaths _environmentPaths;
    private readonly IOptions<AppSettings> _appSettings;
    private readonly JsonFileStore _jsonFileStore;
    private readonly ILogger<AppDataBootstrapper> _logger;

    public AppDataBootstrapper(
        IEnvironmentPaths environmentPaths,
        IOptions<AppSettings> appSettings,
        JsonFileStore jsonFileStore,
        ILogger<AppDataBootstrapper> logger)
    {
        _environmentPaths = environmentPaths;
        _appSettings = appSettings;
        _jsonFileStore = jsonFileStore;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_environmentPaths.UserRoot);
        Directory.CreateDirectory(_environmentPaths.ConfigRoot);
        Directory.CreateDirectory(_environmentPaths.LogsRoot);
        Directory.CreateDirectory(_environmentPaths.ProjectRoot);
        Directory.CreateDirectory(_environmentPaths.DataRoot);
        Directory.CreateDirectory(_environmentPaths.BinRoot);
        Directory.CreateDirectory(_environmentPaths.TempRoot);
        Directory.CreateDirectory(Path.Combine(_environmentPaths.UserRoot, "profiles"));
        Directory.CreateDirectory(Path.Combine(_environmentPaths.UserRoot, "aliases"));
        Directory.CreateDirectory(Path.Combine(_environmentPaths.UserRoot, "templates"));
        Directory.CreateDirectory(Path.Combine(_environmentPaths.UserRoot, "cache"));
        EnsureWelcomeProject();

        if (!File.Exists(_environmentPaths.AppSettingsFile))
        {
            await _jsonFileStore.WriteAsync(_environmentPaths.AppSettingsFile, new { Locora = _appSettings.Value }, cancellationToken);
        }

        if (!File.Exists(_environmentPaths.SupervisorSettingsFile))
        {
            await _jsonFileStore.WriteAsync(
                _environmentPaths.SupervisorSettingsFile,
                new
                {
                    LocoraSupervisor = new
                    {
                        AutoStartServices = new[] { "nginx", "mariadb" }
                    }
                },
                cancellationToken);
        }

        if (!File.Exists(_environmentPaths.ServicesSettingsFile))
        {
            await _jsonFileStore.WriteAsync(
                _environmentPaths.ServicesSettingsFile,
                new
                {
                    LocoraServices = new
                    {
                        Services = new object[]
                        {
                            new
                            {
                                Key = "nginx",
                                DisplayName = "Nginx",
                                Kind = "nginx",
                                Version = "1.27.x",
                                RelativeExecutablePath = "bin/nginx/current/nginx.exe",
                                RelativeWorkingDirectory = "bin/nginx/current",
                                Arguments = new[] { "-c", "{config}/nginx/nginx.conf" },
                                StopArguments = new[] { "-s", "stop" },
                                Port = 80,
                                AutoStart = true,
                                RestartOnCrash = true,
                                RestartBackoffMs = 2000,
                                MaxRestartAttempts = 3,
                                RestartWindowMs = 60000,
                                StartTimeoutMs = 5000,
                                StopTimeoutMs = 3000
                            },
                            new
                            {
                                Key = "mariadb",
                                DisplayName = "MariaDB",
                                Kind = "mariadb",
                                Version = "11.x",
                                RelativeExecutablePath = "bin/mariadb/current/bin/mysqld.exe",
                                RelativeWorkingDirectory = "bin/mariadb/current/bin",
                                Arguments = new[] { "--defaults-file={config}/mariadb/my.ini", "--console" },
                                StopArguments = Array.Empty<string>(),
                                Port = 3306,
                                AutoStart = true,
                                RestartOnCrash = true,
                                RestartBackoffMs = 2000,
                                MaxRestartAttempts = 3,
                                RestartWindowMs = 60000,
                                StartTimeoutMs = 8000,
                                StopTimeoutMs = 5000
                            },
                            new
                            {
                                Key = "postgresql",
                                DisplayName = "PostgreSQL",
                                Kind = "postgresql",
                                Version = "18.x",
                                RelativeExecutablePath = "bin/postgresql/current/bin/postgres.exe",
                                RelativeStopExecutablePath = "bin/postgresql/current/bin/pg_ctl.exe",
                                RelativeWorkingDirectory = "bin/postgresql/current/bin",
                                Arguments = new[] { "-D", "{data}/postgresql/data", "-c", "config_file={config}/postgresql/postgresql.conf" },
                                StopArguments = new[] { "stop", "-D", "{data}/postgresql/data", "-m", "fast", "-W", "-t", "15", "-s" },
                                Port = 5432,
                                AutoStart = false,
                                RestartOnCrash = true,
                                RestartBackoffMs = 2000,
                                MaxRestartAttempts = 3,
                                RestartWindowMs = 60000,
                                StartTimeoutMs = 10000,
                                StopTimeoutMs = 8000
                            },
                            new
                            {
                                Key = "redis",
                                DisplayName = "Redis",
                                Kind = "redis",
                                Version = "7.x",
                                RelativeExecutablePath = "bin/redis/current/redis-server.exe",
                                RelativeStopExecutablePath = "bin/redis/current/redis-cli.exe",
                                RelativeWorkingDirectory = "bin/redis/current",
                                Arguments = new[] { "{config}/redis/redis.conf" },
                                StopArguments = new[] { "-p", "{port}", "shutdown", "nosave" },
                                Port = 6379,
                                AutoStart = false,
                                RestartOnCrash = true,
                                RestartBackoffMs = 2000,
                                MaxRestartAttempts = 3,
                                RestartWindowMs = 60000,
                                StartTimeoutMs = 5000,
                                StopTimeoutMs = 5000
                            },
                            new
                            {
                                Key = "mailpit",
                                DisplayName = "Mailpit",
                                Kind = "mailpit",
                                Version = "1.x",
                                RelativeExecutablePath = "bin/mailpit/current/mailpit.exe",
                                RelativeWorkingDirectory = "bin/mailpit/current",
                                Arguments = new[]
                                {
                                    "--smtp", "127.0.0.1:{port}",
                                    "--listen", "127.0.0.1:8025",
                                    "--database", "{data}/mailpit/mailpit.db",
                                    "--label", "Locora Mailpit",
                                    "--disable-version-check"
                                },
                                StopArguments = Array.Empty<string>(),
                                Port = 1025,
                                AutoStart = false,
                                RestartOnCrash = true,
                                RestartBackoffMs = 2000,
                                MaxRestartAttempts = 3,
                                RestartWindowMs = 60000,
                                StartTimeoutMs = 5000,
                                StopTimeoutMs = 3000
                            }
                        }
                    }
                },
                cancellationToken);
        }

        if (!File.Exists(_environmentPaths.ProjectsSettingsFile))
        {
            await _jsonFileStore.WriteAsync(
                _environmentPaths.ProjectsSettingsFile,
                new
                {
                    LocoraProjects = new
                    {
                        EnableAutoDiscovery = true,
                        GenerateNginxVHosts = true,
                        GenerateHostsPreview = true,
                        DomainSuffix = "locora.test",
                        DefaultScheme = "http",
                        IndexFileNames = new[] { "index.php", "index.html", "index.htm" },
                        IgnoredDirectoryNames = new[] { ".git", ".idea", ".vscode", "node_modules", "vendor" }
                    }
                },
                cancellationToken);
        }

        _logger.LogInformation("Portable workspace root prepared at {Root}", _environmentPaths.AppRoot);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void EnsureWelcomeProject()
    {
        var welcomeRoot = Path.Combine(_environmentPaths.ProjectRoot, "welcome");
        var welcomeIndexPath = Path.Combine(welcomeRoot, "index.html");

        Directory.CreateDirectory(welcomeRoot);

        if (!File.Exists(welcomeIndexPath))
        {
            File.WriteAllText(
                welcomeIndexPath,
                """
                <!doctype html>
                <html lang="en">
                <head>
                  <meta charset="utf-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1" />
                  <title>Locora Welcome</title>
                </head>
                <body>
                  <h1>Locora is ready</h1>
                  <p>Drop more projects into the www folder and refresh the dashboard.</p>
                </body>
                </html>
                """);
        }
    }
}
