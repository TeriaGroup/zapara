using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Schedule;
using Avalonia.Threading;

namespace Vograph.Desktop.Features.Preferences;
public sealed partial class SettingsViewModel
{
    public ObservableCollection<StudySubgroupRow> StudySubgroups{get;}=[];
    private readonly DispatcherTimer studyUndoClock = new() { Interval = TimeSpan.FromSeconds(5) };
    private SubgroupChoiceUndo? studyUndo;
    private int studyUndoEpoch;
    public bool HasStudySubgroupUndo => studyUndo is { } undo && undo.Scope == App.Profile.DatabasePath+":"+App.Settings.MyGroupId && studyUndoEpoch == studyRenderEpoch && DateTimeOffset.UtcNow < undo.ExpiresAt;
    private void ClearStudySubgroupUndo(){studyUndoClock.Stop();studyUndo=null;OnPropertyChanged(nameof(HasStudySubgroupUndo));}
    private void SetStudySubgroupUndo(SubgroupChoiceUndo undo,int epoch)
    {
        studyUndoClock.Stop();studyUndo=undo;studyUndoEpoch=epoch;
        var remaining=undo.ExpiresAt-DateTimeOffset.UtcNow;
        studyUndoClock.Interval=remaining>TimeSpan.Zero?remaining:TimeSpan.FromMilliseconds(1);
        studyUndoClock.Tick-=OnStudyUndoTick;studyUndoClock.Tick+=OnStudyUndoTick;studyUndoClock.Start();
        OnPropertyChanged(nameof(HasStudySubgroupUndo));
    }
    private void OnStudyUndoTick(object? sender,EventArgs e)=>ClearStudySubgroupUndo();
    [ObservableProperty] private string notificationPreview="";
    [ObservableProperty] private bool notificationPreviewVisible;
    public bool HasStudySubgroups=>StudySubgroups.Count>0;
    private async Task LoadStudyChoices()
    {
        var group=App.Settings.MyGroupId;var version=_version;var epoch=studyRenderEpoch;
        var scope=App.Profile.DatabasePath+":"+group;
        var rows=await RunAsync(()=>
        {
            if(string.IsNullOrEmpty(group))return new List<StudySubgroupData>();
            var choices=App.Db.GetSubgroupChoices(group);
            return SubgroupRules.Build(App.Db.GetAllLessonsForGroup(group)).Streams.Select(x=>new StudySubgroupData(x.Id,x.Title,x.Options,choices.GetValueOrDefault(x.Id))).ToList();
        },"study subgroups");
        if(rows is null || group!=App.Settings.MyGroupId || version!=_version || epoch!=studyRenderEpoch)return;
        StudySubgroups.Clear();foreach(var row in rows)StudySubgroups.Add(new(row.Title,row.Options.Select(x=>new StudySubgroupOption(x.Label,x.Id==row.Selected,new AsyncRelayCommand(()=>ChooseStudySubgroup(group!,scope,epoch,row.Id,x.Id)))).ToArray()));
        OnPropertyChanged(nameof(HasStudySubgroups));
    }
    private sealed record StudySubgroupData(string Id,string Title,IReadOnlyList<SubgroupRules.Option> Options,string? Selected);
    private async Task ChooseStudySubgroup(string group,string scope,int epoch,string stream,string option)
    {
        if(group!=App.Settings.MyGroupId || scope!=App.Profile.DatabasePath+":"+group || epoch!=studyRenderEpoch)return;
        ClearStudySubgroupUndo();
        var saved=await RunAsync<SubgroupChoiceUndo>(() =>
        {
            if(group!=App.Settings.MyGroupId || scope!=App.Profile.DatabasePath+":"+group || epoch!=studyRenderEpoch)return null!;
            var target=SubgroupRules.Build(App.Db.GetAllLessonsForGroup(group)).Streams.FirstOrDefault(row=>row.Id==stream);
            if(target is null || target.Options.All(row=>row.Id!=option))return null!;
            var before=App.Db.GetSubgroupChoices(group).GetValueOrDefault(stream);
            App.Db.ToggleSubgroupChoice(group,stream,option);
            var after=App.Db.GetSubgroupChoices(group).GetValueOrDefault(stream);
            return new SubgroupChoiceUndo(scope,stream,before,after,DateTimeOffset.UtcNow.AddSeconds(5));
        },"study subgroup");
        if(saved is not null && scope==App.Profile.DatabasePath+":"+App.Settings.MyGroupId && epoch==studyRenderEpoch)
        {SetStudySubgroupUndo(saved,epoch);_shell.RaiseScheduleChanged();await LoadStudyChoices();}
    }
    [RelayCommand(AllowConcurrentExecutions=false)]private async Task UndoStudySubgroup()
    {
        var undo=studyUndo;var epoch=studyUndoEpoch;ClearStudySubgroupUndo();
        if(undo is null || epoch!=studyRenderEpoch || undo.Scope!=App.Profile.DatabasePath+":"+App.Settings.MyGroupId)return;
        var changed=await RunAsync(() =>
        {
            var group=App.Settings.MyGroupId;if(string.IsNullOrEmpty(group)||epoch!=studyRenderEpoch||undo.Scope!=App.Profile.DatabasePath+":"+group)return "";
            var stream=SubgroupRules.Build(App.Db.GetAllLessonsForGroup(group)).Streams.FirstOrDefault(row=>row.Id==undo.StreamId);
            if(stream is null || undo.Before is {} prior && stream.Options.All(option=>option.Id!=prior))return "";
            var current=App.Db.GetSubgroupChoices(group).GetValueOrDefault(undo.StreamId);
            if(!undo.Allows(App.Profile.DatabasePath+":"+group,current,DateTimeOffset.UtcNow))return "";
            App.Db.ToggleSubgroupChoice(group,undo.StreamId,undo.Before??current!);return "saved";
        },"study subgroup undo");
        if(changed=="saved"&&epoch==studyRenderEpoch){_shell.RaiseScheduleChanged();await LoadStudyChoices();}
    }
    [RelayCommand] private async Task PreviewNotification()
    {
        var text=await App.NotificationScheduler.PreviewTestAsync(_clock());
        if(!CanPublish)return;NotificationPreview=text??"Уведомление пока недоступно.";NotificationPreviewVisible=true;
    }
}
public sealed record StudySubgroupOption(string Label,bool Selected,IAsyncRelayCommand SelectCommand);
public sealed record StudySubgroupRow(string Title,IReadOnlyList<StudySubgroupOption> Options);
