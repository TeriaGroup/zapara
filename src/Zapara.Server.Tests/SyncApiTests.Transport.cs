using System.Net;
using System.Text;
using Xunit;
using Zapara.Contracts.Sync;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed partial class SyncApiTests
{
    [Fact]
    public async Task Counted_body_accepts_64KiB_rejects_extra_bytes_and_strict_nested_fields()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        await using var host = new SyncApiTestHost(db);
        var a = (await Seed(host.Accounts)).AccessToken;
        var metadata = await host.Get<SyncMetadata>("/metadata", a);
        var mutation = Put(metadata);
        var raw = SyncJson.Serialize(mutation);
        var padded = Enumerable.Repeat((byte)0x20, 65536).ToArray();
        raw.CopyTo(padded, 0);
        await host.Send("POST", "/mutations", 200, a, padded);
        foreach (var bytes in new[] { padded.Concat(new byte[] { 32 }).ToArray(), new byte[65537] })
        {
            await using var stream = new MemoryStream(bytes);
            var result = await host.Factory.Server.SendAsync(context =>
            {
                context.Request.Method = "POST"; context.Request.Path = "/api/v1/sync/mutations";
                context.Request.Headers.Authorization = "Bearer " + a;
                context.Request.ContentType = "application/json";
                context.Request.Body = stream;
            }, SyncApiTestHost.Ct);
            Assert.Equal(413, result.Response.StatusCode);
            Assert.Equal("no-store", result.Response.Headers.CacheControl);
        }
        var text = Encoding.UTF8.GetString(raw);
        foreach (var invalid in new[]
        {
            text.Replace("\"value\":{", "\"value\":{\"ownerId\":\"secret-body-canary\","),
            text.Replace("\"value\":{", "\"value\":{\"text\":\"secret-body-canary\","),
            text.Replace(mutation.OpId.ToString("D"), mutation.OpId.ToString("N"))
        }) await host.Send("POST", "/mutations", 400, a, Encoding.UTF8.GetBytes(invalid));
        Assert.Equal(1, await Count(db, "sync_receipts"));
        var logs = string.Join('\n', host.Logs);
        Assert.DoesNotContain("secret-body-canary", logs);
        Assert.DoesNotContain("Тест <>&", logs);
        Assert.DoesNotContain(a, logs);
        Assert.DoesNotContain(Password, logs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authenticated_pending_mutation_reauthenticates_inside_UoW(bool retry)
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        await using var host = new SyncApiTestHost(db, observe: true);
        var session = await Seed(host.Accounts);
        var metadata = await host.Get<SyncMetadata>("/metadata", session.AccessToken);
        var mutation = Put(metadata);
        if (retry) await host.Mutate(mutation, session.AccessToken);
        var callbacks = host.Callbacks;
        await using var stream = new GatedBody(SyncJson.Serialize(mutation));
        var response = host.Factory.Server.SendAsync(context =>
        {
            context.Request.Method = "POST"; context.Request.Path = "/api/v1/sync/mutations";
            context.Request.Headers.Authorization = "Bearer " + session.AccessToken;
            context.Request.ContentType = "application/json"; context.Request.Body = stream;
        }, SyncApiTestHost.Ct);
        try
        {
            await stream.Reading.Task.WaitAsync(TimeSpan.FromSeconds(10), SyncApiTestHost.Ct);
            await host.Accounts.RevokeAllAsync(session.AccessToken, SyncApiTestHost.Ct);
        }
        finally { stream.Release.TrySetResult(); }
        Assert.Equal(401, (await response).Response.StatusCode);
        Assert.Equal(callbacks, host.Callbacks);
        Assert.Equal(retry ? 1 : 0, await Count(db, "sync_receipts"));
        Assert.Equal(retry ? 1 : 0, await Count(db, "sync_changes"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Anonymous_timetable_stays_read_only_with_sync_enabled(bool invalidSync)
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        await using var timetable = await PostgresFixture.CreateAsync(ct: SyncApiTestHost.Ct);
        await using var host = new SyncApiTestHost(db, timetable: timetable,
            overrides: invalidSync ? new() { ["Sync:Schema"] = "public" } : null);
        var snapshot = await ApiTestFactory.PublishAsync(timetable);
        var before = await ApiTestFactory.DatabaseStateAsync(timetable);
        host.Client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer za_" + new string('A', 43));
        await ApiTestFactory.GetAsync(host.Client, "/api/v1/groups?snapshotId=" + snapshot);
        await ApiTestFactory.GetAsync(host.Client, "/api/v1/groups/3313/timetable?snapshotId=" + snapshot);
        await ApiTestFactory.GetAsync(host.Client, "/api/v1/status");
        using var response = await host.Client.PostAsync("/api/v1/ingest", null, SyncApiTestHost.Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await ApiTestFactory.DatabaseStateAsync(timetable));
        Assert.Equal(0, await Count(db, "sync_state"));
    }

    [Fact]
    public async Task Explicit_invalid_enablement_fails_closed()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        Assert.Throws<ArgumentException>(() => new SyncApiTestHost(db, overrides: new() { ["Sync:Enabled"] = "invalid" }));
        Assert.Throws<ArgumentException>(() => new SyncApiTestHost(db, overrides: new() { ["Accounts:Enabled"] = "false" }));
        Assert.Equal(0, await Count(db, "sync_state"));
    }

    private sealed class GatedBody(byte[] bytes) : MemoryStream(bytes)
    {
        internal TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Reading.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
