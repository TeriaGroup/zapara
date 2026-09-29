using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Social;
using Vograph.Desktop.Features.Chat;

namespace Vograph.Desktop.Features.Account;

public sealed partial class AccountPanelViewModel
{
    [ObservableProperty] private Bitmap? avatar;
    private Bitmap? displayedAvatar;
    private AvatarImages? observedAvatars;
    public bool HasAvatar => Avatar is not null;
    public bool NoAvatar => Avatar is null;
    public string AvatarInitials => Vograph.Desktop.Features.Chat.AvatarInitials.FromName(AccountName);
    partial void OnAvatarChanged(Bitmap? value)
    {
        var old = displayedAvatar;
        displayedAvatar = value;
        OnPropertyChanged(nameof(HasAvatar)); OnPropertyChanged(nameof(NoAvatar));
        if (!ReferenceEquals(old, value)) AvatarImages.Retire(old);
    }

    private async Task LoadProfileAvatarAsync(Guid userId)
    {
        if (profiles is null || profiles.Snapshot.ReauthRequired) return;
        var current = profiles.Current;
        var expected = profiles.Snapshot.Identity;
        var store = current.Services.Avatars;
        var access = current.Services.SocialAccess ?? current.Services.CommunityAccess;
        if (store is null || access is null) return;
        if (!ReferenceEquals(observedAvatars, store))
        {
            StopObservingAvatars();
            observedAvatars = store;
            store.AuthorizationFailed += OnAvatarAuthorizationFailed;
        }
        using var work = current.Services.Work.Enter();
        try
        {
            var token = await access(work.Token);
            if (string.IsNullOrWhiteSpace(token)) { Avatar = null; store.Clear(); return; }
            if (!work.IsCurrent || profiles.Snapshot.Identity != expected) return;
            var image = await store.UserAsync(token, userId, work.Token);
            if (work.IsCurrent && profiles.Snapshot.Identity == expected && !profiles.Snapshot.ReauthRequired
                && ReferenceEquals(profiles.Current, current)) Avatar = image;
            else image?.Dispose();
        }
        catch (AvatarClientException ex) when (ex.Status is 401 or 403) { Avatar = null; store.Clear(); }
        catch (Exception ex) when (ex is AvatarClientException or AccountClientException or InvalidDataException or OperationCanceledException) { }
    }

    private void OnAvatarAuthorizationFailed()
    {
        var observed = observedAvatars;
        var expected = snapshot?.Identity;
        Dispatcher.UIThread.Post(() =>
        {
            if (!disposed && ReferenceEquals(observedAvatars, observed) && snapshot?.Identity == expected) Avatar = null;
        });
    }
    private void StopObservingAvatars()
    {
        if (observedAvatars is not null) observedAvatars.AuthorizationFailed -= OnAvatarAuthorizationFailed;
        observedAvatars = null;
    }

    [RelayCommand]
    private async Task ChangeAvatar()
    {
        if (!CanAct || IsGuest || profiles?.Snapshot.Identity is not { } identity) return;
        var current = profiles.Current;
        var path = await current.Services.FileDialogs.OpenChatMediaAsync("image");
        if (path is null || profiles.Snapshot.Identity != identity || !ReferenceEquals(profiles.Current, current)) return;
        var store = current.Services.Avatars;
        var access = current.Services.SocialAccess ?? current.Services.CommunityAccess;
        if (store is null || access is null) return;
        using var work = current.Services.Work.Enter();
        if (!work.IsCurrent) return;
        try
        {
            var info = new FileInfo(path);
            if (info.Length is < 1 or > AvatarUpload.MaxSourceBytes)
            { Status = "Исходное фото должно быть не больше 20 МБ."; return; }
            var bytes = await File.ReadAllBytesAsync(path, work.Token);
            try
            {
                var prepared = AvatarUpload.Prepare(bytes);
                try
                {
                    var token = await access(work.Token);
                    if (string.IsNullOrWhiteSpace(token) || !work.IsCurrent || profiles.Snapshot.Identity != identity
                        || !ReferenceEquals(profiles.Current, current)) return;
                    await store.PutMeAsync(token, identity.UserId, "profile-avatar.jpg", prepared, work.Token);
                    if (work.IsCurrent && profiles.Snapshot.Identity == identity && ReferenceEquals(profiles.Current, current))
                    {
                        var image = await store.UserAsync(token, identity.UserId, work.Token);
                        if (work.IsCurrent && profiles.Snapshot.Identity == identity && ReferenceEquals(profiles.Current, current))
                        { Avatar = image; Status = "Фото профиля обновлено."; }
                        else image?.Dispose();
                    }
                }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(prepared); }
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex) when (ex is AvatarClientException or AccountClientException or IOException or UnauthorizedAccessException or OperationCanceledException)
        { if (work.IsCurrent) Status = "Не удалось обновить фото профиля."; }
    }

    [RelayCommand]
    private async Task RemoveAvatar()
    {
        if (!CanAct || IsGuest || profiles?.Snapshot.Identity is not { } identity) return;
        var current = profiles.Current;
        var store = current.Services.Avatars;
        var access = current.Services.SocialAccess ?? current.Services.CommunityAccess;
        if (store is null || access is null) return;
        using var work = current.Services.Work.Enter();
        if (!work.IsCurrent) return;
        try
        {
            var token = await access(work.Token);
            if (string.IsNullOrWhiteSpace(token) || !work.IsCurrent || profiles.Snapshot.Identity != identity
                || !ReferenceEquals(profiles.Current, current)) return;
            await store.DeleteMeAsync(token, identity.UserId, work.Token);
            if (work.IsCurrent && profiles.Snapshot.Identity == identity && ReferenceEquals(profiles.Current, current))
            { Avatar = null; Status = "Фото профиля удалено."; }
        }
        catch (Exception ex) when (ex is AvatarClientException or AccountClientException or OperationCanceledException)
        { if (work.IsCurrent) Status = "Не удалось удалить фото профиля."; }
    }
}
