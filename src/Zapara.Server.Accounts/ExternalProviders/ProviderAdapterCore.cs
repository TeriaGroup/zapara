using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Zapara.Server.Accounts.ExternalProviders;

internal sealed class ProviderAdapterCore : IExternalProviderAdapter
{
    private readonly bool vk;
    private readonly ProviderMetadata metadata;
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly TimeProvider clock;
    private readonly ProviderTransport transport;

    internal ProviderAdapterCore(bool vk, ProviderMetadata metadata, HttpClient? http, TimeProvider? clock)
    {
        this.vk = vk;
        this.metadata = metadata;
        this.http = http ?? ProviderHttpClientFactory.Create();
        ownsHttp = http is null;
        this.clock = clock ?? TimeProvider.System;
        transport = new(this.http);
    }

    public string Provider => vk ? "vk" : "yandex";
    public bool IsConfigured => metadata.IsConfigured;

    public Uri BuildAuthorizationUri(ProviderState state, CodeChallenge challenge)
    {
        Configured();
        if (state is null || challenge is null) throw new ExternalProviderException(ExternalProviderFailure.InvalidRequest);
        var parameters = new Dictionary<string, string>
        {
            ["response_type"] = "code", ["client_id"] = metadata.ClientId!, ["redirect_uri"] = metadata.CallbackUri!,
            ["state"] = state.Value, ["code_challenge"] = challenge.Value, ["code_challenge_method"] = "S256",
            ["scope"] = vk ? "vkid.personal_info" : "login:info"
        };
        var endpoint = vk ? "https://id.vk.ru/authorize" : "https://oauth.yandex.ru/authorize";
        return new(endpoint + "?" + string.Join("&", parameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))));
    }

    public async Task<VerifiedExternalIdentity> ExchangeIdentityAsync(string code, ProviderState state,
        CodeVerifier verifier, string? deviceId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Configured();
        if (state is null || verifier is null) throw new ExternalProviderException(ExternalProviderFailure.InvalidRequest);
        ProviderValidation.Printable(code, 8192, ExternalProviderFailure.InvalidRequest);
        if (vk) ProviderValidation.Printable(deviceId, 8192, ExternalProviderFailure.InvalidRequest);
        else if (deviceId is not null) throw new ExternalProviderException(ExternalProviderFailure.InvalidRequest);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budget.Token);
        try
        {
            var result = await ExchangeCoreAsync(code, state, verifier, deviceId, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException("External provider operation cancelled.", ct);
            throw new ExternalProviderException(ExternalProviderFailure.Timeout);
        }
        catch (Exception error) when (error is HttpRequestException or IOException)
        {
            throw new ExternalProviderException(ExternalProviderFailure.TransportFailure);
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or InvalidOperationException or FormatException or ArgumentException)
        {
            throw new ExternalProviderException(ExternalProviderFailure.InvalidResponse);
        }
    }

    private async Task<VerifiedExternalIdentity> ExchangeCoreAsync(string code, ProviderState state,
        CodeVerifier verifier, string? deviceId, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["client_id"] = metadata.ClientId!,
            ["code"] = code, ["code_verifier"] = verifier.Value, ["redirect_uri"] = metadata.CallbackUri!
        };
        if (vk) { form["device_id"] = deviceId!; form["state"] = state.Value; }
        if (metadata.Secret is not null) form[vk ? "service_token" : "client_secret"] = metadata.Secret;
        using var tokenRequest = Post(vk ? "https://id.vk.ru/oauth2/auth" : "https://oauth.yandex.ru/token", form);
        form.Clear();
        using var tokens = await transport.SendAsync(tokenRequest, ct).ConfigureAwait(false);
        if (vk && tokens.RootElement.TryGetProperty("state", out var returnedState) &&
            (returnedState.ValueKind != JsonValueKind.String || returnedState.GetString() != state.Value))
            throw new ExternalProviderException(ExternalProviderFailure.IdentityMismatch);
        using var infoRequest = CreateInfoRequest(tokens.RootElement);
        using var info = await transport.SendAsync(infoRequest, ct).ConfigureAwait(false);
        var user = info.RootElement;
        string subject;
        if (vk)
        {
            if (!user.TryGetProperty("user", out user) || user.ValueKind != JsonValueKind.Object ||
                !user.TryGetProperty("user_id", out var id))
                throw new ExternalProviderException(ExternalProviderFailure.InvalidResponse);
            subject = ProviderIdentityReader.VkSubject(id);
            if (tokens.RootElement.TryGetProperty("user_id", out var tokenId) && tokenId.ValueKind != JsonValueKind.Null &&
                ProviderIdentityReader.VkSubject(tokenId) != subject)
                throw new ExternalProviderException(ExternalProviderFailure.IdentityMismatch);
        }
        else
        {
            if (ProviderIdentityReader.RequiredString(user, "client_id", 256) != metadata.ClientId)
                throw new ExternalProviderException(ExternalProviderFailure.IdentityMismatch);
            subject = ProviderIdentityReader.RequiredString(user, "id", 256);
        }
        return new(Provider, subject, ProviderIdentityReader.DisplayName(user, vk));
    }

    private HttpRequestMessage CreateInfoRequest(JsonElement tokens)
    {
        var token = ProviderIdentityReader.RequiredString(tokens, "access_token");
        if (vk) return Post("https://id.vk.ru/oauth2/user_info", new()
        {
            ["client_id"] = metadata.ClientId!, ["access_token"] = token
        });
        var request = new HttpRequestMessage(HttpMethod.Get, "https://login.yandex.ru/info?format=json");
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
        return request;
    }

    private static HttpRequestMessage Post(string endpoint, Dictionary<string, string> form)
        => new(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(form) };

    private void Configured()
    {
        if (!IsConfigured) throw new ExternalProviderException(ExternalProviderFailure.NotConfigured);
    }

    public void Dispose() { if (ownsHttp) http.Dispose(); }
}
