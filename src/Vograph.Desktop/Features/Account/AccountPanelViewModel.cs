using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Legal;
using Vograph.Desktop.Services;
using Vograph.Desktop.Services.Accounts;
using Vograph.Desktop.Services.Profiles;
using Zapara.Contracts.Accounts;
using Zapara.Contracts.Accounts.ExternalResponses;

namespace Vograph.Desktop.Features.Account;

/// <summary>Not a ViewModelBase: login/logout outlive the graph they replace.</summary>
public sealed partial class AccountPanelViewModel : ObservableObject, IDisposable
{
    private readonly ProfileSwitchCoordinator? profiles;
    private readonly AccountUiService? service;
    private readonly CancellationTokenSource lifetime = new();
    private ProfileSnapshot? snapshot;
    private bool disposed;
    private string lastPresentedDisplayName = "";
    private string? cursor;
    [ObservableProperty] private bool registrationAvailable;
    [ObservableProperty] private bool hasPassword;
    private string T(string key) => Loc.Current.T(key);

    [RelayCommand]
    private void OpenAgreement() => OpenDocument = LegalDocuments.Agreement;

    [RelayCommand]
    private void OpenPolicy() => OpenDocument = LegalDocuments.Policy;

    [RelayCommand]
    private void CloseDocument() => OpenDocument = null;

    public AccountPanelViewModel(ProfileSwitchCoordinator? profiles = null, AccountUiService? service = null)
    {
        this.profiles = profiles;
        this.service = service;
        snapshot = profiles?.Snapshot;
        if (profiles is not null) profiles.Changed += Apply;
        Identities.CollectionChanged += OnIdentitiesChanged;
        Devices.CollectionChanged += (_, _) => RefreshDeviceBrowse();
        Status = T("accountUnconfigured");
    }

