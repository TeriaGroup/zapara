using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel
{
    [ObservableProperty] private string rolePanel = "settings";
    [ObservableProperty] private string roleMemberSearch = "";
    [ObservableProperty] private bool roleAssignedOnly;
    private Guid? rolePanelId;
    public ObservableCollection<GroupRoleMemberRow> RoleMembers { get; } = [];
    public bool ShowRoleManager => CanManageRoles || CanManageGrants;
    public bool ShowRoleSettings => HasTrustedRole && RolePanel == "settings";
    public bool ShowRolePowers => HasTrustedRole && RolePanel == "powers";
    public bool ShowRoleMembers => HasTrustedRole && RolePanel == "members";
    public string SelectedRoleHeading => SelectedTrustedRole?.Name ?? "Выберите роль";
    public string RoleCountSummary => $"Ролей: {desk?.Roles.Count ?? 0} из {RoleLimit}. У участника — до {MemberRoleLimit}.";
    private int RoleLimit => space?.Capabilities.MaxRoles ?? desk?.Capabilities.MaxRoles ?? 12;
    private int MemberRoleLimit => space?.Capabilities.MaxRolesPerMember ?? desk?.Capabilities.MaxRolesPerMember ?? 3;
    public string RoleMemberSummary => $"Показано: {RoleMembers.Count} · назначено: {desk?.Grants.Count(x => x.RoleId == SelectedTrustedRole?.RoleId) ?? 0}";
    public string RoleCreateHint => !CanManageRoles ? "У вас нет права создавать роли."
        : (desk?.Roles.Count ?? 0) >= RoleLimit ? $"Достигнут лимит: {RoleLimit} ролей."
        : !IsHeadman && GroupRoleManagement.Position(desk, me) <= 0 ? "Создавать роли можно ниже своего уровня."
        : "Роль без дополнительных возможностей можно использовать как подгруппу для домашки.";
    public string RoleSettingsHint => RoleEditPosition is < 0 or > 10000 ? "Уровень роли должен быть целым числом от 0 до 10000."
        : SelectedTrustedRole is not { } role ? "Выберите роль."
        : GroupRoleManagement.RoleReason(desk, me, role.RoleId, "roles", newPosition: RoleEditPosition);
    public string TrustedGrantHint => SelectedTrustedRole is not { } role || SelectedTrustCandidate is not { } person ? "Выберите роль и участника."
        : MemberActionReason(role.RoleId, person.UserId, removing: false);

    partial void OnRolePanelChanged(string value) => RefreshRolePanelProperties();
    partial void OnRoleMemberSearchChanged(string value) => RefreshRoleMembers();
    partial void OnRoleAssignedOnlyChanged(bool value) => RefreshRoleMembers();
    partial void OnRoleEditPositionChanged(int value) { OnPropertyChanged(nameof(CanSaveRoleSettings)); OnPropertyChanged(nameof(RoleSettingsHint)); }
    partial void OnRoleEditNameChanged(string value) => OnPropertyChanged(nameof(CanSaveRoleSettings));
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsBusy)) { RefreshRoleManager(); NotifyChannelHomeworkState(); }
    }

    [RelayCommand] private void ShowRoleSettingsPanel() => RolePanel = "settings";
    [RelayCommand] private void ShowRolePowersPanel() => RolePanel = "powers";
    [RelayCommand] private void ShowRoleMembersPanel() => RolePanel = "members";

    private void RefreshRolePanelProperties()
    {
        foreach (var name in new[] { nameof(ShowRoleSettings), nameof(ShowRolePowers), nameof(ShowRoleMembers), nameof(SelectedRoleHeading) }) OnPropertyChanged(name);
    }
    private void RefreshRoleManager()
    {
        if (rolePanelId != SelectedTrustedRole?.RoleId)
        {
            rolePanelId = SelectedTrustedRole?.RoleId;
            RoleMemberSearch = "";
            RoleAssignedOnly = false;
            RolePanel = CanManageRoles ? "settings" : "members";
        }
        foreach (var name in new[] { nameof(ShowRoleManager), nameof(RoleCountSummary), nameof(RoleCreateHint), nameof(RoleSettingsHint),
            nameof(CanCreateRole), nameof(CanEditSelectedRole), nameof(CanSaveRoleSettings), nameof(CanGrantTrusted), nameof(TrustedGrantHint) }) OnPropertyChanged(name);
        RefreshRolePanelProperties();
        RefreshRoleMembers();
        RefreshRolePowerRows(desk?.Roles.FirstOrDefault(x => x.RoleId == SelectedTrustedRole?.RoleId));
    }
    private void RefreshRoleMembers()
    {
        RoleMembers.Clear();
        if (desk is not null && SelectedTrustedRole is { } role)
        {
            var query = RoleMemberSearch.Trim();
            foreach (var person in trustClassmates.OrderBy(x => x.DisplayName ?? x.Username))
            {
                var assigned = desk.Grants.Any(x => x.RoleId == role.RoleId && x.UserId == person.UserId);
                if (RoleAssignedOnly && !assigned) continue;
                var name = person.DisplayName ?? person.Username;
                if (query.Length > 0 && !name.Contains(query, StringComparison.CurrentCultureIgnoreCase) && !person.Username.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
                var count = desk.Grants.Count(x => x.UserId == person.UserId);
                var official = person.Role switch { "headman" => "Староста", "curator" => "Куратор", _ => "Участник" };
                var reason = PreviewMode ? "Просмотр от лица участника: изменения отключены." : IsBusy ? "Дождитесь завершения действия." : MemberActionReason(role.RoleId, person.UserId, assigned);
                RoleMembers.Add(new(person.UserId, name, $"{official}{(person.Self ? " · вы" : "")} · ролей: {count}/{MemberRoleLimit}", assigned, reason,
                    new AsyncRelayCommand(() => ChangeRoleMemberAsync(role.RoleId, person.UserId, assigned))));
            }
        }
        OnPropertyChanged(nameof(RoleMemberSummary));
    }
    private string MemberActionReason(Guid roleId, Guid userId, bool removing)
        => GroupRoleManagement.MemberReason(desk, me, roleId, trustClassmates.FirstOrDefault(x => x.UserId == userId), removing, MemberRoleLimit);

    private Task ChangeRoleMemberAsync(Guid roleId, Guid userId, bool removing)
    {
        if (PreviewMode || IsBusy || MemberActionReason(roleId, userId, removing).Length > 0) return Task.CompletedTask;
        return MutateDeskAsync((api, token, community, ct) =>
        {
            if (PreviewMode || MemberActionReason(roleId, userId, removing).Length > 0) return Task.FromResult(desk!);
            return removing ? api.RevokeRoleAsync(token, community, roleId, userId, ct)
                : api.GrantRoleAsync(token, community, roleId, new GroupGrantRequest(userId), ct);
        });
    }
}

