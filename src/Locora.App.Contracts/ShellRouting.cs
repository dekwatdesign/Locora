namespace Locora.App.Contracts;

public enum LocoraShellDestination
{
    Home,
    Workbench,
    Health,
    Terminal,
    Advanced
}

public sealed record LocoraShellRoute(LocoraShellDestination Destination, string Section)
{
    public string Tag => string.IsNullOrWhiteSpace(Section)
        ? Destination.ToString().ToLowerInvariant()
        : $"{Destination.ToString().ToLowerInvariant()}:{Section}";
}

public sealed record LocoraShellCommandRoute(
    string Title,
    string Description,
    string SearchText,
    string RouteTag);

public static class LocoraShellRoutes
{
    private static readonly Dictionary<string, LocoraShellRoute> LegacyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dashboard"] = new(LocoraShellDestination.Home, "overview"),
        ["services"] = new(LocoraShellDestination.Workbench, "services"),
        ["domains"] = new(LocoraShellDestination.Health, "domains"),
        ["diagnostics"] = new(LocoraShellDestination.Health, "issues"),
        ["logs"] = new(LocoraShellDestination.Advanced, "logs"),
        ["settings"] = new(LocoraShellDestination.Advanced, "settings")
    };

    public static LocoraShellRoute Resolve(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return new LocoraShellRoute(LocoraShellDestination.Home, "overview");
        }

        var normalized = tag.Trim();
        if (LegacyAliases.TryGetValue(normalized, out var alias))
        {
            return alias;
        }

        var parts = normalized.Split([':', '/'], 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return new LocoraShellRoute(LocoraShellDestination.Home, "overview");
        }

        if (!Enum.TryParse<LocoraShellDestination>(parts[0], ignoreCase: true, out var destination))
        {
            return new LocoraShellRoute(LocoraShellDestination.Home, "overview");
        }

        var section = parts.Length > 1 ? NormalizeSection(destination, parts[1]) : DefaultSection(destination);
        return new LocoraShellRoute(destination, section);
    }

    public static IReadOnlyList<LocoraShellCommandRoute> CreateNavigationCommands()
    {
        return
        [
            new("Go to Home", "Open the simple overview, pinned projects, and first-run checklist.", "home dashboard overview projects start stop", "home"),
            new("Go to Workbench", "Open projects, services, and stack profiles.", "workbench projects services stack profiles", "workbench"),
            new("Go to Services", "Manage Nginx, Apache, databases, cache, and Mailpit.", "services start stop nginx apache database cache mailpit", "workbench:services"),
            new("Go to Stack Profiles", "Load and save runtime/service stack profiles.", "stack profiles active services packages start all", "workbench:stack"),
            new("Go to Health", "Inspect issues, ports, permissions, domains, SSL, and recovery actions.", "health diagnostics issues ports permissions domains ssl repair report", "health"),
            new("Go to Domains & SSL", "Manage local domains, hosts preview, vhosts, tunnels, and SSL.", "domains hosts vhosts ssl certificates tunnels", "health:domains"),
            new("Go to Terminal", "Open built-in project terminal sessions.", "terminal conpty shell project command", "terminal"),
            new("Go to Advanced", "Open packages, updates, migration, logs, and raw settings.", "advanced settings packages updates migration logs config", "advanced"),
            new("Go to Runtime Versions", "Open PHP, Node.js, Python, and Java runtime version switching.", "runtime versions switch php node python java packages", "advanced:packages"),
            new("Go to Logs", "Open recent Locora shell activity.", "logs activity stdout stderr supervisor", "advanced:logs"),
            new("Go to Settings", "Open app paths, preferences, integrations, and raw config files.", "settings preferences paths report config", "advanced:settings")
        ];
    }

    private static string NormalizeSection(LocoraShellDestination destination, string section)
    {
        var normalized = section.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return DefaultSection(destination);
        }

        return destination switch
        {
            LocoraShellDestination.Workbench when normalized is "projects" or "services" or "stack" => normalized,
            LocoraShellDestination.Health when normalized is "issues" or "domains" or "ssl" or "ports" or "permissions" or "tunnels" or "reports" => normalized,
            LocoraShellDestination.Advanced when normalized is "settings" or "packages" or "updates" or "migration" or "logs" => normalized,
            LocoraShellDestination.Home => "overview",
            LocoraShellDestination.Terminal => "sessions",
            _ => DefaultSection(destination)
        };
    }

    private static string DefaultSection(LocoraShellDestination destination)
    {
        return destination switch
        {
            LocoraShellDestination.Home => "overview",
            LocoraShellDestination.Workbench => "projects",
            LocoraShellDestination.Health => "issues",
            LocoraShellDestination.Terminal => "sessions",
            LocoraShellDestination.Advanced => "settings",
            _ => "overview"
        };
    }
}

public static class LocoraLanguagePreference
{
    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "en-US",
        "th-TH"
    };

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return "Auto";
        }

        return SupportedLanguages.Contains(value.Trim())
            ? value.Trim()
            : "Auto";
    }

    public static string ResolveOverride(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? string.Empty : normalized;
    }
}