    [ObservableProperty] private string username = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string displayName = "";
    public bool HasUnsavedDisplayName => IsAccount && DisplayName != lastPresentedDisplayName;
    partial void OnDisplayNameChanged(string value) => OnPropertyChanged(nameof(HasUnsavedDisplayName));
    public void DiscardDisplayNameDraft()
    { if (!Busy) DisplayName = lastPresentedDisplayName; }
    [ObservableProperty] private string currentPassword = "";
    [ObservableProperty] private string newPassword = "";
    public bool IsSameNewPassword => AccountPasswordRules.Same(CurrentPassword, NewPassword);
    partial void OnCurrentPasswordChanged(string value) => OnPropertyChanged(nameof(IsSameNewPassword));
    partial void OnNewPasswordChanged(string value) => OnPropertyChanged(nameof(IsSameNewPassword));
    [ObservableProperty] private string accountName = "";
    [ObservableProperty] private string status = "";
    [ObservableProperty] private bool registration;
    [ObservableProperty] private bool busy;
    [ObservableProperty] private bool ready;
    [ObservableProperty] private bool confirmLogout;
    [ObservableProperty] private bool confirmDelete;
    [ObservableProperty] private DeviceResponse? pendingRevokeDevice;
    [ObservableProperty] private bool confirmRevokeAll;
    public bool HasPendingRevokeDevice => PendingRevokeDevice is not null;
    public string RevokeDeviceCaption => PendingRevokeDevice is { } device
        ? device.IsCurrent ? $"Завершить текущее устройство «{device.DeviceName}»? Здесь потребуется войти снова."
            : $"Завершить сеанс устройства «{device.DeviceName}»? На нём потребуется войти снова."
        : "";
    partial void OnPendingRevokeDeviceChanged(DeviceResponse? value)
    { OnPropertyChanged(nameof(HasPendingRevokeDevice)); OnPropertyChanged(nameof(RevokeDeviceCaption)); }
    [ObservableProperty] private bool hasMore;
    [ObservableProperty] private bool vkAvailable;
    [ObservableProperty] private bool yandexAvailable;
    [ObservableProperty] private bool recoveryAvailable;
    [ObservableProperty] private bool capabilitiesFailed;
    [ObservableProperty] private bool resetRequested;
    [ObservableProperty] private string resetToken = "";
    [ObservableProperty] private string resetNewPassword = "";
    public bool CanRequestReset => ShowRecovery && !Busy && Username.Trim().Length >= 3;
    public bool CanConfirmReset => ShowRecovery && ResetRequested && !Busy && ResetToken.Trim().Length >= 8
        && ResetNewPassword.Length is >= 12 and <= 128;
    partial void OnResetRequestedChanged(bool value) => OnPropertyChanged(nameof(CanConfirmReset));
    partial void OnResetTokenChanged(string value) => OnPropertyChanged(nameof(CanConfirmReset));
    partial void OnResetNewPasswordChanged(string value) => OnPropertyChanged(nameof(CanConfirmReset));
    partial void OnUsernameChanged(string value) => OnPropertyChanged(nameof(CanRequestReset));
    [ObservableProperty] private string proof = "";
    [ObservableProperty] private ExportJobResponse? exportJob;
    [ObservableProperty] private byte[]? exportPayload;
    [ObservableProperty] private string? exportPath;
    [ObservableProperty] private string? exportFileName;
    [ObservableProperty] private string? authorizeUrl;
    [ObservableProperty] private LegalText? openDocument;
    public bool HasOpenDocument => OpenDocument is not null;
    public string OpenTitle => OpenDocument?.Title ?? "";
    public string OpenBody => OpenDocument?.Body ?? "";
    partial void OnOpenDocumentChanged(LegalText? value)
    {
        DocumentSearch = "";
        SelectedDocumentParagraph = "";
        OnPropertyChanged(nameof(HasOpenDocument));
        OnPropertyChanged(nameof(OpenTitle));
        OnPropertyChanged(nameof(OpenBody));
        OnPropertyChanged(nameof(DocumentMatches));
    }
    public ObservableCollection<DeviceResponse> Devices { get; } = [];
    [ObservableProperty] private string deviceSearch = "";
    [ObservableProperty] private int deviceScopeIndex;
    public IReadOnlyList<string> DeviceScopes { get; } = ["Все устройства", "Другие устройства", "Текущее устройство"];
    public IReadOnlyList<DeviceResponse> FilteredDevices => DeviceBrowse.Filter(Devices, DeviceSearch, DeviceScopeIndex);
    public bool HasDeviceFilters => DeviceSearch.Trim().Length > 0 || DeviceScopeIndex != 0;
    public bool NoDeviceMatches => Devices.Count > 0 && FilteredDevices.Count == 0 && HasDeviceFilters;
    public string DeviceResultCount => $"Показано {FilteredDevices.Count} из {Devices.Count} загруженных";
    partial void OnDeviceSearchChanged(string value) => RefreshDeviceBrowse();
    partial void OnDeviceScopeIndexChanged(int value) => RefreshDeviceBrowse();
    [RelayCommand] private void ResetDeviceFilters() { DeviceSearch = ""; DeviceScopeIndex = 0; }
    private void RefreshDeviceBrowse()
    { OnPropertyChanged(nameof(FilteredDevices)); OnPropertyChanged(nameof(HasDeviceFilters)); OnPropertyChanged(nameof(NoDeviceMatches)); OnPropertyChanged(nameof(DeviceResultCount)); }
    public ObservableCollection<ExternalIdentityResponse> Identities { get; } = [];
    public bool IsGuest => snapshot?.Profile.IsGuest != false;

    public async Task<SupportThreadResponse[]?> LoadSupportAsync(CancellationToken ct)
    {
        if (service is null || IsGuest) return null;
        return await service.SupportAsync(ct);
    }

    public async Task<SupportThreadResponse?> SendSupportAsync(Guid? threadId, string subject, string body, CancellationToken ct)
        => await SendSupportAsync(threadId, subject, body, [], ct);

