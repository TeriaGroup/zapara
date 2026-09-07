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

    /// <summary>The network half of a first start, run BEFORE the caller takes the Core gate. Never throws: a dead
    /// network comes back as (null, reason) and the run falls through to the bundled snapshot below.</summary>
    public static async Task<(string? Xml, string? Error)> FetchAsync(AppServices app)
    {
        try
        {
            return ((await app.Refresher.CheckAsync(null)).Xml, null);
        }
        catch (Exception ex)
        {
            app.Log.Error("bootstrap fetch", ex);
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// The Core half: every line here reads or writes SQLite, so the caller runs the whole method under the gate.
    /// The download does not belong in it — this used to call Parser.RefreshAsync(), which fetches, and it was the
    /// last path in the app that held the gate across an HTTP request: on the one launch where there is nothing to
    /// show yet, every other Core call (and AppServices.Dispose, at two seconds) queued behind the 60 s timeout.
    /// Now the caller fetches first with <see cref="FetchAsync"/> and hands the result in, exactly as
    /// ScheduleRefresher + Parser.RefreshAsync(xmlOverride) already do for every later refresh.
    /// </summary>
    /// <param name="timetableXml">What the caller fetched outside the gate, or null (offline, or the fetch failed).</param>
    /// <param name="fetchError">Why there is no XML, for the «данные могут быть устаревшими» line.</param>
    public static async Task<BootstrapResult> RunAsync(AppServices app, string? timetableXml = null, string? fetchError = null)
    {
        var groups = app.Db.GetAllGroups();
        var settings = app.Db.GetSettings();
        // The shell only bootstraps an empty database, where this is always true — a caller that fetched first
        // therefore never wasted the request.
        if (!NeedsRefresh(groups.Count, settings.LastFetchedAt, DateTime.UtcNow))
            return new BootstrapResult(HasData: true, Refreshed: false, Stale: false, Error: null);

        var error = fetchError;
        if (timetableXml is not null)
        {
            try
            {
                await app.Parser.RefreshAsync(xmlOverride: timetableXml);
                return new BootstrapResult(true, true, false, null);
            }
            catch (Exception ex)
            {
                error ??= ex.Message;
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
