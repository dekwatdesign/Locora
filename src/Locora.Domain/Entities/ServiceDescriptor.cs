using Locora.Domain.Enums;

namespace Locora.Domain.Entities;

public sealed record ServiceDescriptor(
    string Key,
    string DisplayName,
    string Version,
    int? Port,
    ServiceState State,
    bool AutoStart,
    string? Note);
