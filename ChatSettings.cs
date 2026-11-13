using System.IO;
using System.Text.Json;

namespace Smiley;

public enum ChatProviderKind { Demonstration, Ollama }
public sealed record ChatSettings(ChatProviderKind Provider = ChatProviderKind.Demonstration, string Model = "");
internal static class ChatSettingsStore
{
    private static readonly string Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smiley", "chat-settings.json");
    internal static ChatSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<ChatSettings>(File.ReadAllText(Path));
            return settings is not null && Enum.IsDefined(settings.Provider) && settings.Model is { Length: <= 256 } ? settings : new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    internal static bool Save(ChatSettings settings)
    {
        string temporary = Path + ".tmp";
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings));
            File.Move(temporary, Path, true); return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
