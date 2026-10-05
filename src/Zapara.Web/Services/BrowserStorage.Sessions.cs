using Microsoft.JSInterop;

namespace Zapara.Web.Services;

public sealed record SessionMarker(string Generation, string Phase, string OwnerTab, long ChangedAt)
{
    public static readonly SessionMarker Initial = new("initial", "stable", "", 0);
}
public sealed record SessionCoordinationState(string TabId, SessionMarker Marker, bool ExclusiveSupported);

public sealed partial class BrowserStorage
{
    private Task<IJSObjectReference>? sessionsModule;
    private Task<IJSObjectReference> SessionsModule => sessionsModule ??= js.InvokeAsync<IJSObjectReference>("import", "./js/session-coordination.js").AsTask();
    internal async Task<SessionCoordinationState> InitializeSessionsAsync(DotNetObjectReference<BrowserApiClient> reference) =>
        await (await SessionsModule).InvokeAsync<SessionCoordinationState>("initialize", reference);
    internal async Task<SessionMarker> SessionMarkerAsync(string? expected = null) =>
        await (await SessionsModule).InvokeAsync<SessionMarker>("current", expected);
    internal async Task<SessionMarker?> BeginSessionTransitionAsync(string phase) =>
        await (await SessionsModule).InvokeAsync<SessionMarker?>("begin", phase);
    internal async Task<SessionMarker?> RecoverSessionTransitionAsync() =>
        await (await SessionsModule).InvokeAsync<SessionMarker?>("recover");
    internal async Task<bool> FinishSessionTransitionAsync(string generation) =>
        await (await SessionsModule).InvokeAsync<bool>("finish", generation);
    internal async Task<bool> AbandonSessionTransitionAsync(string generation) =>
        await (await SessionsModule).InvokeAsync<bool>("abandon", generation);
    internal async Task<bool> ParkExternalTransitionAsync(string generation) =>
        await (await SessionsModule).InvokeAsync<bool>("park", generation);
    internal async Task<bool> ReleaseSessionViewAsync(string generation) =>
        await (await SessionsModule).InvokeAsync<bool>("releaseView", generation);
    internal async ValueTask DisposeSessionsAsync()
    {
        if (sessionsModule is not null && sessionsModule.IsCompletedSuccessfully)
        {
            try { await sessionsModule.Result.InvokeVoidAsync("dispose"); await sessionsModule.Result.DisposeAsync(); }
            catch (JSDisconnectedException) { }
            sessionsModule = null;
        }
    }
}
