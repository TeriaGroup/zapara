using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Zapara.Server.Timetable;

namespace Zapara.Server.Tests;

internal static class ApiTestFactory
{
    internal static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static WebApplicationFactory<Program> Create(TimetableConfiguration configuration, TimeProvider clock)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimetableConfiguration>();
            services.AddSingleton(configuration);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(clock);
        }));

    internal static async Task<Guid> PublishAsync(PostgresFixture db, ValidatedSnapshot? snapshot = null)
    {
        await using var lease = await db.Store.TryAcquireAsync(Ct);
        Assert.NotNull(lease);
        return await db.Store.PublishAsync(lease, snapshot ?? TestSnapshotFactory.Create(db.Clock), Ct);
    }

    internal static async Task<JsonElement> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.Clone();
    }

    internal static async Task ProblemAsync(HttpClient client, string path, HttpStatusCode status, string code)
    {
        using var response = await client.GetAsync(path, Ct);
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = json.RootElement;
        Keys(root, "title", "status", "code");
        Assert.Equal((int)status, root.GetProperty("status").GetInt32());
        Assert.Equal(code, root.GetProperty("code").GetString());
        Assert.Matches("[А-Яа-я]", root.GetProperty("title").GetString()!);
    }

    internal static void Keys(JsonElement json, params string[] keys)
        => Assert.Equal(keys.Order(StringComparer.Ordinal), json.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));

    internal static async Task<string> DatabaseStateAsync(PostgresFixture db)
        => await db.ScalarAsync<string>($"""
            SELECT json_build_object('snapshots',(SELECT json_agg(s ORDER BY snapshot_id) FROM {db.QuotedSchema}.snapshots s),
              'attempts',(SELECT json_agg(a ORDER BY sequence) FROM {db.QuotedSchema}.refresh_attempts a),
              'state',(SELECT json_agg(s) FROM {db.QuotedSchema}.state s),
              'version',(SELECT json_agg(v) FROM {db.QuotedSchema}.schema_version v))::text
            """, Ct);
}
