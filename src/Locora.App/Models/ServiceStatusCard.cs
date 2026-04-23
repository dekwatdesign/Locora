namespace Locora.App.Models;

public sealed record ServiceStatusCard(
    string Key,
    string Name,
    string Version,
    string Port,
    string StateLabel,
    string Note,
    bool AutoStart)
{
    public string AutoStartLabel => AutoStart ? "Auto-start: enabled" : "Auto-start: disabled";

    public bool IsMailpitService => Key.Equals("mailpit", StringComparison.OrdinalIgnoreCase);

    public bool SupportsDatabaseAdminTool =>
        Key.Equals("mariadb", StringComparison.OrdinalIgnoreCase) ||
        Key.Equals("mysql", StringComparison.OrdinalIgnoreCase) ||
        Key.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
        Key.Equals("postgres", StringComparison.OrdinalIgnoreCase);

    public string RepairAutomationName => $"Repair service {Name}";

    public string StartAutomationName => $"Start service {Name}";

    public string StopAutomationName => $"Stop service {Name}";

    public string OpenDatabaseAdminAutomationName => $"Open database admin tool for {Name}";

    public string CopyDatabaseConnectionAutomationName => $"Copy database connection settings for {Name}";

    public string OpenDatabaseDetailsAutomationName => $"Open database connection details for {Name}";

    public string OpenMailpitInboxAutomationName => $"Open Mailpit inbox for {Name}";

    public string CopyMailpitSmtpAutomationName => $"Copy Mailpit SMTP settings for {Name}";

    public bool SupportsRepair =>
        Key.Equals("nginx", StringComparison.OrdinalIgnoreCase) ||
        Key.Equals("apache", StringComparison.OrdinalIgnoreCase) ||
        Key.Equals("mariadb", StringComparison.OrdinalIgnoreCase) ||
        Key.Equals("mysql", StringComparison.OrdinalIgnoreCase) ||
        Key.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
        Key.Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
        Key.Equals("redis", StringComparison.OrdinalIgnoreCase) ||
        Key.Equals("memcached", StringComparison.OrdinalIgnoreCase) ||
        Key.Equals("mailpit", StringComparison.OrdinalIgnoreCase);
}
