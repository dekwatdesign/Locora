namespace Locora.Domain.Entities;

public sealed record ProjectDescriptor(
    string Name,
    string Path,
    string Url,
    string Runtime,
    string Description,
    IReadOnlyList<string> Tags,
    bool UsesHttps);
