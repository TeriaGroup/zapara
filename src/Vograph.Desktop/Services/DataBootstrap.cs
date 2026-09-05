using System.Globalization;

namespace Vograph.Desktop.Services;

public sealed record BootstrapResult(bool HasData, bool Refreshed, bool Stale, string? Error);

/// <summary>Port of the WPF EnsureDataAsync without hard-coded developer paths.</summary>
public static class DataBootstrap
{
    public static bool NeedsRefresh(int groupCount, string? lastFetchedAt, DateTime utcNow)
    {
        if (groupCount == 0) return true;
        if (!DateTime.TryParse(lastFetchedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last)) return true;
        return (utcNow - last.ToUniversalTime()).TotalDays > 3;
    }

    public static async Task<BootstrapResult> RunAsync(AppServices app, bool allowNetwork = true)
    {
        var groups = app.Db.GetAllGroups();
        var settings = app.Db.GetSettings();
        if (!NeedsRefresh(groups.Count, settings.LastFetchedAt, DateTime.UtcNow))
            return new BootstrapResult(HasData: true, Refreshed: false, Stale: false, Error: null);

        string? error = null;
        if (allowNetwork)
        {
            try
            {
                await app.Parser.RefreshAsync();
                return new BootstrapResult(true, true, false, null);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                app.Log.Error("bootstrap refresh", ex);
            }
        }

        // Offline fallback for a first start: a timetable snapshot shipped next to the exe (optional).
        var bundled = Path.Combine(AppContext.BaseDirectory, "TimetableGroup50.xml");
        if (groups.Count == 0 && File.Exists(bundled))
        {
            try
            {
                await app.Parser.RefreshAsync(xmlOverride: await File.ReadAllTextAsync(bundled));
                return new BootstrapResult(true, true, true, error);
            }
            catch (Exception ex)
            {
                app.Log.Error("bootstrap bundled xml", ex);
                error ??= ex.Message;
            }
        }

        return new BootstrapResult(HasData: groups.Count > 0, Refreshed: false, Stale: true, Error: error);
    }
}
