using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Contracts.Accounts.ExternalResponses;
using Xunit;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

public partial class AccountClientTests
{
    private static readonly Guid ExportId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly Guid TransactionId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static ProofRequest Proof => new(new string('A', 43));
    private static ExportJobResponse ExportJob => new(ExportId, "ready", Now, Now, Now.AddHours(24));
    private static PasswordProofRequest PasswordProof => new(Password, "export");
    private static ExternalStartRequest ExternalStart => new("login", "challenge", "S256",
        new DeviceInput(DeviceId, "Windows", "windows"), new NativeReturn("windows", 45001));

    [Fact]
    public async Task Lifecycle_operations_use_exact_contract_and_per_request_bearer()
    {
        var proof = new ReauthResponse(new string('B', 43), "export", Now.AddMinutes(5));
        var deleting = new DeleteAccountResponse("deleting", false);
        var reset = new PasswordResetRequest("Test.User");
        var confirm = new PasswordResetConfirmRequest(new string('C', 43), Password + "new");
        var exchangeReq = new ExternalExchangeRequest(TransactionId, "verifier", new string('D', 43));
        var start = new ExternalStartResponse(TransactionId, "https://example.invalid/mock/authorize", Now.AddMinutes(10));
        var exchange = new ExternalExchangeResponse("completed", Session());
        var status = new ExternalStatusResponse("awaitingApp");
        var identities = new[] { new ExternalIdentityResponse("vk", Now) };
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/root"), new AccountClientClock());
        var expected = new Queue<(string Method, string Path, object? Body, bool Auth, object? Response, HttpStatusCode Status)>([
            ("POST", "account/exports", Proof, true, ExportJob, HttpStatusCode.Accepted),
            ("GET", $"account/exports/{ExportId:D}", null, true, ExportJob, HttpStatusCode.OK),
            ("DELETE", "account", Proof, true, deleting, HttpStatusCode.Accepted),
            ("POST", "auth/password-reset/request", reset, false, new { }, HttpStatusCode.Accepted),
            ("POST", "auth/password-reset/confirm", confirm, false, null, HttpStatusCode.NoContent),
            ("POST", "account/reauthenticate", PasswordProof, true, proof, HttpStatusCode.OK),
            ("POST", "auth/external/vk/start", ExternalStart, false, start, HttpStatusCode.OK),
            ("POST", "auth/external/exchange", exchangeReq, false, exchange, HttpStatusCode.OK),
            ("GET", $"auth/external/{TransactionId:D}/status", null, false, status, HttpStatusCode.OK),
            ("GET", "account/identities", null, true, identities, HttpStatusCode.OK),
            ("DELETE", "account/identities/vk", Proof, true, null, HttpStatusCode.NoContent)
        ]);
        handler.Send = async (request, ct) =>
        {
            var e = expected.Dequeue();
            Assert.Equal(e.Method, request.Method.Method);
            Assert.Equal("https://example.invalid/root/api/v1/" + e.Path, request.RequestUri!.AbsoluteUri);
            Assert.DoesNotContain("oauth.vk.com", request.RequestUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("yandex", request.RequestUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(e.Auth ? "Bearer " + Token("za_") : null, request.Headers.Authorization?.ToString());
            Assert.Null(http.DefaultRequestHeaders.Authorization);
            Assert.Single(request.Headers.Accept, h => h.MediaType == "application/json");
            if (e.Body is null) Assert.Null(request.Content);
            else
            {
                Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
                Assert.Equal(JsonSerializer.Serialize(e.Body, AccountJson.CreateOptions()), await request.Content.ReadAsStringAsync(ct));
            }
            return e.Response is null ? new(e.Status) : Json(e.Response, e.Status);
        };
        Assert.Equal(ExportJob, await client.CreateExportAsync(Token("za_"), Proof, Ct));
        Assert.Equal(ExportJob, await client.GetExportAsync(Token("za_"), ExportId, Ct));
        Assert.Equal(deleting, await client.DeleteAccountAsync(Token("za_"), Proof, Ct));
        await client.RequestPasswordResetAsync(reset, Ct);
        await client.ConfirmPasswordResetAsync(confirm, Ct);
        Assert.Equal(proof, await client.ReauthenticateAsync(Token("za_"), PasswordProof, Ct));
        Assert.Equal(start, await client.StartExternalAsync("vk", ExternalStart, ct: Ct));
        Assert.Equal(exchange, await client.ExchangeExternalAsync(exchangeReq, ct: Ct));
        Assert.Equal(status, await client.GetExternalStatusAsync(TransactionId, Ct));
        Assert.Equal(identities, await client.ListIdentitiesAsync(Token("za_"), Ct));
        await client.UnlinkIdentityAsync(Token("za_"), "vk", Proof, Ct);
        Assert.Empty(expected);
    }

    [Fact]
    public async Task Export_download_is_authenticated_json_bytes_with_filename()
    {
        var payload = Encoding.UTF8.GetBytes("""{"profile":{"username":"Test.User"}}""");
        var fileName = "zapara-export-" + ExportId.ToString("D") + ".json";
        using var handler = new AccountClientHandler { Send = (request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"https://example.invalid/root/api/v1/account/exports/{ExportId:D}/download", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer " + Token("za_"), request.Headers.Authorization?.ToString());
            Assert.Null(request.Content);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
            response.Content.Headers.ContentType = new("application/json");
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = fileName };
            return Task.FromResult(response);
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/root"));
        var file = await client.DownloadExportAsync(Token("za_"), ExportId, Ct);
        Assert.Equal(payload, file.Payload);
        Assert.Equal(fileName, file.FileName);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Export_download_rejects_non_json_content_type()
    {
        using var handler = new AccountClientHandler { Send = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("{}"u8.ToArray()) };
            response.Content.Headers.ContentType = new("application/octet-stream");
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "zapara-export.json" };
            return Task.FromResult(response);
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        Assert.Equal(AccountClientFailure.InvalidPayload, (await Assert.ThrowsAsync<AccountClientException>(
            () => client.DownloadExportAsync(Token("za_"), ExportId, Ct))).Failure);
    }

    [Theory]
    [InlineData(401, "invalid_session", AccountClientFailure.InvalidSession)]
    [InlineData(403, "invalid_external_proof", AccountClientFailure.InvalidExternalProof)]
    [InlineData(503, "provider_unavailable", AccountClientFailure.ProviderUnavailable)]
    [InlineData(400, "invalid_request", AccountClientFailure.InvalidRequest)]
    public async Task Export_and_delete_map_invalid_proof_and_session(int status, string code, AccountClientFailure failure)
    {
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(
            Json(new { title = Password, status, code }, (HttpStatusCode)status)) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        var export = await Assert.ThrowsAsync<AccountClientException>(() => client.CreateExportAsync(Token("za_"), Proof, Ct));
        Assert.Equal(failure, export.Failure);
        Assert.Equal(status, export.Status);
        Assert.DoesNotContain(Password, export.ToString());
        Assert.DoesNotContain(Proof.ProofToken, export.ToString());
        var delete = await Assert.ThrowsAsync<AccountClientException>(() => client.DeleteAccountAsync(Token("za_"), Proof, Ct));
        Assert.Equal(failure, delete.Failure);
        Assert.Equal(status, delete.Status);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Download_401_is_invalid_session_and_missing_token_does_not_network()
    {
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(
            Json(new { title = "Нет", status = 401, code = "invalid_session" }, HttpStatusCode.Unauthorized)) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        var error = await Assert.ThrowsAsync<AccountClientException>(() => client.DownloadExportAsync(Token("za_"), ExportId, Ct));
        Assert.Equal(AccountClientFailure.InvalidSession, error.Failure);
        Assert.Equal(401, error.Status);
        await Assert.ThrowsAsync<ArgumentException>(() => client.DownloadExportAsync("invalid", ExportId, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => client.GetExportAsync(Token("za_"), Guid.Empty, Ct));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Recovery_request_202_is_identical_and_does_not_leak_whether_user_exists()
    {
        var bodies = new List<string>();
        using var handler = new AccountClientHandler { Send = async (request, ct) =>
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("https://example.invalid/api/v1/auth/password-reset/request", request.RequestUri!.AbsoluteUri);
            return Json(new { }, HttpStatusCode.Accepted);
        } };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        await client.RequestPasswordResetAsync(new("no_such_user"), Ct);
        await client.RequestPasswordResetAsync(new("Test.User"), Ct);
        Assert.Equal(2, handler.Calls);
        Assert.NotEqual(bodies[0], bodies[1]);
        Assert.Contains("no_such_user", bodies[0], StringComparison.Ordinal);
        Assert.Contains("Test.User", bodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recovery_202_rejects_existence_fields()
    {
        using var handler = new AccountClientHandler { Send = (_, _) => Task.FromResult(
            Json(new { exists = true, username = "Test.User" }, HttpStatusCode.Accepted)) };
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        var error = await Assert.ThrowsAsync<AccountClientException>(
            () => client.RequestPasswordResetAsync(new("Test.User"), Ct));
        Assert.Equal(AccountClientFailure.InvalidPayload, error.Failure);
        Assert.DoesNotContain("Test.User", error.ToString());
        Assert.DoesNotContain(Password, error.ToString());
    }

    [Fact]
    public async Task Null_proof_and_unknown_provider_do_not_network()
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new AccountHttpClient(http, new("https://example.invalid/"));
        Assert.Equal(AccountClientFailure.InvalidRequest, (await Assert.ThrowsAsync<AccountClientException>(
            () => client.CreateExportAsync(Token("za_"), null!, Ct))).Failure);
        Assert.Equal(AccountClientFailure.InvalidRequest, (await Assert.ThrowsAsync<AccountClientException>(
            () => client.DeleteAccountAsync(Token("za_"), null!, Ct))).Failure);
        Assert.Equal(AccountClientFailure.InvalidRequest, (await Assert.ThrowsAsync<AccountClientException>(
            () => client.StartExternalAsync("google", ExternalStart, ct: Ct))).Failure);
        Assert.Equal(AccountClientFailure.InvalidRequest, (await Assert.ThrowsAsync<AccountClientException>(
            () => client.UnlinkIdentityAsync(Token("za_"), "live", Proof, Ct))).Failure);
        Assert.Equal(0, handler.Calls);
    }
}
