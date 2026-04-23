namespace Locora.App.Models;

public sealed record ValidationResultCard(
    string Name,
    string State,
    string Summary,
    string Details,
    string CheckedAt,
    string Key = "");
