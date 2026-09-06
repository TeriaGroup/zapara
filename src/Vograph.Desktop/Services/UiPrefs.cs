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

    /// <summary>The two daily lesson notifications; on by default, the times themselves live in Core's settings.</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>LAN sync server on :8765 — off by default, restarted at startup when the user left it on.</summary>
    public bool LanSync { get; set; }

    private Action<Exception>? _onSaveError;

    /// <param name="onSaveError">Where a failed Save reports (AppServices wires it to AppLog). Save never throws.</param>
    public static UiPrefs Load(string path, Action<Exception>? onSaveError = null)
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
        prefs._onSaveError = onSaveError;
        return prefs;
    }

    /// <summary>Writes ui.json via a temp file + move (a crash mid-write leaves the old file intact).
    /// Returns false instead of throwing: a prefs write must never take the app down.</summary>
    public bool Save()
    {
        if (string.IsNullOrEmpty(FilePath)) return false;
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _onSaveError?.Invoke(ex);
            return false;
        }
    }
}
