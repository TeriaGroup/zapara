using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Communities;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel
{
    private GroupDeskResponse? desk;
    private IReadOnlyList<ClassmateResponse> trustClassmates = [];

    public ObservableCollection<GroupTrustedRoleRow> TrustedRoles { get; } = [];
    public ObservableCollection<GroupTrustedPersonRow> TrustCandidates { get; } = [];
    public ObservableCollection<GroupTrustedGrantRow> TrustedGrants { get; } = [];
    [ObservableProperty] private bool isHeadman;
    [ObservableProperty] private string trustedRoleName = "Доверенный по каналам";
    [ObservableProperty] private GroupTrustedRoleRow? selectedTrustedRole;
    [ObservableProperty] private GroupTrustedPersonRow? selectedTrustCandidate;

    public string ChannelPowerAction => SelectedTrustedRole?.HasChannelsPower == true ? "Отозвать право управления" : "Разрешить управлять каналами";
    public bool CanGrantTrusted => !IsBusy && CanManageGrants && SelectedTrustedRole is { } role
        && SelectedTrustCandidate is { } person && MemberActionReason(role.RoleId, person.UserId, removing: false).Length == 0;
    public bool HasTrustedRole => SelectedTrustedRole is not null;
    public bool SelectedRoleHasOtherPowers => SelectedTrustedRole?.HasOtherPowers == true;

    partial void OnSelectedTrustedRoleChanged(GroupTrustedRoleRow? value)
    {
        StashRoleEditorDraft();
        RefreshTrustedGrants();
        LoadRoleEditor();
        NotifySpace();
        OnPropertyChanged(nameof(ChannelPowerAction));
        OnPropertyChanged(nameof(HasTrustedRole));
        OnPropertyChanged(nameof(SelectedRoleHasOtherPowers));
        OnPropertyChanged(nameof(CanGrantTrusted));
    }
    partial void OnSelectedTrustCandidateChanged(GroupTrustedPersonRow? value)
    { OnPropertyChanged(nameof(CanGrantTrusted)); OnPropertyChanged(nameof(TrustedGrantHint)); }
    partial void OnIsHeadmanChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditGroupAvatar));
        OnPropertyChanged(nameof(CanGrantTrusted));
        OnPropertyChanged(nameof(ShowTrustedManagement));
    }

    private async Task LoadDeskAsync(string token, Guid community, IReadOnlyList<ClassmateResponse> classmates, int ticket, CancellationToken ct)
    {
        try
        {
            var loaded = await Api!.DeskAsync(token, community, ct);
            if (ct.IsCancellationRequested || navigationGeneration != ticket || communityId != community) return;
            trustClassmates = classmates;
            ApplyDesk(loaded);
        }
        catch(CommunityClientException ex)when(ReadDenied(ex) && (ex.Failure!=CommunityClientFailure.NotFound || !legacySpace)){if(!ct.IsCancellationRequested && navigationGeneration==ticket && communityId==community)ClearRevokedContent();}
        catch (CommunityClientException)
        {
            if (!ct.IsCancellationRequested && navigationGeneration == ticket && communityId == community) ClearDesk();
        }
    }

    private async Task RefreshDeskAsync(Guid id, int ticket)
    {
        if (communityId is not Guid community || Api is null || Access is null) return;
        using var operation = App.Work.Enter();
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrWhiteSpace(token) || !operation.IsCurrent || !CurrentChat(id, ticket)) return;
            var latest = await Api.DeskAsync(token, community, operation.Token);
            if (operation.IsCurrent && CurrentChat(id, ticket) && communityId == community) ApplyDesk(latest);
        }
        catch (CommunityClientException ex) when (ReadDenied(ex) && (ex.Failure!=CommunityClientFailure.NotFound || !legacySpace))
        { if (operation.IsCurrent && CurrentChat(id, ticket)) ClearRevokedContent(); }
        catch (CommunityClientException) { }
        catch (AccountClientException ex) when (operation.IsCurrent && CurrentChat(id, ticket)) { FailSession(ex); }
        catch (OperationCanceledException) { }
    }

    private void ApplyDesk(GroupDeskResponse value)
    {
        if(!CurrentSpace())return;
        if(desk is not null && System.Text.Json.JsonSerializer.Serialize(desk)==System.Text.Json.JsonSerializer.Serialize(value) && TrustCandidates.Select(x=>(x.UserId,x.Name,x.OfficialRole,x.Self)).SequenceEqual(trustClassmates.Select(x=>(x.UserId,x.DisplayName??x.Username,x.Role,x.Self))))return;
        InvalidateAccessPreview();
        ConfirmRemoveRole = false; RoleImpact = "";
        desk = value;
        OnPropertyChanged(nameof(CanEditGroupAvatar));
        IsHeadman = value.Headman;
        var selectedRoleId = SelectedTrustedRole?.RoleId;
        var selectedUserId = SelectedTrustCandidate?.UserId;
        TrustedRoles.Clear();
        foreach (var role in value.Roles.OrderByDescending(x => x.Position))
            TrustedRoles.Add(new(role.RoleId, role.Name,
                value.Powers.Where(power => power.RoleId == role.RoleId).Select(power => power.Power).ToArray(), role.Position,
                value.Grants.Count(grant => grant.RoleId == role.RoleId)));
        SelectedTrustedRole = TrustedRoles.FirstOrDefault(role => role.RoleId == selectedRoleId) ?? TrustedRoles.FirstOrDefault();
        TrustCandidates.Clear();
        foreach (var person in trustClassmates)
            TrustCandidates.Add(new(person.UserId, person.DisplayName ?? person.Username, person.Role, person.Self));
        SelectedTrustCandidate = TrustCandidates.FirstOrDefault(person => person.UserId == selectedUserId)
            ?? TrustCandidates.FirstOrDefault();
        RefreshTrustedGrants();
        LoadRoleEditor();
        ReconcileCreationRoles();
        NotifySpace();
        RefreshHomeworkRecipients();
    }

    private void RefreshTrustedGrants()
    {
        TrustedGrants.Clear();
        if (desk is null || SelectedTrustedRole is not { } role) return;
        foreach (var grant in desk.Grants.Where(grant => grant.RoleId == role.RoleId))
        {
            var person = trustClassmates.FirstOrDefault(person => person.UserId == grant.UserId);
            var name = person?.DisplayName ?? person?.Username ?? "Участник";
            TrustedGrants.Add(new(grant.RoleId, grant.UserId, name,
                new AsyncRelayCommand(() => RevokeTrustedAsync(grant.RoleId, grant.UserId))));
        }
        OnPropertyChanged(nameof(CanGrantTrusted));
    }

    private async Task<GroupDeskResponse?> MutateDeskAsync(Func<CommunityHttpClient, string, Guid, CancellationToken, Task<GroupDeskResponse>> action)
    {
        if (PreviewMode || IsBusy || !(CanManageRoles || CanManageGrants) || communityId is not Guid community || Api is null || Access is null) return null;
        var ticket = navigationGeneration;
        using var operation = App.Work.Enter();
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (!operation.IsCurrent || communityId != community || navigationGeneration != ticket || PreviewMode) return null;
            if (string.IsNullOrWhiteSpace(token)) { ShowAccount(); return null; }
            var result = await action(Api, token, community, operation.Token);
            if (!operation.IsCurrent || communityId != community || navigationGeneration != ticket) return null;
            ApplyDesk(result);
            Status = "";
            return result;
        }
        catch (CommunityClientException) { if (operation.IsCurrent) Status = "Не удалось сохранить права доступа."; }
        catch (AccountClientException ex) when (operation.IsCurrent) { FailSession(ex); }
        catch (OperationCanceledException) { }
        finally { if (operation.IsCurrent) Busy(false); }
        return null;
    }

    [RelayCommand]
    private async Task CreateTrustedRole()
    {
        if (!CanCreateRole || string.IsNullOrWhiteSpace(TrustedRoleName)) return;
        var name = TrustedRoleName.Trim();
        var result = await MutateDeskAsync((api, token, community, ct) =>
            !CanManageRoles || (desk?.Roles.Count ?? 0) >= RoleLimit || !IsHeadman && GroupRoleManagement.Position(desk, me) <= 0
                ? Task.FromResult(desk!) : api.CreateRoleAsync(token, community, new GroupRoleNameRequest(name), ct));
        if (result is null) return;
        SelectedTrustedRole = TrustedRoles.FirstOrDefault(role => role.Name == name);
        TrustedRoleName = "";
    }

    [RelayCommand]
    private Task ToggleChannelPower()
    {
        if (PreviewMode || IsBusy || !IsHeadman || SelectedTrustedRole is not { } role) return Task.CompletedTask;
        var enabled = !role.HasChannelsPower;
        return MutateDeskAsync((api, token, community, ct) =>
            !IsHeadman || PreviewMode ? Task.FromResult(desk!) : api.SetRolePowerAsync(token, community, role.RoleId, new GroupPowerRequest("channels", enabled), ct));
    }

    [RelayCommand]
    private Task GrantTrusted()
    {
        if (!CanGrantTrusted || SelectedTrustedRole is not { } role || SelectedTrustCandidate is not { } person) return Task.CompletedTask;
        return ChangeRoleMemberAsync(role.RoleId, person.UserId, removing: false);
    }

    private Task RevokeTrustedAsync(Guid roleId, Guid userId)
    {
        return ChangeRoleMemberAsync(roleId, userId, removing: true);
    }

    private void ClearDesk()
    {
        desk = null;
        ClearHomeworkRecipients();
        trustClassmates = [];
        IsHeadman = false;
        TrustedRoles.Clear();
        TrustedGrants.Clear();
        TrustCandidates.Clear();
        SelectedTrustedRole = null;
        SelectedTrustCandidate = null;
        RoleMembers.Clear(); RoleMemberSearch = ""; RoleAssignedOnly = false; rolePanelId = null;
        RefreshRoleManager();
    }
}

