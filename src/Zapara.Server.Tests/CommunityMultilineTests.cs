using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;
using Zapara.Contracts.Communities;
using Zapara.Server.Communities;
using static Zapara.Server.Tests.AccountTestSupport;

namespace Zapara.Server.Tests;

public sealed class CommunityMultilineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Browser_cookie_route_keeps_paragraphs_and_exposes_only_own_optional_state()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await db.Accounts.Migrations.EnsureAsync(Ct);
        await using var host = new WebAccountHost(db.Accounts, moduleSettings: new()
        {
            ["Communities:Enabled"] = "true", ["Communities:Schema"] = db.Schema
        });
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new Zapara.Contracts.Accounts.RegisterRequest("paragraph_browser", WebAccountHost.Password));
        var session = await host.Login("paragraph_browser");
        var user = session.GetProperty("user").GetProperty("userId").GetGuid();
        var community = Guid.NewGuid();
        await db.SeedCommunityAsync(community);
        await db.SeedStaffAsync(community, user);
        var announcement = await host.Send("POST", $"/communities/{community}/announcements", 201,
            new AnnouncementUpsert("Новость", "Первый абзац\nВторой абзац", 0));
        Assert.Equal("Первый абзац\nВторой абзац", announcement.GetProperty("body").GetString());
        var list = await host.Send("GET", $"/communities/{community}/announcements", 200);
        Assert.Equal("Первый абзац\nВторой абзац", list[0].GetProperty("body").GetString());
        var ownRequest = await host.Send("GET", $"/communities/{community}/join-request", 200);
        Assert.Equal(JsonValueKind.Null, ownRequest.GetProperty("request").ValueKind);
    }

    [Fact]
    public void Body_normalizes_crlf_and_keeps_lf_tabs_without_relaxing_other_controls()
    {
        Assert.Equal("Первая строка\n\tВторая строка", new HomeworkUpsert("Задание", "Первая строка\r\n\tВторая строка", 0).Body);
        Assert.Throws<ArgumentException>(() => new HomeworkUpsert("Задание\nдва", "Текст", 0));
        Assert.Throws<ArgumentException>(() => new HomeworkUpsert("Задание", "Текст\u0001", 0));
        Assert.Throws<ArgumentException>(() => new AnnouncementUpsert("Новость", "Текст\rразрыв", 0));
        Assert.Throws<ArgumentException>(() => new AnnouncementUpsert("Новость", new string('а', 8001), 0));
    }

    [Fact]
    public async Task Migration_two_preserves_rows_and_accepts_only_canonical_multiline_bodies()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync();
        await db.Migrations.EnsureAsync(Ct, targetVersion: 1);
        var accounts = new Zapara.Server.Accounts.AccountService(db.Accounts.DataSource, db.Accounts.Configuration, new AccountClock());
        var staff = await Seed(accounts, "upgrade.paragraphs");
        var community = Guid.NewGuid();
        await db.SeedCommunityAsync(community);
        await db.SeedStaffAsync(community, staff.User.UserId);
        var service = new CommunityService(accounts, db.Configuration);
        var old = await service.PublishHomeworkAsync(staff.AccessToken, community, new("Старое", "Сохранить текст", 0), Ct);
        await db.Migrations.EnsureAsync(Ct);
        Assert.Equal("Сохранить текст", (await service.GetHomeworkAsync(staff.AccessToken, community, old.HomeworkId, Ct)).Body);
        var created = await service.PublishHomeworkAsync(staff.AccessToken, community, new("Новое", "Первый абзац\n\tВторой", 0), Ct);
        Assert.Equal("Первый абзац\n\tВторой", await db.Accounts.ScalarAsync<string>($"SELECT body FROM {db.QuotedSchema}.shared_homework WHERE homework_id='{created.HomeworkId}'"));
        Assert.Equal(2L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.schema_migrations"));
        await db.Migrations.EnsureAsync(Ct);
        Assert.Equal(2L, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.schema_migrations"));
    }

    [Fact]
    public async Task V2_keeps_paragraphs_while_v1_projects_a_legacy_readable_body()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync(true);
        await using var host = await CommunityApiTestHost.StartAsync(db, clock: new AccountClock());
        var staff = await Seed(host.Accounts, "paragraphs.staff");
        var community = Guid.NewGuid();
        await db.SeedCommunityAsync(community);
        await db.SeedStaffAsync(community, staff.User.UserId);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/communities/{community}/homework");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staff.AccessToken);
        request.Content = new ByteArrayContent(CommunityJson.Serialize(new HomeworkUpsert("Тема", "Первый абзац\n\tВторой абзац", 0)));
        request.Content.Headers.ContentType = new("application/json");
        using var response = await host.Client.SendAsync(request, Ct);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        var modern = CommunityJson.Parse<HomeworkResponse>(await response.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal("Первый абзац\n\tВторой абзац", modern.Body);
        var legacy = await host.Get<HomeworkResponse>($"/{community}/homework/{modern.HomeworkId}", staff.AccessToken);
        Assert.Equal("Первый абзац  Второй абзац", legacy.Body);
        Assert.DoesNotContain(legacy.Body, char.IsControl);
        Assert.Equal(modern.Body, await db.Accounts.ScalarAsync<string>($"SELECT body FROM {db.QuotedSchema}.shared_homework WHERE homework_id='{modern.HomeworkId}'"));
    }

    [Fact]
    public async Task Migration_two_catalog_matches_its_pinned_postgresql_shape()
    {
        await using var db = await CommunityPostgresFixture.CreateAsync();
        await db.Migrations.EnsureAsync(Ct, targetVersion: 1);
        using var resource = typeof(CommunitiesMigrations).Assembly.GetManifestResourceStream("Zapara.Server.Communities.Sql.002_multiline_bodies.sql")!;
        using var reader = new StreamReader(resource);
        await db.Accounts.ExecuteAsync((await reader.ReadToEndAsync(Ct)).Replace("__COM__", db.QuotedSchema, StringComparison.Ordinal));
        await using var connection = db.Accounts.DataSource.CreateConnection();
        await connection.OpenAsync(Ct);
        await using var tx = await connection.BeginTransactionAsync(Ct);
        var fingerprint = await CommunitiesSchemaShape.FingerprintAsync(connection, tx, db.Configuration, Ct);
        Assert.True(CommunitiesSchemaShape.Expected == fingerprint, $"PostgreSQL16 Communities002 fingerprint: {fingerprint}");
    }
}
