using Microsoft.JSInterop;

namespace Zapara.Web.Services;

public sealed partial class BrowserApiClient
{
    public bool Transitioning { get; private set; }
    public event Action? TransitionChanged;
    public string? TransitionError { get; private set; }
    public bool ExternalTransitionPending { get; private set; }
    public string? ObservedSessionGeneration { get; private set; }
    public string? ConfirmedSessionGeneration { get; private set; }
    private bool coordinated, coordinationDisposed, sessionPublishPending, exclusiveSupported, coordinationRequested;
    private string? tabId, sessionGeneration, ownedTransition, invalidatedGeneration, publishedSessionGeneration;
    private DotNetObjectReference<BrowserApiClient>? sessionReference;
    private Task? coordinationInitialization;
    private int markerHandling, sessionNotificationDepth;
    private bool externalCompletionRequested;

    public Task InitializeBrowserCoordinationAsync()
    {
        coordinationRequested = true;
        if (coordinationInitialization is { IsCompleted: true } && !coordinated) coordinationInitialization = null;
        return coordinationInitialization ??= InitializeCoordinationCoreAsync();
    }
    private async Task InitializeCoordinationCoreAsync()
    {
        try
        {
            sessionReference = DotNetObjectReference.Create(this);
            var status = await storage.InitializeSessionsAsync(sessionReference);
            tabId = status.TabId; coordinated = true; exclusiveSupported = status.ExclusiveSupported;
            ObservedSessionGeneration = status.Marker.Generation;
            if (status.Marker.Phase == "stable") sessionGeneration = status.Marker.Generation;
            else { ExternalTransitionPending = status.Marker.Phase == "redirect" && status.Marker.OwnerTab == tabId; SetTransitioning(true); await RefreshSessionAsync(); }
        }
        catch (Exception error) when (error is JSException or BrowserApiException)
        { TransitionError = "Не удалось подтвердить переключение аккаунта. Проверьте соединение и повторите проверку."; SetTransitioning(true); }
    }

    public async Task<string> BeginExternalTransitionAsync(CancellationToken ct = default)
    {
        if (Session.CsrfToken.Length == 0) await RefreshSessionAsync(ct);
        await sessionGate.WaitAsync(ct);
        try { return (await BeginOwnedTransitionAsync("redirect", ct))?.Generation ?? ""; }
        finally { sessionGate.Release(); }
    }
    public Task CompleteExternalTransitionAsync(CancellationToken ct = default) => FinishExternalAsync(ct);
    public Task AbortExternalTransitionAsync(CancellationToken ct = default) => FinishExternalAsync(ct);
    private async Task FinishExternalAsync(CancellationToken ct)
    {
        if (coordinated && ownedTransition is { } generation && ExternalTransitionPending)
        {
            await storage.ParkExternalTransitionAsync(generation);
            ownedTransition = null;
        }
        externalCompletionRequested = true; ExternalTransitionPending = false;
        try { await RefreshSessionAsync(ct); }
        finally { externalCompletionRequested = false; }
    }

    [JSInvokable] public Task SessionMarkerChanged(SessionMarker marker)
    {
        ObserveMarker(marker);
        if (coordinationDisposed || !coordinated || marker.Generation == ownedTransition) return Task.CompletedTask;
        if (marker.Generation != sessionGeneration || marker.Phase != "stable")
        {
            if (invalidatedGeneration != marker.Generation) { invalidatedGeneration = marker.Generation; Revision++; }
            SetTransitioning(true);
            _ = ReconcileMarkerAsync();
        }
        return Task.CompletedTask;
    }
    [JSInvokable] public Task SessionCoordinationFailed()
    {
        TransitionError = "Координация вкладок недоступна. Личные данные скрыты до проверки аккаунта.";
        Available = false; SetTransitioning(true); return Task.CompletedTask;
    }
    private async Task ReconcileMarkerAsync()
    {
        if (Interlocked.CompareExchange(ref markerHandling, 1, 0) != 0) return;
        try { await RefreshSessionAsync(); }
        catch (Exception error) when (error is BrowserApiException or JSException or OperationCanceledException)
        { Available = false; TransitionError = "Переключение аккаунта ожидает подтверждения. Проверьте соединение."; }
        finally { Interlocked.Exchange(ref markerHandling, 0); TransitionChanged?.Invoke(); }
    }

    private void SetTransitioning(bool value)
    {
        Transitioning = value;
        if (!value) TransitionError = null;
        TransitionChanged?.Invoke();
    }

    private async Task<SessionMarker?> PrepareBootstrapAsync(CancellationToken ct)
    {
        if (coordinationRequested && !coordinated) throw Failure(0, "session_coordination_unavailable");
        if (!coordinated) return null;
        ct.ThrowIfCancellationRequested();
        var marker = await ReadMarkerSafeAsync(sessionGeneration);
        if (marker.Phase != "stable" && marker.Generation != ownedTransition)
        {
            SetTransitioning(true);
            marker = await storage.RecoverSessionTransitionAsync() ?? throw Failure(409, "session_transition");
            ownedTransition = marker.Generation;
        }
        if (!externalCompletionRequested) ExternalTransitionPending = marker.Phase == "redirect" && marker.OwnerTab == tabId;
        if (marker.Generation != sessionGeneration) SetTransitioning(true);
        return marker;
    }

