using System.Text.Json;

namespace Thalamus.Core.Commands;

public sealed record CommandEnvelope(int Version, AppCommand Command)
{
    public const int CurrentVersion = 1;

    public static CommandEnvelope Create(AppCommand command) => new(CurrentVersion, command);

    public string Serialize() => JsonSerializer.Serialize(this);

    public static CommandEnvelope? Deserialize(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<CommandEnvelope>(json);
            return value?.Version == CurrentVersion ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
