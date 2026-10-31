using System.Text.Json.Serialization;

namespace ZeiLand.Settings;

[JsonConverter(typeof(JsonStringEnumConverter<ShortcutKind>))]
public enum ShortcutKind
{
    App,
    File,
    Folder,
    Website,
}

/// <summary>One of the user's launch buttons in the open island.</summary>
/// <param name="Target">App: its Start menu id (AppUserModelID). File / folder: the path. Website: the address.</param>
public sealed record ShortcutItem(ShortcutKind Kind, string Target, string Name);
