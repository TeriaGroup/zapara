using Microsoft.JSInterop;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalRequests;
using Zapara.Contracts.Accounts.ExternalResponses;

namespace Zapara.Web.Services;

public sealed record BrowserOAuthPending(Guid TransactionId, string Action, Guid? FamilyId, DateTimeOffset ExpiresAt);
public sealed record BrowserOAuthResult(string Status, ReauthResponse? Proof);
public sealed record BrowserOAuthResume(string Action, bool Completed, bool ProofReady, Guid? FamilyId = null);

public sealed class BrowserAccountService : IAsyncDisposable
{
    private readonly BrowserApiClient api;
    private readonly IJSRuntime js;
    private readonly TimeProvider clock;
    private Task<IJSObjectReference>? module;
    private ReauthResponse? proof;
    private Guid? proofFamily;
    private Guid? family;
    public MeResponse? Me { get; private set; }
    public IReadOnlyList<ExternalIdentityResponse> Identities { get; private set; } = [];
    public IReadOnlyList<DeviceResponse> Devices { get; private set; } = [];
    public string? NextCursor { get; private set; }
    public ExportJobResponse? Export { get; private set; }
    private BrowserOAuthResume? completedOAuthResume;
    public event Action? OAuthResumed;
    public BrowserOAuthResume? TakeOAuthResume() { var result = completedOAuthResume; completedOAuthResume = null; return result; }
    public bool HasPassword => Me?.AuthenticationMethods.Contains("password") == true;
    public BrowserAccountService(BrowserApiClient api, IJSRuntime js, TimeProvider? clock = null)
    {
        this.api = api; this.js = js; this.clock = clock ?? TimeProvider.System;
        family = api.Session.FamilyId;
        api.SessionChanged += SessionChanged;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var current = Family();
        var me = api.SendAsync<MeResponse>(HttpMethod.Get, "account/me", expectedFamily: current, ct: ct);
        var identities = api.SendAsync<ExternalIdentityResponse[]>(HttpMethod.Get, "account/identities", expectedFamily: current, ct: ct);
        await Task.WhenAll(me, identities);
        Ensure(current);
        Me = await me; Identities = await identities;
        if (Me.FamilyId != current) { Me = null; Identities = []; throw Changed(); }
    }

    public async Task UpdateNameAsync(string? displayName, CancellationToken ct = default)
    {
        var current = Family();
        await api.SendAsync<UserResponse>(HttpMethod.Patch, "account/me", new UpdateProfileRequest(string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim()), current, ct);
        Ensure(current);
        await api.RefreshSessionAsync(ct);
        await RefreshAsync(ct);
    }

    public async Task LoadDevicesAsync(bool more = false, CancellationToken ct = default)
    {
        var current = Family();
        if (more && NextCursor is null) return;
        var path = "account/devices?limit=20" + (more ? "&cursor=" + Uri.EscapeDataString(NextCursor!) : "");
        var page = await api.SendAsync<DevicesResponse>(HttpMethod.Get, path, expectedFamily: current, ct: ct);
        Ensure(current);
        Devices = more ? Devices.Concat(page.Devices).DistinctBy(device => device.FamilyId).ToArray() : page.Devices;
        NextCursor = page.NextCursor;
    }

    public async Task RevokeDeviceAsync(Guid id, CancellationToken ct = default)
    {
        var current = Family();
        await api.SendAsync(HttpMethod.Delete, "account/devices/" + AccountValidation.Id(id), expectedFamily: current, ct: ct);
        if (id == current) await api.RefreshSessionAsync(ct);
        else { Ensure(current); await LoadDevicesAsync(ct: ct); }
    }
    public async Task RevokeAllAsync(CancellationToken ct = default)
    {
        await api.SendAsync(HttpMethod.Post, "account/sessions/revoke-all", expectedFamily: Family(), ct: ct);
        ClearProof(); await api.RefreshSessionAsync(ct);
    }
    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
    {
        await api.SendAsync(HttpMethod.Post, "account/password/change", new ChangePasswordRequest(currentPassword, newPassword), Family(), ct);
        ClearProof(); await api.RefreshSessionAsync(ct);
    }

