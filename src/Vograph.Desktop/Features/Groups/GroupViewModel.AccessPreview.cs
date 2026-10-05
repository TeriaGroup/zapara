using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel
{
    private int accessEditorGeneration;
    private string? previewedAccessFingerprint;
    [ObservableProperty] private bool accessPreviewReady;
    [ObservableProperty] private string accessPreviewSummary = "";
    [ObservableProperty] private string accessPreviewSearch = "";
    [ObservableProperty] private bool accessPreviewChangedOnly;
    public ObservableCollection<SpaceAccessPreviewPerson> AccessPreviewPeople { get; } = [];
    public IReadOnlyList<SpaceAccessPreviewPerson> VisibleAccessPreviewPeople => AccessPreviewPeople.Where(person =>
        (!AccessPreviewChangedOnly || person.Changed) &&
        (AccessPreviewSearch.Trim().Length == 0 || person.Name.Contains(AccessPreviewSearch.Trim(), StringComparison.OrdinalIgnoreCase)))
        .ToArray();
    public bool NoAccessPreviewMatches => AccessPreviewReady && AccessPreviewPeople.Count > 0 && VisibleAccessPreviewPeople.Count == 0;
    partial void OnAccessPreviewSearchChanged(string value) => NotifyAccessPreviewBrowse();
    partial void OnAccessPreviewChangedOnlyChanged(bool value) => NotifyAccessPreviewBrowse();
    [RelayCommand] private void ResetAccessPreviewBrowse() { AccessPreviewSearch = ""; AccessPreviewChangedOnly = false; }
    private void NotifyAccessPreviewBrowse()
    { OnPropertyChanged(nameof(VisibleAccessPreviewPeople)); OnPropertyChanged(nameof(NoAccessPreviewMatches)); }
    public bool CanSaveProposedAccess => CanManageAccess && AccessPreviewReady &&
        accessBaseline is { } baseline && SelectedChannel?.TopicId == baseline.TopicId &&
        previewedAccessFingerprint == AccessFingerprint();
    partial void OnAccessPreviewReadyChanged(bool value) => OnPropertyChanged(nameof(CanSaveProposedAccess));
    private GroupTopicAccessRequest? ProposedAccess() => accessBaseline is { } baseline
        ? new(AccessRules.Where(row=>!GroupOnlyPowers.Contains(row.Power)).Select(row => new GroupAccessRule(row.RoleId,row.Power,row.State.Code)).ToArray(),baseline.Revision) : null;
    private string? AccessFingerprint() => ProposedAccess() is { } request ? JsonSerializer.Serialize(request) : null;
    private void AccessRuleChanged(object? sender,PropertyChangedEventArgs args)
    { if(args.PropertyName == nameof(SpaceAccessRow.State)){InvalidateAccessPreview();SyncSimpleAccess();} }
    private void InvalidateAccessPreview()
    {
        accessEditorGeneration++;
        AccessPreviewReady=false;previewedAccessFingerprint=null;AccessPreviewSummary="";AccessPreviewPeople.Clear();
        NotifyAccessPreviewBrowse();
        OnPropertyChanged(nameof(CanSaveProposedAccess));
    }
    private void ClearAccessEditor()
    {
        foreach(var row in AccessRules)row.PropertyChanged-=AccessRuleChanged;
        AccessRules.Clear();SimpleAccessRoles.Clear();accessBaseline=null;InvalidateAccessPreview();
    }
    [RelayCommand] private Task PreviewProposedAccess()
    {
        if(!CanManageAccess || accessBaseline is not {} baseline || SelectedChannel?.TopicId != baseline.TopicId || ProposedAccess() is not {} request)
            return Task.CompletedTask;
        var topic=baseline.TopicId;var generation=accessEditorGeneration;var fingerprint=AccessFingerprint();
        return SpaceAction(async(api,token,community,ct)=>
        {
            var preview=await api.TopicAccessPreviewAsync(token,community,topic,request,ct);
            if(!CurrentSpace() || SelectedChannel?.TopicId!=topic || generation!=accessEditorGeneration || fingerprint!=AccessFingerprint())return;
            if(preview.TopicId!=topic || preview.Revision!=baseline.Revision) { InvalidateAccessPreview();Status="Тема изменилась. Загрузите актуальные правила доступа.";return; }
            var opened=preview.AfterReaders.Except(preview.BeforeReaders).Count();var closed=preview.BeforeReaders.Except(preview.AfterReaders).Count();
            AccessPreviewSummary=$"Изменятся права {preview.AffectedCount} участников. Читать: {preview.BeforeReaders.Count} → {preview.AfterReaders.Count}."+
                (opened>0?$" Доступ откроется для {opened} участников.":"")+(closed>0?$" Доступ закроется для {closed} участников.":"");
            AccessPreviewPeople.Clear();
            foreach(var person in preview.Participants)
            {
                var member=trustClassmates.FirstOrDefault(row=>row.UserId==person.UserId);
                var name=member?.DisplayName ?? member?.Username ?? "Участник";
                var before=string.Join(", ",person.BeforePermissions.Select(PowerLabel).OrderBy(label=>label,StringComparer.Ordinal));
                var after=string.Join(", ",person.AfterPermissions.Select(PowerLabel).OrderBy(label=>label,StringComparer.Ordinal));
                var reasons=person.Sources.Select(source=>new SpaceAccessReason(PowerLabel(source.Key),person.AfterPermissions.Contains(source.Key),source.Value)).ToArray();
                AccessPreviewPeople.Add(new(name,before.Length==0?"Нет действий":before,after.Length==0?"Нет действий":after,reasons));
            }
            previewedAccessFingerprint=fingerprint;AccessPreviewReady=true;
            NotifyAccessPreviewBrowse();
        });
    }
}
public sealed record SpaceAccessReason(string Power,bool Allowed,string Source)
{public string Label=>Power+" · "+(Allowed?"Разрешено":"Недоступно")+" · "+Source;}
public sealed record SpaceAccessPreviewPerson(string Name,string Before,string After,IReadOnlyList<SpaceAccessReason> Reasons)
{ public bool Changed => !string.Equals(Before, After, StringComparison.Ordinal); }
