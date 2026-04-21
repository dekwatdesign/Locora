namespace Locora.App.Models;

public sealed record TimelineEntry(
    string Timestamp,
    string Level,
    string Message);