    public bool HasProof(string purpose) => proof is not null && proofFamily == api.Session.FamilyId && proof.Purpose == purpose && proof.ExpiresAt > clock.GetUtcNow();
    public async Task PasswordProofAsync(string password, string purpose, CancellationToken ct = default)
    {
        ValidatePurpose(purpose);
        AccountValidation.Password(password);
        var current = Family();
        var result = await api.SendAsync<ReauthResponse>(HttpMethod.Post, "account/reauthenticate", new PasswordProofRequest(password, purpose), current, ct);
        ct.ThrowIfCancellationRequested(); Ensure(current); SetProof(result, purpose, current);
    }
    public async Task SetPasswordAsync(string newPassword, CancellationToken ct = default)
    {
        var current = Family();
        await api.SendAsync(HttpMethod.Post, "account/password/set", new FirstPasswordRequest(AccountValidation.Password(newPassword), TakeProof("set_password")), current, ct);
        await api.RefreshSessionAsync(ct);
    }
    public async Task UnlinkAsync(string provider, CancellationToken ct = default)
    {
        Provider(provider);
        var current = Family();
        await api.SendAsync(HttpMethod.Delete, "account/identities/" + provider, new ProofRequest(TakeProof("unlink:" + provider)), current, ct);
        Ensure(current); await RefreshAsync(ct);
    }
    public Task StartRecoveryEmailAsync(string email, CancellationToken ct = default)
        => api.SendAsync(HttpMethod.Post, "account/recovery-email/start", new StartRecoveryEmailRequest(email, TakeProof("set_recovery_email")), Family(), ct);
    public Task ConfirmRecoveryEmailAsync(string token, CancellationToken ct = default)
        => api.SendAsync(HttpMethod.Post, "account/recovery-email/confirm", new ConfirmRecoveryEmailRequest(token.Trim()), ct: ct);
    public Task RequestPasswordResetAsync(string username, CancellationToken ct = default)
        => api.SendAsync(HttpMethod.Post, "auth/password-reset/request", new PasswordResetRequest(username.Trim()), ct: ct);
    public Task ConfirmPasswordResetAsync(string token, string newPassword, CancellationToken ct = default)
        => api.SendAsync(HttpMethod.Post, "auth/password-reset/confirm", new PasswordResetConfirmRequest(token.Trim(), newPassword), ct: ct);

