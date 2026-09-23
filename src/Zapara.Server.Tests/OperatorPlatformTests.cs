using Microsoft.Extensions.Configuration;
using Xunit;
using Zapara.Server.Operator;
using Zapara.Server.Social;

namespace Zapara.Server.Tests;

public sealed class OperatorPlatformTests
{
    [Fact]
    public void Saved_public_fields_round_trip_and_secrets_are_not_returned()
    {
        var saved = OperatorConfig.Apply(new Dictionary<string, string>(), new Dictionary<string, string?>
        {
            ["vk_enabled"] = "true",
            ["vk_client_id"] = "vk-app",
            ["vk_callback"] = "https://voen.teriahost.ru/auth/vk/callback",
            ["vk_secret"] = "super-secret-value",
            ["yandex_client_id"] = "ya-app",
            ["yandex_callback"] = "https://voen.teriahost.ru/auth/yandex/callback",
            ["yandex_secret"] = "yandex-secret-value",
            ["s3_endpoint"] = "https://s3.example",
            ["s3_region"] = "ru-central1",
            ["s3_bucket"] = "zapara",
            ["s3_access_key"] = "AKIAEXAMPLE",
            ["s3_secret"] = "s3-secret-value",
        });
        var view = OperatorConfig.Public(saved);
        Assert.Equal("vk-app", view["vk_client_id"]);
        Assert.Equal("ya-app", view["yandex_client_id"]);
        Assert.Equal("https://s3.example", view["s3_endpoint"]);
        Assert.Equal("zapara", view["s3_bucket"]);
        Assert.Equal("configured", view["vk_secret"]);
        Assert.Equal("configured", view["yandex_secret"]);
        Assert.Equal("configured", view["s3_secret"]);
        Assert.DoesNotContain(view.Values, value => value.Contains("secret", StringComparison.Ordinal));
        var kept = OperatorConfig.Apply(saved, new Dictionary<string, string?> { ["vk_client_id"] = "vk-2", ["vk_secret"] = "" });
        Assert.Equal("vk-2", kept["vk_client_id"]);
        Assert.Equal("super-secret-value", kept["vk_secret"]);
        Assert.Equal(OperatorConfig.DefaultGroupBytes, OperatorConfig.Quota(saved, "quota_group_bytes", 1));
        Assert.Equal(OperatorConfig.DefaultUserBytes, OperatorConfig.Quota(saved, "quota_user_bytes", 1));
    }

    [Fact]
    public void Quota_stores_under_both_limits_and_refuses_either_limit()
    {
        var store = new MemoryObjectStore();
        var intake = new UploadIntake(store);
        var open = new QuotaState(0, QuotaRules.DefaultUserBytes, 0, QuotaRules.DefaultGroupBytes);
        var small = new byte[] { 1, 2, 3, 4 };
        var kept = intake.Store(open, "file-ok-1", small);
        Assert.True(kept.Stored);
        Assert.Equal(small, store.Get("file-ok-1"));
        var overUser = intake.Store(open with { UserUsed = QuotaRules.DefaultUserBytes - 2 }, "file-user", new byte[3]);
        Assert.False(overUser.Stored);
        Assert.Equal("Превышен лимит трафика студента.", overUser.Reason);
        Assert.False(store.Contains("file-user"));
        var overGroup = intake.Store(open with { GroupUsed = QuotaRules.DefaultGroupBytes - 1 }, "file-group", new byte[2]);
        Assert.False(overGroup.Stored);
        Assert.Equal("Превышен лимит трафика группы.", overGroup.Reason);
        Assert.False(store.Contains("file-group"));
        var tighter = intake.Store(open with { UserLimit = 4 }, "file-tight", new byte[5]);
        Assert.False(tighter.Stored);
        var fits = intake.Store(open with { UserLimit = 8, GroupLimit = 8 }, "file-fit-1", new byte[8]);
        Assert.True(fits.Stored);
        Assert.Equal(8, store.Get("file-fit-1")!.Length);
    }

    [Fact]
    public void Support_reply_is_the_next_message_in_the_thread()
    {
        var desk = new SupportDesk();
        var opened = desk.Open("user-1", "Кнопка не нажимается", "На сводке кнопка чётности не отвечает.", DateTimeOffset.Parse("2026-09-23T10:00:00Z"));
        Assert.Equal(opened.Id, Assert.Single(desk.List()).Id);
        Assert.Equal("user", Assert.Single(opened.Messages).Author);
        var replied = desk.Reply(opened.Id, "Поправили переключатель.", DateTimeOffset.Parse("2026-09-23T10:05:00Z"));
        Assert.Equal(2, replied.Messages.Count);
        Assert.Equal("user", replied.Messages[0].Author);
        Assert.Equal("operator", replied.Messages[1].Author);
        Assert.Equal("Поправили переключатель.", replied.Messages[1].Body);
    }

