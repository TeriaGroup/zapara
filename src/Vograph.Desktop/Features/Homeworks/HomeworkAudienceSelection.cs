using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Homeworks;

public sealed record HomeworkAudienceSnapshot(int Mode, string Roles, string Users);

public sealed partial class HomeworkAudienceSelection : ObservableObject
{
    private readonly HashSet<Guid> selectedRoles = [];
    private readonly HashSet<Guid> selectedUsers = [];
    private IReadOnlyList<GroupGrantResponse> grants = [];
    private Guid? community;
    private bool restoring;
    public event Action? Changed;
    public IReadOnlyList<string> Modes { get; } = ["Вся группа", "Выбрать получателей"];
    public ObservableCollection<HomeworkRecipientChoice> Roles { get; } = [];
    public ObservableCollection<HomeworkRecipientChoice> People { get; } = [];
    [ObservableProperty] private int mode;
    [ObservableProperty] private string query = "";
    [ObservableProperty] private bool supported;
    [ObservableProperty] private bool enabled = true;
    [ObservableProperty] private string groupLabel = "Учебная группа";
    public bool Selecting => Mode == 1;
    public bool CanChoose => Supported && Enabled;
    public bool Valid => !Selecting || Supported && selectedRoles.Count + selectedUsers.Count > 0
        && selectedRoles.All(id => Roles.Any(row => row.Id == id)) && selectedUsers.All(id => People.Any(row => row.Id == id));
    public string Availability => Supported ? "Подгруппы задаются ролями группы. Можно выбрать роли и отдельных участников вместе."
        : "Адресные задания пока недоступны. Можно отправить всей группе.";
    public string Validation => Valid ? "" : !Supported ? "Адресная отправка сейчас недоступна. Выбор получателей сохранён."
        : selectedRoles.Count + selectedUsers.Count == 0 ? "Выберите хотя бы одну подгруппу или участника."
        : "Состав группы изменился. Проверьте выбранных получателей.";
    public string Summary => !Selecting ? "Все участники группы" : $"Подгрупп: {selectedRoles.Count} · участников отдельно: {selectedUsers.Count} · всего сейчас: {RecipientCount}";
    public int RecipientCount => !Selecting ? People.Count : selectedUsers.Concat(grants.Where(g => selectedRoles.Contains(g.RoleId)).Select(g => g.UserId))
        .Distinct().Count(id => People.Any(p => p.Id == id));
    public IReadOnlyList<HomeworkRecipientChoice> VisibleRoles => Roles.Where(Matches).ToArray();
    public IReadOnlyList<HomeworkRecipientChoice> VisiblePeople => People.Where(Matches).ToArray();
    public bool NoMatches => VisibleRoles.Count == 0 && VisiblePeople.Count == 0;
    private bool Matches(HomeworkRecipientChoice row) => Query.Trim().Length == 0 || row.Name.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase);
    public HomeworkAudienceSnapshot Snapshot => new(Mode, string.Join(',', selectedRoles.Order()), string.Join(',', selectedUsers.Order()));
    public HomeworkAudience? Payload
    {
        get
        {
            if (!Valid) throw new InvalidOperationException(Validation);
            return PayloadFor(Snapshot, Supported);
        }
    }
    public static HomeworkAudience? PayloadFor(HomeworkAudienceSnapshot snapshot, bool supported)
    {
        if (!supported && snapshot.Mode == 1) throw new InvalidOperationException("Адресные задания пока недоступны.");
        return supported ? new HomeworkAudience(snapshot.Mode == 1 ? "selected" : "all",
            snapshot.Mode == 1 ? snapshot.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToArray() : [],
            snapshot.Mode == 1 ? snapshot.Users.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToArray() : []) : null;
    }
    public void Clear()
    {
        community = null; Roles.Clear(); People.Clear(); grants = [];
        Supported = false; Restore(new HomeworkAudienceSnapshot(0, "", ""));
    }
    public void Load(Guid id, string label, GroupDeskResponse desk, IReadOnlyList<ClassmateResponse> people)
    {
        if (community is not null && community != id) Restore(new HomeworkAudienceSnapshot(Mode, "", ""));
        community = id;
        GroupLabel = label;
        Supported = desk.Capabilities.HomeworkAudience;
        grants = desk.Grants;
        Roles.Clear(); People.Clear();
        foreach (var role in desk.Roles.OrderByDescending(r => r.Position).ThenBy(r => r.Name))
            Roles.Add(new(role.RoleId, role.Name, $"Участников: {people.Count(p => grants.Any(g => g.RoleId == role.RoleId && g.UserId == p.UserId))}",
                selectedRoles.Contains(role.RoleId), value => Select(selectedRoles, role.RoleId, value)));
        foreach (var person in people.OrderBy(p => p.DisplayName ?? p.Username))
            People.Add(new(person.UserId, person.DisplayName ?? person.Username, person.Self ? "Это вы" : "Участник группы",
                selectedUsers.Contains(person.UserId), value => Select(selectedUsers, person.UserId, value)));
        Notify();
    }
    public void Restore(HomeworkAudience? audience) => Restore(new HomeworkAudienceSnapshot(audience?.Kind == "selected" ? 1 : 0,
        string.Join(',', audience?.RoleIds ?? []), string.Join(',', audience?.UserIds ?? [])));
    public void Restore(HomeworkAudienceSnapshot snapshot)
    {
        restoring = true;
        try
        {
            selectedRoles.Clear(); selectedUsers.Clear();
            foreach (var raw in snapshot.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries)) if (Guid.TryParse(raw, out var id)) selectedRoles.Add(id);
            foreach (var raw in snapshot.Users.Split(',', StringSplitOptions.RemoveEmptyEntries)) if (Guid.TryParse(raw, out var id)) selectedUsers.Add(id);
            Mode = snapshot.Mode;
            foreach (var row in Roles) row.Selected = selectedRoles.Contains(row.Id);
            foreach (var row in People) row.Selected = selectedUsers.Contains(row.Id);
        }
        finally { restoring = false; Notify(); }
    }
    private void Select(HashSet<Guid> selected, Guid id, bool value)
    {
        if (restoring) return;
        if (value) selected.Add(id); else selected.Remove(id);
        Notify();
    }
    [RelayCommand] private void ClearQuery() => Query = "";
    [RelayCommand] private void ClearSelection() => Restore(new HomeworkAudienceSnapshot(Mode, "", ""));
    [RelayCommand] private void SelectWholeGroup() => Mode = 0;
    partial void OnModeChanged(int value) { if (!restoring) Notify(); }
    partial void OnQueryChanged(string value) => Notify();
    partial void OnSupportedChanged(bool value) => Notify();
    partial void OnEnabledChanged(bool value) => OnPropertyChanged(nameof(CanChoose));
    private void Notify()
    {
        foreach (var name in new[] { nameof(Selecting), nameof(CanChoose), nameof(Valid), nameof(Availability), nameof(Validation), nameof(Summary), nameof(RecipientCount), nameof(VisibleRoles), nameof(VisiblePeople), nameof(NoMatches), nameof(Snapshot) }) OnPropertyChanged(name);
        if (!restoring) Changed?.Invoke();
    }
}

public sealed partial class HomeworkRecipientChoice(Guid id, string name, string detail, bool selected, Action<bool> change) : ObservableObject
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public string Detail { get; } = detail;
    [ObservableProperty] private bool _selected = selected;
    partial void OnSelectedChanged(bool value) => change(value);
}
