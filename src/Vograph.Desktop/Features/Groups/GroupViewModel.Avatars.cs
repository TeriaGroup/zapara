using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Social;
using Vograph.Desktop.Features.Chat;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel
{
    private readonly HashSet<Guid> avatarLoadingUsers = [];
    private readonly Dictionary<Guid, Bitmap?> authorAvatars = [];
    [ObservableProperty] private Bitmap? groupAvatar;
    public string GroupInitials => AvatarInitials.FromName(HomeTitle);
    public bool HasGroupAvatar => GroupAvatar is not null;
    public bool NoGroupAvatar => GroupAvatar is null;
    public bool CanEditGroupAvatar => HasHome && communityId is not null && !PreviewMode
        && (IsHeadman || desk?.Mine.Contains("channels") == true);
    partial void OnGroupAvatarChanged(Bitmap? value)
    { OnPropertyChanged(nameof(HasGroupAvatar)); OnPropertyChanged(nameof(NoGroupAvatar)); }

    private async Task LoadListAvatarsAsync(IReadOnlyList<Guid> ids, int ticket)
    {
        if (App.Avatars is null || Access is null) return;
        using var work = App.Work.Enter();
        try
        {
            var token = await Access(work.Token);
            if (string.IsNullOrEmpty(token) || !work.IsCurrent || ticket != navigationGeneration) return;
            foreach (var id in ids)
            {
                var image = await App.Avatars.GroupAsync(token, id, work.Token);
                if (!work.IsCurrent || ticket != navigationGeneration) { image?.Dispose(); return; }
                var found = false;
                foreach (var row in Communities.Where(row => row.CommunityId == id))
                {
                    found = true;
                    var old = row.Avatar;
                    row.Avatar = image;
                    if (!ReferenceEquals(old, image)) AvatarImages.Retire(old);
                }
                if (!found) image?.Dispose();
            }
        }
        catch (AvatarClientException ex) when (ex.Status is 401 or 403) { ClearVisibleAvatars(); }
        catch (Exception ex) when (ex is AvatarClientException or AccountClientException or InvalidDataException or OperationCanceledException) { }
    }

    private async Task LoadCurrentAvatarsAsync(Guid id, IReadOnlyList<Guid> members, int ticket)
    {
        if (App.Avatars is null || Access is null) return;
        using var work = App.Work.Enter();
        try
        {
            var token = await Access(work.Token);
            if (string.IsNullOrEmpty(token) || !work.IsCurrent || ticket != navigationGeneration) return;
            var group = await App.Avatars.GroupAsync(token, id, work.Token);
            if (!work.IsCurrent || ticket != navigationGeneration || communityId != id) { group?.Dispose(); return; }
            AvatarImages.Retire(GroupAvatar);
            GroupAvatar = group;
            foreach (var person in members)
            {
                var photo = await App.Avatars.UserAsync(token, person, work.Token);
                if (!work.IsCurrent || ticket != navigationGeneration || communityId != id) { photo?.Dispose(); return; }
                var old = authorAvatars.GetValueOrDefault(person);
                authorAvatars[person] = photo;
                foreach (var row in People.Where(row => row.UserId == person)) row.Avatar = photo;
                foreach (var row in Directs.Where(row => row.UserId == person)) row.Avatar = photo;
                foreach (var row in Messages.Where(row => row.SenderId == person)) row.Avatar = photo;
                if (!ReferenceEquals(old, photo)) AvatarImages.Retire(old);
            }
        }
        catch (AvatarClientException ex) when (ex.Status is 401 or 403) { ClearVisibleAvatars(); }
        catch (Exception ex) when (ex is AvatarClientException or AccountClientException or InvalidDataException or OperationCanceledException) { }
    }

    private async Task LoadMessageAvatarAsync(Guid id, int ticket)
    {
        if (authorAvatars.TryGetValue(id, out var known))
        { foreach (var row in Messages.Where(row => row.SenderId == id)) row.Avatar = known; return; }
        if (!avatarLoadingUsers.Add(id) || App.Avatars is null || Access is null) return;
        using var work = App.Work.Enter();
        try
        {
            var token = await Access(work.Token);
            if (string.IsNullOrEmpty(token) || !work.IsCurrent || ticket != navigationGeneration) return;
            var image = await App.Avatars.UserAsync(token, id, work.Token);
            if (!work.IsCurrent || ticket != navigationGeneration) { image?.Dispose(); return; }
            var old = authorAvatars.GetValueOrDefault(id);
            authorAvatars[id] = image;
            foreach (var row in Messages.Where(row => row.SenderId == id)) row.Avatar = image;
            if (!ReferenceEquals(old, image)) AvatarImages.Retire(old);
        }
        catch (AvatarClientException ex) when (ex.Status is 401 or 403) { ClearVisibleAvatars(); }
        catch (Exception ex) when (ex is AvatarClientException or AccountClientException or InvalidDataException or OperationCanceledException) { }
        finally { avatarLoadingUsers.Remove(id); }
    }

    private void ClearVisibleAvatars()
    {
        App.Avatars?.Clear();
        ReleaseVisibleAvatars();
    }

    private void ReleaseVisibleAvatars()
    {
        var images = new[] { GroupAvatar }
            .Concat(authorAvatars.Values)
            .Concat(Communities.Select(row => row.Avatar))
            .Concat(People.Select(row => row.Avatar))
            .Concat(Directs.Select(row => row.Avatar))
            .Concat(Messages.Select(row => row.Avatar))
            .OfType<Bitmap>().Distinct<Bitmap>(ReferenceEqualityComparer.Instance).ToArray();
        GroupAvatar = null;
        authorAvatars.Clear();
        foreach (var row in Communities) row.Avatar = null;
        foreach (var row in People) row.Avatar = null;
        foreach (var row in Directs) row.Avatar = null;
        foreach (var row in Messages) row.Avatar = null;
        foreach (var image in images) AvatarImages.Retire(image);
    }

    [RelayCommand]
    private async Task ChangeGroupAvatar()
    {
        if (!CanEditGroupAvatar || communityId is not Guid id || Access is null || App.Avatars is null) return;
        var path = await App.FileDialogs.OpenChatMediaAsync("image");
        if (path is null) return;
        using var work = App.Work.Enter();
        if (!work.IsCurrent || !CanEditGroupAvatar || communityId != id) return;
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
                    var token = await Access(work.Token);
                    if (string.IsNullOrWhiteSpace(token) || !work.IsCurrent || communityId != id || !CanEditGroupAvatar) return;
                    await App.Avatars.PutGroupAsync(token, id, "group-avatar.jpg", prepared, work.Token);
                    if (work.IsCurrent && communityId == id)
                    {
                        var image = await App.Avatars.GroupAsync(token, id, work.Token);
                        if (!work.IsCurrent || communityId != id) { image?.Dispose(); return; }
                        var old = GroupAvatar;
                        GroupAvatar = image;
                        foreach (var row in Communities.Where(row => row.CommunityId == id)) row.Avatar = GroupAvatar;
                        AvatarImages.Retire(old);
                        Status = "Фото группы обновлено.";
                    }
                }
                finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(prepared); }
            }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex) when (ex is AvatarClientException or AccountClientException or IOException or UnauthorizedAccessException or OperationCanceledException)
        { if (work.IsCurrent) Status = "Не удалось обновить фото группы."; }
    }

    [RelayCommand]
    private async Task RemoveGroupAvatar()
    {
        if (!CanEditGroupAvatar || communityId is not Guid id || Access is null || App.Avatars is null) return;
        using var work = App.Work.Enter();
        try
        {
            var token = await Access(work.Token);
            if (string.IsNullOrWhiteSpace(token) || !work.IsCurrent || communityId != id || !CanEditGroupAvatar) return;
            await App.Avatars.DeleteGroupAsync(token, id, work.Token);
            if (work.IsCurrent && communityId == id)
            {
                var old = GroupAvatar;
                GroupAvatar = null;
                foreach (var row in Communities.Where(row => row.CommunityId == id)) row.Avatar = null;
                AvatarImages.Retire(old);
                Status = "Фото группы удалено.";
            }
        }
        catch (Exception ex) when (ex is AvatarClientException or AccountClientException or OperationCanceledException)
        { if (work.IsCurrent) Status = "Не удалось удалить фото группы."; }
    }
}