    [Fact]
    public void Failed_probe_names_the_part_and_schedule_keeps_the_last_success()
    {
        var parser = OperatorStatus.Parser("failed", "source_timeout");
        Assert.False(parser.Working);
        Assert.Contains("source_timeout", parser.Detail);
        var fresh = OperatorStatus.Schedule(true, false, false, DateTimeOffset.Parse("2026-09-23T08:00:00Z"));
        Assert.True(fresh.Working);
        Assert.Contains("2026-09-23 08:00Z", fresh.Detail);
        var stale = OperatorStatus.Schedule(true, true, false, DateTimeOffset.Parse("2026-09-21T08:00:00Z"));
        Assert.False(stale.Working);
        Assert.Contains("устарел", stale.Detail);
        Assert.Contains("2026-09-21 08:00Z", stale.Detail);
        var accounts = OperatorStatus.Probe("accounts", "missing");
        Assert.False(accounts.Working);
        Assert.Contains("accounts", accounts.Detail);
        Assert.DoesNotContain("Работает", accounts.Detail);
        Assert.True(OperatorStatus.Probe("timetable", "present").Working);
        Assert.True(OperatorStatus.Parser("running", null).Working);
        Assert.Contains("обновление идёт", OperatorStatus.Parser("running", null).Detail);
        Assert.True(OperatorStatus.Parser("success", null).Working);
        var none = OperatorStatus.Parser(null, null);
        Assert.False(none.Working);
        Assert.Contains("нет попыток", none.Detail);
        var missing = OperatorStatus.Schedule(false, false, false, null);
        Assert.False(missing.Working);
        Assert.Contains("снимка нет", missing.Detail);
        var failedRefresh = OperatorStatus.Schedule(true, false, true, DateTimeOffset.Parse("2026-09-23T08:00:00Z"));
        Assert.False(failedRefresh.Working);
        Assert.Contains("обновление после успеха не удалось", failedRefresh.Detail);
        Assert.False(OperatorStatus.Probe("sync", "missing").Working);
        Assert.False(OperatorStatus.Probe("communities", "missing").Working);
        Assert.Contains("схема", OperatorStatus.Probe("communities", "missing").Detail);
        var unconfigured = OperatorStatus.Storage("missing");
        Assert.False(unconfigured.Working);
        Assert.Equal("Не настроено", unconfigured.Detail);
        Assert.True(OperatorStatus.Storage("reachable").Working);
        Assert.Equal("Доступно", OperatorStatus.Storage("reachable").Detail);
        var failing = OperatorStatus.Storage("failing", "хранилище не отвечает");
        Assert.False(failing.Working);
        Assert.Contains("хранилище не отвечает", failing.Detail);
        Assert.DoesNotContain("Работает", failing.Detail);
    }

