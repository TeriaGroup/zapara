using System.Net;
using Xunit;
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

public sealed class IngestTests(ITestOutputHelper output)
{
    private static IngestService Service(PostgresFixture db) => new(db.Store, new TimetableInput(db.Clock));
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TT006_Invalid_ingest_keeps_lastgood()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await using (var lease = await db.Store.TryAcquireAsync(Ct))
            await db.Store.PublishAsync(Assert.IsType<RefreshLease>(lease), TestSnapshotFactory.Create(db.Clock), Ct);
        var before = Assert.IsType<SnapshotRead>(await db.Store.ReadCurrentAsync(Ct));
        Assert.Equal(2, await Service(db).IngestFileAsync(PostgresFixture.FixturePath("invalid.xml"), Ct));
        var after = Assert.IsType<SnapshotRead>(await db.Store.ReadCurrentAsync(Ct));
        Assert.Equal(before.Meta.SnapshotId, after.Meta.SnapshotId);
        Assert.Equal("failed", after.Refresh.LastAttemptStatus);
        Assert.Equal("snapshot_malformed", after.Refresh.LastFailureCode);
        Assert.True(after.Meta.Stale);
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.snapshots", Ct));
        await db.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task Valid_A_B_and_repeat_publish_new_generations()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        var service = Service(db);
        Assert.Equal(0, await service.IngestFileAsync(PostgresFixture.FixturePath("valid-a.xml"), Ct));
        var first = Assert.IsType<SnapshotRead>(await db.Store.ReadCurrentAsync(Ct));
        Assert.Equal(2, first.Payload.Groups.Length);
        Assert.Single(first.Payload.Lessons);
        Assert.Equal(0, await service.IngestFileAsync(PostgresFixture.FixturePath("valid-b.xml"), Ct));
        var second = Assert.IsType<SnapshotRead>(await db.Store.ReadCurrentAsync(Ct));
        Assert.Single(second.Payload.Groups);
        Assert.NotEqual(first.Meta.SnapshotId, second.Meta.SnapshotId);
        Assert.Equal(0, await service.IngestFileAsync(PostgresFixture.FixturePath("valid-b.xml"), Ct));
        Assert.Equal(3L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.snapshots", Ct));
        await db.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task Busy_precedes_missing_file_and_HTTP_without_attempt()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        await using var held = await db.Store.TryAcquireAsync(Ct);
        var calls = 0;
        using var http = new HttpClient(new IngestHandler((_, _) => { calls++; throw new IOException(); }));
        Assert.Equal(3, await Service(db).IngestFileAsync("missing-secret-sentinel.xml", Ct));
        Assert.Equal(3, await Service(db).IngestFetchAsync(http, Ct));
        Assert.Equal(0, calls);
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.refresh_attempts", Ct));
        await db.ReceiptAsync(Ct);
    }

    [Theory]
    [InlineData("reject", 2, "source_rejected")]
    [InlineData("timeout", 4, "source_timeout")]
    [InlineData("cancel", 6, "cancelled")]
    [InlineData("valid", 0, null)]
    public async Task TT016_Fetch_composes_input_and_store(string mode, int expected, string? error)
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        using var http = new HttpClient(new IngestHandler(async (request, token) =>
        {
            Assert.Equal(Vograph.Timetable.TimetableParser.DefaultUrl, request.RequestUri!.AbsoluteUri);
            Assert.Equal("running", (await db.Store.ReadSelectionAsync(ct: Ct)).Refresh.LastAttemptStatus);
            if (mode == "timeout") throw new OperationCanceledException();
            if (mode == "cancel") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
            return new HttpResponseMessage(mode == "reject" ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(await File.ReadAllBytesAsync(PostgresFixture.FixturePath("valid-a.xml"), Ct))
            };
        }));
        Assert.Equal(expected, await Service(db).IngestFetchAsync(http, cancellation.Token));
        var selection = await db.Store.ReadSelectionAsync(ct: Ct);
        Assert.Equal(error, selection.Refresh.LastFailureCode);
        Assert.Equal(expected == 0 ? "success" : "failed", selection.Refresh.LastAttemptStatus);
        await db.ReceiptAsync(Ct);
    }

    [Fact]
    public async Task Cancellation_before_lease_creates_no_attempt()
    {
        await using var db = await PostgresFixture.CreateAsync(output.WriteLine, ct: Ct);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Equal(6, await Service(db).IngestFileAsync("missing.xml", cancelled.Token));
        Assert.Null((await db.Store.ReadSelectionAsync(ct: Ct)).Refresh.LastAttemptId);
        await db.ReceiptAsync(Ct);
    }
}

internal sealed class IngestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => send(request, cancellationToken);
}
