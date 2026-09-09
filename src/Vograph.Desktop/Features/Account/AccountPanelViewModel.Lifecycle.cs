using CommunityToolkit.Mvvm.Input;
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
            var job = await service!.CreateExportAsync(secret, lifetime.Token);
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
            var result = await service!.DeleteAccountAsync(secret, lifetime.Token);
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
        if (link)
        {
            if (IsGuest || (provider == "vk" ? !ShowVkLink : !ShowYandexLink)) return;
        }
        else if (provider == "vk" ? !ShowVkLogin : !ShowYandexLogin) return;
        await RunAsync(async () =>
        {
            var start = link
                ? await service!.StartExternalLinkAsync(provider, secret, lifetime.Token)
                : await service!.StartExternalLoginAsync(provider, lifetime.Token);
            AuthorizeUrl = start.AuthorizeUrl;
            await profiles!.Current.Services.Launcher.OpenUrlAsync(start.AuthorizeUrl);
            Status = T(provider == "vk" ? "accountVk" : "accountYandex");
        });
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
        if (!CanAct || identity is null || !Identities.Contains(identity)) return;
        await RunAsync(async () =>
        {
            await service!.UnlinkIdentityAsync(identity.Provider, secret, lifetime.Token);
            Identities.Remove(identity);
            Status = T("accountUnlink");
        });
    }
}