public sealed partial class GroupCommunityRow
{
    [ObservableProperty] private Bitmap? avatar;
    public string Initials => AvatarInitials.FromName(Name);
    public bool HasAvatar => Avatar is not null;
    public bool NoAvatar => Avatar is null;
    partial void OnAvatarChanged(Bitmap? value)
    { OnPropertyChanged(nameof(HasAvatar)); OnPropertyChanged(nameof(NoAvatar)); }
}

public sealed partial class GroupPersonRow
{
    [ObservableProperty] private Bitmap? avatar;
    public string Initials => AvatarInitials.FromName(Name);
    public bool HasAvatar => Avatar is not null;
    public bool NoAvatar => Avatar is null;
    partial void OnAvatarChanged(Bitmap? value)
    { OnPropertyChanged(nameof(HasAvatar)); OnPropertyChanged(nameof(NoAvatar)); }
}

public sealed partial class GroupMessageRow
{
    [ObservableProperty] private Bitmap? avatar;
    public string Initials => AvatarInitials.FromName(Author);
    public bool HasAvatar => Avatar is not null;
    public bool NoAvatar => Avatar is null;
    partial void OnAvatarChanged(Bitmap? value)
    { OnPropertyChanged(nameof(HasAvatar)); OnPropertyChanged(nameof(NoAvatar)); }
}
