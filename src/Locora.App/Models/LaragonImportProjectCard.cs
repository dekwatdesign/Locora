namespace Locora.App.Models;

public sealed record LaragonImportProjectCard(
    string Key,
    string Name,
    string SourcePath,
    string Domain,
    string DocumentRoot,
    string Runtime,
    string Framework,
    string Status,
    IReadOnlyList<string> Tags)
{
    public string DomainLabel => string.IsNullOrWhiteSpace(Domain) ? "Domain: generated" : $"Domain: {Domain}";

    public string DocumentRootLabel => string.IsNullOrWhiteSpace(DocumentRoot) ? "Document root: project root" : $"Document root: {DocumentRoot}";

    public string RuntimeLabel => $"Runtime: {Runtime}";

    public string FrameworkLabel => $"Framework: {Framework}";

    public string TagsLabel => Tags.Count == 0 ? "Tags: none" : $"Tags: {string.Join(", ", Tags)}";
}