public sealed class GroupTrustedRoleRow(Guid roleId, string name, IReadOnlyList<string> powers, int position = 0, int assignments = 0)
{
    public Guid RoleId { get; } = roleId;
    public string Name { get; } = name;
    public int Position { get; } = position;
    public int Assignments { get; } = assignments;
    public string Summary => $"Возможностей: {powers.Count} · участников: {Assignments} · уровень: {Position}";
    public bool HasChannelsPower => powers.Contains("channels");
    public bool HasOtherPowers => powers.Any(power => power != "channels");
    public string Display => Name + (HasChannelsPower ? " · управляет каналами" : "") + (HasOtherPowers ? " · есть другие права" : "");
}

public sealed class GroupTrustedPersonRow(Guid userId, string name, string officialRole = "member", bool self = false)
{
    public Guid UserId { get; } = userId;
    public string Name { get; } = name;
    public string OfficialRole { get; } = officialRole;
    public bool Self { get; } = self;
}

public sealed class GroupTrustedGrantRow(Guid roleId, Guid userId, string name, IAsyncRelayCommand revoke)
{
    public Guid RoleId { get; } = roleId;
    public Guid UserId { get; } = userId;
    public string Name { get; } = name;
    public IAsyncRelayCommand RevokeCommand { get; } = revoke;
}
