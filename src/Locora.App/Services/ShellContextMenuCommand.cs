namespace Locora.App.Services;

public sealed record ShellContextMenuCommand(string Action, string TargetPath)
{
    public const string ArgumentPrefix = "--locora-shell";

    public static ShellContextMenuCommand? TryParse(IReadOnlyList<string> args)
    {
        if (args.Count < 3 ||
            !args[0].Equals(ArgumentPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var action = args[1].Trim();
        var targetPath = string.Join(" ", args.Skip(2)).Trim();
        return string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(targetPath)
            ? null
            : new ShellContextMenuCommand(action, targetPath);
    }
}
