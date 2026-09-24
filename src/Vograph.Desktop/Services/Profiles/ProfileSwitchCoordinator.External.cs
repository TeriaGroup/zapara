using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Accounts;

namespace Vograph.Desktop.Services.Profiles;

public sealed partial class ProfileSwitchCoordinator
{
    // The generation starts before opening the browser, never when a delayed callback arrives.
    internal async Task<ProfileSwitchResult> ExternalAsync(Func<CancellationToken, Task<SessionResponse?>> complete,
        bool login, CancellationToken ct)
    {
        var request = await BeginAsync(ct);
        SessionResponse? issued = null;
        var activated = false;
        try
        {
            issued = await complete(request.Cancellation.Token).ConfigureAwait(false);
            Check(request);
            if (!login) return new(true, Snapshot);
            if (issued is null) throw new AccountClientException(AccountClientFailure.InvalidPayload);
            var result = await SwitchAsync(request, issued).ConfigureAwait(false);
            activated = result.Committed;
            return result;
        }
        catch (OperationCanceledException) { return new(false, Snapshot with { Failure = ProfileFailure.Cancelled }); }
        catch (AccountClientException ex)
        { return new(false, Snapshot with { Failure = ProfileFailure.Credentials, AccountFailure = ex.Failure }); }
        finally
        {
            // Dispatch can fail after SwitchAsync has durably activated the session.
            if (issued is not null && !activated && Snapshot.Identity != AccountSessionIdentity.From(issued))
                StartRemoteLogout(issued);
            FinishRequest(request);
        }
    }
}
