using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Sync;
using Zapara.Server.Accounts;
using Zapara.Server.Notifications;
using Zapara.Server.Sync;

namespace Zapara.Server.Tests;

public sealed class WebPushTests
{
    [Theory]
    [InlineData("http://fcm.googleapis.com/push")]
    [InlineData("https://127.0.0.1/push")]
    [InlineData("https://fcm.googleapis.com.evil.invalid/push")]
    [InlineData("https://fcm.googleapis.com@evil.invalid/push")]
    [InlineData("https://fcm.googleapis.com:8443/push")]
    [InlineData("https://fcm.googleapis.com./push")]
    [InlineData("https://web.push.apple.com/push#fragment")]
    public void SubscriptionEndpointsCannotBecomeArbitraryHttpTargets(string endpoint)
        => Assert.Throws<PushOperationException>(() => PushValidation.Endpoint(endpoint));

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("198.18.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("2001:db8::1")]
    public void DnsResultsRejectPrivateAndReservedAddresses(string address)
        => Assert.False(PushTransport.PublicAddress(IPAddress.Parse(address)));

    [Fact]
    public async Task PushWireUsesModernEncryptionVapidAndNeutralPayloadWithoutFollowingRedirect()
    {
        var options = PushConfiguration.Read(new ConfigurationBuilder().AddInMemoryCollection(Settings()).Build());
        Assert.True(options.Available);
        using var handler = new CapturingPushHandler();
        using var http = new HttpClient(handler);
        using var transport = new PushTransport(options, http);
        var outcome = await transport.SendAsync(Subscription(), Ct);
        Assert.Equal(PushDeliveryOutcome.Accepted, outcome);
        Assert.Equal("aes128gcm", handler.Encoding);
        Assert.Equal("vapid", handler.AuthorizationScheme);
        Assert.True(handler.Payload.Length > 100);
        Assert.DoesNotContain("Откройте", Encoding.UTF8.GetString(handler.Payload));
        Assert.Equal("300", handler.Ttl);
        handler.Status = HttpStatusCode.TemporaryRedirect;
        Assert.Equal(PushDeliveryOutcome.Unavailable, await transport.SendAsync(Subscription(), Ct));
        Assert.Equal(2, handler.Calls);
        handler.Status = HttpStatusCode.Gone;
        Assert.Equal(PushDeliveryOutcome.Gone, await transport.SendAsync(Subscription(), Ct));
    }

    [Fact]
    public async Task BothSyncedTimesDriveDurableClaimsAndNativeRevocationRemovesSubscription()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var clock = new AccountClock();
        var capture = new CapturingPush();
        await using var host = Host(db, clock, capture);
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("push_user", WebAccountHost.Password));
        await host.Login("push_user");
        var accounts = host.Factory.Services.GetRequiredService<AccountService>();
        var native = await accounts.LoginAsync(new("push_user", WebAccountHost.Password, new(Guid.NewGuid(), "Windows", "windows")), Ct);
        var sync = host.Factory.Services.GetRequiredService<SyncService>();
        var metadata = await sync.MetadataAsync(native.AccessToken, Ct);
        await sync.MutateAsync(native.AccessToken, new(metadata.SyncEpoch, Guid.NewGuid(), "settings", Guid.Parse("00000000-0000-0000-0000-000000000001"), 0,
            "upsert", new SettingsValue(null, false, "15:00", "15:00", 50, false)), Ct);
        var request = Subscription();
        var saved = await host.Send("POST", "/notifications/subscriptions", 201, request);
        Assert.Equal("15:00", saved.GetProperty("times")[0].GetString());
        var encrypted = await db.Accounts.ScalarAsync<string>($"SELECT protected_subscription FROM {db.Accounts.QuotedSchema}.web_push_subscriptions");
        Assert.DoesNotContain(request.Endpoint, encrypted);
        Assert.DoesNotContain(request.Keys.Auth, encrypted);
        var service = host.Factory.Services.GetRequiredService<PushSubscriptionService>();
        await service.RunScheduledAsync(Ct);
        await service.RunScheduledAsync(Ct);
        Assert.Equal(1, capture.Calls);
        clock.Now = clock.Now.AddMinutes(1);
        await sync.MutateAsync(native.AccessToken, new(metadata.SyncEpoch, Guid.NewGuid(), "settings", Guid.Parse("00000000-0000-0000-0000-000000000001"), 1,
            "upsert", new SettingsValue(null, false, null, "15:01", 50, false)), Ct);
        await service.RunScheduledAsync(Ct);
        Assert.Equal(2, capture.Calls);
        await accounts.RevokeSessionAsync(native.AccessToken, Guid.Parse(host.Family!), Ct);
        Assert.Equal(0, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.Accounts.QuotedSchema}.web_push_subscriptions"));
        await service.RunScheduledAsync(Ct);
        Assert.Equal(2, capture.Calls);
    }

    [Fact]
    public async Task ExpiredProviderSubscriptionIsRemovedAndUnconfiguredTestNeverPretendsSuccess()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var capture = new CapturingPush { Outcome = PushDeliveryOutcome.Gone };
        await using var host = Host(db, new AccountClock(), capture);
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("push_expired", WebAccountHost.Password));
        await host.Login("push_expired");
        var subscription = await host.Send("POST", "/notifications/subscriptions", 201, Subscription());
        await host.Send("POST", "/notifications/test", 503, new PushTestRequest(subscription.GetProperty("subscriptionId").GetGuid()));
        Assert.Equal(0, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.Accounts.QuotedSchema}.web_push_subscriptions"));
        await using var disabled = new WebAccountHost(db.Accounts);
        await disabled.Bootstrap();
        await disabled.Login("push_expired");
        var capability = await disabled.Send("GET", "/notifications/capabilities", 200);
        Assert.False(capability.GetProperty("available").GetBoolean());
        await disabled.Send("POST", "/notifications/test", 503, new PushTestRequest(Guid.NewGuid()));
        Assert.Equal(1, capture.Calls);
    }

    [Fact]
    public async Task DisablingAndUnsubscribingRemoveProtectedEndpointImmediately()
    {
        await using var db = await SyncPostgresFixture.CreateAsync(true);
        var capture = new CapturingPush();
        await using var host = Host(db, new AccountClock(), capture);
        await host.Bootstrap();
        await host.Send("POST", "/auth/register", 201, new RegisterRequest("push_optout", WebAccountHost.Password));
        await host.Login("push_optout");
        var request = Subscription();
        await host.Send("POST", "/notifications/subscriptions", 201, request);
        await host.Send("POST", "/notifications/subscriptions", 201, request with { Enabled = false });
        Assert.Equal(0, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.Accounts.QuotedSchema}.web_push_subscriptions"));
        var renewed = await host.Send("POST", "/notifications/subscriptions", 201, request);
        await using var unavailable = new WebAccountHost(db.Accounts, new AccountClock());
        unavailable.Client.DefaultRequestHeaders.Add("Cookie", host.CookieHeader);
        await unavailable.Bootstrap();
        var existing = await unavailable.Send("GET", "/notifications/subscriptions", 200);
        Assert.Single(existing.EnumerateArray());
        await unavailable.Send("DELETE", "/notifications/subscriptions/" + renewed.GetProperty("subscriptionId").GetString(), 204);
        Assert.Equal(0, await db.Accounts.ScalarAsync<long>($"SELECT count(*) FROM {db.Accounts.QuotedSchema}.web_push_subscriptions"));
        Assert.Equal(0, capture.Calls);
    }

    internal static Dictionary<string, string?> Settings()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = ec.ExportParameters(true);
        return new()
        {
            ["Sync:Enabled"] = "true", ["Web:Push:Enabled"] = "true",
            ["Web:Push:PublicKey"] = WebEncoders.Base64UrlEncode([4, ..key.Q.X!, ..key.Q.Y!]),
            ["Web:Push:PrivateKey"] = WebEncoders.Base64UrlEncode(key.D!), ["Web:Push:Subject"] = "mailto:operator@example.invalid"
        };
    }
    internal static PushSubscriptionRequest Subscription()
    {
        using var ec = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var key = ec.ExportParameters(false);
        return new("https://fcm.googleapis.com/fcm/send/synthetic-canary", new(WebEncoders.Base64UrlEncode([4, ..key.Q.X!, ..key.Q.Y!]), WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16))));
    }
    private static WebAccountHost Host(SyncPostgresFixture db, AccountClock clock, CapturingPush capture)
    {
        var settings = Settings(); settings["Sync:Schema"] = db.Schema;
        return new(db.Accounts, clock, moduleSettings: settings, configureServices: services =>
        { services.RemoveAll<IPushTransport>(); services.AddSingleton<IPushTransport>(capture); });
    }
    private sealed class CapturingPush : IPushTransport
    {
        internal int Calls;
        internal PushDeliveryOutcome Outcome = PushDeliveryOutcome.Accepted;
        public Task<PushDeliveryOutcome> SendAsync(PushSubscriptionRequest subscription, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Interlocked.Increment(ref Calls); return Task.FromResult(Outcome); }
    }
    private sealed class CapturingPushHandler : HttpMessageHandler
    {
        internal int Calls;
        internal HttpStatusCode Status = HttpStatusCode.Created;
        internal string? Encoding, AuthorizationScheme, Ttl;
        internal byte[] Payload = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal("fcm.googleapis.com", request.RequestUri!.Host);
            Encoding = request.Content!.Headers.ContentEncoding.Single();
            AuthorizationScheme = request.Headers.Authorization!.Scheme;
            Ttl = request.Headers.GetValues("TTL").Single();
            Payload = await request.Content.ReadAsByteArrayAsync(ct);
            return new(Status) { Content = new StringContent(""), Headers = { Location = new Uri("http://127.0.0.1/private") } };
        }
    }
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