    private async Task<SessionMarker?> BeginOwnedTransitionAsync(string phase, CancellationToken ct)
    {
        if (coordinationRequested && !coordinated) throw Failure(0, "session_coordination_unavailable");
        if (!coordinated) return null;
        if (!exclusiveSupported) throw Failure(0, "session_coordination_unavailable");
        ct.ThrowIfCancellationRequested();
        if (ownedTransition is not null) throw Failure(409, "session_transition");
        var current = await ReadMarkerSafeAsync(sessionGeneration);
        if (current.Generation != sessionGeneration) { SetTransitioning(true); throw Failure(409, "account_changed"); }
        var marker = await storage.BeginSessionTransitionAsync(phase) ?? throw Failure(409, "session_transition");
        ObservedSessionGeneration = marker.Generation;
        ExternalTransitionPending = phase == "redirect";
        ownedTransition = marker.Generation; SetTransitioning(true);
        return marker;
    }

    private async Task<SessionMarker?> RequestMarkerAsync(bool bootstrap, HttpMethod method, string path)
    {
        if (coordinationRequested && !coordinated) throw Failure(0, "session_coordination_unavailable");
        if (!coordinated) return null;
        var marker = await ReadMarkerSafeAsync(sessionGeneration);
        var owned = marker.Generation == ownedTransition && marker.OwnerTab == tabId;
        var authRequest = path is "auth/login" or "auth/logout" || ChangesSession(method, path)
            || path.StartsWith("auth/external/", StringComparison.Ordinal) && path.EndsWith("/start", StringComparison.Ordinal);
        if (owned && marker.Phase != "stable" && marker.Generation != sessionGeneration && !bootstrap && !authRequest)
            throw Failure(409, "session_transition");
        if ((!bootstrap && marker.Generation != sessionGeneration && !owned) || marker.Phase != "stable" && !owned)
        { SetTransitioning(true); throw Failure(409, "account_changed"); }
        return marker;
    }

    private async Task CheckMarkerAsync(SessionMarker? started)
    {
        if (started is null) return;
        var current = await ReadMarkerSafeAsync(started.Generation);
        if (current.Generation != started.Generation || current.Phase != "stable" && current.Generation != ownedTransition)
        { SetTransitioning(true); throw Failure(409, "account_changed"); }
    }

    private void BindMarker(SessionMarker? marker)
    {
        if (marker is not null) { sessionGeneration = marker.Generation; ConfirmedSessionGeneration = marker.Generation; invalidatedGeneration = null; }
    }

    private async Task<SessionMarker> ReadMarkerSafeAsync(string? expected)
    {
        try { var marker = await storage.SessionMarkerAsync(expected); ObserveMarker(marker); return marker; }
        catch (JSException)
        {
            Available = false; SetTransitioning(true);
            throw Failure(0, "session_coordination_unavailable");
        }
    }

    private void ObserveMarker(SessionMarker marker)
    {
        ObservedSessionGeneration = marker.Generation;
        if (ownedTransition is not null && marker.Generation != ownedTransition)
        {
            ownedTransition = null;
            ExternalTransitionPending = marker.Phase == "redirect" && marker.OwnerTab == tabId;
        }
    }

    private async Task CompleteMarkerAsync(SessionMarker? marker)
    {
        if (marker is null || sessionNotificationDepth > 0 || sessionPublishPending) return;
        await CheckMarkerAsync(marker);
        if (ExternalTransitionPending)
        {
            await storage.ParkExternalTransitionAsync(marker.Generation);
            return;
        }
        if (ownedTransition == marker.Generation)
        {
            if (!await storage.FinishSessionTransitionAsync(marker.Generation)) throw Failure(409, "account_changed");
            ownedTransition = null;
        }
        if (await storage.ReleaseSessionViewAsync(marker.Generation)) SetTransitioning(false);
    }

    private async Task RecoverFailedTransitionAsync(SessionMarker? marker)
    {
        if (marker is null || ownedTransition != marker.Generation) return;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var session = await ReadSessionAsync(cleanup.Token);
            await CheckMarkerAsync(marker); BindMarker(marker); Available = true;
            await SetSessionAsync(session); await CompleteMarkerAsync(marker);
        }
        catch (Exception error) when (error is BrowserApiException or JSException or OperationCanceledException)
        {
            Available = false;
            await AbandonMarkerAsync();
        }
    }

    private async Task AbandonMarkerAsync()
    {
        var hadOwnedTransition = ownedTransition is not null;
        if (ownedTransition is { } generation)
        {
            try { await storage.AbandonSessionTransitionAsync(generation); }
            catch (JSException) { }
            ownedTransition = null;
        }
        if (coordinated && (hadOwnedTransition || Transitioning || sessionPublishPending)) SetTransitioning(true);
    }

    private bool ChangesSession(HttpMethod method, string path) => method != HttpMethod.Get &&
        (path is "account/password/change" or "account/password/set" or "account/sessions/revoke-all" or "auth/password-reset/confirm"
         || method == HttpMethod.Delete && (path == "account" || Session.FamilyId is { } current && path == "account/devices/" + current.ToString("D")));

    private async Task<byte[]> SendSessionMutationAsync(HttpMethod method, string path, object? body, Guid? expectedFamily, CancellationToken ct)
    {
        await sessionGate.WaitAsync(ct);
        SessionMarker? marker = null;
        try
        {
            var before = Capture(expectedFamily);
            marker = await BeginOwnedTransitionAsync("transition", ct);
            var bytes = await SendBytesAsync(method, path, body, before, ct);
            var session = await ReadSessionAsync(ct);
            await CheckMarkerAsync(marker); BindMarker(marker); Available = true;
            await SetSessionAsync(session); await CompleteMarkerAsync(marker);
            return bytes;
        }
        catch { await RecoverFailedTransitionAsync(marker); throw; }
        finally { sessionGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        coordinationDisposed = true;
        await storage.DisposeSessionsAsync(); sessionReference?.Dispose();
    }
}
