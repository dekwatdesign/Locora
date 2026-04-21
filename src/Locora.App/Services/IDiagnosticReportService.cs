using Locora.Domain.Entities;

namespace Locora.App.Services;

public interface IDiagnosticReportService
{
    string BuildReport(EnvironmentSnapshot snapshot);

    void CopyToClipboard(EnvironmentSnapshot snapshot);

    Task<string> ExportReportAsync(EnvironmentSnapshot snapshot, CancellationToken cancellationToken = default);
}
