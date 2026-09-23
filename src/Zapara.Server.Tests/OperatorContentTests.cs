using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Zapara.Server.Accounts;
using Zapara.Server.Operator;
using Zapara.Server.Social;
using Zapara.Server.Storage;

namespace Zapara.Server.Tests;

public sealed class OperatorContentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Password = "Synthetic password canary 987!";

    [Fact]
    public async Task Signed_in_content_round_trips_through_the_api_and_quota_uses_the_real_group()
    {
        await using var communities = await CommunityPostgresFixture.CreateAsync(true);
        var op = "op_" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), "zapara-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var handler = new Bucket();
        await communities.Accounts.ExecuteAsync($"CREATE SCHEMA {op}");
        await communities.Accounts.ExecuteAsync($"""
            CREATE TABLE {op}.system_settings (
                key text PRIMARY KEY,
                value text NOT NULL,
                updated_at timestamptz NOT NULL
            )
            """);
        await Setting(communities, op, "s3_endpoint", "http://127.0.0.1:9");
        await Setting(communities, op, "s3_region", "ru-central1");
        await Setting(communities, op, "s3_bucket", "zapara-bucket");
        await Setting(communities, op, "s3_access_key", "AKIAEXAMPLE");
        await Setting(communities, op, "s3_secret", "s3-secret-value");
        await Setting(communities, op, "vk_enabled", "true");
        await Setting(communities, op, "vk_client_id", "vk-app");
        await Setting(communities, op, "vk_callback", "https://voen.teriahost.ru/auth/vk/callback");
        await Setting(communities, op, "yandex_enabled", "true");
        await Setting(communities, op, "yandex_client_id", "ya-app");
        await Setting(communities, op, "yandex_callback", "https://voen.teriahost.ru/auth/yandex/callback");
        var configuration = new Dictionary<string, string?>
        {
            ["Accounts:Enabled"] = "true",
            ["Accounts:Schema"] = communities.Accounts.Schema,
            ["Communities:Enabled"] = "true",
            ["Communities:Schema"] = communities.Schema,
            ["ConnectionStrings:Accounts"] = Environment.GetEnvironmentVariable("ZAPARA_TEST_POSTGRES"),
            ["Operator:Schema"] = op,
            ["Social:MediaRoot"] = root,
        };
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Accounts:Enabled", "true");
            builder.UseSetting("Communities:Enabled", "true");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<RoutingObjectStore>();
                services.RemoveAll<IObjectStore>();
                services.RemoveAll<IContentArchive>();
                services.AddSingleton(sp => new RoutingObjectStore(sp.GetRequiredService<IConfiguration>(), _ => new HttpClient(handler, disposeHandler: false)));
                services.AddSingleton<IObjectStore>(sp => sp.GetRequiredService<RoutingObjectStore>());
                services.AddSingleton<IContentArchive>(sp => sp.GetRequiredService<RoutingObjectStore>());
            });
        });
        try
        {
            using var client = factory.CreateClient();
            var open = await Send(client, "GET", "/api/v1/auth/capabilities", 200);
            Assert.True(open.GetProperty("vk").GetBoolean());
            Assert.True(open.GetProperty("yandex").GetBoolean());
            await communities.Accounts.ExecuteAsync($"UPDATE {op}.system_settings SET value='false' WHERE key='vk_enabled'");
            var closed = await Send(client, "GET", "/api/v1/auth/capabilities", 200);
            Assert.False(closed.GetProperty("vk").GetBoolean());
            Assert.True(closed.GetProperty("yandex").GetBoolean());
            await communities.Accounts.ExecuteAsync($"UPDATE {op}.system_settings SET value='true' WHERE key='vk_enabled'");
            Assert.True(factory.Services.GetRequiredService<RoutingObjectStore>().RemoteConfigured());

            var left = await Register(client, "left.user");
            var right = await Register(client, "right.user");
            var home = await Send(client, "GET", "/api/v1/social/home", 200, bearer: left);
            var code = home.GetProperty("code").GetString()!;
            await Send(client, "POST", "/api/v1/social/invites", 200, bearer: right, body: new { code });
            var incoming = await Send(client, "GET", "/api/v1/social/home", 200, bearer: left);
            var friendship = incoming.GetProperty("incoming")[0].GetProperty("friendshipId").GetGuid();
            await Send(client, "POST", "/api/v1/social/invites/" + friendship.ToString("D") + "/accept", 200, bearer: left);
            var friends = await Send(client, "GET", "/api/v1/social/home", 200, bearer: left);
            var conversation = friends.GetProperty("friends")[0].GetProperty("conversationId").GetGuid();
            const string text = "Пара перенесена на четверг";
            await Send(client, "POST", "/api/v1/social/conversations/" + conversation.ToString("D") + "/messages", 201, bearer: left, body: new { body = text });
            var page = await Send(client, "GET", "/api/v1/social/conversations/" + conversation.ToString("D") + "/messages", 200, bearer: left);
            Assert.Equal(text, page.GetProperty("messages")[0].GetProperty("body").GetString());
            Assert.Contains(handler.Calls, call => call.Method == "PUT" && Encoding.UTF8.GetString(call.Body) == text);

            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
            var image = await Upload(client, "/api/v1/social/conversations/" + conversation.ToString("D") + "/images", left, png, "photo.png", "image/png");
            var attachment = image.GetProperty("attachmentId").GetGuid();
            var downloaded = await Raw(client, "GET", "/api/v1/social/attachments/" + attachment.ToString("D"), left);
            var storedPhoto = handler.Calls.Last(call => call.Method == "PUT" && call.Path.EndsWith(".webp", StringComparison.Ordinal)).Body;
            Assert.Equal(storedPhoto, downloaded);

            var fileBytes = "конспект"u8.ToArray();
            var file = await Upload(client, "/api/v1/social/conversations/" + conversation.ToString("D") + "/files", left, fileBytes, "notes.txt", "text/plain");
            var fileId = file.GetProperty("attachmentId").GetGuid();
            Assert.Equal(fileBytes, await Raw(client, "GET", "/api/v1/social/attachments/" + fileId.ToString("D"), left));
            var movie = new byte[16];
            "ftypisom"u8.CopyTo(movie.AsSpan(4));
            var circle = await Upload(client, "/api/v1/social/conversations/" + conversation.ToString("D") + "/circles", left, movie, "note.mp4", "video/mp4", "1000");
            var circleId = circle.GetProperty("attachmentId").GetGuid();
            Assert.Equal(movie, await Raw(client, "GET", "/api/v1/social/attachments/" + circleId.ToString("D"), left));
            var imageMessage = image.GetProperty("messageId").GetGuid();
            await Send(client, "POST", "/api/v1/social/conversations/" + conversation.ToString("D") + "/messages/" + imageMessage.ToString("D") + "/delete", 200, bearer: left);
            Assert.Contains(handler.Calls, call => call.Method == "DELETE" && call.Path.EndsWith(".webp", StringComparison.Ordinal));

            var me = await Send(client, "GET", "/api/v1/account/me", 200, bearer: left);
            var userId = me.GetProperty("user").GetProperty("userId").GetGuid();
            var communityId = Guid.NewGuid();
            await communities.SeedCommunityAsync(communityId);
            await communities.SeedCatalogAsync(communityId);
            await communities.SeedStaffAsync(communityId, userId);
            const string homework = "Доделать лабу по базам";
            var published = await Send(client, "POST", "/api/v1/communities/" + communityId.ToString("D") + "/homework", 201, bearer: left,
                body: new { title = "Базы данных", body = homework, expectedRevision = 0 });
            var homeworkId = published.GetProperty("homeworkId").GetGuid();
            Assert.Equal(homework, published.GetProperty("body").GetString());
            var loaded = await Send(client, "GET", "/api/v1/communities/" + communityId.ToString("D") + "/homework/" + homeworkId.ToString("D"), 200, bearer: left);
            Assert.Equal(homework, loaded.GetProperty("body").GetString());
            Assert.Contains(handler.Calls, call => call.Method == "PUT" && Encoding.UTF8.GetString(call.Body) == homework);

            var homeworkFile = "файл задания"u8.ToArray();
            var saved = await Upload(client, "/api/v1/homework-files", left, homeworkFile, "task.txt", "text/plain", community: communityId, homework: homeworkId, status: HttpStatusCode.OK);
            var name = saved.GetProperty("name").GetString()!;
            Assert.Equal(homeworkFile, await Raw(client, "GET", "/api/v1/homework-files/" + name, null));
            var userBytes = await communities.Accounts.ScalarAsync<long>($"SELECT bytes FROM {op}.quota_counters WHERE scope='user' AND scope_id='{userId:D}'");
            var groupBytes = await communities.Accounts.ScalarAsync<long>($"SELECT bytes FROM {op}.quota_counters WHERE scope='group' AND scope_id='O3313'");
            Assert.True(userBytes >= homeworkFile.Length);
            Assert.True(groupBytes >= homeworkFile.Length);

            await Setting(communities, op, "quota_user_bytes", userBytes.ToString());
            await Setting(communities, op, "quota_group_bytes", (groupBytes + 100).ToString());
            var overUser = await UploadStatus(client, "/api/v1/homework-files", left, new byte[] { 1, 2 }, "more.txt", communityId, homeworkId);
            Assert.Equal(413, overUser.Status);
            Assert.Contains("студента", overUser.Title);
            Assert.Equal(userBytes, await communities.Accounts.ScalarAsync<long>($"SELECT bytes FROM {op}.quota_counters WHERE scope='user' AND scope_id='{userId:D}'"));
            Assert.Equal(groupBytes, await communities.Accounts.ScalarAsync<long>($"SELECT bytes FROM {op}.quota_counters WHERE scope='group' AND scope_id='O3313'"));

            await Setting(communities, op, "quota_user_bytes", (userBytes + 100).ToString());
            await Setting(communities, op, "quota_group_bytes", groupBytes.ToString());
            var overGroup = await UploadStatus(client, "/api/v1/homework-files", left, new byte[] { 3 }, "group.txt", communityId, homeworkId);
            Assert.Equal(413, overGroup.Status);
            Assert.Contains("группы", overGroup.Title);
            Assert.Equal(userBytes, await communities.Accounts.ScalarAsync<long>($"SELECT bytes FROM {op}.quota_counters WHERE scope='user' AND scope_id='{userId:D}'"));
            Assert.Equal(1L, await communities.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {op}.quota_counters WHERE scope='group'"));

            var alone = await Register(client, "alone.user");
            var aloneMe = await Send(client, "GET", "/api/v1/account/me", 200, bearer: alone);
            var aloneId = aloneMe.GetProperty("user").GetProperty("userId").GetGuid();
            var uploads = factory.Services.GetRequiredService<StudentUpload>();
            var accounts = factory.Services.GetRequiredService<IAccountUnitOfWork>();
            var personal = new byte[] { 4, 5, 6 };
            await uploads.Accept(accounts, alone, null, "alonebin", personal, Ct);
            Assert.Equal(personal.Length, await communities.Accounts.ScalarAsync<long>($"SELECT bytes FROM {op}.quota_counters WHERE scope='user' AND scope_id='{aloneId:D}'"));
            Assert.Equal(1L, await communities.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {op}.quota_counters WHERE scope='group'"));

            var opened = await Send(client, "POST", "/api/v1/support", 200, bearer: left, body: new { subject = "Кнопка не нажимается", body = "На сводке кнопка чётности не отвечает." });
            var threadId = opened.GetProperty("id").GetGuid();
            var store = factory.Services.GetRequiredService<SupportStore>();
            await store.ReplyAsync(threadId, "Поправили переключатель.", Ct);
            await Send(client, "POST", "/api/v1/support/" + threadId.ToString("D"), 200, bearer: left, body: new { body = "Теперь нажимается." });
            var threads = await Send(client, "GET", "/api/v1/support", 200, bearer: left);
            var messages = threads[0].GetProperty("messages");
            Assert.Equal(3, messages.GetArrayLength());
            Assert.Equal("user", messages[0].GetProperty("author").GetString());
            Assert.Equal("На сводке кнопка чётности не отвечает.", messages[0].GetProperty("body").GetString());
            Assert.Equal("operator", messages[1].GetProperty("author").GetString());
            Assert.Equal("Поправили переключатель.", messages[1].GetProperty("body").GetString());
            Assert.Equal("user", messages[2].GetProperty("author").GetString());
            Assert.Equal("Теперь нажимается.", messages[2].GetProperty("body").GetString());
            Assert.Contains(handler.Calls, call => call.Method == "PUT" && Encoding.UTF8.GetString(call.Body) == "На сводке кнопка чётности не отвечает.");
            Assert.Contains(handler.Calls, call => call.Method == "PUT" && Encoding.UTF8.GetString(call.Body) == "Поправили переключатель.");
        }
        finally
        {
            await communities.Accounts.ExecuteAsync($"DROP SCHEMA IF EXISTS {op} CASCADE");
        }
    }

    private static async Task Setting(CommunityPostgresFixture db, string schema, string key, string value)
        => await db.Accounts.ExecuteAsync($"""
            INSERT INTO {schema}.system_settings(key,value,updated_at) VALUES ('{key}','{value.Replace("'", "''")}',CURRENT_TIMESTAMP)
            ON CONFLICT (key) DO UPDATE SET value=EXCLUDED.value, updated_at=CURRENT_TIMESTAMP
            """);

    private static async Task<string> Register(HttpClient client, string username)
    {
        await Send(client, "POST", "/api/v1/auth/register", 201, body: new { username, password = Password, displayName = "Студент" });
        var session = await Send(client, "POST", "/api/v1/auth/login", 200, body: new
        {
            username,
            password = Password,
            device = new { deviceId = Guid.NewGuid(), deviceName = "Проверка", platform = "windows" }
        });
        return session.GetProperty("accessToken").GetString()!;
    }

    private static async Task<JsonElement> Send(HttpClient client, string method, string path, int status, string? bearer = null, object? body = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal(status, (int)response.StatusCode);
        if (status == 204 || text.Length == 0) return default;
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> Upload(HttpClient client, string path, string bearer, byte[] bytes, string fileName, string type, string? duration = null, Guid? community = null, Guid? homework = null, HttpStatusCode status = HttpStatusCode.Created)
    {
        using var form = new MultipartFormDataContent();
        if (community is Guid communityId) form.Add(new StringContent(communityId.ToString("D")), "communityId");
        if (homework is Guid homeworkId) form.Add(new StringContent(homeworkId.ToString("D")), "homeworkId");
        if (duration is not null) form.Add(new StringContent(duration), "durationMs");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal(status, response.StatusCode);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static async Task<(int Status, string Title)> UploadStatus(HttpClient client, string path, string bearer, byte[] bytes, string fileName, Guid community, Guid homework)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(community.ToString("D")), "communityId");
        form.Add(new StringContent(homework.ToString("D")), "homeworkId");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        using var document = JsonDocument.Parse(text);
        return ((int)response.StatusCode, document.RootElement.GetProperty("title").GetString() ?? "");
    }

    private static async Task<byte[]> Raw(HttpClient client, string method, string path, string? bearer)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync(Ct);
    }

    private sealed class Bucket : HttpMessageHandler
    {
        public List<(string Method, string Path, byte[] Body)> Calls { get; } = [];
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => SendAsync(request, cancellationToken).GetAwaiter().GetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Calls.Add((request.Method.Method, path, body));
            if (request.Method == HttpMethod.Put)
            {
                files[path] = body;
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            if (request.Method == HttpMethod.Delete)
            {
                files.Remove(path);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return files.TryGetValue(path, out var stored)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(stored) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