    [Fact]
    public void S3_client_put_get_and_delete_target_the_bucket_and_the_router_uses_it_only_when_settings_are_complete()
    {
        var handler = new Bucket();
        using var http = new HttpClient(handler);
        var remote = new Zapara.Server.Storage.S3ObjectStore(
            new Zapara.Server.Storage.S3Target("http://127.0.0.1:9", "ru-central1", "zapara-bucket", "AKIAEXAMPLE", "s3-secret-value"), http);
        var payload = new byte[] { 9, 8, 7, 6 };
        remote.Put("photo.webp", payload);
        Assert.Equal(payload, remote.Get("photo.webp"));
        remote.Delete("photo.webp");
        Assert.Null(remote.Get("photo.webp"));
        var put = Assert.Single(handler.Calls, call => call.Method == "PUT" && call.Path.Contains("/zapara-bucket/photo.webp", StringComparison.Ordinal) && call.Body.AsSpan().SequenceEqual(payload));
        Assert.Equal("127.0.0.1:9", put.Host);
        Assert.Equal(Signature("PUT", put.Path, put.Host, put.Hash, put.Date, "ru-central1", "s3-secret-value"), put.Signature);
        Assert.NotEqual(Signature("PUT", put.Path, "127.0.0.1", put.Hash, put.Date, "ru-central1", "s3-secret-value"), put.Signature);
        Assert.Contains(handler.Calls, call => call.Method == "GET" && call.Path.Contains("/zapara-bucket/photo.webp", StringComparison.Ordinal));
        Assert.Contains(handler.Calls, call => call.Method == "DELETE" && call.Path.Contains("/zapara-bucket/photo.webp", StringComparison.Ordinal));
        Assert.Equal("s3.example", Zapara.Server.Storage.S3ObjectStore.SignedHost(new Uri("https://s3.example/bucket")));

        var root = Path.Combine(Path.GetTempPath(), "zapara-store-" + Guid.NewGuid().ToString("N"));
        var complete = StoreConfig(root, true);
        var routed = new Zapara.Server.Storage.RoutingObjectStore(complete, _ => new HttpClient(handler));
        Assert.True(routed.RemoteConfigured());
        routed.Put("notes.bin", payload);
        Assert.False(File.Exists(Path.Combine(root, "notes.bin")));
        Assert.Equal(payload, routed.Get("notes.bin"));
        var localRoot = Path.Combine(Path.GetTempPath(), "zapara-local-" + Guid.NewGuid().ToString("N"));
        var local = new Zapara.Server.Storage.RoutingObjectStore(StoreConfig(localRoot, false));
        Assert.False(local.RemoteConfigured());
        local.Put("notes.bin", payload);
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(localRoot, "notes.bin")));
        Assert.Equal(payload, local.Get("notes.bin"));
        local.Delete("notes.bin");
        Assert.False(File.Exists(Path.Combine(localRoot, "notes.bin")));
    }

    [Fact]
    public void A_student_without_a_group_is_not_given_a_group_counter()
    {
        Assert.Null(StudyGroupScope.Choose(null, []));
        Assert.Null(StudyGroupScope.Choose("made-up", []));
        Assert.Equal("O3313", StudyGroupScope.Choose(null, ["O3313"]));
        Assert.Equal("O3313", StudyGroupScope.Choose("other", ["O3313"]));
        var open = new QuotaState(0, 10, 0, 1, false);
        var allowed = QuotaRules.Decide(open, 5);
        Assert.True(allowed.Allowed);
        var blocked = QuotaRules.Decide(open with { UserUsed = 9 }, 2);
        Assert.Equal("Превышен лимит трафика студента.", blocked.Reason);
    }

    private static Microsoft.Extensions.Configuration.IConfiguration StoreConfig(string root, bool complete)
    {
        var values = new Dictionary<string, string?>
        {
            ["Social:MediaRoot"] = root,
            ["S3:Endpoint"] = complete ? "http://127.0.0.1:9" : "",
            ["S3:Region"] = complete ? "ru-central1" : "",
            ["S3:Bucket"] = complete ? "zapara-bucket" : "",
            ["S3:AccessKey"] = complete ? "AKIAEXAMPLE" : "",
            ["S3:Secret"] = complete ? "s3-secret-value" : "",
        };
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static string Signature(string method, string path, string host, string hash, string amzDate, string region, string secret)
    {
        var day = amzDate[..8];
        var canonical = string.Join('\n', method, path, "", "host:" + host, "x-amz-content-sha256:" + hash, "x-amz-date:" + amzDate, "", "host;x-amz-content-sha256;x-amz-date", hash);
        var scope = day + "/" + region + "/s3/aws4_request";
        var toSign = "AWS4-HMAC-SHA256\n" + amzDate + "\n" + scope + "\n" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        byte[] Hmac(byte[] key, string data)
        {
            using var hmac = new System.Security.Cryptography.HMACSHA256(key);
            return hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
        }
        var signing = Hmac(Hmac(Hmac(Hmac(System.Text.Encoding.UTF8.GetBytes("AWS4" + secret), day), region), "s3"), "aws4_request");
        return Convert.ToHexString(Hmac(signing, toSign)).ToLowerInvariant();
    }

    private sealed class Bucket : HttpMessageHandler
    {
        public List<(string Method, string Path, byte[] Body, string Host, string Signature, string Date, string Hash)> Calls { get; } = [];
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => SendAsync(request, cancellationToken).GetAwaiter().GetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : "";
            var signature = authorization.Split("Signature=", StringSplitOptions.None).Last();
            var date = request.Headers.GetValues("x-amz-date").Single();
            var hash = request.Headers.GetValues("x-amz-content-sha256").Single();
            Calls.Add((request.Method.Method, path, body, request.Headers.Host ?? "", signature, date, hash));
            if (request.Method == HttpMethod.Put)
            {
                files[path] = body;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            }
            if (request.Method == HttpMethod.Delete)
            {
                files.Remove(path);
                return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
            }
            if (!files.TryGetValue(path, out var stored)) return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(stored) };
        }
    }
}
