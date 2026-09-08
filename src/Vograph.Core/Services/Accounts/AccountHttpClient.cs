using System.Net;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalResponses;

namespace Vograph.Core.Services.Accounts;

/// <summary>Injected clients are trusted: disable redirects, cookies, decompression and default headers.</summary>
public sealed partial class AccountHttpClient : IDisposable
{
    private readonly HttpClient http;
    private readonly TimeProvider clock;
    private bool ownsHttp;
    public AccountServerScope Scope { get; }

    public AccountHttpClient(HttpClient http, Uri baseUri, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        Scope = new(baseUri);
        if (http.DefaultRequestHeaders.Any()) throw new ArgumentException("Требуется отдельный HTTP-клиент аккаунтов.");
        this.http = http;
        this.clock = clock ?? TimeProvider.System;
    }

    public static AccountHttpClient CreateOwned(Uri baseUri, TimeProvider? clock = null)
    {
        var scope = new AccountServerScope(baseUri);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false, Credentials = null, DefaultProxyCredentials = null, MaxConnectionsPerServer = 4
        };
        return new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, scope.BaseUri, clock) { ownsHttp = true };
    }

    public Task<AuthCapabilitiesResponse> GetCapabilitiesAsync(CancellationToken ct = default)
        => SendAsync<AuthCapabilitiesResponse>(HttpMethod.Get, "auth/capabilities", null, null, 200, ct);
    public Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
        => SendAsync<UserResponse>(HttpMethod.Post, "auth/register", Required(request), null, 201, ct);
    public Task<SessionResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
        => SendAsync<SessionResponse>(HttpMethod.Post, "auth/login", Required(request), null, 200, ct);
    public Task<SessionResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
        => SendAsync<SessionResponse>(HttpMethod.Post, "auth/refresh", new RefreshRequest(refreshToken), null, 200, ct);
    public Task<MeResponse> GetMeAsync(string accessToken, CancellationToken ct = default)
        => SendAsync<MeResponse>(HttpMethod.Get, "account/me", null, Access(accessToken), 200, ct);
    public Task<UserResponse> UpdateProfileAsync(string accessToken, UpdateProfileRequest request, CancellationToken ct = default)
        => SendAsync<UserResponse>(HttpMethod.Patch, "account/me", Required(request), Access(accessToken), 200, ct);
    public Task<DevicesResponse> ListDevicesAsync(string accessToken, int limit = 20, string? cursor = null, CancellationToken ct = default)
    {
        if (limit is < 1 or > 100 || !AccountResponseReader.ValidCursor(cursor))
            throw new AccountClientException(AccountClientFailure.InvalidRequest);
        return SendAsync<DevicesResponse>(HttpMethod.Get, "account/devices?limit=" + limit.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), null, Access(accessToken), 200, ct);
    }
    public Task RevokeSessionAsync(string accessToken, Guid familyId, CancellationToken ct = default)
        => SendAsync<object>(HttpMethod.Delete, $"account/devices/{AccountValidation.Id(familyId):D}", null, Access(accessToken), 204, ct);
    public Task RevokeAllAsync(string accessToken, CancellationToken ct = default)
        => SendAsync<object>(HttpMethod.Post, "account/sessions/revoke-all", null, Access(accessToken), 204, ct);
    public Task LogoutAsync(string accessToken, CancellationToken ct = default)
        => SendAsync<object>(HttpMethod.Post, "auth/logout", null, Access(accessToken), 204, ct);
    public Task ChangePasswordAsync(string accessToken, ChangePasswordRequest request, CancellationToken ct = default)
        => SendAsync<object>(HttpMethod.Post, "account/password/change", Required(request), Access(accessToken), 204, ct);
    private static string Access(string value) => AccountValidation.Token(value, "za_");
    private static T Required<T>(T request) where T : class => request ?? throw new AccountClientException(AccountClientFailure.InvalidRequest);
    public void Dispose() { if (ownsHttp) http.Dispose(); }
}
