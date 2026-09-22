using System.Net;
using Vograph.Timetable;
using Xunit;
using Zapara.Server.Timetable;
using static Zapara.Server.Tests.JsonTimetableInputTests;

namespace Zapara.Server.Tests;

public sealed class JsonTimetablePublicationTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task New_json_provenance_publishes_under_the_existing_exclusive_lease()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        using var http = new HttpClient(new InputHandler((request, _) => Task.FromResult(Response(request,
            request.RequestUri!.AbsoluteUri == VoenmehScheduleClient.MetaUrl ? Meta : Lessons))));
        var snapshot = await new JsonTimetableInput(db.Clock).FetchAsync(http, Ct);
        await using var lease = await db.Store.TryAcquireAsync(Ct);
        Assert.NotNull(lease);
        var id = await db.Store.PublishAsync(lease, snapshot, Ct);
        var current = Assert.IsType<SnapshotRead>(await db.Store.ReadCurrentAsync(Ct));
        Assert.Equal(id, current.Meta.SnapshotId);
        Assert.Equal(VoenmehScheduleClient.MetaUrl, current.Meta.SourceUrl);
        Assert.Equal("http", current.Meta.SourceKind);
        Assert.Equal(2, current.Payload.Groups.Length);
        Assert.Equal(2, await db.ScalarAsync<int>($"SELECT version FROM {db.QuotedSchema}.schema_version", Ct));
        await lease.DisposeAsync();
        await db.Store.EnsureSchemaAsync(Ct);
    }

    [Fact]
    public async Task Upgrade_from_real_v1_preserves_published_snapshot_and_rejects_unknown_http_source()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, initialize: false, ct: Ct);
        using var stream = typeof(SnapshotStore).Assembly.GetManifestResourceStream("Zapara.Server.Timetable.Sql.001_timetable.sql")!;
        using var reader = new StreamReader(stream);
        await db.ExecuteAsync((await reader.ReadToEndAsync(Ct)).Replace("{{schema}}", db.QuotedSchema)
            .Replace("{{url}}", "'" + TimetableParser.DefaultUrl + "'"), Ct);
        Guid before;
        await using (var lease = await db.Store.TryAcquireAsync(Ct))
            before = await db.Store.PublishAsync(Assert.IsType<RefreshLease>(lease), TestSnapshotFactory.Create(db.Clock), Ct);
        await db.Store.EnsureSchemaAsync(Ct);
        Assert.Equal(before, (await db.Store.ReadCurrentAsync(Ct))!.Meta.SnapshotId);
        Assert.Equal(2, await db.ScalarAsync<int>($"SELECT version FROM {db.QuotedSchema}.schema_version", Ct));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.ExecuteAsync($"UPDATE {db.QuotedSchema}.snapshots SET source_kind='http', source_url='http://127.0.0.1/private'", Ct));
        output.WriteLine(await db.ScalarAsync<string>($"SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE connamespace='{db.Schema}'::regnamespace AND conname='snapshot_provenance'", Ct));
    }
}