    public async Task<ExportJobResponse> RequestExportAsync(CancellationToken ct = default)
    {
        var current = Family();
        var job = await api.SendAsync<ExportJobResponse>(HttpMethod.Post, "account/exports", new ProofRequest(TakeProof("export")), current, ct);
        Ensure(current); return Export = job;
    }
    public async Task<ExportJobResponse> RefreshExportAsync(CancellationToken ct = default)
    {
        var current = Family();
        var id = Export?.ExportId ?? throw new InvalidOperationException("Сначала подготовьте копию данных.");
        var job = await api.SendAsync<ExportJobResponse>(HttpMethod.Get, "account/exports/" + id, expectedFamily: current, ct: ct);
        Ensure(current); return Export = job;
    }
    public async Task<ExportJobResponse> WaitExportAsync(CancellationToken ct = default)
    {
        var current = Family();
        for (var attempt = 0; Export is { Status: "queued" or "processing" } && attempt < 15; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), clock, ct); Ensure(current); await RefreshExportAsync(ct);
        }
        return Export ?? throw new InvalidOperationException("Не удалось найти подготовленную копию.");
    }
    public async Task DownloadExportAsync(CancellationToken ct = default)
    {
        var current = Family();
        var export = Export is { Status: "ready" } value ? value : throw new InvalidOperationException("Копия данных ещё не готова.");
        var bytes = await api.DownloadAsync("account/exports/" + export.ExportId + "/download", ct);
        Ensure(current);
        await (await Module()).InvokeVoidAsync("download", ct, bytes, "zapara-export-" + export.ExportId.ToString("D") + ".json");
    }
    public async Task DeleteAccountAsync(CancellationToken ct = default)
    {
        await api.SendAsync<DeleteAccountResponse>(HttpMethod.Delete, "account", new ProofRequest(TakeProof("delete_account")), Family(), ct);
        ClearProof(); await api.RefreshSessionAsync(ct);
    }

    public async Task StartOAuthAsync(string provider, string purpose = "login", string? proofPurpose = null, CancellationToken ct = default)
    {
        Provider(provider);
        var current = purpose == "login" ? api.Session.FamilyId : Family();
        if (purpose == "reauth") ValidatePurpose(proofPurpose!);
        else if (purpose is not ("login" or "link")) throw new ArgumentException("Неизвестное действие входа.");
        var request = new { purpose, proofToken = purpose == "link" ? TakeProof("link:" + provider) : null, proofPurpose };
        var bridge = await Module();
        await api.BeginExternalTransitionAsync(ct);
        try
        {
            var start = await api.SendAsync<ExternalStartResponse>(HttpMethod.Post, "auth/external/" + provider + "/start", request, current, ct);
            if (purpose != "login") Ensure(current!.Value);
            ValidateProviderUrl(provider, start.AuthorizeUrl);
            var action = purpose == "reauth" ? proofPurpose! : purpose == "link" ? "link:" + provider : "login";
            await bridge.InvokeVoidAsync("savePending", ct, new BrowserOAuthPending(start.TransactionId, action, purpose == "login" ? null : current, start.ExpiresAt));
            ClearProof();
            await bridge.InvokeVoidAsync("navigateProvider", ct, provider, start.AuthorizeUrl);
        }
        catch
        {
            ClearProof();
            try { await bridge.InvokeVoidAsync("clearPending"); } catch (JSException) { }
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await api.AbortExternalTransitionAsync(recovery.Token); } catch (Exception) { /* Keep the original error; coordinator recovery remains authoritative. */ }
            throw;
        }
    }

    public async Task<BrowserOAuthResume?> ResumeOAuthAsync(CancellationToken ct = default)
    {
        var bridge = await Module();
        var pending = await bridge.InvokeAsync<BrowserOAuthPending?>("readPending", ct);
        if (pending is null) return null;
        if (pending.TransactionId == Guid.Empty || pending.ExpiresAt <= clock.GetUtcNow())
        { await bridge.InvokeVoidAsync("clearPending", ct); await api.AbortExternalTransitionAsync(ct); throw new InvalidOperationException("Время подтверждения истекло. Начните вход ещё раз."); }
        if (pending.Action != "login") ValidatePurpose(pending.Action);
        if (pending.Action != "login" && (pending.FamilyId is not { } boundFamily || api.Session.FamilyId != boundFamily))
        { await bridge.InvokeVoidAsync("clearPending", ct); throw Changed(); }
        BrowserOAuthResult result;
        try { result = await api.GetAsync<BrowserOAuthResult>("auth/external/" + pending.TransactionId + "/result", ct); }
        catch (BrowserApiException error) when (error.Status == 410)
        { await bridge.InvokeVoidAsync("clearPending", ct); await api.AbortExternalTransitionAsync(ct); throw; }
        if (result.Status == "pending") return new(pending.Action, false, false);
        await bridge.InvokeVoidAsync("clearPending", ct);
        if (result.Status != "completed")
        { await api.AbortExternalTransitionAsync(ct); throw new InvalidOperationException("Подтверждение не завершено. Повторите вход."); }
        await api.CompleteExternalTransitionAsync(ct);
        if (pending.Action == "login") return PublishResume(new("login", api.Session.Authenticated, false, api.Session.FamilyId));
        var expected = pending.FamilyId!.Value;
        Ensure(expected);
        if (result.Proof is not null) SetProof(result.Proof, pending.Action, expected);
        await RefreshAsync(ct);
        return PublishResume(new(pending.Action, true, result.Proof is not null, expected));
    }

    public async Task CancelOAuthAsync(CancellationToken ct = default)
    {
        var bridge = await Module();
        if (await bridge.InvokeAsync<BrowserOAuthPending?>("readPending", ct) is { } pending)
        {
            await api.SendAsync(HttpMethod.Post, "auth/external/" + pending.TransactionId + "/cancel", ct: ct);
            await bridge.InvokeVoidAsync("clearPending", ct);
        }
        ClearProof(); await api.AbortExternalTransitionAsync(ct);
    }

    public void ClearProof() { proof = null; proofFamily = null; }
    private BrowserOAuthResume PublishResume(BrowserOAuthResume value) { completedOAuthResume = value; OAuthResumed?.Invoke(); return value; }
    public void NewExport() { Export = null; ClearProof(); }
    private void SetProof(ReauthResponse value, string purpose, Guid current)
    {
        if (value.Purpose != purpose || value.ProofToken is not { Length: 43 } || value.ExpiresAt <= clock.GetUtcNow()) throw new InvalidOperationException("Подтверждение устарело. Повторите вход.");
        proof = value; proofFamily = current;
    }
    private string TakeProof(string purpose)
    {
        if (!HasProof(purpose)) throw new InvalidOperationException("Подтвердите вход для этого действия.");
        var token = proof!.ProofToken; ClearProof(); return token;
    }
    public static void ValidateProviderUrl(string provider, string url)
    {
        Provider(provider);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var value) || value.Scheme != "https" || value.Port != 443 || value.UserInfo.Length != 0
            || value.Fragment.Length != 0 || value.AbsolutePath != "/authorize" || value.Host != (provider == "vk" ? "id.vk.ru" : "oauth.yandex.ru"))
            throw new InvalidOperationException("Сервер вернул неподходящий адрес входа.");
    }
    private static void Provider(string value) { if (value is not ("vk" or "yandex")) throw new ArgumentException("Неизвестный способ входа."); }
    private static void ValidatePurpose(string value)
    {
        if (value is not ("link:vk" or "link:yandex" or "unlink:vk" or "unlink:yandex" or "set_password" or "set_recovery_email" or "export" or "delete_account"))
            throw new ArgumentException("Неизвестное действие подтверждения.");
    }
    private Guid Family() => api.Session is { Authenticated: true, FamilyId: { } current } ? current : throw Changed();
    private void Ensure(Guid expected) { if (!api.Session.Authenticated || api.Session.FamilyId != expected) throw Changed(); }
    private static BrowserApiException Changed() => new(409, "account_changed", "Аккаунт изменился. Повторите действие в текущем аккаунте.");
    private Task<IJSObjectReference> Module()
    {
        if (module is { IsFaulted: true } or { IsCanceled: true }) module = null;
        return module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/accounts.js").AsTask();
    }
    private Task SessionChanged(BrowserSession session)
    {
        if (family != session.FamilyId)
        { family = session.FamilyId; Me = null; Identities = []; Devices = []; NextCursor = null; Export = null; completedOAuthResume = null; ClearProof(); }
        return Task.CompletedTask;
    }
    public async ValueTask DisposeAsync()
    {
        api.SessionChanged -= SessionChanged; ClearProof();
        if (module is { IsCompletedSuccessfully: true }) await module.Result.DisposeAsync();
    }
}
