using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using Zapara.Contracts.Accounts;

namespace Zapara.Server.Tests;

public sealed partial class AccountApiTests
{
    [Fact]
    public async Task Refresh_v2_retries_a_lost_response_without_revoking_the_session()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        await host.Register();
        var initial = await host.Login();
        var attempt = Guid.NewGuid();

        var first = await Retry(host.Client, initial.RefreshToken, attempt);
        Assert.Equal(initial.RefreshExpiresAt, first.RefreshExpiresAt);
        var refreshHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(first.RefreshToken)));
        var accessHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(first.AccessToken)));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.refresh_tokens WHERE token_hash=decode('{refreshHash}','hex') AND consumed_at IS NULL"));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.refresh_tokens WHERE replacement_hash=decode('{refreshHash}','hex')"));
        Assert.Equal(1L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.access_tokens WHERE token_hash=decode('{accessHash}','hex')"));
        var repeated = await Retry(host.Client, initial.RefreshToken, attempt);

        Assert.Equal(first.AccessToken, repeated.AccessToken);
        Assert.Equal(first.RefreshToken, repeated.RefreshToken);
        Assert.Equal(first.AccessExpiresAt, repeated.AccessExpiresAt);
        Assert.Equal(first.RefreshExpiresAt, repeated.RefreshExpiresAt);
        Assert.Equal(first.User, repeated.User);
        Assert.NotEqual(initial.RefreshToken, repeated.RefreshToken);
        await host.Send("GET", "/account/me", 200, bearer: repeated.AccessToken);
        Assert.Equal(0L, await db.ScalarAsync<long>($"SELECT count(*) FROM {db.QuotedSchema}.account_security_events WHERE action='refresh_replay'"));
        var logs = string.Join('\n', host.Logs);
        Assert.DoesNotContain(initial.RefreshToken, logs);
        Assert.DoesNotContain(repeated.RefreshToken, logs);
    }

    [Fact]
    public async Task Refresh_v2_requires_an_attempt_id_before_consuming_the_token()
    {
        await using var db = await AccountsPostgresFixture.CreateAsync(output.WriteLine, true);
        await using var host = new AccountApiTestHost(db);
        await host.Register();
        var initial = await host.Login();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/auth/refresh");
        request.Content = new StringContent(JsonSerializer.Serialize(new { refreshToken = initial.RefreshToken }),
            Encoding.UTF8, "application/json");
        using var response = await host.Client.SendAsync(request, AccountApiTestHost.Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var renewed = await Retry(host.Client, initial.RefreshToken, Guid.NewGuid());
        await host.Send("GET", "/account/me", 200, bearer: renewed.AccessToken);
    }

    private static async Task<SessionResponse> Retry(HttpClient client, string token, Guid attempt)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/auth/refresh");
        request.Headers.Add("X-Zapara-Refresh-Attempt", attempt.ToString("D"));
        request.Content = new StringContent(JsonSerializer.Serialize(new { refreshToken = token }), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, AccountApiTestHost.Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<SessionResponse>(AccountApiTestHost.Json, AccountApiTestHost.Ct))!;
    }
}
