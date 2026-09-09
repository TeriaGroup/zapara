using CommunityToolkit.Mvvm.Input;
using Zapara.Contracts.Accounts;

namespace Vograph.Desktop.Features.Account;

public sealed partial class AccountPanelViewModel
{
    private async Task ProfileAction<T>(Func<Task<T>> action, Action<T> publish)
    {
        if (!CanAct || IsGuest) return;
        var expected = profiles!.Snapshot.Identity;
        await RunAsync(async () =>
        {
            var current = profiles.Current;
            using var work = current.Services.Work.Enter();
            work.ThrowIfStale();
            var result = await action();
            work.ThrowIfStale();
            if (profiles.Snapshot.Identity != expected) throw new OperationCanceledException();
            publish(result);
        });
    }

    [RelayCommand]
    private Task RefreshProfile() => ProfileAction(() => service!.MeAsync(lifetime.Token), me =>
    {
        Present(me.User);
        Status = T("accountProfileLoaded");
    });

    [RelayCommand]
    private Task SaveProfile() => ProfileAction(() => service!.SaveAsync(string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName, lifetime.Token), user =>
    {
        Present(user);
        Status = T("accountProfileSaved");
    });

    [RelayCommand]
    private Task LoadDevices() => ProfileAction(() => service!.DevicesAsync(null, lifetime.Token), page =>
    {
        Devices.Clear();
        foreach (var device in page.Devices) Devices.Add(device);
        cursor = page.NextCursor; HasMore = cursor is not null;
        Status = T("accountDevicesLoaded");
    });

    [RelayCommand]
    private Task MoreDevices() => !HasMore ? Task.CompletedTask : ProfileAction(() => service!.DevicesAsync(cursor, lifetime.Token), page =>
    {
        foreach (var device in page.Devices)
            if (!Devices.Any(d => d.FamilyId == device.FamilyId)) Devices.Add(device);
        cursor = page.NextCursor; HasMore = cursor is not null;
    });

    [RelayCommand]
    private async Task RevokeDevice(DeviceResponse? device)
    {
        if (!CanAct || device is null || !Devices.Contains(device)) return;
        await RunAsync(async () =>
        {
            var result = await service!.RevokeAsync(device.FamilyId, lifetime.Token);
            if (result is not null) Apply(result.Snapshot);
            else { Devices.Remove(device); Status = T("accountDeviceRevoked"); }
        });
    }

    [RelayCommand]
    private async Task RevokeAll()
    {
        if (!CanAct || IsGuest) return;
        await RunAsync(async () =>
        {
            var result = await service!.RevokeAllAsync(lifetime.Token);
            if (result is not null) Apply(result.Snapshot);
        });
    }

    [RelayCommand]
    private async Task ChangePassword()
    {
        var current = CurrentPassword; var next = NewPassword;
        ClearSecrets();
        if (!CanAct || IsGuest) return;
        await RunAsync(async () =>
        {
            var result = await service!.PasswordAsync(new ChangePasswordRequest(current, next), lifetime.Token);
            if (result is not null) Apply(result.Snapshot);
        });
    }
}
