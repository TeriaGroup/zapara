using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Shell;
using Zapara.Contracts.Accounts;

namespace Vograph.Desktop.Services.Profiles;

/// <summary>Owns the current graph. All credential mutations must go through this boundary, not the SDK manager.</summary>
public sealed partial class ProfileSwitchCoordinator
{
    private readonly AccountHttpClient client;
    private readonly IAccountSessionVault vault;
    private readonly AccountSessionManager manager;
    private readonly Guid installationId;
    private readonly Func<Action, Task> dispatch;
    private readonly Action<ProfileRoot> publish;
    private readonly Func<ProfileDescriptor, AppServices> factory;
    private readonly TimeSpan timeout;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim transition = new(1, 1);
    private readonly object requests = new();
    private CancellationTokenSource? latest;
    private long requestGeneration;
    private TaskCompletionSource? commitFinished;
    private bool exiting;
    private ProfileRoot? retiring;
    private ExclusiveProfileLease? retiringLease;

    public ProfileSwitchCoordinator(ProfileRoot root, AccountHttpClient client, IAccountSessionVault vault,
        Guid installationId, Func<Action, Task> dispatch, Action<ProfileRoot> publish,
        Func<ProfileDescriptor, AppServices>? factory = null, TimeSpan? timeout = null, TimeProvider? clock = null)
    {
        if (installationId == Guid.Empty) throw new ArgumentException("Требуется идентификатор установки.");
        Current = root;
        this.client = client;
        this.vault = vault;
        manager = new(client, vault, clock);
        this.installationId = installationId;
        this.dispatch = dispatch;
        this.publish = publish;
        this.factory = factory ?? (profile => Current.Services.CreateProfile(profile));
        this.timeout = timeout ?? TimeSpan.FromSeconds(10);
        if (this.timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        this.clock = clock ?? TimeProvider.System;
        Snapshot = new(root.Services.Profile, null, false, ProfilePhase.Idle);
    }

    public ProfileRoot Current { get; private set; }
    public ProfileSnapshot Snapshot { get; private set; }
    /// <summary>Raised on the supplied dispatcher; no credentials are included.</summary>
    public event Action<ProfileSnapshot>? Changed;

    private sealed record Request(long Generation, CancellationTokenSource Cancellation, AccountSessionIdentity? Expected);
    private async Task<Request> BeginAsync(CancellationToken ct)
    {
        while (true)
        {
            Task? committed;
            lock (requests)
            {
                if (exiting) throw new InvalidOperationException("Приложение завершает работу.");
                committed = commitFinished?.Task;
                if (committed is null)
                {
                    if (latest is { } previous) _ = previous.CancelAsync();
                    latest = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    return new(++requestGeneration, latest, Snapshot.Identity);
                }
            }
            // The commit has linearized. A new request starts against its resulting identity, never its predecessor.
            await committed.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    private void Check(Request request)
    {
        request.Cancellation.Token.ThrowIfCancellationRequested();
        lock (requests)
            if (request.Generation != requestGeneration || request.Expected != Snapshot.Identity || exiting)
                throw new OperationCanceledException(request.Cancellation.Token);
    }

    public async Task<ProfileSwitchResult> LoginAsync(string login, string password, CancellationToken ct = default)
    {
        var request = await BeginAsync(ct).ConfigureAwait(false);
        try
        {
            // Transport only: the active vault is untouched until the old graph is drained.
            var session = await client.LoginAsync(new LoginRequest(login, password,
                new DeviceInput(installationId, "Windows", "windows")), request.Cancellation.Token).ConfigureAwait(false);
            Check(request);
            return await SwitchAsync(request, session).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return new(false, Snapshot with { Failure = ProfileFailure.Cancelled }); }
        catch (AccountClientException ex)
        { return new(false, Snapshot with { Failure = ProfileFailure.Credentials, AccountFailure = ex.Failure }); }
        finally { FinishRequest(request); }
    }

    public async Task<ProfileSwitchResult> LogoutAsync(CancellationToken ct = default)
    {
        var request = await BeginAsync(ct).ConfigureAwait(false);
        try { return await SwitchAsync(request, null).ConfigureAwait(false); }
        catch (OperationCanceledException) { return new(false, Snapshot with { Failure = ProfileFailure.Cancelled }); }
        finally { FinishRequest(request); }
    }

    private void FinishRequest(Request request)
    {
        lock (requests)
        {
            if (ReferenceEquals(latest, request.Cancellation)) latest = null;
            request.Cancellation.Dispose();
        }
    }

    private async Task<ProfileRoot> PrepareAsync(ProfileDescriptor profile)
    {
        var services = await Task.Run(() => factory(profile)).ConfigureAwait(false);
        try
        {
            ShellViewModel? shell = null;
            await dispatch(() => shell = new ShellViewModel(services, attach: false)).ConfigureAwait(false);
            return new(services, shell!);
        }
        catch { await services.CloseCandidateAsync().ConfigureAwait(false); throw; }
    }

    private async Task NotifyAsync()
    {
        await dispatch(() =>
        {
            // One faulty observer must not alter the credential/graph transaction.
            foreach (var handler in Changed?.GetInvocationList() ?? [])
            {
                try { ((Action<ProfileSnapshot>)handler)(Snapshot); }
                catch (Exception ex) { Current.Services.Log.Warn("profile observer failed: " + ex.GetType().Name); }
            }
        }).ConfigureAwait(false);
    }
}
