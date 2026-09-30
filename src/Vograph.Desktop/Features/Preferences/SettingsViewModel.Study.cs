using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;

namespace Vograph.Desktop.Features.Preferences;
public sealed partial class SettingsViewModel
{
    public ObservableCollection<StudySubgroupRow> StudySubgroups{get;}=[];
    [ObservableProperty] private string notificationPreview="";
    [ObservableProperty] private bool notificationPreviewVisible;
    public bool HasStudySubgroups=>StudySubgroups.Count>0;
    private async Task LoadStudyChoices()
    {
        var group=App.Settings.MyGroupId;var version=_version;
        var rows=await RunAsync(()=>
        {
            if(string.IsNullOrEmpty(group))return new List<StudySubgroupData>();
            var choices=App.Db.GetSubgroupChoices(group);
            return SubgroupRules.Build(App.Db.GetAllLessonsForGroup(group)).Streams.Select(x=>new StudySubgroupData(x.Id,x.Title,x.Options,choices.GetValueOrDefault(x.Id))).ToList();
        },"study subgroups");
        if(rows is null || group!=App.Settings.MyGroupId || version!=_version)return;
        StudySubgroups.Clear();foreach(var row in rows)StudySubgroups.Add(new(row.Title,row.Options.Select(x=>new StudySubgroupOption(x.Label,x.Id==row.Selected,new AsyncRelayCommand(()=>ChooseStudySubgroup(group!,row.Id,x.Id)))).ToArray()));
        OnPropertyChanged(nameof(HasStudySubgroups));
    }
    private sealed record StudySubgroupData(string Id,string Title,IReadOnlyList<SubgroupRules.Option> Options,string? Selected);
    private async Task ChooseStudySubgroup(string group,string stream,string option)
    {
        if(group!=App.Settings.MyGroupId)return;
        if(await RunAsync(()=>App.Db.ToggleSubgroupChoice(group,stream,option),"study subgroup")){_shell.RaiseScheduleChanged();await LoadStudyChoices();}
    }
    [RelayCommand] private async Task PreviewNotification()
    {
        var text=await App.NotificationScheduler.PreviewTestAsync(_clock());
        if(!CanPublish)return;NotificationPreview=text??"Уведомление пока недоступно.";NotificationPreviewVisible=true;
    }
}
public sealed record StudySubgroupOption(string Label,bool Selected,IAsyncRelayCommand SelectCommand);
public sealed record StudySubgroupRow(string Title,IReadOnlyList<StudySubgroupOption> Options);
