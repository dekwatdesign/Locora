using Locora.Application.Abstractions;
using Locora.Supervisor.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Locora.Supervisor.Services;

public sealed class ServiceConfigurationWriter
{
    private readonly IEnvironmentPaths _paths;
    private readonly ManagedServicesOptions _options;
    private readonly ILogger<ServiceConfigurationWriter> _logger;

    public ServiceConfigurationWriter(
        IEnvironmentPaths paths,
        IOptions<ManagedServicesOptions> options,
        ILogger<ServiceConfigurationWriter> logger)
    {
        _paths = paths;
        _options = options.Value;
        _logger = logger;
    }

    public async Task GenerateAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var definition in _options.Services)
        {
            await GenerateForDefinitionAsync(definition, cancellationToken);
        }
    }

    public async Task<bool> GenerateForServiceAsync(string serviceKey, CancellationToken cancellationToken = default)
    {
        var definition = _options.Services.FirstOrDefault(service =>
            service.Key.Equals(serviceKey, StringComparison.OrdinalIgnoreCase));

        if (definition is null)
        {
            throw new KeyNotFoundException($"Managed service '{serviceKey}' is not configured.");
        }

        return await GenerateForDefinitionAsync(definition, cancellationToken);
    }

    private async Task<bool> GenerateForDefinitionAsync(ManagedServiceDefinition definition, CancellationToken cancellationToken)
    {
        switch (definition.Kind.ToLowerInvariant())
        {
            case "nginx":
                await GenerateNginxConfigAsync(definition, cancellationToken);
                return true;
            case "apache":
                await GenerateApacheConfigAsync(definition, cancellationToken);
                return true;
            case "mariadb":
            case "mysql":
                await GenerateMariaDbConfigAsync(definition, cancellationToken);
                return true;
            case "postgresql":
            case "postgres":
                await GeneratePostgreSqlConfigAsync(definition, cancellationToken);
                return true;
            case "redis":
                await GenerateRedisConfigAsync(definition, cancellationToken);
                return true;
            case "memcached":
                await GenerateMemcachedArtifactsAsync(definition, cancellationToken);
                return true;
            case "mailpit":
                await GenerateMailpitArtifactsAsync(definition, cancellationToken);
                return true;
            default:
                return false;
        }
    }

    private async Task GenerateNginxConfigAsync(ManagedServiceDefinition definition, CancellationToken cancellationToken)
    {
        var nginxExecutable = ResolvePath(definition.RelativeExecutablePath);
        var nginxRoot = Path.GetDirectoryName(nginxExecutable) ?? Path.Combine(_paths.BinRoot, "nginx", "current");
        var configRoot = Path.Combine(_paths.ConfigRoot, "nginx");
        var tempRoot = Path.Combine(_paths.TempRoot, "nginx");

        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(Path.Combine(tempRoot, "client_temp"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "proxy_temp"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "fastcgi_temp"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "uwsgi_temp"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "scgi_temp"));

        var configPath = Path.Combine(configRoot, "nginx.conf");
        var mimeTypesFile = Path.Combine(nginxRoot, "conf", "mime.types");
        var includeMimeTypes = File.Exists(mimeTypesFile)
            ? $"include       \"{ToNginxPath(mimeTypesFile)}\";"
            : string.Empty;
        var vhostsRoot = ToNginxPath(Path.Combine(configRoot, "vhosts"));
        var projectRoot = ToNginxPath(_paths.ProjectRoot);
        var tempPath = ToNginxPath(tempRoot);

        var content = $$"""
        worker_processes  1;

        error_log  "{{ToNginxPath(_paths.GetServiceErrorLogPath(definition.Key))}}";
        pid        "{{ToNginxPath(Path.Combine(tempRoot, "nginx.pid"))}}";

        events {
            worker_connections  1024;
        }

        http {
            {{includeMimeTypes}}
            default_type  application/octet-stream;

            access_log    "{{ToNginxPath(_paths.GetServiceOutputLogPath(definition.Key))}}";
            sendfile      on;
            keepalive_timeout  65;
            client_body_temp_path "{{tempPath}}/client_temp";
            proxy_temp_path       "{{tempPath}}/proxy_temp";
            fastcgi_temp_path     "{{tempPath}}/fastcgi_temp";
            uwsgi_temp_path       "{{tempPath}}/uwsgi_temp";
            scgi_temp_path        "{{tempPath}}/scgi_temp";

            server {
                listen       {{definition.Port ?? 80}};
                server_name  localhost *.locora.test;
                root         "{{projectRoot}}";
                index        index.php index.html index.htm;

                location / {
                    try_files $uri $uri/ /index.php?$query_string;
                }

                location ~ \.php$ {
                    return 501;
                }
            }

            include "{{vhostsRoot}}/*.conf";
        }
        """;

        await File.WriteAllTextAsync(configPath, content, cancellationToken);
        _logger.LogInformation("Generated Nginx config at {Path}", configPath);
    }

    private async Task GenerateApacheConfigAsync(ManagedServiceDefinition definition, CancellationToken cancellationToken)
    {
        var apacheExecutable = ResolvePath(definition.RelativeExecutablePath);
        var binDirectory = Path.GetDirectoryName(apacheExecutable) ?? Path.Combine(_paths.BinRoot, "apache", "current", "bin");
        var serverRoot = Directory.GetParent(binDirectory)?.FullName ?? Path.Combine(_paths.BinRoot, "apache", "current");
        var modulesRoot = Path.Combine(serverRoot, "modules");
        var bundledConfigRoot = Path.Combine(serverRoot, "conf");
        var configRoot = Path.Combine(_paths.ConfigRoot, "apache");
        var tempRoot = Path.Combine(_paths.TempRoot, "apache");

        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(tempRoot);

        var configPath = Path.Combine(configRoot, "httpd.conf");
        var mimeTypesPath = Path.Combine(bundledConfigRoot, "mime.types");
        var moduleLines = BuildApacheLoadModuleLines(modulesRoot);
        var mimeTypesDirective = File.Exists(mimeTypesPath)
            ? $"    TypesConfig \"{ToApachePath(mimeTypesPath)}\""
            : "    # mime.types not found in the Apache package";

        var content = $$"""
        ServerRoot "{{ToApachePath(serverRoot)}}"
        Listen {{definition.Port ?? 8080}}
        ServerName localhost:{{definition.Port ?? 8080}}
        PidFile "{{ToApachePath(Path.Combine(tempRoot, "httpd.pid"))}}"
        ErrorLog "{{ToApachePath(_paths.GetServiceErrorLogPath(definition.Key))}}"

        {{moduleLines}}

        <IfModule mime_module>
        {{mimeTypesDirective}}
        </IfModule>

        DocumentRoot "{{ToApachePath(_paths.ProjectRoot)}}"
        <Directory "{{ToApachePath(_paths.ProjectRoot)}}">
            Options FollowSymLinks
            AllowOverride None
            Require all granted
            DirectoryIndex index.html index.htm
        </Directory>

        <FilesMatch "\.php$">
            Require all denied
        </FilesMatch>
        """;

        await File.WriteAllTextAsync(configPath, content, cancellationToken);
        _logger.LogInformation("Generated Apache config at {Path}", configPath);
    }

    private async Task GenerateMariaDbConfigAsync(ManagedServiceDefinition definition, CancellationToken cancellationToken)
    {
        var mariadbExecutable = ResolvePath(definition.RelativeExecutablePath);
        var binDirectory = Path.GetDirectoryName(mariadbExecutable) ?? Path.Combine(_paths.BinRoot, "mariadb", "current", "bin");
        var baseDirectory = Directory.GetParent(binDirectory)?.FullName ?? Path.Combine(_paths.BinRoot, "mariadb", "current");
        var configRoot = Path.Combine(_paths.ConfigRoot, "mariadb");
        var dataRoot = Path.Combine(_paths.DataRoot, "mariadb");
        var tempRoot = Path.Combine(_paths.TempRoot, "mariadb");

        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(tempRoot);

        var configPath = Path.Combine(configRoot, "my.ini");
        var content = $$"""
        [client]
        port={{definition.Port ?? 3306}}
        default-character-set=utf8mb4

        [mysqld]
        port={{definition.Port ?? 3306}}
        basedir="{{ToWindowsPath(baseDirectory)}}"
        datadir="{{ToWindowsPath(dataRoot)}}"
        tmpdir="{{ToWindowsPath(tempRoot)}}"
        bind-address=127.0.0.1
        character-set-server=utf8mb4
        collation-server=utf8mb4_unicode_ci
        skip-name-resolve
        log-error="{{ToWindowsPath(_paths.GetServiceErrorLogPath(definition.Key))}}"
        general_log_file="{{ToWindowsPath(_paths.GetServiceOutputLogPath(definition.Key))}}"
        general_log=0
        max_connections=100
        sql_mode=STRICT_TRANS_TABLES,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION
        """;

        await File.WriteAllTextAsync(configPath, content, cancellationToken);
        _logger.LogInformation("Generated MariaDB config at {Path}", configPath);
    }

    private async Task GeneratePostgreSqlConfigAsync(ManagedServiceDefinition definition, CancellationToken cancellationToken)
    {
        var configRoot = Path.Combine(_paths.ConfigRoot, "postgresql");
        var dataRoot = Path.Combine(_paths.DataRoot, "postgresql", "data");
        var tempRoot = Path.Combine(_paths.TempRoot, "postgresql");

        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(tempRoot);

        var configPath = Path.Combine(configRoot, "postgresql.conf");
        var hbaPath = Path.Combine(configRoot, "pg_hba.conf");
        var connectionDetailsPath = Path.Combine(configRoot, "connection-details.md");

        var configContent = $$"""
        listen_addresses = '127.0.0.1'
        port = {{definition.Port ?? 5432}}
        max_connections = 100
        shared_buffers = 128MB
        logging_collector = off
        log_destination = 'stderr'
        ssl = off
        hba_file = '{{ToPostgresPath(hbaPath)}}'
        external_pid_file = '{{ToPostgresPath(Path.Combine(tempRoot, "postgresql.pid"))}}'
        """;

        var hbaContent = """
        # Locora local development defaults
        host    all             all             127.0.0.1/32            trust
        host    all             all             ::1/128                 trust
        """;

        var connectionDetails = $$"""
        # Locora PostgreSQL

        Host: 127.0.0.1
        Port: {{definition.Port ?? 5432}}
        Database: postgres
        Username: postgres
        Password: (none, local trust auth)
        URL: postgresql://postgres@127.0.0.1:{{definition.Port ?? 5432}}/postgres
        Data directory: {{dataRoot}}
        Config file: {{configPath}}
        HBA file: {{hbaPath}}
        """;

        await File.WriteAllTextAsync(configPath, configContent, cancellationToken);
        await File.WriteAllTextAsync(hbaPath, hbaContent, cancellationToken);
        await File.WriteAllTextAsync(connectionDetailsPath, connectionDetails, cancellationToken);
        _logger.LogInformation("Generated PostgreSQL config at {Path}", configPath);
    }

    private async Task GenerateRedisConfigAsync(ManagedServiceDefinition definition, CancellationToken cancellationToken)
    {
        var configRoot = Path.Combine(_paths.ConfigRoot, "redis");
        var dataRoot = Path.Combine(_paths.DataRoot, "redis");
        var tempRoot = Path.Combine(_paths.TempRoot, "redis");

        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(tempRoot);

        var configPath = Path.Combine(configRoot, "redis.conf");
        var content = $$"""
        bind 127.0.0.1
        protected-mode yes
        port {{definition.Port ?? 6379}}
        tcp-backlog 511
        timeout 0
        tcp-keepalive 300
        daemonize no
        supervised no
        pidfile "{{ToWindowsPath(Path.Combine(tempRoot, "redis.pid"))}}"
        loglevel notice
        logfile ""
        databases 16
        save 900 1
        save 300 10
        save 60 10000
        stop-writes-on-bgsave-error no
        rdbcompression yes
        dbfilename dump.rdb
        dir "{{ToWindowsPath(dataRoot)}}"
        appendonly no
        """;

        await File.WriteAllTextAsync(configPath, content, cancellationToken);
        _logger.LogInformation("Generated Redis config at {Path}", configPath);
    }

    private async Task GenerateMemcachedArtifactsAsync(ManagedServiceDefinition definition, CancellationToken cancellationToken)
    {
        var configRoot = Path.Combine(_paths.ConfigRoot, "memcached");
        var tempRoot = Path.Combine(_paths.TempRoot, "memcached");

        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(tempRoot);

        var bindHost = NormalizeLoopbackHost(ExpandToken(TryGetArgumentValue(definition.Arguments, "-l") ?? "127.0.0.1", definition));
        var tcpPort = definition.Port ?? ExtractIntValue(TryGetArgumentValue(definition.Arguments, "-p"), 11211);
        var udpPort = ExtractIntValue(TryGetArgumentValue(definition.Arguments, "-U"), 0);
        var memoryLimitMb = ExtractIntValue(TryGetArgumentValue(definition.Arguments, "-m"), 64);
        var maxConnections = ExtractIntValue(TryGetArgumentValue(definition.Arguments, "-c"), 1024);
        var executablePath = ResolvePath(definition.RelativeExecutablePath, definition);
        var startupArgumentsPath = Path.Combine(configRoot, "startup-arguments.txt");
        var connectionDetailsPath = Path.Combine(configRoot, "connection-details.md");

        var startupArguments = string.Join(
            Environment.NewLine,
            definition.Arguments.Select(argument => ExpandToken(argument, definition)));

        var connectionDetails = $$"""
        # Locora Memcached

        Host: {{bindHost}}
        Port: {{tcpPort}}
        UDP port: {{(udpPort == 0 ? "disabled" : udpPort.ToString())}}
        Memory limit: {{memoryLimitMb}} MB
        Max connections: {{maxConnections}}
        Binary: {{executablePath}}
        Startup arguments file: {{startupArgumentsPath}}
        Service stdout log: {{_paths.GetServiceOutputLogPath(definition.Key)}}
        Service stderr log: {{_paths.GetServiceErrorLogPath(definition.Key)}}
        """;

        await File.WriteAllTextAsync(startupArgumentsPath, startupArguments + Environment.NewLine, cancellationToken);
        await File.WriteAllTextAsync(connectionDetailsPath, connectionDetails, cancellationToken);
        _logger.LogInformation("Generated Memcached connection details at {Path}", connectionDetailsPath);
    }

    private async Task GenerateMailpitArtifactsAsync(ManagedServiceDefinition definition, CancellationToken cancellationToken)
    {
        var configRoot = Path.Combine(_paths.ConfigRoot, "mailpit");
        var dataRoot = Path.Combine(_paths.DataRoot, "mailpit");

        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(dataRoot);

        var smtpBindAddress = ExpandToken(TryGetArgumentValue(definition.Arguments, "--smtp") ?? $"127.0.0.1:{definition.Port ?? 1025}", definition);
        var uiBindAddress = ExpandToken(TryGetArgumentValue(definition.Arguments, "--listen") ?? "127.0.0.1:8025", definition);
        var databaseArgument = TryGetArgumentValue(definition.Arguments, "--database") ?? "{data}/mailpit/mailpit.db";
        var databasePath = ResolvePath(databaseArgument, definition);

        if (!string.IsNullOrWhiteSpace(Path.GetDirectoryName(databasePath)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        }

        var connectionDetailsPath = Path.Combine(configRoot, "connection-details.md");
        var connectionDetails = $$"""
        # Locora Mailpit

        SMTP host: {{ExtractHost(smtpBindAddress)}}
        SMTP port: {{ExtractPort(smtpBindAddress, definition.Port ?? 1025)}}
        Encryption: none
        Username: (none)
        Password: (none)
        Web UI: {{BuildHttpUrl(uiBindAddress)}}
        API: {{BuildHttpUrl(uiBindAddress)}}api/v1/
        Database: {{databasePath}}
        """;

        await File.WriteAllTextAsync(connectionDetailsPath, connectionDetails, cancellationToken);
        _logger.LogInformation("Generated Mailpit connection details at {Path}", connectionDetailsPath);
    }

    private string ResolvePath(string value, ManagedServiceDefinition? definition = null)
    {
        var expanded = ExpandToken(value, definition);
        return Path.IsPathRooted(expanded)
            ? expanded
            : Path.GetFullPath(Path.Combine(_paths.AppRoot, expanded));
    }

    private string ExpandToken(string value, ManagedServiceDefinition? definition = null)
    {
        return value
            .Replace("{root}", _paths.AppRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{bin}", _paths.BinRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{config}", _paths.ConfigRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{logs}", _paths.LogsRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{data}", _paths.DataRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{temp}", _paths.TempRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("{port}", definition?.Port?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{projects}", _paths.ProjectRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetArgumentValue(IReadOnlyList<string> arguments, string flag)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index].Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    private static string BuildHttpUrl(string bindAddress)
    {
        var normalized = bindAddress.StartsWith("0.0.0.0:", StringComparison.OrdinalIgnoreCase)
            ? "127.0.0.1" + bindAddress["0.0.0.0".Length..]
            : bindAddress.StartsWith(":", StringComparison.OrdinalIgnoreCase)
                ? "127.0.0.1" + bindAddress
                : bindAddress;

        return $"http://{normalized.TrimEnd('/')}/";
    }

    private static string ExtractHost(string bindAddress)
    {
        var separatorIndex = bindAddress.LastIndexOf(':');
        if (separatorIndex <= 0)
        {
            return "127.0.0.1";
        }

        var host = bindAddress[..separatorIndex];
        return host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase)
            ? "127.0.0.1"
            : host;
    }

    private static int ExtractPort(string bindAddress, int fallbackPort)
    {
        var separatorIndex = bindAddress.LastIndexOf(':');
        if (separatorIndex < 0 || separatorIndex == bindAddress.Length - 1)
        {
            return fallbackPort;
        }

        return int.TryParse(bindAddress[(separatorIndex + 1)..], out var port)
            ? port
            : fallbackPort;
    }

    private static int ExtractIntValue(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed >= 0
            ? parsed
            : fallback;
    }

    private static string NormalizeLoopbackHost(string host)
    {
        return host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase)
            ? "127.0.0.1"
            : host;
    }

    private static string BuildApacheLoadModuleLines(string modulesRoot)
    {
        var moduleMap = new (string ModuleName, string FileName)[]
        {
            ("mpm_winnt_module", "mod_mpm_winnt.so"),
            ("authz_core_module", "mod_authz_core.so"),
            ("authz_host_module", "mod_authz_host.so"),
            ("dir_module", "mod_dir.so"),
            ("mime_module", "mod_mime.so")
        };

        return string.Join(
            Environment.NewLine,
            moduleMap
                .Where(module => File.Exists(Path.Combine(modulesRoot, module.FileName)))
                .Select(module => $"LoadModule {module.ModuleName} modules/{module.FileName}"));
    }

    private static string ToApachePath(string path) => path.Replace('\\', '/');

    private static string ToPostgresPath(string path) => path.Replace('\\', '/').Replace("'", "''");

    private static string ToNginxPath(string path) => path.Replace('\\', '/');

    private static string ToWindowsPath(string path) => path.Replace('/', '\\');
}
