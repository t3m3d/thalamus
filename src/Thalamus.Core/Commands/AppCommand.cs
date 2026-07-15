namespace Thalamus.Core.Commands;

public enum AppCommandKind
{
    ShowOverview,
    TileActive,
    SwitchWorkspace,
    MoveActiveToWorkspace,
    SaveLayout,
    RestoreLayout
}

public enum TileTarget
{
    Left,
    Right,
    Top,
    Bottom,
    LeftThird,
    CenterThird,
    RightThird,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Maximize,
    Restore
}

public enum Direction { Next, Previous }

public sealed record AppCommand(AppCommandKind Kind, string? Argument = null)
{
    public static AppCommand Overview { get; } = new(AppCommandKind.ShowOverview);
}

public sealed record CommandParseResult(AppCommand? Command, string? Error)
{
    public bool Success => Command is not null && Error is null;
}

public static class CommandParser
{
    public static CommandParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return new(AppCommand.Overview, null);

        if (args.Count == 1 && Equal(args[0], "--overview"))
            return new(AppCommand.Overview, null);

        if (args.Count != 2)
            return Failure("Expected one command and, where required, one argument.");

        var command = args[0].ToLowerInvariant();
        var argument = args[1].Trim();
        if (argument.Length == 0)
            return Failure("The command argument cannot be empty.");

        return command switch
        {
            "--tile-active" when TryTile(argument, out var tile) =>
                Success(AppCommandKind.TileActive, TileArgument(tile)),
            "--workspace" when TryDirection(argument, out var direction) =>
                Success(AppCommandKind.SwitchWorkspace, direction.ToString().ToLowerInvariant()),
            "--move-active-workspace" when TryDirection(argument, out var direction) =>
                Success(AppCommandKind.MoveActiveToWorkspace, direction.ToString().ToLowerInvariant()),
            "--save-layout" => Success(AppCommandKind.SaveLayout, argument),
            "--restore-layout" => Success(AppCommandKind.RestoreLayout, argument),
            _ => Failure($"Unknown command or argument: {command} {argument}")
        };
    }

    public static string[] ToArguments(AppCommand command) => command.Kind switch
    {
        AppCommandKind.ShowOverview => ["--overview"],
        AppCommandKind.TileActive => ["--tile-active", command.Argument ?? "restore"],
        AppCommandKind.SwitchWorkspace => ["--workspace", command.Argument ?? "next"],
        AppCommandKind.MoveActiveToWorkspace => ["--move-active-workspace", command.Argument ?? "next"],
        AppCommandKind.SaveLayout => ["--save-layout", command.Argument ?? throw new ArgumentException("Layout name required.")],
        AppCommandKind.RestoreLayout => ["--restore-layout", command.Argument ?? throw new ArgumentException("Layout name required.")],
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    private static bool TryTile(string value, out TileTarget target) =>
        Enum.TryParse(value.Replace("-", string.Empty), true, out target);

    private static bool TryDirection(string value, out Direction direction) =>
        Enum.TryParse(value, true, out direction);

    private static string TileArgument(TileTarget target) => target switch
    {
        TileTarget.LeftThird => "left-third",
        TileTarget.CenterThird => "center-third",
        TileTarget.RightThird => "right-third",
        TileTarget.TopLeft => "top-left",
        TileTarget.TopRight => "top-right",
        TileTarget.BottomLeft => "bottom-left",
        TileTarget.BottomRight => "bottom-right",
        _ => target.ToString().ToLowerInvariant()
    };

    private static bool Equal(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static CommandParseResult Success(AppCommandKind kind, string argument) =>
        new(new AppCommand(kind, argument), null);

    private static CommandParseResult Failure(string error) => new(null, error);
}