public sealed record GroupRoleMemberRow(Guid UserId, string Name, string Summary, bool Assigned, string DisabledReason, IAsyncRelayCommand ActionCommand)
{
    public bool CanAct => DisabledReason.Length == 0;
    public string ActionLabel => Assigned ? "Снять роль" : "Назначить роль";
    public string AssignmentLabel => Assigned ? "Роль назначена" : "Без этой роли";
}

internal static class GroupRoleManagement
{
    public static int Position(GroupDeskResponse? desk, Guid user)
    {
        var roles = desk?.Grants.Where(x => x.UserId == user).Select(x => x.RoleId).ToHashSet() ?? [];
        return desk?.Roles.Where(x => roles.Contains(x.RoleId)).Select(x => x.Position).DefaultIfEmpty(-1).Max() ?? -1;
    }

    public static string RoleReason(GroupDeskResponse? desk, Guid actor, Guid roleId, string permission, int? newPosition = null, string? addedPower = null)
    {
        if (newPosition is < 0 or > 10000) return "Уровень роли должен быть от 0 до 10000.";
        if (desk is null) return "Права группы ещё не загружены.";
        var role = desk.Roles.FirstOrDefault(x => x.RoleId == roleId);
        if (role is null) return "Роль больше не доступна.";
        if (desk.Headman) return "";
        if (!desk.Mine.Contains(permission)) return permission == "roles" ? "Нет права управлять ролями." : "Нет права назначать роли.";
        if (desk.Grants.Any(x => x.UserId == actor && x.RoleId == roleId)) return "Нельзя изменять или назначать собственную роль.";
        if (Position(desk, actor) <= Math.Max(role.Position, newPosition ?? role.Position)) return "Доступны только роли ниже вашего уровня.";
        if (desk.Powers.Where(x => x.RoleId == roleId).Any(x => !desk.Mine.Contains(x.Power)) || addedPower is not null && !desk.Mine.Contains(addedPower))
            return "Нельзя передать возможности, которых нет у вас.";
        return "";
    }

    public static string MemberReason(GroupDeskResponse? desk, Guid actor, Guid roleId, ClassmateResponse? person, bool removing, int limit = 3)
    {
        var roleReason = RoleReason(desk, actor, roleId, "grants");
        if (roleReason.Length > 0) return roleReason;
        if (person is null) return "Участник больше не в группе.";
        if (!desk!.Headman)
        {
            if (person.UserId == actor || person.Self) return "Нельзя менять свои назначения.";
            if (person.Role is "headman" or "curator") return "Официальный статус защищает назначения этого участника.";
            if (Position(desk, person.UserId) >= Position(desk, actor)) return "Участник находится на вашем уровне или выше.";
        }
        var assigned = desk!.Grants.Any(x => x.RoleId == roleId && x.UserId == person.UserId);
        if (removing) return assigned ? "" : "Роль уже снята.";
        if (assigned) return "Эта роль уже назначена.";
        return desk.Grants.Count(x => x.UserId == person.UserId) >= limit ? $"Достигнут лимит: {limit} роли у участника." : "";
    }
}
