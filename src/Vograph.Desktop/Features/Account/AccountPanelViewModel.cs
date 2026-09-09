using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Services;
using Vograph.Desktop.Services.Accounts;
using Vograph.Desktop.Services.Profiles;
using Zapara.Contracts.Accounts;

namespace Vograph.Desktop.Features.Account;

/// <summary>Not a ViewModelBase: login/logout outlive the graph they replace.</summary>
public sealed partial class AccountPanelViewModel : ObservableObject, IDisposable
{
    private readonly ProfileSwitchCoordinator? profiles;
    private readonly AccountUiService? service;
    private readonly CancellationTokenSource lifetime = new();
    private ProfileSnapshot? snapshot;
    private bool disposed;
    private string? cursor;
    [ObservableProperty] private bool registrationAvailable;
    private string T(string key) => Loc.Current.T(key);

    public AccountPanelViewModel(ProfileSwitchCoordinator? profiles = null, AccountUiService? service = null)
    {
        this.profiles = profiles;
        this.service = service;
        snapshot = profiles?.Snapshot;
        if (profiles is not null) profiles.Changed += Apply;
        Status = T("accountUnconfigured");
    }

    [ObservableProperty] private string username = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string displayName = "";
    [ObservableProperty] private string currentPassword = "";
    [ObservableProperty] private string newPassword = "";
    [ObservableProperty] private string accountName = "";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool registration;
    [ObservableProperty] private bool busy;
    [ObservableProperty] private bool ready;
    [ObservableProperty] private bool confirmLogout;
    [ObservableProperty] private bool hasMore;
    public ObservableCollection<DeviceResponse> Devices { get; } = [];
    public bool IsGuest => snapshot?.Profile.IsGuest != false;
    public bool IsAccount => !IsGuest;
    public bool CanAct => Ready && !Busy && !disposed && service is not null && snapshot?.Phase == ProfilePhase.Idle;
    public bool NeedsRecovery => snapshot?.Phase == ProfilePhase.RecoveryRequired;
    public bool ShowLogin => IsGuest || snapshot?.ReauthRequired == true;
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanAct));
    partial void OnReadyChanged(bool value) => OnPropertyChanged(nameof(CanAct));
    partial void OnRegistrationChanged(bool value)
    {
        ClearSecrets();
        if (value && !RegistrationAvailable) Registration = false;
    }

    public async Task InitializeAsync()
    {
        if (profiles is null) { Ready = true; return; }
        await RunAsync(async () =>
        {
            Apply(await profiles.RestoreAsync(lifetime.Token));
            var expected = profiles.Snapshot.Identity;
            var user = await service!.CachedUserAsync(lifetime.Token);
            if (profiles.Snapshot.Identity == expected && user is not null) Present(user);
        });
        Ready = true;
        await RunAsync(async () =>
        {
            RegistrationAvailable = (await service!.CapabilitiesAsync(lifetime.Token)).Registration;
        });
    }

    private void Apply(ProfileSnapshot value)
    {
        if (disposed) return;
        if (snapshot?.Identity != value.Identity)
        {
            Devices.Clear(); cursor = null; HasMore = false; AccountName = ""; DisplayName = "";
            ClearSecrets(); ConfirmLogout = false;
        }
        snapshot = value;
        Status = value.Phase == ProfilePhase.RecoveryRequired ? T("accountRecovery")
            : value.AccountFailure is { } failure ? FailureText(failure)
            : value.Failure is not null ? T("accountTransitionFailed")
            : value.ReauthRequired ? T("accountReauth") : T(value.Profile.IsGuest ? "accountGuest" : "accountLocal");
        foreach (var name in new[] { nameof(IsGuest), nameof(IsAccount), nameof(CanAct), nameof(NeedsRecovery), nameof(ShowLogin) })
            OnPropertyChanged(name);
    }

    private void Present(UserResponse user)
    {
        AccountName = user.Username;
        DisplayName = user.DisplayName ?? "";
    }

    [RelayCommand] private void ToggleRegistration() { if (CanAct && RegistrationAvailable) Registration = !Registration; }
    [RelayCommand] private void RequestLogout() { ClearSecrets(); ConfirmLogout = true; }
    [RelayCommand] private void CancelLogout() => ConfirmLogout = false;

    [RelayCommand]
    private async Task Submit()
    {
        var secret = Password;
        ClearSecrets();
        if (!CanAct) return;
        await RunAsync(async () =>
        {
            var request = new RegisterRequest(Username, secret, string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName);
            if (Registration)
            {
                if (!RegistrationAvailable) { Status = T("accountRegistrationUnavailable"); return; }
                await service!.RegisterAsync(request, lifetime.Token);
                Registration = false;
                Status = T("accountCreated");
            }
            else
            {
                var result = await profiles!.LoginAsync(request.Username, request.Password, lifetime.Token);
                Apply(result.Snapshot);
                if (result.Committed && result.Snapshot.Phase == ProfilePhase.Idle)
                {
                    var user = await service!.CachedUserAsync(lifetime.Token);
                    if (user is not null) Present(user);
                }
            }
        });
    }

    [RelayCommand]
    private async Task Logout()
    {
        ClearSecrets();
        if (!CanAct || !ConfirmLogout) return;
        await RunAsync(async () =>
        {
            var result = await profiles!.LogoutAsync(lifetime.Token);
            Apply(result.Snapshot);
            if (result.Committed) Status = T("accountLogoutLocal");
        });
    }

    [RelayCommand]
    private async Task Recover()
    {
        if (Busy || !NeedsRecovery) return;
        await RunAsync(async () => Apply(await profiles!.RecoverAsync(lifetime.Token)));
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (Busy || disposed) return;
        var expected = profiles?.Snapshot.Identity;
        Busy = true;
        try { await action(); }
        catch (AccountClientException ex)
        {
            if (IsCurrent())
            {
                if (ex.Failure is AccountClientFailure.InvalidSession or AccountClientFailure.ReauthenticationRequired && snapshot is not null)
                    Apply(snapshot with { ReauthRequired = true });
                Status = FailureText(ex.Failure);
            }
        }
        catch (ArgumentException) { if (IsCurrent()) Status = T("accountValidation"); }
        catch (OperationCanceledException) { if (IsCurrent()) Status = T("accountCancelled"); }
        catch (Exception) { if (IsCurrent()) Status = T("accountFailed"); }
        finally { ClearSecrets(); Busy = false; }
        bool IsCurrent() => !disposed && profiles?.Snapshot.Identity == expected;
    }

    private string FailureText(AccountClientFailure failure) => T(failure switch
    {
        AccountClientFailure.InvalidCredentials => "accountBadLogin",
        AccountClientFailure.UsernameUnavailable => "accountUsernameTaken",
        AccountClientFailure.InvalidSession or AccountClientFailure.ReauthenticationRequired => "accountReauth",
        AccountClientFailure.Transport or AccountClientFailure.Timeout => "accountOffline",
        AccountClientFailure.NotConfigured => "accountUnconfigured",
        AccountClientFailure.RateLimited => "accountRateLimited",
        AccountClientFailure.RegistrationUnavailable => "accountRegistrationUnavailable",
        _ => "accountFailed"
    });

    public void ClearSecrets() { Password = ""; CurrentPassword = ""; NewPassword = ""; }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (profiles is not null) profiles.Changed -= Apply;
        lifetime.Cancel(); ClearSecrets(); Devices.Clear(); lifetime.Dispose();
    }
}