    public async Task<SupportThreadResponse?> SendSupportAsync(Guid? threadId, string subject, string body, IReadOnlyList<SupportUpload> files, CancellationToken ct)
    {
        if (service is null || IsGuest) return null;
        if (files.Count == 0)
        {
            return threadId is Guid plain
                ? await service.ContinueSupportAsync(plain, body, ct)
                : await service.OpenSupportAsync(subject, body, ct);
        }
        return threadId is Guid id
            ? await service.ContinueSupportAsync(id, body, files, ct)
            : await service.OpenSupportAsync(subject, body, files, ct);
    }
    public bool IsAccount => !IsGuest;
    public bool CanAct => Ready && !Busy && !disposed && service is not null && snapshot?.Phase == ProfilePhase.Idle;
    public bool NeedsRecovery => snapshot?.Phase == ProfilePhase.RecoveryRequired;
    public bool ShowLogin => IsGuest || snapshot?.ReauthRequired == true;
    public bool ShowRecovery => RecoveryAvailable && ShowLogin;
    public bool ShowVkLogin => VkAvailable && ShowLogin;
    public bool ShowYandexLogin => YandexAvailable && ShowLogin;
    public bool ShowExternalLogin => ShowVkLogin || ShowYandexLogin;
    public bool ShowVkLink => VkAvailable && IsAccount && Identities.All(i => i.Provider != "vk");
    public bool ShowYandexLink => YandexAvailable && IsAccount && Identities.All(i => i.Provider != "yandex");
    public bool ShowProviderProof => IsAccount && !HasPassword && Identities.Count > 0;
    public bool CanUnlinkIdentity => HasPassword || Identities.Count > 1;
    public bool CanDownloadExport => ExportJob?.Status == "ready";
    partial void OnBusyChanged(bool value) { OnPropertyChanged(nameof(CanAct)); OnPropertyChanged(nameof(CanRequestReset)); OnPropertyChanged(nameof(CanConfirmReset)); }
    partial void OnReadyChanged(bool value) => OnPropertyChanged(nameof(CanAct));
    partial void OnVkAvailableChanged(bool value) => NotifyExternal();
    partial void OnYandexAvailableChanged(bool value) => NotifyExternal();
    partial void OnRecoveryAvailableChanged(bool value) { OnPropertyChanged(nameof(ShowRecovery)); OnPropertyChanged(nameof(CanRequestReset)); OnPropertyChanged(nameof(CanConfirmReset)); }
    partial void OnExportJobChanged(ExportJobResponse? value) => OnPropertyChanged(nameof(CanDownloadExport));
    private void OnIdentitiesChanged(object? sender, NotifyCollectionChangedEventArgs e) => NotifyExternal();
    partial void OnHasPasswordChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowProviderProof));
        OnPropertyChanged(nameof(CanUnlinkIdentity));
    }
    [ObservableProperty] private bool documentsAccepted;

    partial void OnRegistrationChanged(bool value)
    {
        ClearSecrets();
        DocumentsAccepted = false;
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
            if (expected is not null) await RefreshAuthenticationAsync();
        });
        Ready = true;
        await RefreshCapabilities();
    }

    private void Apply(ProfileSnapshot value)
    {
        if (disposed) return;
        if (snapshot?.Identity != value.Identity)
        {
            StopObservingAvatars();
            Avatar = null;
            Devices.Clear(); Identities.Clear(); cursor = null; HasMore = false; AccountName = ""; DisplayName = "";
            ClearSecrets(); ConfirmLogout = false; ConfirmDelete = false;
            PendingRevokeDevice = null; ConfirmRevokeAll = false;
            ResetRequested = false; ResetToken = ""; ResetNewPassword = "";
            HasPassword = false;
            ExportJob = null; ExportPayload = null; ExportPath = null; ExportFileName = null; AuthorizeUrl = null;
        }
        snapshot = value;
        if (value.ReauthRequired) Avatar = null;
        Status = value.Phase == ProfilePhase.RecoveryRequired ? T("accountRecovery")
            : value.AccountFailure is { } failure ? FailureText(failure)
            : value.Failure is not null ? T("accountTransitionFailed")
            : value.ReauthRequired ? T("accountReauth") : T(value.Profile.IsGuest ? "accountGuest" : "accountLocal");
        foreach (var name in new[] { nameof(IsGuest), nameof(IsAccount), nameof(CanAct), nameof(NeedsRecovery), nameof(ShowLogin) })
            OnPropertyChanged(name);
        OnPropertyChanged(nameof(HasUnsavedDisplayName));
        OnPropertyChanged(nameof(ShowProviderProof));
        NotifyExternal();
    }

    private void NotifyExternal()
    {
        foreach (var name in new[] { nameof(ShowRecovery), nameof(ShowVkLogin), nameof(ShowYandexLogin), nameof(ShowExternalLogin),
            nameof(ShowVkLink), nameof(ShowYandexLink), nameof(ShowProviderProof), nameof(CanUnlinkIdentity), nameof(CanRequestReset), nameof(CanConfirmReset) })
            OnPropertyChanged(name);
    }

    private void Present(UserResponse user)
    {
        AccountName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
        DisplayName = user.DisplayName ?? "";
        lastPresentedDisplayName = DisplayName;
        OnPropertyChanged(nameof(HasUnsavedDisplayName));
        OnPropertyChanged(nameof(AvatarInitials));
        _ = LoadProfileAvatarAsync(user.UserId);
    }

    public async Task RefreshRemoteAsync()
    {
        if (!CanAct || IsGuest || profiles?.Snapshot.Identity is not { } expected || service is null) return;
        var current = profiles.Current;
        using var work = current.Services.Work.Enter();
        if (!work.IsCurrent) return;
        try
        {
            var me = await service.MeAsync(work.Token);
            if (!work.IsCurrent || disposed || !ReferenceEquals(profiles.Current, current)
                || profiles.Snapshot.Identity != expected || profiles.Snapshot.ReauthRequired) return;
            var draft = DisplayName;
            var preserveDraft = draft != lastPresentedDisplayName;
            PresentAuthentication(me);
            if (preserveDraft) DisplayName = draft;
        }
        catch (AccountClientException ex) when (ex.Failure is AccountClientFailure.InvalidSession or AccountClientFailure.ReauthenticationRequired)
        {
            if (!disposed && work.IsCurrent && ReferenceEquals(profiles.Current, current)
                && profiles.Snapshot.Identity == expected)
                Apply(profiles.Snapshot with { ReauthRequired = true });
        }
        catch (Exception ex) when (ex is AccountClientException or OperationCanceledException) { }
    }

    private async Task RefreshAuthenticationAsync()
    {
        var expected = profiles!.Snapshot.Identity;
        var current = profiles.Current;
        if (expected is null) return;
        var me = await service!.MeAsync(lifetime.Token);
        if (disposed || profiles.Current != current || profiles.Snapshot.Identity != expected) return;
        PresentAuthentication(me);
        if (!HasPassword)
        {
            try
            {
                var identities = await service.ListIdentitiesAsync(lifetime.Token);
                if (disposed || profiles.Current != current || profiles.Snapshot.Identity != expected) return;
                Identities.Clear();
                foreach (var identity in identities) Identities.Add(identity);
            }
            catch (OperationCanceledException) { throw; }
            catch (AccountClientException) { }
        }
    }

    private void PresentAuthentication(MeResponse me)
    {
        var expected = profiles?.Snapshot.Identity;
        if (expected is null || me.User.UserId != expected.UserId || me.FamilyId != expected.FamilyId)
            throw new AccountClientException(AccountClientFailure.InvalidPayload);
        Present(me.User);
        HasPassword = me.AuthenticationMethods.Contains("password", StringComparer.Ordinal);
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
                if (!DocumentsAccepted) { Status = T("accountAcceptRequired"); return; }
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
                    HasPassword = true; // This session was authenticated with the app password.
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
        AccountClientFailure.InvalidSession or AccountClientFailure.ReauthenticationRequired
            or AccountClientFailure.InvalidExternalProof => "accountReauth",
        AccountClientFailure.Transport or AccountClientFailure.Timeout => "accountOffline",
        AccountClientFailure.NotConfigured or AccountClientFailure.ProviderUnavailable => "accountUnconfigured",
        AccountClientFailure.RateLimited => "accountRateLimited",
        AccountClientFailure.RegistrationUnavailable => "accountRegistrationUnavailable",
        _ => "accountFailed"
    });

    public void ClearSecrets() { Password = ""; CurrentPassword = ""; NewPassword = ""; Proof = ""; }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (profiles is not null) profiles.Changed -= Apply;
        Identities.CollectionChanged -= OnIdentitiesChanged;
        lifetime.Cancel(); ClearSecrets(); Devices.Clear(); Identities.Clear();
        StopObservingAvatars();
        Avatar = null;
        ExportPayload = null; lifetime.Dispose();
    }
}
