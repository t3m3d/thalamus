using System.Text.Json;

namespace Thalamus.Core.Commands;

public sealed record CommandEnvelope(int Version, AppCommand Command)
{
    public const int CurrentVersion = 1;

    public static CommandEnvelope Create(AppCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new(CurrentVersion, command);
    }

    public string Serialize() => JsonSerializer.Serialize(this);

    public static CommandEnvelope? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var value = JsonSerializer.Deserialize<CommandEnvelope>(json);
            if (value is not { Version: CurrentVersion, Command: not null })
                return null;

            var parsed = CommandParser.Parse(CommandParser.ToArguments(value.Command));
            return parsed.Success && parsed.Command == value.Command ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
