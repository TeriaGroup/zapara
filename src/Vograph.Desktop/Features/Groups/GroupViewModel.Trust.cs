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
    public bool CanGrantTrusted => IsHeadman && SelectedTrustedRole?.HasChannelsPower == true
        && SelectedTrustCandidate is { } person && desk?.Grants.All(grant => grant.RoleId != SelectedTrustedRole.RoleId || grant.UserId != person.UserId) == true;
    public bool HasTrustedRole => SelectedTrustedRole is not null;
    public bool SelectedRoleHasOtherPowers => SelectedTrustedRole?.HasOtherPowers == true;

    partial void OnSelectedTrustedRoleChanged(GroupTrustedRoleRow? value)
    {
        RefreshTrustedGrants();
        OnPropertyChanged(nameof(ChannelPowerAction));
        OnPropertyChanged(nameof(HasTrustedRole));
        OnPropertyChanged(nameof(SelectedRoleHasOtherPowers));
        OnPropertyChanged(nameof(CanGrantTrusted));
    }
    partial void OnSelectedTrustCandidateChanged(GroupTrustedPersonRow? value) => OnPropertyChanged(nameof(CanGrantTrusted));
    partial void OnIsHeadmanChanged(bool value) => OnPropertyChanged(nameof(CanGrantTrusted));

    private async Task LoadDeskAsync(string token, Guid community, IReadOnlyList<ClassmateResponse> classmates, int ticket, CancellationToken ct)
    {
        try
        {
            var loaded = await Api!.DeskAsync(token, community, ct);
            if (ct.IsCancellationRequested || navigationGeneration != ticket || communityId != community) return;
            trustClassmates = classmates;
            ApplyDesk(loaded);
        }
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
        catch (CommunityClientException ex) when (ex.Failure is CommunityClientFailure.Forbidden or CommunityClientFailure.NotFound)
        { if (operation.IsCurrent && CurrentChat(id, ticket)) ClearDesk(); }
        catch (CommunityClientException) { }
        catch (AccountClientException ex) when (operation.IsCurrent && CurrentChat(id, ticket)) { FailSession(ex); }
        catch (OperationCanceledException) { }
    }

    private void ApplyDesk(GroupDeskResponse value)
    {
        desk = value;
        IsHeadman = value.Headman;
        var selectedRoleId = SelectedTrustedRole?.RoleId;
        var selectedUserId = SelectedTrustCandidate?.UserId;
        TrustedRoles.Clear();
        foreach (var role in value.Roles)
            TrustedRoles.Add(new(role.RoleId, role.Name,
                value.Powers.Where(power => power.RoleId == role.RoleId).Select(power => power.Power).ToArray()));
        SelectedTrustedRole = TrustedRoles.FirstOrDefault(role => role.RoleId == selectedRoleId) ?? TrustedRoles.FirstOrDefault();
        TrustCandidates.Clear();
        foreach (var person in trustClassmates.Where(person => !person.Self))
            TrustCandidates.Add(new(person.UserId, person.DisplayName ?? person.Username));
        SelectedTrustCandidate = TrustCandidates.FirstOrDefault(person => person.UserId == selectedUserId)
            ?? TrustCandidates.FirstOrDefault();
        RefreshTrustedGrants();
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
        if (!IsHeadman || communityId is not Guid community || Api is null || Access is null) return null;
        using var operation = App.Work.Enter();
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if (string.IsNullOrWhiteSpace(token)) { ShowAccount(); return null; }
            var result = await action(Api, token, community, operation.Token);
            if (!operation.IsCurrent || communityId != community) return null;
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
        if (!IsHeadman || string.IsNullOrWhiteSpace(TrustedRoleName)) return;
        var name = TrustedRoleName.Trim();
        var result = await MutateDeskAsync((api, token, community, ct) =>
            api.CreateRoleAsync(token, community, new GroupRoleNameRequest(name), ct));
        if (result is null) return;
        SelectedTrustedRole = TrustedRoles.FirstOrDefault(role => role.Name == name);
        TrustedRoleName = "";
    }

    [RelayCommand]
    private Task ToggleChannelPower()
    {
        if (!IsHeadman || SelectedTrustedRole is not { } role) return Task.CompletedTask;
        var enabled = !role.HasChannelsPower;
        return MutateDeskAsync((api, token, community, ct) =>
            api.SetRolePowerAsync(token, community, role.RoleId, new GroupPowerRequest("channels", enabled), ct));
    }

    [RelayCommand]
    private Task GrantTrusted()
    {
        if (!CanGrantTrusted || SelectedTrustedRole is not { } role || SelectedTrustCandidate is not { } person) return Task.CompletedTask;
        return MutateDeskAsync((api, token, community, ct) =>
            api.GrantRoleAsync(token, community, role.RoleId, new GroupGrantRequest(person.UserId), ct));
    }

    private Task RevokeTrustedAsync(Guid roleId, Guid userId)
    {
        if (!IsHeadman || desk?.Grants.Any(grant => grant.RoleId == roleId && grant.UserId == userId) != true) return Task.CompletedTask;
        return MutateDeskAsync((api, token, community, ct) => api.RevokeRoleAsync(token, community, roleId, userId, ct));
    }

    private void ClearDesk()
    {
        desk = null;
        trustClassmates = [];
        IsHeadman = false;
        TrustedRoles.Clear();
        TrustedGrants.Clear();
        TrustCandidates.Clear();
        SelectedTrustedRole = null;
        SelectedTrustCandidate = null;
    }
}

public sealed class GroupTrustedRoleRow(Guid roleId, string name, IReadOnlyList<string> powers)
{
    public Guid RoleId { get; } = roleId;
    public string Name { get; } = name;
    public bool HasChannelsPower => powers.Contains("channels");
    public bool HasOtherPowers => powers.Any(power => power != "channels");
    public string Display => Name + (HasChannelsPower ? " · управляет каналами" : "") + (HasOtherPowers ? " · есть другие права" : "");
}

public sealed class GroupTrustedPersonRow(Guid userId, string name)
{
    public Guid UserId { get; } = userId;
    public string Name { get; } = name;
}

public sealed class GroupTrustedGrantRow(Guid roleId, Guid userId, string name, IAsyncRelayCommand revoke)
{
    public Guid RoleId { get; } = roleId;
    public Guid UserId { get; } = userId;
    public string Name { get; } = name;
    public IAsyncRelayCommand RevokeCommand { get; } = revoke;
}
