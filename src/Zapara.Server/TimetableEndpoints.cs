using System.Collections.Immutable;
using Npgsql;
using Zapara.Server.Timetable;

namespace Zapara.Server;

internal static class TimetableEndpoints
{
    internal static void MapTimetableEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Json(new { status = "live" }));
        app.MapGet("/health/ready", (Delegate)((HttpContext context) =>
            ReadAsync(context, false, _ => Results.Json(new { status = "ready" }))));
        app.MapGet("/api/v1/status", (Delegate)((HttpContext context) =>
            ReadAsync(context, false, snapshot => Results.Json(new StatusResponse(snapshot.Meta, snapshot.Refresh)))));
        app.MapGet("/api/v1/groups", (Delegate)((HttpContext context) => ReadAsync(context, true, snapshot =>
            Results.Json(new GroupsResponse(snapshot.Payload.Period, snapshot.Meta, snapshot.Refresh,
                snapshot.Payload.Groups.OrderBy(group => group.Name, StringComparer.Ordinal)
                    .ThenBy(group => group.Id, StringComparer.Ordinal).ToImmutableArray())))));
        app.MapGet("/api/v1/groups/{groupId}/timetable", (HttpContext context, string groupId) =>
            ReadAsync(context, true, snapshot => Timetable(snapshot, groupId)));
    }

    private static IResult Timetable(SnapshotRead snapshot, string groupId)
    {
        var group = snapshot.Payload.Groups.FirstOrDefault(group => group.Id == groupId);
        if (group is null) return ApiErrors.GroupNotFound();
        var lessons = snapshot.Payload.Lessons.Where(lesson => lesson.GroupId == groupId)
            .Select(lesson => lesson.Value).OrderBy(lesson => lesson.DayOfWeek)
            .ThenBy(lesson => lesson.Parity).ThenBy(lesson => lesson.Index).ToImmutableArray();
        return Results.Json(new TimetableResponse(snapshot.Payload.Period, snapshot.Meta, snapshot.Refresh, group, lessons));
    }

    private static async Task<IResult> ReadAsync(HttpContext context, bool allowPin, Func<SnapshotRead, IResult> response)
    {
        context.Response.Headers.CacheControl = "no-store";
        var ct = context.RequestAborted;
        ct.ThrowIfCancellationRequested();
        Guid? pin = null;
        if (allowPin && context.Request.Query.TryGetValue("snapshotId", out var values))
        {
            if (values.Count != 1 || !Guid.TryParse(values[0], out var parsed))
                return ApiErrors.InvalidSnapshotId();
            pin = parsed;
        }

        SnapshotStore store;
        try
        {
            // Resolve only after query validation; liveness/startup never require valid DB configuration.
            store = context.RequestServices.GetRequiredService<SnapshotStore>();
        }
        catch (ArgumentException)
        {
            ct.ThrowIfCancellationRequested();
            return ApiErrors.DatabaseUnavailable();
        }

        try
        {
            var selection = await store.ReadSelectionAsync(pin, ct);
            ct.ThrowIfCancellationRequested();
            if (selection.CurrentId is null) return ApiErrors.SnapshotUnavailable();
            if (selection.Selected is null) return ApiErrors.SnapshotNotFound();
            return response(selection.Selected);
        }
        catch (Exception error) when (error is NpgsqlException or StoreException)
        {
            ct.ThrowIfCancellationRequested();
            return ApiErrors.DatabaseUnavailable();
        }
    }
}
