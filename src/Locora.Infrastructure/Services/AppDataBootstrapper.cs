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
        Directory.CreateDirectory(_environmentPaths.AliasesRoot);
        Directory.CreateDirectory(_environmentPaths.ShellIntegrationRoot);
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
                                Key = "apache",
                                DisplayName = "Apache",
                                Kind = "apache",
                                Version = "2.4.x",
                                RelativeExecutablePath = "bin/apache/current/bin/httpd.exe",
                                RelativeWorkingDirectory = "bin/apache/current/bin",
                                Arguments = new[] { "-f", "{config}/apache/httpd.conf" },
                                StopArguments = new[] { "-k", "stop", "-f", "{config}/apache/httpd.conf" },
                                Port = 8080,
                                AutoStart = false,
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
                            },
                            new
                            {
                                Key = "memcached",
                                DisplayName = "Memcached",
                                Kind = "memcached",
                                Version = "1.6.x",
                                RelativeExecutablePath = "bin/memcached/current/memcached.exe",
                                RelativeWorkingDirectory = "bin/memcached/current",
                                Arguments = new[]
                                {
                                    "-l", "127.0.0.1",
                                    "-p", "{port}",
                                    "-U", "0",
                                    "-m", "64"
                                },
                                StopArguments = Array.Empty<string>(),
                                Port = 11211,
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
                        GenerateApacheVHosts = true,
                        GenerateHostsPreview = true,
                        DomainSuffix = "locora.test",
                        DefaultScheme = "http",
                        IndexFileNames = new[] { "index.php", "index.html", "index.htm" },
                        IgnoredDirectoryNames = new[] { ".git", ".idea", ".vscode", "node_modules", "vendor" }
                    }
                },
                cancellationToken);
        }

        if (!File.Exists(_environmentPaths.ProfilesSettingsFile))
        {
            await _jsonFileStore.WriteAsync(
                _environmentPaths.ProfilesSettingsFile,
                new
                {
                    LocoraProfiles = new
                    {
                        SchemaVersion = 1,
                        ActiveProfileKey = "full-stack",
                        Profiles = CreateDefaultProfiles()
                    }
                },
                cancellationToken);
        }

        if (!File.Exists(_environmentPaths.TerminalCommandsSettingsFile))
        {
            await _jsonFileStore.WriteAsync(
                _environmentPaths.TerminalCommandsSettingsFile,
                new
                {
                    LocoraTerminal = new
                    {
                        Commands = new object[]
                        {
                            new
                            {
                                Key = "git-status",
                                DisplayName = "Git status",
                                Alias = "gst",
                                Description = "Inspect the selected terminal workspace before making changes.",
                                CommandText = "git status",
                                Scope = "any",
                                Tags = new[] { "git", "status", "workspace" }
                            },
                            new
                            {
                                Key = "php-version",
                                DisplayName = "PHP version",
                                Alias = "phpv",
                                Description = "Print the active PHP runtime version from the injected PATH.",
                                CommandText = "php -v",
                                Scope = "any",
                                Tags = new[] { "php", "runtime", "version" }
                            },
                            new
                            {
                                Key = "node-version",
                                DisplayName = "Node.js version",
                                Alias = "nodev",
                                Description = "Print the active Node.js runtime version from the injected PATH.",
                                CommandText = "node --version",
                                Scope = "any",
                                Tags = new[] { "node", "nodejs", "runtime", "version" }
                            },
                            new
                            {
                                Key = "python-version",
                                DisplayName = "Python version",
                                Alias = "pyv",
                                Description = "Print the active Python runtime version from the injected PATH.",
                                CommandText = "python --version",
                                Scope = "any",
                                Tags = new[] { "python", "runtime", "version" }
                            },
                            new
                            {
                                Key = "java-version",
                                DisplayName = "Java version",
                                Alias = "javav",
                                Description = "Print the active Java runtime version from the injected PATH.",
                                CommandText = "java -version",
                                Scope = "any",
                                Tags = new[] { "java", "runtime", "version" }
                            },
                            new
                            {
                                Key = "composer-version",
                                DisplayName = "Composer version",
                                Alias = "compv",
                                Description = "Print the active Composer tool version from the injected PATH.",
                                CommandText = "composer --version",
                                Scope = "any",
                                Tags = new[] { "composer", "tool", "version" }
                            },
                            new
                            {
                                Key = "composer-install",
                                DisplayName = "Composer install",
                                Alias = "cinst",
                                Description = "Install PHP project dependencies in the selected project terminal tab.",
                                CommandText = "composer install",
                                Scope = "project",
                                Tags = new[] { "composer", "php", "dependencies", "project" }
                            },
                            new
                            {
                                Key = "npm-install",
                                DisplayName = "NPM install",
                                Alias = "npmi",
                                Description = "Install Node.js project dependencies in the selected project terminal tab.",
                                CommandText = "npm install",
                                Scope = "project",
                                Tags = new[] { "npm", "node", "dependencies", "project" }
                            },
                            new
                            {
                                Key = "npm-dev",
                                DisplayName = "NPM dev server",
                                Alias = "npmdev",
                                Description = "Start the common npm development server in the selected project terminal tab.",
                                CommandText = "npm run dev",
                                Scope = "project",
                                Tags = new[] { "npm", "node", "dev", "project" }
                            }
                        }
                    }
                },
                cancellationToken);
        }

        _logger.LogInformation("Portable workspace root prepared at {Root}", _environmentPaths.AppRoot);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static object[] CreateDefaultProfiles()
    {
        return
        [
            new
            {
                Key = "full-stack",
                DisplayName = "Full Stack",
                Description = "Nginx, MariaDB, Mailpit, PHP, Node.js, Python, and Java selections for general local development.",
                ServiceKeys = new[] { "nginx", "mariadb", "mailpit" },
                PackageSelections = new Dictionary<string, string>
                {
                    ["nginx"] = "1.27.x",
                    ["mariadb"] = "11.x",
                    ["mailpit"] = "1.x",
                    ["php"] = "8.3.x",
                    ["nodejs"] = "22.x",
                    ["python"] = "3.12.x",
                    ["java"] = "21.x"
                },
                Tags = new[] { "web", "php", "node", "database", "mail" }
            },
            new
            {
                Key = "php-mariadb",
                DisplayName = "PHP + MariaDB",
                Description = "Lean PHP stack with Nginx, MariaDB, Mailpit, and Composer-ready tooling.",
                ServiceKeys = new[] { "nginx", "mariadb", "mailpit" },
                PackageSelections = new Dictionary<string, string>
                {
                    ["nginx"] = "1.27.x",
                    ["mariadb"] = "11.x",
                    ["mailpit"] = "1.x",
                    ["php"] = "8.3.x"
                },
                Tags = new[] { "php", "mysql", "laravel", "wordpress" }
            },
            new
            {
                Key = "node-postgres",
                DisplayName = "Node.js + PostgreSQL",
                Description = "Node.js app stack with PostgreSQL and Mailpit for API and full-stack JavaScript work.",
                ServiceKeys = new[] { "nginx", "postgresql", "mailpit" },
                PackageSelections = new Dictionary<string, string>
                {
                    ["nginx"] = "1.27.x",
                    ["postgresql"] = "18.x",
                    ["mailpit"] = "1.x",
                    ["nodejs"] = "22.x"
                },
                Tags = new[] { "node", "postgres", "api" }
            }
        ];
    }

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
