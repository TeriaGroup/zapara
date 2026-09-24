using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Accounts.ExternalResponses;

namespace Vograph.Desktop.Features.Account;

public sealed partial class AccountPanelViewModel
{
    [RelayCommand]
    private async Task ResetPassword()
    {
        if (!CanAct || !RecoveryAvailable) return;
        var username = Username;
        await RunAsync(async () =>
        {
            await service!.RequestPasswordResetAsync(username, lifetime.Token);
            Status = T("accountResetPassword");
        });
    }

    [RelayCommand]
    private Task Export()
    {
        var secret = Proof;
        ClearSecrets();
        return ProfileAction(async () =>
        {
            var job = HasPassword
                ? await service!.CreateExportAsync(secret, lifetime.Token)
                : await service!.CreateExportWithProofAsync(await ProviderProofAsync("export", null), lifetime.Token);
            if (job.Status is "queued" or "running")
                job = await service.GetExportAsync(job.ExportId, lifetime.Token);
            return job;
        }, job =>
        {
            ExportJob = job;
            Status = T(job.Status == "ready" ? "accountExportDownload" : "accountExport");
        });
    }

    [RelayCommand]
    private async Task DownloadExport()
    {
        if (!CanAct || IsGuest || ExportJob is not { Status: "ready" } job) return;
        await RunAsync(async () =>
        {
            var file = await service!.DownloadExportAsync(job.ExportId, lifetime.Token);
            ExportPayload = file.Payload;
            ExportFileName = file.FileName;
            var path = await profiles!.Current.Services.FileDialogs.SaveJsonAsync(file.FileName);
            if (path is not null)
            {
                await File.WriteAllBytesAsync(path, file.Payload, lifetime.Token);
                ExportPath = path;
            }
            Status = T("accountExportDownload");
        });
    }

    [RelayCommand] private void RequestDelete() { ClearSecrets(); ConfirmDelete = true; }
    [RelayCommand] private void CancelDelete() => ConfirmDelete = false;

    [RelayCommand]
    private async Task DeleteAccount()
    {
        var secret = Proof;
        ClearSecrets();
        if (!CanAct || IsGuest || !ConfirmDelete) return;
        await RunAsync(async () =>
        {
            var result = HasPassword
                ? await service!.DeleteAccountAsync(secret, lifetime.Token)
                : await service!.DeleteAccountWithProofAsync(await ProviderProofAsync("delete_account", null), lifetime.Token);
            if (result is not null) Apply(result.Snapshot);
            Status = T("accountDelete");
        });
    }

    [RelayCommand] private Task StartVk() => StartExternal("vk", false);
    [RelayCommand] private Task StartYandex() => StartExternal("yandex", false);
    [RelayCommand] private Task LinkVk() => StartExternal("vk", true);
    [RelayCommand] private Task LinkYandex() => StartExternal("yandex", true);

    private async Task StartExternal(string provider, bool link)
    {
        var secret = Proof;
        ClearSecrets();
        if (!CanAct) return;
        if (!link && Registration && !DocumentsAccepted) { Status = T("accountAcceptRequired"); return; }
        if (link)
        {
            if (IsGuest || (provider == "vk" ? !ShowVkLink : !ShowYandexLink)) return;
        }
        else if (provider == "vk" ? !ShowVkLogin : !ShowYandexLogin) return;
        await RunAsync(async () =>
        {
            using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            externalCancellation = pending;
            OnPropertyChanged(nameof(ExternalPending));
            try
            {
                var result = link && !HasPassword
                    ? await service!.CompleteExternalWithProofAsync(provider,
                        await ProviderProofCoreAsync("link:" + provider, null, pending.Token),
                        url => LaunchExternalAsync(provider, url), pending.Token)
                    : await service!.CompleteExternalAsync(provider, link ? secret : null,
                        url => LaunchExternalAsync(provider, url), pending.Token);
                Apply(result.Snapshot);
                if (result.Committed && IsAccount)
                {
                    var user = await service.CachedUserAsync(lifetime.Token);
                    if (user is not null) Present(user);
                    await RefreshAuthenticationAsync();
                    if (link)
                    {
                        var identities = await service.ListIdentitiesAsync(lifetime.Token);
                        Identities.Clear();
                        foreach (var identity in identities) Identities.Add(identity);
                    }
                }
            }
            finally
            {
                externalCancellation = null;
                AuthorizeUrl = null;
                OnPropertyChanged(nameof(ExternalPending));
            }
        });
    }

    private CancellationTokenSource? externalCancellation;
    public bool ExternalPending => externalCancellation is not null;
    [RelayCommand] private void CancelExternal() => externalCancellation?.Cancel();

    private async Task LaunchExternalAsync(string provider, string url)
    {
        AuthorizeUrl = url;
        Status = T(provider == "vk" ? "accountVk" : "accountYandex");
        await profiles!.Current.Services.Launcher.OpenUrlAsync(url);
    }

    private async Task<string> ProviderProofAsync(string purpose, string? exclude)
    {
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        externalCancellation = pending;
        OnPropertyChanged(nameof(ExternalPending));
        try { return await ProviderProofCoreAsync(purpose, exclude, pending.Token); }
        finally
        {
            externalCancellation = null;
            AuthorizeUrl = null;
            OnPropertyChanged(nameof(ExternalPending));
        }
    }

    private async Task<string> ProviderProofCoreAsync(string purpose, string? exclude, CancellationToken ct)
    {
        var identities = Identities.Count > 0 ? Identities.ToArray() : (await service!.ListIdentitiesAsync(ct)).ToArray();
        var provider = identities.Select(item => item.Provider).FirstOrDefault(item =>
            item != exclude && (item == "yandex" && YandexAvailable || item == "vk" && VkAvailable));
        if (provider is null) throw new AccountClientException(AccountClientFailure.ProviderUnavailable);
        var proof = await service!.ExternalProofAsync(provider, purpose, url => LaunchExternalAsync(provider, url), ct);
        return proof.ProofToken;
    }

    [RelayCommand]
    private Task LoadIdentities() => ProfileAction(() => service!.ListIdentitiesAsync(lifetime.Token), items =>
    {
        Identities.Clear();
        foreach (var item in items) Identities.Add(item);
        Status = T("accountIdentities");
    });

    [RelayCommand]
    private async Task UnlinkIdentity(ExternalIdentityResponse? identity)
    {
        var secret = Proof;
        ClearSecrets();
        if (!CanAct || !CanUnlinkIdentity || identity is null || !Identities.Contains(identity)) return;
        await RunAsync(async () =>
        {
            if (HasPassword) await service!.UnlinkIdentityAsync(identity.Provider, secret, lifetime.Token);
            else
            {
                if (Identities.Count <= 1) throw new AccountClientException(AccountClientFailure.InvalidPayload);
                await service!.UnlinkIdentityWithProofAsync(identity.Provider,
                    await ProviderProofAsync("unlink:" + identity.Provider, identity.Provider), lifetime.Token);
            }
            Identities.Remove(identity);
            Status = T("accountUnlink");
        });
    }
}
