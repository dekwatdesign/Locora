using Locora.Application.Abstractions;
using Locora.Infrastructure.Configuration;
using Locora.Infrastructure.Diagnostics;
using Locora.Infrastructure.Persistence;
using Locora.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Locora.Infrastructure.Hosting;

public static class LocoraHostBuilder
{
    public static IHost BuildHost(
        string processName,
        Action<HostBuilderContext, IServiceCollection> configureServices)
    {
        var basePath = ResolveBasePath();
        EnsurePortableLayout(basePath);
        var logPath = Path.Combine(basePath, "usr", "logs", $"{processName}.log.jsonl");

        return Host.CreateDefaultBuilder()
            .UseContentRoot(basePath)
            .ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.Sources.Clear();
                configuration.SetBasePath(basePath);
                configuration.AddJsonFile("usr/config/appsettings.json", optional: true, reloadOnChange: true);
                configuration.AddJsonFile("usr/config/supervisor.json", optional: true, reloadOnChange: true);
                configuration.AddJsonFile("usr/config/services.json", optional: true, reloadOnChange: true);
                configuration.AddJsonFile("usr/config/projects.json", optional: true, reloadOnChange: true);
                configuration.AddEnvironmentVariables(prefix: "LOCORA_");
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddProvider(new JsonLineFileLoggerProvider(logPath));
            })
            .ConfigureServices((context, services) =>
            {
                services.AddOptions<AppSettings>()
                    .Bind(context.Configuration.GetSection(AppSettings.SectionName));

                services.AddSingleton<IEnvironmentPaths, EnvironmentPaths>();
                services.AddSingleton<JsonFileStore>();
                services.AddSingleton<AppDataBootstrapper>();
                services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<AppDataBootstrapper>());

                configureServices(context, services);
            })
            .Build();
    }

    private static string ResolveBasePath()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("LOCORA_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return Path.GetFullPath(overrideRoot);
        }

        return Path.GetFullPath(AppContext.BaseDirectory);
    }

    private static void EnsurePortableLayout(string basePath)
    {
        var userRoot = Path.Combine(basePath, "usr");
        var configRoot = Path.Combine(userRoot, "config");
        var logsRoot = Path.Combine(userRoot, "logs");

        Directory.CreateDirectory(userRoot);
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(logsRoot);
        Directory.CreateDirectory(Path.Combine(userRoot, "profiles"));
        Directory.CreateDirectory(Path.Combine(userRoot, "aliases"));
        Directory.CreateDirectory(Path.Combine(userRoot, "templates"));
        Directory.CreateDirectory(Path.Combine(userRoot, "cache"));
        Directory.CreateDirectory(Path.Combine(basePath, "www"));
        Directory.CreateDirectory(Path.Combine(basePath, "bin"));
        Directory.CreateDirectory(Path.Combine(basePath, "data"));
        Directory.CreateDirectory(Path.Combine(basePath, "temp"));

        EnsureFile(
            Path.Combine(configRoot, "appsettings.json"),
            """
            {
              "Locora": {
                "Directories": {
                  "UserRootName": "usr",
                  "ProjectRootName": "www",
                  "BinRootName": "bin",
                  "DataRootName": "data",
                  "TempRootName": "temp"
                },
                "Supervisor": {
                  "PipeName": "locora-supervisor",
                  "ConnectTimeoutMs": 1500
                },
                "Experience": {
                  "PreferredWebServer": "Nginx",
                  "PreferredDatabase": "MariaDB",
                  "PreferredShell": "PowerShell",
                  "PreferredEditor": "VS Code"
                }
              }
            }
            """);

        EnsureFile(
            Path.Combine(configRoot, "supervisor.json"),
            """
            {
              "LocoraSupervisor": {
                "AutoStartServices": [
                  "nginx",
                  "mariadb"
                ],
                "ProbeIntervalMs": 3000
              }
            }
            """);

        EnsureFile(
            Path.Combine(configRoot, "services.json"),
            $$"""
            {
              "LocoraServices": {
                "Services": [
                  {
                    "Key": "nginx",
                    "DisplayName": "Nginx",
                    "Kind": "nginx",
                    "Version": "1.27.x",
                    "RelativeExecutablePath": "bin/nginx/current/nginx.exe",
                    "RelativeWorkingDirectory": "bin/nginx/current",
                    "Arguments": [
                      "-c",
                      "{config}/nginx/nginx.conf"
                    ],
                    "StopArguments": [
                      "-s",
                      "stop"
                    ],
                    "Port": 80,
                    "AutoStart": true,
                    "RestartOnCrash": true,
                    "RestartBackoffMs": 2000,
                    "MaxRestartAttempts": 3,
                    "RestartWindowMs": 60000,
                    "StartTimeoutMs": 5000,
                    "StopTimeoutMs": 3000
                  },
                  {
                    "Key": "mariadb",
                    "DisplayName": "MariaDB",
                    "Kind": "mariadb",
                    "Version": "11.x",
                    "RelativeExecutablePath": "bin/mariadb/current/bin/mysqld.exe",
                    "RelativeWorkingDirectory": "bin/mariadb/current/bin",
                    "Arguments": [
                      "--defaults-file={config}/mariadb/my.ini",
                      "--console"
                    ],
                    "Port": 3306,
                    "AutoStart": true,
                    "RestartOnCrash": true,
                    "RestartBackoffMs": 2000,
                    "MaxRestartAttempts": 3,
                    "RestartWindowMs": 60000,
                    "StartTimeoutMs": 8000,
                    "StopTimeoutMs": 5000
                  },
                  {
                    "Key": "postgresql",
                    "DisplayName": "PostgreSQL",
                    "Kind": "postgresql",
                    "Version": "18.x",
                    "RelativeExecutablePath": "bin/postgresql/current/bin/postgres.exe",
                    "RelativeStopExecutablePath": "bin/postgresql/current/bin/pg_ctl.exe",
                    "RelativeWorkingDirectory": "bin/postgresql/current/bin",
                    "Arguments": [
                      "-D",
                      "{data}/postgresql/data",
                      "-c",
                      "config_file={config}/postgresql/postgresql.conf"
                    ],
                    "StopArguments": [
                      "stop",
                      "-D",
                      "{data}/postgresql/data",
                      "-m",
                      "fast",
                      "-W",
                      "-t",
                      "15",
                      "-s"
                    ],
                    "Port": 5432,
                    "AutoStart": false,
                    "RestartOnCrash": true,
                    "RestartBackoffMs": 2000,
                    "MaxRestartAttempts": 3,
                    "RestartWindowMs": 60000,
                    "StartTimeoutMs": 10000,
                    "StopTimeoutMs": 8000
                  },
                  {
                    "Key": "redis",
                    "DisplayName": "Redis",
                    "Kind": "redis",
                    "Version": "7.x",
                    "RelativeExecutablePath": "bin/redis/current/redis-server.exe",
                    "RelativeStopExecutablePath": "bin/redis/current/redis-cli.exe",
                    "RelativeWorkingDirectory": "bin/redis/current",
                    "Arguments": [
                      "{config}/redis/redis.conf"
                    ],
                    "StopArguments": [
                      "-p",
                      "{port}",
                      "shutdown",
                      "nosave"
                    ],
                    "Port": 6379,
                    "AutoStart": false,
                    "RestartOnCrash": true,
                    "RestartBackoffMs": 2000,
                    "MaxRestartAttempts": 3,
                    "RestartWindowMs": 60000,
                    "StartTimeoutMs": 5000,
                    "StopTimeoutMs": 5000
                  },
                  {
                    "Key": "mailpit",
                    "DisplayName": "Mailpit",
                    "Kind": "mailpit",
                    "Version": "1.x",
                    "RelativeExecutablePath": "bin/mailpit/current/mailpit.exe",
                    "RelativeWorkingDirectory": "bin/mailpit/current",
                    "Arguments": [
                      "--smtp",
                      "127.0.0.1:{port}",
                      "--listen",
                      "127.0.0.1:8025",
                      "--database",
                      "{data}/mailpit/mailpit.db",
                      "--label",
                      "Locora Mailpit",
                      "--disable-version-check"
                    ],
                    "Port": 1025,
                    "AutoStart": false,
                    "RestartOnCrash": true,
                    "RestartBackoffMs": 2000,
                    "MaxRestartAttempts": 3,
                    "RestartWindowMs": 60000,
                    "StartTimeoutMs": 5000,
                    "StopTimeoutMs": 3000
                  }
                ]
              }
            }
            """);

        EnsureFile(
            Path.Combine(configRoot, "projects.json"),
            """
            {
              "LocoraProjects": {
                "EnableAutoDiscovery": true,
                "GenerateNginxVHosts": true,
                "GenerateHostsPreview": true,
                "DomainSuffix": "locora.test",
                "DefaultScheme": "http",
                "IndexFileNames": [
                  "index.php",
                  "index.html",
                  "index.htm"
                ],
                "IgnoredDirectoryNames": [
                  ".git",
                  ".idea",
                  ".vscode",
                  "node_modules",
                  "vendor"
                ]
              }
            }
            """);
    }

    private static void EnsureFile(string path, string content)
    {
        if (File.Exists(path))
        {
            return;
        }

        File.WriteAllText(path, NormalizeJson(content));
    }

    private static string NormalizeJson(string content)
    {
        using var document = JsonDocument.Parse(content);
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }
}
