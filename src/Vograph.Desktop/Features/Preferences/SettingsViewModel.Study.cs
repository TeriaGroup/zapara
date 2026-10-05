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
    [ObservableProperty] private DateTime? studyImpactWeekDate;
    [ObservableProperty] private bool showStudyImpact;
    [ObservableProperty] private string studyImpactText = "";
    private sealed record ImpactPending(string Scope, int Epoch, string Group, string Stream,
        string Option, DateTime Monday, string Fingerprint);
    private ImpactPending? impactPending;
    public bool CanApplyStudyImpact => impactPending is not null;
    private void ClearStudyImpact()
    { impactPending=null; OnPropertyChanged(nameof(CanApplyStudyImpact)); ShowStudyImpact=false; StudyImpactText=""; }
    partial void OnStudyImpactWeekDateChanged(DateTime? value) => ClearStudyImpact();
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
        StudySubgroups.Clear();foreach(var row in rows)StudySubgroups.Add(new(row.Title,row.Options.Select(x=>new StudySubgroupOption(x.Label,x.Id==row.Selected,
            new AsyncRelayCommand(async()=>{await ChooseStudySubgroup(group!,scope,epoch,row.Id,x.Id);}),
            new AsyncRelayCommand(()=>PreviewStudyImpact(group!,scope,epoch,row.Id,x.Id)))).ToArray()));
        OnPropertyChanged(nameof(HasStudySubgroups));
    }
    private sealed record StudySubgroupData(string Id,string Title,IReadOnlyList<SubgroupRules.Option> Options,string? Selected);
    private async Task<bool> ChooseStudySubgroup(string group,string scope,int epoch,string stream,string option,string? expectedFingerprint=null)
    {
        if(group!=App.Settings.MyGroupId || scope!=App.Profile.DatabasePath+":"+group || epoch!=studyRenderEpoch)return false;
        ClearStudySubgroupUndo();
        var saved=await RunAsync<SubgroupChoiceUndo>(() =>
        {
            if(group!=App.Settings.MyGroupId || scope!=App.Profile.DatabasePath+":"+group || epoch!=studyRenderEpoch)return null!;
            var lessons=App.Db.GetAllLessonsForGroup(group);
            if(expectedFingerprint is not null && StudyImpactPlanner.Fingerprint(App.Db.GetSettings(),lessons,
                App.Db.GetSubgroupChoices(group))!=expectedFingerprint)return null!;
            var target=SubgroupRules.Build(lessons).Streams.FirstOrDefault(row=>row.Id==stream);
            if(target is null || target.Options.All(row=>row.Id!=option))return null!;
            var before=App.Db.GetSubgroupChoices(group).GetValueOrDefault(stream);
            App.Db.ToggleSubgroupChoice(group,stream,option);
            var after=App.Db.GetSubgroupChoices(group).GetValueOrDefault(stream);
            return new SubgroupChoiceUndo(scope,stream,before,after,DateTimeOffset.UtcNow.AddSeconds(5));
        },"study subgroup");
        if(saved is not null && scope==App.Profile.DatabasePath+":"+App.Settings.MyGroupId && epoch==studyRenderEpoch)
        {SetStudySubgroupUndo(saved,epoch);_shell.RaiseScheduleChanged();await LoadStudyChoices();return true;}
        return false;
    }
    private async Task PreviewStudyImpact(string group,string scope,int epoch,string stream,string option)
    {
        if(group!=App.Settings.MyGroupId || scope!=App.Profile.DatabasePath+":"+group || epoch!=studyRenderEpoch ||
            StudyImpactWeekDate is not { } date)return;
        var result=await RunAsync(() => StudyImpactPlanner.Preview(App.Db.GetSettings(),
            App.Db.GetAllLessonsForGroup(group),App.Db.GetSubgroupChoices(group),date,stream,option),"study impact");
        if(result is null || group!=App.Settings.MyGroupId || scope!=App.Profile.DatabasePath+":"+group ||
            epoch!=studyRenderEpoch || StudyImpactWeekDate?.Date!=date.Date)return;
        if(!result.Known){ClearStudyImpact();StudyImpactText="Для этой недели нет полного известного расписания или выбранная подгруппа больше недоступна.";ShowStudyImpact=true;return;}
        impactPending=new(scope,epoch,group,stream,option,result.Monday,result.Fingerprint);
        OnPropertyChanged(nameof(CanApplyStudyImpact));
        StudyImpactText=result.Changes.Count==0?"На этой фактической неделе состав пар не изменится. Можно применить выбор." :
            $"После выбора подгруппы: добавится {result.Changes.Count(row=>row.Kind=="Добавлено")}, " +
            $"исчезнет {result.Changes.Count(row=>row.Kind=="Убрано")} пар.\n" +
            string.Join("\n",result.Changes.Select(row=>$"• {row.Kind} · {result.Monday.AddDays(row.Weekday-1):dd.MM.yyyy} · " +
                $"{row.Row.Time} · {row.Row.SubjectRaw} · {row.Row.Teacher} · {row.Row.ClassroomRaw}"));
        ShowStudyImpact=true;
    }
    [RelayCommand(AllowConcurrentExecutions=false)] private async Task ApplyStudyImpact()
    {
        var pending=impactPending;
        if(pending is null || StudyImpactWeekDate?.Date.AddDays(-((int)StudyImpactWeekDate.Value.DayOfWeek+6)%7)!=pending.Monday)return;
        if(await ChooseStudySubgroup(pending.Group,pending.Scope,pending.Epoch,pending.Stream,pending.Option,pending.Fingerprint))
        {ClearStudyImpact();return;}
        impactPending=null;OnPropertyChanged(nameof(CanApplyStudyImpact));StudyImpactText="Расписание или выбор подгруппы изменились. Проверьте влияние заново.";
    }
    [RelayCommand] private void CancelStudyImpact()=>ClearStudyImpact();
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
public sealed record StudySubgroupOption(string Label,bool Selected,IAsyncRelayCommand SelectCommand,
    IAsyncRelayCommand PreviewCommand);
public sealed record StudySubgroupRow(string Title,IReadOnlyList<StudySubgroupOption> Options);
