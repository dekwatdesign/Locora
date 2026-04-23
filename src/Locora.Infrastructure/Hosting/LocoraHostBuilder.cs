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
                configuration.AddJsonFile("usr/config/profiles.json", optional: true, reloadOnChange: true);
                configuration.AddJsonFile("usr/config/custom-tools.json", optional: true, reloadOnChange: true);
                configuration.AddJsonFile("usr/config/local-tunnels.json", optional: true, reloadOnChange: true);
                configuration.AddJsonFile("usr/config/sources.json", optional: true, reloadOnChange: true);
                configuration.AddJsonFile("usr/config/packages.lock.json", optional: true, reloadOnChange: true);
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
        Directory.CreateDirectory(Path.Combine(userRoot, "shell"));
        Directory.CreateDirectory(Path.Combine(userRoot, "templates"));
        Directory.CreateDirectory(Path.Combine(userRoot, "cache"));
        Directory.CreateDirectory(Path.Combine(userRoot, "cache", "packages"));
        Directory.CreateDirectory(Path.Combine(userRoot, "updates"));
        Directory.CreateDirectory(Path.Combine(userRoot, "updates", "downloads"));
        Directory.CreateDirectory(Path.Combine(userRoot, "distribution"));
        Directory.CreateDirectory(Path.Combine(userRoot, "distribution", "artifacts"));
        Directory.CreateDirectory(Path.Combine(basePath, "www"));
        Directory.CreateDirectory(Path.Combine(basePath, "bin"));
        Directory.CreateDirectory(Path.Combine(basePath, "data"));
        Directory.CreateDirectory(Path.Combine(basePath, "temp"));
        Directory.CreateDirectory(Path.Combine(basePath, "archives"));
        Directory.CreateDirectory(Path.Combine(basePath, "package-manifests"));

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
                },
                "Updates": {
                  "Channel": "stable",
                  "ManifestUri": "",
                  "ReleasePageUri": "",
                  "AllowPrerelease": false,
                  "CheckOnStartup": false,
                  "CheckTimeoutMs": 5000
                },
                "Distribution": {
                  "Configuration": "Release",
                  "RuntimeIdentifier": "win-x64",
                  "IncludeRuntimeBinaries": false,
                  "IncludePackageCache": false,
                  "IncludeUserData": false,
                  "CreateReleaseManifest": true
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
                    "Key": "apache",
                    "DisplayName": "Apache",
                    "Kind": "apache",
                    "Version": "2.4.x",
                    "RelativeExecutablePath": "bin/apache/current/bin/httpd.exe",
                    "RelativeWorkingDirectory": "bin/apache/current/bin",
                    "Arguments": [
                      "-f",
                      "{config}/apache/httpd.conf"
                    ],
                    "StopArguments": [
                      "-k",
                      "stop",
                      "-f",
                      "{config}/apache/httpd.conf"
                    ],
                    "Port": 8080,
                    "AutoStart": false,
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
                  },
                  {
                    "Key": "memcached",
                    "DisplayName": "Memcached",
                    "Kind": "memcached",
                    "Version": "1.6.x",
                    "RelativeExecutablePath": "bin/memcached/current/memcached.exe",
                    "RelativeWorkingDirectory": "bin/memcached/current",
                    "Arguments": [
                      "-l",
                      "127.0.0.1",
                      "-p",
                      "{port}",
                      "-U",
                      "0",
                      "-m",
                      "64"
                    ],
                    "Port": 11211,
                    "AutoStart": false,
                    "RestartOnCrash": true,
                    "RestartBackoffMs": 2000,
                    "MaxRestartAttempts": 3,
                    "RestartWindowMs": 60000,
                    "StartTimeoutMs": 5000,
                    "StopTimeoutMs": 3000
                  }
                ],
                "Presets": [
                  {
                    "Key": "web-db-mail",
                    "DisplayName": "Web + DB + Mail",
                    "Description": "Start the default web server, MariaDB, and Mailpit for PHP and CMS work.",
                    "ServiceKeys": [
                      "nginx",
                      "mariadb",
                      "mailpit"
                    ],
                    "Tags": [
                      "web",
                      "database",
                      "mail"
                    ]
                  },
                  {
                    "Key": "node-api",
                    "DisplayName": "Node API",
                    "Description": "Start Nginx, PostgreSQL, Redis, and Mailpit for API and full-stack JavaScript projects.",
                    "ServiceKeys": [
                      "nginx",
                      "postgresql",
                      "redis",
                      "mailpit"
                    ],
                    "Tags": [
                      "node",
                      "api",
                      "postgres",
                      "cache"
                    ]
                  },
                  {
                    "Key": "cache-lab",
                    "DisplayName": "Cache Lab",
                    "Description": "Start Redis and Memcached for cache integration testing.",
                    "ServiceKeys": [
                      "redis",
                      "memcached"
                    ],
                    "Tags": [
                      "cache",
                      "testing"
                    ]
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
                "GenerateApacheVHosts": true,
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
                ],
                "ProjectOverrides": []
              }
            }
            """);

        EnsureFile(
            Path.Combine(configRoot, "project-pins.json"),
            """
            {
              "PinnedProjectKeys": []
            }
            """);

        EnsureFile(
            Path.Combine(configRoot, "profiles.json"),
            """
            {
              "LocoraProfiles": {
                "SchemaVersion": 1,
                "ActiveProfileKey": "full-stack",
                "Profiles": [
                  {
                    "Key": "full-stack",
                    "DisplayName": "Full Stack",
                    "Description": "Nginx, MariaDB, Mailpit, PHP, Node.js, Python, and Java selections for general local development.",
                    "ServiceKeys": [
                      "nginx",
                      "mariadb",
                      "mailpit"
                    ],
                    "PackageSelections": {
                      "nginx": "1.27.x",
                      "mariadb": "11.x",
                      "mailpit": "1.x",
                      "php": "8.3.x",
                      "nodejs": "22.x",
                      "python": "3.12.x",
                      "java": "21.x"
                    },
                    "Tags": [
                      "web",
                      "php",
                      "node",
                      "database",
                      "mail"
                    ]
                  },
                  {
                    "Key": "php-mariadb",
                    "DisplayName": "PHP + MariaDB",
                    "Description": "Lean PHP stack with Nginx, MariaDB, Mailpit, and Composer-ready tooling.",
                    "ServiceKeys": [
                      "nginx",
                      "mariadb",
                      "mailpit"
                    ],
                    "PackageSelections": {
                      "nginx": "1.27.x",
                      "mariadb": "11.x",
                      "mailpit": "1.x",
                      "php": "8.3.x"
                    },
                    "Tags": [
                      "php",
                      "mysql",
                      "laravel",
                      "wordpress"
                    ]
                  },
                  {
                    "Key": "node-postgres",
                    "DisplayName": "Node.js + PostgreSQL",
                    "Description": "Node.js app stack with PostgreSQL and Mailpit for API and full-stack JavaScript work.",
                    "ServiceKeys": [
                      "nginx",
                      "postgresql",
                      "mailpit"
                    ],
                    "PackageSelections": {
                      "nginx": "1.27.x",
                      "postgresql": "18.x",
                      "mailpit": "1.x",
                      "nodejs": "22.x"
                    },
                    "Tags": [
                      "node",
                      "postgres",
                      "api"
                    ]
                  }
                ]
              }
            }
            """);

        EnsureFile(
            Path.Combine(configRoot, "terminal-commands.json"),
            """
            {
              "LocoraTerminal": {
                "Commands": [
                  {
                    "Key": "git-status",
                    "DisplayName": "Git status",
                    "Alias": "gst",
                    "Description": "Inspect the selected terminal workspace before making changes.",
                    "CommandText": "git status",
                    "Scope": "any",
                    "Tags": [
                      "git",
                      "status",
                      "workspace"
                    ]
                  },
                  {
                    "Key": "php-version",
                    "DisplayName": "PHP version",
                    "Alias": "phpv",
                    "Description": "Print the active PHP runtime version from the injected PATH.",
                    "CommandText": "php -v",
                    "Scope": "any",
                    "Tags": [
                      "php",
                      "runtime",
                      "version"
                    ]
                  },
                  {
                    "Key": "node-version",
                    "DisplayName": "Node.js version",
                    "Alias": "nodev",
                    "Description": "Print the active Node.js runtime version from the injected PATH.",
                    "CommandText": "node --version",
                    "Scope": "any",
                    "Tags": [
                      "node",
                      "nodejs",
                      "runtime",
                      "version"
                    ]
                  },
                  {
                    "Key": "python-version",
                    "DisplayName": "Python version",
                    "Alias": "pyv",
                    "Description": "Print the active Python runtime version from the injected PATH.",
                    "CommandText": "python --version",
                    "Scope": "any",
                    "Tags": [
                      "python",
                      "runtime",
                      "version"
                    ]
                  },
                  {
                    "Key": "java-version",
                    "DisplayName": "Java version",
                    "Alias": "javav",
                    "Description": "Print the active Java runtime version from the injected PATH.",
                    "CommandText": "java -version",
                    "Scope": "any",
                    "Tags": [
                      "java",
                      "runtime",
                      "version"
                    ]
                  },
                  {
                    "Key": "composer-version",
                    "DisplayName": "Composer version",
                    "Alias": "compv",
                    "Description": "Print the active Composer tool version from the injected PATH.",
                    "CommandText": "composer --version",
                    "Scope": "any",
                    "Tags": [
                      "composer",
                      "tool",
                      "version"
                    ]
                  },
                  {
                    "Key": "composer-install",
                    "DisplayName": "Composer install",
                    "Alias": "cinst",
                    "Description": "Install PHP project dependencies in the selected project terminal tab.",
                    "CommandText": "composer install",
                    "Scope": "project",
                    "Tags": [
                      "composer",
                      "php",
                      "dependencies",
                      "project"
                    ]
                  },
                  {
                    "Key": "npm-install",
                    "DisplayName": "NPM install",
                    "Alias": "npmi",
                    "Description": "Install Node.js project dependencies in the selected project terminal tab.",
                    "CommandText": "npm install",
                    "Scope": "project",
                    "Tags": [
                      "npm",
                      "node",
                      "dependencies",
                      "project"
                    ]
                  },
                  {
                    "Key": "npm-dev",
                    "DisplayName": "NPM dev server",
                    "Alias": "npmdev",
                    "Description": "Start the common npm development server in the selected project terminal tab.",
                    "CommandText": "npm run dev",
                    "Scope": "project",
                    "Tags": [
                      "npm",
                      "node",
                      "dev",
                      "project"
                    ]
                  }
                ]
              }
            }
            """);

        EnsureFile(
            Path.Combine(configRoot, "custom-tools.json"),
            """
            {
              "LocoraTools": {
                "Tools": [
                  {
                    "Key": "open-localhost",
                    "DisplayName": "Open localhost",
                    "Description": "Open the default local HTTP endpoint in the browser.",
                    "Action": "url",
                    "Target": "http://localhost/",
                    "Scope": "root",
                    "Tags": [
                      "browser",
                      "web"
                    ],
                    "IsEnabled": true
                  },
                  {
                    "Key": "open-config-folder",
                    "DisplayName": "Open config folder",
                    "Description": "Open Locora's portable configuration folder.",
                    "Action": "folder",
                    "Target": "{configRoot}",
                    "Scope": "root",
                    "Tags": [
                      "config",
                      "folder"
                    ],
                    "IsEnabled": true
                  },
                  {
                    "Key": "composer-diagnose",
                    "DisplayName": "Composer diagnose",
                    "Description": "Run Composer diagnostics in the selected project terminal tab.",
                    "Action": "terminal",
                    "Target": "composer diagnose",
                    "Scope": "project",
                    "Tags": [
                      "composer",
                      "php",
                      "diagnostics"
                    ],
                    "IsEnabled": true
                  }
                ]
              }
            }
            """);

        EnsureFile(
            Path.Combine(configRoot, "local-tunnels.json"),
            """
            {
              "LocoraTunnels": {
                "Profiles": [
                  {
                    "Key": "cloudflared-selected-project",
                    "DisplayName": "Cloudflared quick tunnel",
                    "Provider": "cloudflared",
                    "Description": "Expose the selected project URL through a temporary Cloudflare Tunnel.",
                    "CommandText": "cloudflared tunnel --url {localUrl}",
                    "Scope": "project",
                    "ProjectName": "",
                    "LocalUrl": "{projectUrl}",
                    "Port": 80,
                    "Tags": [
                      "share",
                      "cloudflared",
                      "temporary"
                    ],
                    "IsEnabled": true
                  },
                  {
                    "Key": "ngrok-http-80",
                    "DisplayName": "Ngrok HTTP 80",
                    "Provider": "ngrok",
                    "Description": "Expose the default local web server port with ngrok.",
                    "CommandText": "ngrok http {port}",
                    "Scope": "root",
                    "ProjectName": "",
                    "LocalUrl": "http://127.0.0.1:{port}",
                    "Port": 80,
                    "Tags": [
                      "share",
                      "ngrok",
                      "http"
                    ],
                    "IsEnabled": true
                  },
                  {
                    "Key": "dev-tunnel-http-80",
                    "DisplayName": "Dev Tunnel HTTP 80",
                    "Provider": "devtunnel",
                    "Description": "Expose the default local web server port with Microsoft dev tunnels.",
                    "CommandText": "devtunnel host -p {port} --allow-anonymous",
                    "Scope": "root",
                    "ProjectName": "",
                    "LocalUrl": "http://127.0.0.1:{port}",
                    "Port": 80,
                    "Tags": [
                      "share",
                      "devtunnel",
                      "http"
                    ],
                    "IsEnabled": true
                  }
                ]
              }
            }
            """);

        EnsureFile(
            Path.Combine(configRoot, "sources.json"),
            """
            {
              "LocoraPackageSources": {
                "SchemaVersion": 1,
                "Sources": [
                  {
                    "Id": "locora-bundled",
                    "DisplayName": "Locora bundled Windows x64 manifest",
                    "Kind": "file",
                    "Channel": "stable",
                    "ManifestPath": "package-manifests/locora.windows-x64.json",
                    "Priority": 100,
                    "IsEnabled": true,
                    "IsBundled": true
                  }
                ]
              }
            }
            """);

        EnsureFile(
            Path.Combine(configRoot, "packages.lock.json"),
            """
            {
              "LocoraPackagesLock": {
                "SchemaVersion": 1,
                "ActiveSelections": [
                  {
                    "PackageId": "nginx",
                    "RequestedVersion": "1.27.x",
                    "SelectionStrategy": "latest-matching",
                    "SourceId": "locora-bundled"
                  },
                  {
                    "PackageId": "apache",
                    "RequestedVersion": "2.4.x",
                    "SelectionStrategy": "latest-matching",
                    "SourceId": "locora-bundled"
                  },
                  {
                    "PackageId": "mariadb",
                    "RequestedVersion": "11.x",
                    "SelectionStrategy": "latest-matching",
                    "SourceId": "locora-bundled"
                  },
                  {
                    "PackageId": "postgresql",
                    "RequestedVersion": "18.x",
                    "SelectionStrategy": "latest-matching",
                    "SourceId": "locora-bundled"
                  },
                  {
                    "PackageId": "redis",
                    "RequestedVersion": "7.x",
                    "SelectionStrategy": "latest-matching",
                    "SourceId": "locora-bundled"
                  },
                  {
                    "PackageId": "mailpit",
                    "RequestedVersion": "1.x",
                    "SelectionStrategy": "latest-matching",
                    "SourceId": "locora-bundled"
                  },
                  {
                    "PackageId": "memcached",
                    "RequestedVersion": "1.6.x",
                    "SelectionStrategy": "latest-matching",
                    "SourceId": "locora-bundled"
                  },
                  {
                    "PackageId": "php",
                    "RequestedVersion": "8.3.x",
                    "SelectionStrategy": "latest-matching",
                    "SourceId": "locora-bundled"
                  },
                  {
                    "PackageId": "nodejs",
                    "RequestedVersion": "22.x",
                    "SelectionStrategy": "latest-matching",
                    "SourceId": "locora-bundled"
                  },
                  {
                    "PackageId": "python",
                    "RequestedVersion": "3.12.x",
                    "SelectionStrategy": "latest-matching",
                    "SourceId": "locora-bundled"
                  },
                  {
                    "PackageId": "java",
                    "RequestedVersion": "21.x",
                    "SelectionStrategy": "latest-matching",
                    "SourceId": "locora-bundled"
                  }
                ],
                "InstalledPackages": []
              }
            }
            """);

        EnsureFile(
            Path.Combine(basePath, "package-manifests", "locora.windows-x64.json"),
            """
            {
              "Schema": "locora.package-manifest.v1",
              "ManifestId": "locora.windows-x64",
              "DisplayName": "Locora bundled Windows x64 packages",
              "Channel": "stable",
              "Platform": "windows",
              "Architecture": "x64",
              "Packages": [
                {
                  "PackageId": "nginx",
                  "DisplayName": "Nginx",
                  "Kind": "service",
                  "Family": "nginx",
                  "InstallRootPath": "bin/nginx",
                  "ActiveAliasPath": "bin/nginx/current",
                  "DefaultVersion": "1.27.0",
                  "SupportsSideBySideInstall": true,
                  "SupportsActiveAlias": true,
                  "Tags": [ "web", "http", "service" ],
                  "Description": "Portable Nginx packages used by Locora web-server service definitions.",
                  "Versions": [
                    {
                      "Version": "1.27.0",
                      "Channel": "stable",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/nginx/1.27.0/windows-x64.zip",
                      "RelativeInstallPath": "1.27.0",
                      "ExecutablePath": "nginx.exe",
                      "WorkingDirectoryPath": ".",
                      "ProvidesCommands": [ "nginx.exe" ],
                      "Dependencies": []
                    }
                  ]
                },
                {
                  "PackageId": "apache",
                  "DisplayName": "Apache",
                  "Kind": "service",
                  "Family": "apache",
                  "InstallRootPath": "bin/apache",
                  "ActiveAliasPath": "bin/apache/current",
                  "DefaultVersion": "2.4.0",
                  "SupportsSideBySideInstall": true,
                  "SupportsActiveAlias": true,
                  "Tags": [ "web", "http", "service" ],
                  "Description": "Portable Apache HTTP Server packages for alternate Locora web stacks.",
                  "Versions": [
                    {
                      "Version": "2.4.0",
                      "Channel": "stable",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/apache/2.4.0/windows-x64.zip",
                      "RelativeInstallPath": "2.4.0",
                      "ExecutablePath": "bin/httpd.exe",
                      "StopExecutablePath": "bin/httpd.exe",
                      "WorkingDirectoryPath": "bin",
                      "ProvidesCommands": [ "httpd.exe" ],
                      "Dependencies": []
                    }
                  ]
                },
                {
                  "PackageId": "mariadb",
                  "DisplayName": "MariaDB",
                  "Kind": "service",
                  "Family": "mariadb",
                  "InstallRootPath": "bin/mariadb",
                  "ActiveAliasPath": "bin/mariadb/current",
                  "DefaultVersion": "11.0.0",
                  "SupportsSideBySideInstall": true,
                  "SupportsActiveAlias": true,
                  "Tags": [ "database", "sql", "service" ],
                  "Description": "Portable MariaDB server builds for the default Locora SQL stack.",
                  "Versions": [
                    {
                      "Version": "11.0.0",
                      "Channel": "stable",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/mariadb/11.0.0/windows-x64.zip",
                      "RelativeInstallPath": "11.0.0",
                      "ExecutablePath": "bin/mysqld.exe",
                      "WorkingDirectoryPath": "bin",
                      "ProvidesCommands": [ "mysqld.exe", "mysql.exe" ],
                      "Dependencies": []
                    }
                  ]
                },
                {
                  "PackageId": "postgresql",
                  "DisplayName": "PostgreSQL",
                  "Kind": "service",
                  "Family": "postgresql",
                  "InstallRootPath": "bin/postgresql",
                  "ActiveAliasPath": "bin/postgresql/current",
                  "DefaultVersion": "18.0.0",
                  "SupportsSideBySideInstall": true,
                  "SupportsActiveAlias": true,
                  "Tags": [ "database", "sql", "service" ],
                  "Description": "Portable PostgreSQL server builds with side-by-side version folders.",
                  "Versions": [
                    {
                      "Version": "18.0.0",
                      "Channel": "stable",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/postgresql/18.0.0/windows-x64.zip",
                      "RelativeInstallPath": "18.0.0",
                      "ExecutablePath": "bin/postgres.exe",
                      "StopExecutablePath": "bin/pg_ctl.exe",
                      "WorkingDirectoryPath": "bin",
                      "ProvidesCommands": [ "postgres.exe", "pg_ctl.exe", "initdb.exe" ],
                      "Dependencies": []
                    }
                  ]
                },
                {
                  "PackageId": "redis",
                  "DisplayName": "Redis",
                  "Kind": "service",
                  "Family": "redis",
                  "InstallRootPath": "bin/redis",
                  "ActiveAliasPath": "bin/redis/current",
                  "DefaultVersion": "7.0.0",
                  "SupportsSideBySideInstall": true,
                  "SupportsActiveAlias": true,
                  "Tags": [ "cache", "service", "key-value" ],
                  "Description": "Portable Redis packages for local cache and queue workloads.",
                  "Versions": [
                    {
                      "Version": "7.0.0",
                      "Channel": "stable",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/redis/7.0.0/windows-x64.zip",
                      "RelativeInstallPath": "7.0.0",
                      "ExecutablePath": "redis-server.exe",
                      "StopExecutablePath": "redis-cli.exe",
                      "WorkingDirectoryPath": ".",
                      "ProvidesCommands": [ "redis-server.exe", "redis-cli.exe" ],
                      "Dependencies": []
                    }
                  ]
                },
                {
                  "PackageId": "mailpit",
                  "DisplayName": "Mailpit",
                  "Kind": "tool",
                  "Family": "mailpit",
                  "InstallRootPath": "bin/mailpit",
                  "ActiveAliasPath": "bin/mailpit/current",
                  "DefaultVersion": "1.0.0",
                  "SupportsSideBySideInstall": true,
                  "SupportsActiveAlias": true,
                  "Tags": [ "mail", "smtp", "tool" ],
                  "Description": "Mailpit builds for local SMTP capture and inbox workflows.",
                  "Versions": [
                    {
                      "Version": "1.0.0",
                      "Channel": "stable",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/mailpit/1.0.0/windows-x64.zip",
                      "RelativeInstallPath": "1.0.0",
                      "ExecutablePath": "mailpit.exe",
                      "WorkingDirectoryPath": ".",
                      "ProvidesCommands": [ "mailpit.exe" ],
                      "Dependencies": []
                    }
                  ]
                },
                {
                  "PackageId": "composer",
                  "DisplayName": "Composer",
                  "Kind": "tool",
                  "Family": "composer",
                  "InstallRootPath": "bin/composer",
                  "ActiveAliasPath": "bin/composer/current",
                  "DefaultVersion": "2.8.0",
                  "SupportsSideBySideInstall": true,
                  "SupportsActiveAlias": true,
                  "Tags": [ "tool", "php", "composer" ],
                  "Description": "Composer CLI builds for PHP dependency management inside Locora project terminals.",
                  "Versions": [
                    {
                      "Version": "2.8.0",
                      "Channel": "stable",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/composer/2.8.0/windows-x64.zip",
                      "RelativeInstallPath": "2.8.0",
                      "ExecutablePath": "composer.bat",
                      "WorkingDirectoryPath": ".",
                      "ProvidesCommands": [ "composer.bat" ],
                      "Dependencies": [ "php" ]
                    }
                  ]
                },
                {
                  "PackageId": "memcached",
                  "DisplayName": "Memcached",
                  "Kind": "service",
                  "Family": "memcached",
                  "InstallRootPath": "bin/memcached",
                  "ActiveAliasPath": "bin/memcached/current",
                  "DefaultVersion": "1.6.0",
                  "SupportsSideBySideInstall": true,
                  "SupportsActiveAlias": true,
                  "Tags": [ "cache", "service", "memory" ],
                  "Description": "Portable Memcached builds for simple local caching scenarios.",
                  "Versions": [
                    {
                      "Version": "1.6.0",
                      "Channel": "stable",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/memcached/1.6.0/windows-x64.zip",
                      "RelativeInstallPath": "1.6.0",
                      "ExecutablePath": "memcached.exe",
                      "WorkingDirectoryPath": ".",
                      "ProvidesCommands": [ "memcached.exe" ],
                      "Dependencies": []
                    }
                  ]
                },
                {
                  "PackageId": "php",
                  "DisplayName": "PHP",
                  "Kind": "runtime",
                  "Family": "php",
                  "InstallRootPath": "bin/php",
                  "ActiveAliasPath": "bin/php/current",
                  "DefaultVersion": "8.3.0",
                  "SupportsSideBySideInstall": true,
                  "SupportsActiveAlias": true,
                  "Tags": [ "runtime", "php", "cli" ],
                  "Description": "Side-by-side PHP runtimes for per-project version switching.",
                  "Versions": [
                    {
                      "Version": "8.3.0",
                      "Channel": "stable",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/php/8.3.0/windows-x64.zip",
                      "RelativeInstallPath": "8.3.0",
                      "ExecutablePath": "php.exe",
                      "WorkingDirectoryPath": ".",
                      "ProvidesCommands": [ "php.exe", "php-cgi.exe" ],
                      "Dependencies": []
                    },
                    {
                      "Version": "8.4.0",
                      "Channel": "stable",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/php/8.4.0/windows-x64.zip",
                      "RelativeInstallPath": "8.4.0",
                      "ExecutablePath": "php.exe",
                      "WorkingDirectoryPath": ".",
                      "ProvidesCommands": [ "php.exe", "php-cgi.exe" ],
                      "Dependencies": []
                    }
                  ]
                },
                {
                  "PackageId": "nodejs",
                  "DisplayName": "Node.js",
                  "Kind": "runtime",
                  "Family": "nodejs",
                  "InstallRootPath": "bin/nodejs",
                  "ActiveAliasPath": "bin/nodejs/current",
                  "DefaultVersion": "22.0.0",
                  "SupportsSideBySideInstall": true,
                  "SupportsActiveAlias": true,
                  "Tags": [ "runtime", "node", "javascript" ],
                  "Description": "Side-by-side Node.js runtimes for local app tooling and dev servers.",
                  "Versions": [
                    {
                      "Version": "20.0.0",
                      "Channel": "lts",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/nodejs/20.0.0/windows-x64.zip",
                      "RelativeInstallPath": "20.0.0",
                      "ExecutablePath": "node.exe",
                      "WorkingDirectoryPath": ".",
                      "ProvidesCommands": [ "node.exe", "npm.cmd", "npx.cmd" ],
                      "Dependencies": []
                    },
                    {
                      "Version": "22.0.0",
                      "Channel": "stable",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/nodejs/22.0.0/windows-x64.zip",
                      "RelativeInstallPath": "22.0.0",
                      "ExecutablePath": "node.exe",
                      "WorkingDirectoryPath": ".",
                      "ProvidesCommands": [ "node.exe", "npm.cmd", "npx.cmd" ],
                      "Dependencies": []
                    }
                  ]
                },
                {
                  "PackageId": "python",
                  "DisplayName": "Python",
                  "Kind": "runtime",
                  "Family": "python",
                  "InstallRootPath": "bin/python",
                  "ActiveAliasPath": "bin/python/current",
                  "DefaultVersion": "3.12.0",
                  "SupportsSideBySideInstall": true,
                  "SupportsActiveAlias": true,
                  "Tags": [ "runtime", "python", "cli" ],
                  "Description": "Side-by-side Python runtimes for local scripts, package installs, and app tooling.",
                  "Versions": [
                    {
                      "Version": "3.11.0",
                      "Channel": "stable",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/python/3.11.0/windows-x64.zip",
                      "RelativeInstallPath": "3.11.0",
                      "ExecutablePath": "python.exe",
                      "WorkingDirectoryPath": ".",
                      "ProvidesCommands": [ "python.exe", "pythonw.exe", "pip.exe" ],
                      "Dependencies": []
                    },
                    {
                      "Version": "3.12.0",
                      "Channel": "stable",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/python/3.12.0/windows-x64.zip",
                      "RelativeInstallPath": "3.12.0",
                      "ExecutablePath": "python.exe",
                      "WorkingDirectoryPath": ".",
                      "ProvidesCommands": [ "python.exe", "pythonw.exe", "pip.exe" ],
                      "Dependencies": []
                    }
                  ]
                },
                {
                  "PackageId": "java",
                  "DisplayName": "Java",
                  "Kind": "runtime",
                  "Family": "java",
                  "InstallRootPath": "bin/java",
                  "ActiveAliasPath": "bin/java/current",
                  "DefaultVersion": "21.0.0",
                  "SupportsSideBySideInstall": true,
                  "SupportsActiveAlias": true,
                  "Tags": [ "runtime", "java", "jdk" ],
                  "Description": "Side-by-side Java runtimes for Spring, Gradle, Maven, and other JVM-based project tooling.",
                  "Versions": [
                    {
                      "Version": "17.0.0",
                      "Channel": "lts",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/java/17.0.0/windows-x64.zip",
                      "RelativeInstallPath": "17.0.0",
                      "ExecutablePath": "bin/java.exe",
                      "WorkingDirectoryPath": "bin",
                      "ProvidesCommands": [ "java.exe", "javac.exe", "jar.exe", "jshell.exe" ],
                      "Dependencies": []
                    },
                    {
                      "Version": "21.0.0",
                      "Channel": "lts",
                      "Architecture": "x64",
                      "ArchiveType": "zip",
                      "ArtifactPath": "archives/java/21.0.0/windows-x64.zip",
                      "RelativeInstallPath": "21.0.0",
                      "ExecutablePath": "bin/java.exe",
                      "WorkingDirectoryPath": "bin",
                      "ProvidesCommands": [ "java.exe", "javac.exe", "jar.exe", "jshell.exe" ],
                      "Dependencies": []
                    }
                  ]
                }
              ]
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
