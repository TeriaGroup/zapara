using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Services.Profiles;
using Zapara.Contracts.Accounts;

namespace Vograph.Desktop.Services.Accounts;

/// <summary>Installation-owned adapter; never activates or clears credentials itself.</summary>
public sealed class AccountUiService(AccountHttpClient client, IAccountSessionVault vault,
    ProfileSwitchCoordinator profiles)
{
    private readonly AccountSessionManager sessions = new(client, vault);

    public Task<Zapara.Contracts.Accounts.ExternalResponses.AuthCapabilitiesResponse> CapabilitiesAsync(CancellationToken ct)
        => client.GetCapabilitiesAsync(ct);

    public Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken ct) => client.RegisterAsync(request, ct);

    public async Task<UserResponse?> CachedUserAsync(CancellationToken ct)
    {
        var expected = profiles.Snapshot.Identity;
        using var work = profiles.Current.Services.Work.Enter();
        work.ThrowIfStale();
        using var lease = await vault.AcquireAsync(ct);
        work.ThrowIfStale();
        var entry = lease.Read();
        return expected is not null && entry is not null && AccountSessionIdentity.From(entry.Session) == expected
            && profiles.Snapshot.Identity == expected ? entry.Session.User : null;
    }

    public Task<MeResponse> MeAsync(CancellationToken ct) => RunAsync((s, token) => client.GetMeAsync(s.AccessToken, token), ct);
    public Task<UserResponse> SaveAsync(string? name, CancellationToken ct)
        => RunAsync((s, token) => client.UpdateProfileAsync(s.AccessToken, new(name), token), ct);
    public Task<DevicesResponse> DevicesAsync(string? cursor, CancellationToken ct)
        => RunAsync((s, token) => client.ListDevicesAsync(s.AccessToken, 10, cursor, token), ct);

    public Task<ProfileSwitchResult?> RevokeAsync(Guid family, CancellationToken ct)
        => MutateAsync((s, token) => client.RevokeSessionAsync(s.AccessToken, family, token),
            profiles.Snapshot.Identity?.FamilyId == family, ct);
    public Task<ProfileSwitchResult?> RevokeAllAsync(CancellationToken ct)
        => MutateAsync((s, token) => client.RevokeAllAsync(s.AccessToken, token), true, ct);
    public Task<ProfileSwitchResult?> PasswordAsync(ChangePasswordRequest request, CancellationToken ct)
        => MutateAsync((s, token) => client.ChangePasswordAsync(s.AccessToken, request, token), true, ct);

    private async Task<T> RunAsync<T>(Func<SessionResponse, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        var current = profiles.Current;
        var expected = profiles.Snapshot.Identity ?? throw new AccountClientException(AccountClientFailure.ReauthenticationRequired);
        using var work = current.Services.Work.Enter();
        work.ThrowIfStale();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, work.Token);
        var session = await sessions.GetValidSessionAsync(linked.Token);
        Check();
        var result = await action(session, linked.Token);
        Check();
        return result;

        void Check()
        {
            work.ThrowIfStale();
            if (profiles.Snapshot.Identity != expected || AccountSessionIdentity.From(session) != expected || profiles.Current != current)
                throw new AccountClientException(AccountClientFailure.SessionChanged);
        }
    }

    private async Task<ProfileSwitchResult?> MutateAsync(Func<SessionResponse, CancellationToken, Task> action,
        bool leave, CancellationToken ct)
    {
        var expected = profiles.Snapshot.Identity;
        await RunAsync(async (session, token) => { await action(session, token); return true; }, ct);
        // RunAsync has released its outer work lease. A transition here must never drain itself.
        if (profiles.Snapshot.Identity != expected) throw new AccountClientException(AccountClientFailure.SessionChanged);
        return leave ? await profiles.LogoutAsync(ct) : null;
    }
}
