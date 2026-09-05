using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vograph.Desktop.Services;

public enum ThemeChoice { System, Light, Dark }

public sealed record WindowBounds(int X, int Y, int Width, int Height, bool Maximized);

/// <summary>Desktop-only UI preferences. Lives in ui.json next to the DB so the Core schema stays untouched.</summary>
public sealed class UiPrefs
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [JsonIgnore] public string FilePath { get; private set; } = "";

    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public bool SidebarCollapsed { get; set; }
    public bool Animations { get; set; } = true;
    public WindowBounds? Window { get; set; }

    public static UiPrefs Load(string path)
    {
        UiPrefs prefs;
        try
        {
            prefs = File.Exists(path)
                ? JsonSerializer.Deserialize<UiPrefs>(File.ReadAllText(path), Json) ?? new UiPrefs()
                : new UiPrefs();
        }
        catch (Exception)
        {
            // A corrupt prefs file must never prevent startup; defaults win.
            prefs = new UiPrefs();
        }
        prefs.FilePath = path;
        return prefs;
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
