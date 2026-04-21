using System.Text;
using Locora.Application.Abstractions;
using Locora.Domain.Entities;

namespace Locora.App.Services;

public sealed class DiagnosticReportService : IDiagnosticReportService
{
    private readonly IEnvironmentPaths _paths;
    private readonly IClipboardService _clipboardService;

    public DiagnosticReportService(IEnvironmentPaths paths, IClipboardService clipboardService)
    {
        _paths = paths;
        _clipboardService = clipboardService;
    }

    public string BuildReport(EnvironmentSnapshot snapshot)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# Locora Diagnostic Report");
        builder.AppendLine();
        builder.AppendLine("Generated for support, troubleshooting, and portable runtime repair.");
        builder.AppendLine();

        AppendEnvironment(builder, snapshot);
        AppendServices(builder, snapshot);
        AppendHealthIssues(builder, snapshot);
        AppendValidationResults(builder, snapshot);
        AppendPortDiagnostics(builder, snapshot);
        AppendPermissionDiagnostics(builder, snapshot);
        AppendSslStatus(builder, snapshot);
        AppendProjects(builder, snapshot);
        AppendKeyPaths(builder);

        return builder.ToString();
    }

    public void CopyToClipboard(EnvironmentSnapshot snapshot)
    {
        _clipboardService.CopyText(BuildReport(snapshot));
    }

    public async Task<string> ExportReportAsync(EnvironmentSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var diagnosticsRoot = Path.Combine(_paths.LogsRoot, "diagnostics");
        Directory.CreateDirectory(diagnosticsRoot);

        var fileName = $"locora-diagnostic-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.md";
        var reportPath = Path.Combine(diagnosticsRoot, fileName);

        await File.WriteAllTextAsync(reportPath, BuildReport(snapshot), Encoding.UTF8, cancellationToken);
        return reportPath;
    }

    private void AppendEnvironment(StringBuilder builder, EnvironmentSnapshot snapshot)
    {
        AppendSection(builder, "Environment");
        AppendField(builder, "Generated local", snapshot.GeneratedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
        AppendField(builder, "Generated UTC", $"{snapshot.GeneratedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss}Z");
        AppendField(builder, "Active profile", snapshot.ActiveProfile);
        AppendField(builder, "Environment root", FormatInlineCode(snapshot.EnvironmentRoot));
        AppendField(builder, "Supervisor reachable", FormatBool(snapshot.IsSupervisorReachable));
        AppendField(builder, "Supervisor elevated", FormatBool(snapshot.IsSupervisorElevated));
        AppendField(builder, "Supervisor PID", snapshot.SupervisorProcessId?.ToString() ?? "Unavailable");
        builder.AppendLine();
    }

    private static void AppendServices(StringBuilder builder, EnvironmentSnapshot snapshot)
    {
        AppendSection(builder, "Services");

        if (snapshot.Services.Count == 0)
        {
            builder.AppendLine("No services reported.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| Service | State | Version | Port | Auto-start | Note |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- |");

        foreach (var service in snapshot.Services.OrderBy(service => service.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(
                $"| {EscapeTable(service.DisplayName)} | {EscapeTable(service.State.ToString())} | {EscapeTable(service.Version)} | {EscapeTable(service.Port?.ToString() ?? "None")} | {EscapeTable(FormatBool(service.AutoStart))} | {EscapeTable(service.Note ?? "No note")} |");
        }

        builder.AppendLine();
    }

    private static void AppendHealthIssues(StringBuilder builder, EnvironmentSnapshot snapshot)
    {
        AppendSection(builder, "Health Issues");

        if (snapshot.Issues.Count == 0)
        {
            builder.AppendLine("No health issues reported.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| Severity | Title | Description | Suggested action |");
        builder.AppendLine("| --- | --- | --- | --- |");

        foreach (var issue in snapshot.Issues.OrderByDescending(issue => issue.Severity))
        {
            builder.AppendLine(
                $"| {EscapeTable(issue.Severity.ToString())} | {EscapeTable(issue.Title)} | {EscapeTable(issue.Description)} | {EscapeTable(issue.SuggestedAction)} |");
        }

        builder.AppendLine();
    }

    private static void AppendValidationResults(StringBuilder builder, EnvironmentSnapshot snapshot)
    {
        AppendSection(builder, "Config Validation");

        if (snapshot.ValidationResults.Count == 0)
        {
            builder.AppendLine("No validation results reported.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| Target | State | Summary | Details | Checked |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");

        foreach (var validation in snapshot.ValidationResults.OrderBy(validation => validation.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(
                $"| {EscapeTable(validation.DisplayName)} | {EscapeTable(validation.IsValid ? "Valid" : "Invalid")} | {EscapeTable(validation.Summary)} | {EscapeTable(validation.Details)} | {EscapeTable(validation.CheckedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"))} |");
        }

        builder.AppendLine();
    }

    private static void AppendPortDiagnostics(StringBuilder builder, EnvironmentSnapshot snapshot)
    {
        AppendSection(builder, "Port Diagnostics");

        if (snapshot.PortDiagnostics.Count == 0)
        {
            builder.AppendLine("No port diagnostics reported.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| Check | Port | State | Owner | Summary | Suggested action |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- |");

        foreach (var diagnostic in snapshot.PortDiagnostics.OrderBy(diagnostic => diagnostic.Port).ThenBy(diagnostic => diagnostic.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(
                $"| {EscapeTable(diagnostic.DisplayName)} | {EscapeTable(diagnostic.Port.ToString())} | {EscapeTable(diagnostic.State.ToString())} | {EscapeTable(FormatOwner(diagnostic.OwnerProcessId, diagnostic.OwnerProcessName))} | {EscapeTable(diagnostic.Summary)} | {EscapeTable(diagnostic.SuggestedAction)} |");
        }

        builder.AppendLine();
    }

    private static void AppendPermissionDiagnostics(StringBuilder builder, EnvironmentSnapshot snapshot)
    {
        AppendSection(builder, "Permission Diagnostics");

        if (snapshot.PermissionDiagnostics.Count == 0)
        {
            builder.AppendLine("No permission diagnostics reported.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| Check | State | Summary | Details | Suggested action |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");

        foreach (var diagnostic in snapshot.PermissionDiagnostics.OrderBy(diagnostic => diagnostic.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(
                $"| {EscapeTable(diagnostic.DisplayName)} | {EscapeTable(diagnostic.State.ToString())} | {EscapeTable(diagnostic.Summary)} | {EscapeTable(diagnostic.Details)} | {EscapeTable(diagnostic.SuggestedAction)} |");
        }

        builder.AppendLine();
    }

    private static void AppendSslStatus(StringBuilder builder, EnvironmentSnapshot snapshot)
    {
        AppendSection(builder, "Local SSL");
        AppendField(builder, "Authority", snapshot.SslStatus.AuthorityName);
        AppendField(builder, "Summary", snapshot.SslStatus.Summary);
        AppendField(builder, "Details", snapshot.SslStatus.Details);
        AppendField(builder, "CA exists", FormatBool(snapshot.SslStatus.CaExists));
        AppendField(builder, "CurrentUser trusted", FormatBool(snapshot.SslStatus.IsCurrentUserTrusted));
        AppendField(builder, "Trust checks supported", FormatBool(snapshot.SslStatus.TrustSupported));
        AppendField(builder, "HTTPS projects", snapshot.SslStatus.HttpsProjectCount.ToString());
        AppendField(builder, "Project certificates", snapshot.SslStatus.ProjectCertificateCount.ToString());
        AppendField(builder, "Missing project certificates", snapshot.SslStatus.MissingProjectCertificateCount.ToString());
        AppendField(builder, "Expired or invalid project certificates", snapshot.SslStatus.ExpiredProjectCertificateCount.ToString());
        AppendField(builder, "Expiring project certificates", snapshot.SslStatus.ExpiringProjectCertificateCount.ToString());
        AppendField(builder, "CA certificate path", FormatInlineCode(snapshot.SslStatus.CaCertificatePath));
        AppendField(builder, "Certificates root", FormatInlineCode(snapshot.SslStatus.CertificatesRoot));
        AppendField(
            builder,
            "CA expires",
            snapshot.SslStatus.CaExpiresAt is { } caExpiresAt
                ? caExpiresAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
                : "Unavailable");
        AppendField(
            builder,
            "Last generated",
            snapshot.SslStatus.LastGeneratedAt is { } lastGeneratedAt
                ? lastGeneratedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
                : "Never");
        builder.AppendLine();

        if (snapshot.SslStatus.CertificateDiagnostics.Count == 0)
        {
            builder.AppendLine("No project certificate diagnostics reported.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| Project | Host | State | Reason | Expires | Summary | Suggested action |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");

        foreach (var diagnostic in snapshot.SslStatus.CertificateDiagnostics.OrderBy(diagnostic => diagnostic.ProjectName, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(
                $"| {EscapeTable(diagnostic.ProjectName)} | {EscapeTable(diagnostic.Host)} | {EscapeTable(diagnostic.State.ToString())} | {EscapeTable(diagnostic.Reason)} | {EscapeTable(diagnostic.ExpiresAt?.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unavailable")} | {EscapeTable(diagnostic.Summary)} | {EscapeTable(diagnostic.SuggestedAction)} |");
        }

        builder.AppendLine();
    }

    private static void AppendProjects(StringBuilder builder, EnvironmentSnapshot snapshot)
    {
        AppendSection(builder, "Projects");

        if (snapshot.Projects.Count == 0)
        {
            builder.AppendLine("No projects discovered.");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("| Project | Runtime | URL | HTTPS | Path |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");

        foreach (var project in snapshot.Projects.OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine(
                $"| {EscapeTable(project.Name)} | {EscapeTable(project.Runtime)} | {EscapeTable(project.Url)} | {EscapeTable(FormatBool(project.UsesHttps))} | {EscapeTable(project.Path)} |");
        }

        builder.AppendLine();
    }

    private void AppendKeyPaths(StringBuilder builder)
    {
        AppendSection(builder, "Key Paths");
        AppendField(builder, "App root", FormatInlineCode(_paths.AppRoot));
        AppendField(builder, "User root", FormatInlineCode(_paths.UserRoot));
        AppendField(builder, "Config root", FormatInlineCode(_paths.ConfigRoot));
        AppendField(builder, "Logs root", FormatInlineCode(_paths.LogsRoot));
        AppendField(builder, "Projects root", FormatInlineCode(_paths.ProjectRoot));
        AppendField(builder, "Data root", FormatInlineCode(_paths.DataRoot));
        AppendField(builder, "Bin root", FormatInlineCode(_paths.BinRoot));
        AppendField(builder, "Temp root", FormatInlineCode(_paths.TempRoot));
        AppendField(builder, "App settings", FormatInlineCode(_paths.AppSettingsFile));
        AppendField(builder, "Services settings", FormatInlineCode(_paths.ServicesSettingsFile));
        builder.AppendLine();
    }

    private static void AppendSection(StringBuilder builder, string title)
    {
        builder.AppendLine($"## {title}");
        builder.AppendLine();
    }

    private static void AppendField(StringBuilder builder, string name, string value)
    {
        builder.AppendLine($"- {name}: {NormalizeValue(value)}");
    }

    private static string FormatBool(bool value) => value ? "Yes" : "No";

    private static string FormatOwner(int? processId, string? processName)
    {
        if (processId is null)
        {
            return "None";
        }

        return string.IsNullOrWhiteSpace(processName)
            ? $"PID {processId}"
            : $"{processName} (PID {processId})";
    }

    private static string FormatInlineCode(string value)
    {
        var normalized = NormalizeValue(value).Replace('`', '\'');
        return $"`{normalized}`";
    }

    private static string EscapeTable(string value)
    {
        return NormalizeValue(value)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .ReplaceLineEndings(" ");
    }

    private static string NormalizeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unavailable" : value.Trim();
    }
}
