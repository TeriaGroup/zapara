using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;
using Vograph.Core.Services.Communities;
using Vograph.Core.Services.Accounts;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Shell;
using Zapara.Client.Domain;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Schedule;
public sealed partial class ScheduleViewModel
{
    public ObservableCollection<PlannerDeadlineRow> Deadlines{get;}=[];
    [ObservableProperty] private string deadlineFeedback="";
    [ObservableProperty] private string sourceSummary="";
    [ObservableProperty] private DateTime? nextStudyDate;
    private Func<Task>? deadlineUndo;
    private int deadlineGeneration;
    private string? deadlineDataKey;
    private bool deadlineUndoShared;
    public bool HasDeadlineFeedback=>DeadlineFeedback.Length>0;
    public bool HasNextStudyDate=>NextStudyDate is not null;
    public string NextStudyCaption=>NextStudyDate is {} date?$"Следующий учебный день · {date.ToString("d MMMM", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))}" : "";
    // #12: как на web — «Ближайшие сроки · N».
    // #12: как на web — «Ближайшие сроки · N»; #19 (D-05): пустой блок — одна строка.
    public string DeadlineTitle=>Deadlines.Count==0?"Сроков на 3 дня нет":$"{Vograph.Desktop.Services.Loc.Current.T("deadlinesTitle")} · {Deadlines.Count}";
    public bool HasDeadlineRows=>Deadlines.Count>0;
    public string DeadlineAttention=>$"Невыполненные сроки: {Deadlines.Count(x=>!x.Done)}";
    public bool HasDeadlineAttention=>Deadlines.Any(row=>!row.Done);
    private void RefreshDeadlineSummary()
    {OnPropertyChanged(nameof(DeadlineTitle));OnPropertyChanged(nameof(HasDeadlineRows));OnPropertyChanged(nameof(DeadlineAttention));OnPropertyChanged(nameof(HasDeadlineAttention));}
    public string DayPriorityCaption=>Date.Date==_clock().Date && Lessons.Count>0 && Lessons.All(x=>x.IsPast)?"Пары закончились":Date.Date>_clock().Date?"Первая пара":"Текущая или следующая пара";
    public bool HasPriority=>Lessons.Any(x=>x.IsNext);
    public bool ShowDayState=>HasPriority || Date.Date==_clock().Date && Lessons.Count>0 && Lessons.All(x=>x.IsPast);
    partial void OnDeadlineFeedbackChanged(string value)=>OnPropertyChanged(nameof(HasDeadlineFeedback));
    partial void OnNextStudyDateChanged(DateTime? value){OnPropertyChanged(nameof(HasNextStudyDate));OnPropertyChanged(nameof(NextStudyCaption));}
    [RelayCommand] private void OpenNextStudyDay(){if(NextStudyDate is {} day)SelectDate(day);}
    [RelayCommand] private async Task UndoDeadline(){var undo=deadlineUndo;deadlineUndo=null;DeadlineFeedback="";try{if(undo is not null)await undo();}catch(Exception ex)when(ex is CommunityClientException or AccountClientException or OperationCanceledException){App.Toasts.Error("Не удалось отменить отметку. Обновите задание.");}}
    private async Task LoadDeadlines(DateTime day,int reload)
    {
        using var operation=App.Work.Enter();if(!operation.IsCurrent)return;
        var generation=++deadlineGeneration;
        var groupId=App.Settings.MyGroupId;
        var cacheKey=App.DataDir+":"+groupId+":"+day.Date.ToString("yyyy-MM-dd");
        HashSet<Guid>? eligibleCommunities=null;
        if(deadlineDataKey!=cacheKey)PurgeSharedDeadlines();
        var visible=Lessons.Select(x=>ParityService.NormalizeSubject(x.Row.Lesson.SubjectRaw)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var model=await RunAsync(()=>new HomeworkComposer(App).Compose(_clock().Date),"planner deadlines");
        if(model is null||reload!=_reloadVersion||generation!=deadlineGeneration)return;
        var local=model.Groups.SelectMany(x=>x.Items).Where(x=>DayPlanning.InDeadlineWindow(DateOnly.FromDateTime(day),x.Due is {} due?DateOnly.FromDateTime(due):null,visible.Contains(x.Homework.SubjectRawNormalized))).ToArray();
        var result=new List<PlannerDeadlineRow>();
        foreach(var entry in local)
        {
            var key="local:"+entry.Homework.Id;
            var row=Deadlines.FirstOrDefault(x=>x.Key==key)??new(key,()=>EditPlannerHomework(entry),r=>TogglePlannerHomework(entry,r),()=>DiscussHomework(entry.SubjectRaw,entry.Homework.Text,entry.Due));
            row.SetActions(()=>Deadlines.Contains(row)?EditPlannerHomework(entry):Task.CompletedTask,r=>Deadlines.Contains(r)?TogglePlannerHomework(entry,r):Task.CompletedTask,()=>{if(Deadlines.Contains(row))DiscussHomework(entry.SubjectRaw,entry.Homework.Text,entry.Due);});
            row.Update(entry.Homework.Text,entry.Subject,entry.Due,entry.Status=="done",entry.Status=="overdue");result.Add(row);
        }
        if(App.Communities is {} api && App.CommunityAccess is {} access)
        try
        {
            var token=await access(operation.Token);
            if(!string.IsNullOrEmpty(token))
            {
                var groupName=await RunAsync(()=>App.Db.GetGroup(App.Settings.MyGroupId??"")?.Name??"","planner group");
                var communities=await api.ListAsync(token,ct:operation.Token);
                eligibleCommunities=communities.Where(x=>x.Role is not null && (x.Name==groupName || x.Name=="Группа "+groupName)).Select(x=>x.CommunityId).ToHashSet();
                foreach(var community in communities.Where(x=>eligibleCommunities.Contains(x.CommunityId)))
                foreach(var item in await api.ListHomeworkCopiesAsync(token,community.CommunityId,operation.Token))
                {
                    if(!item.CanComplete)continue;
                    var localDue=item.DeadlineAt?.LocalDateTime;
                    // Unscheduled shared homework has a subject in its title, following the existing share contract.
                    var subjectIsVisible=visible.Contains(ParityService.NormalizeSubject(item.Title));
                    if(!DayPlanning.InDeadlineWindow(DateOnly.FromDateTime(day),localDue is {} due?DateOnly.FromDateTime(due):null,subjectIsVisible))continue;
                    var key="shared:"+community.CommunityId+":"+item.HomeworkId;
                    var row=Deadlines.FirstOrDefault(x=>x.Key==key)??new(key,()=>OpenSharedDeadline(community.CommunityId,item),r=>ToggleSharedDeadline(community.CommunityId,item,r),()=>DiscussHomework(item.Title,item.Body,localDue));
                    row.CommunityId=community.CommunityId;
                    row.SetActions(()=>Deadlines.Contains(row)?OpenSharedDeadline(community.CommunityId,item):Task.CompletedTask,r=>Deadlines.Contains(r)?ToggleSharedDeadline(community.CommunityId,item,r):Task.CompletedTask,()=>{if(Deadlines.Contains(row))DiscussHomework(item.Title,item.Body,localDue);});
                    row.Update(item.Body,item.Title,localDue,item.Completed,item.DeadlineAt<DateTimeOffset.Now&&!item.Completed);result.Add(row);
                }
            }
        }
        catch(CommunityClientException ex)when(ex.Failure is CommunityClientFailure.Transport or CommunityClientFailure.Timeout or CommunityClientFailure.ServerUnavailable)
        {
            if(deadlineDataKey==cacheKey)result.AddRange(Deadlines.Where(x=>x.Key.StartsWith("shared:",StringComparison.Ordinal) &&
                (eligibleCommunities is null || x.CommunityId is {} cid && eligibleCommunities.Contains(cid)) && !result.Any(y=>y.Key==x.Key)));
        }
        catch(Exception ex)when(ex is AccountClientException || ex is CommunityClientException clientError && clientError.Failure is CommunityClientFailure.InvalidSession or CommunityClientFailure.Forbidden or CommunityClientFailure.NotFound)
        {
            result.RemoveAll(x=>x.Key.StartsWith("shared:",StringComparison.Ordinal));
            if(deadlineUndoShared){deadlineUndo=null;DeadlineFeedback="";}
        }
        catch(CommunityClientException) { }
        catch(OperationCanceledException){return;}
        if(!operation.IsCurrent||reload!=_reloadVersion||generation!=deadlineGeneration||Date.Date!=day.Date||App.Settings.MyGroupId!=groupId)return;
        deadlineDataKey=cacheKey;
        var ordered=result.OrderBy(x=>x.Due??DateTime.MaxValue).ToArray();
        for(var i=0;i<ordered.Length;i++){var row=ordered[i];var old=Deadlines.IndexOf(row);if(old<0)Deadlines.Insert(i,row);else if(old!=i)Deadlines.Move(old,i);}
        while(Deadlines.Count>ordered.Length)Deadlines.RemoveAt(Deadlines.Count-1);
        RefreshDeadlineSummary();
    }
    private async Task EditPlannerHomework(HomeworkEntry entry)
    {
        var owner=_shell.Section<HomeworkViewModel>(SectionKey.Homework);
        var row=await RunAsync(()=>new HomeworkRowViewModel(entry,owner,0),"planner edit");if(row is not null)await owner.EditAsync(row);
    }
    private async Task TogglePlannerHomework(HomeworkEntry entry,PlannerDeadlineRow row)
    {
        var before=row.Done;var day=Date;
        if(!await RunAsync(()=>App.Homework.MarkDone(entry.Homework.Id,!before),"planner ready"))return;
        row.Done=!before;RefreshDeadlineSummary();
        deadlineUndoShared=false;
        deadlineUndo=async()=>{if(await RunAsync(()=>App.Homework.MarkDone(entry.Homework.Id,before),"planner undo")){if(Date==day){row.Done=before;RefreshDeadlineSummary();}await RaiseHomeworkAsync();}};
        DeadlineFeedback=before?"Отметка снята":"Отмечено готово";await RaiseHomeworkAsync();
    }
    private Task OpenSharedDeadline(Guid community,GroupHomeworkCopyResponse item)
    {
        var group=_shell.Section<Features.Groups.GroupViewModel>(SectionKey.Group);group.RequestCommunity(community);group.RequestHomework(item);_shell.NavigateTo(SectionKey.Group);return Task.CompletedTask;
    }
    private void PurgeSharedDeadlines(Guid? community=null)
    {
        foreach(var row in Deadlines.Where(x=>x.Key.StartsWith("shared:",StringComparison.Ordinal) && (community is null || x.CommunityId==community)).ToArray())Deadlines.Remove(row);
        if(deadlineUndoShared){deadlineUndo=null;DeadlineFeedback="";}
        RefreshDeadlineSummary();
    }
    private async Task ToggleSharedDeadline(Guid community,GroupHomeworkCopyResponse item,PlannerDeadlineRow row)
    {
        if(!item.CanComplete || App.Communities is not {} api||App.CommunityAccess is not {} access)return;
        try{var token=await access(CancellationToken.None);if(string.IsNullOrWhiteSpace(token))return;var before=row.Done;var state=await api.GetCompletionAsync(token,community,item.HomeworkId);var changed=await api.UpsertCompletionAsync(token,community,item.HomeworkId,new(!before,state.Revision));row.Done=!before;RefreshDeadlineSummary();deadlineUndoShared=true;deadlineUndo=async()=>{await api.UpsertCompletionAsync(token,community,item.HomeworkId,new(before,changed.Revision));row.Done=before;RefreshDeadlineSummary();};DeadlineFeedback=before?"Отметка снята":"Отмечено готово";}
        catch(Exception ex)when(ex is AccountClientException || ex is CommunityClientException e && e.Failure is CommunityClientFailure.InvalidSession or CommunityClientFailure.Forbidden or CommunityClientFailure.NotFound){PurgeSharedDeadlines(community);App.Toasts.Error("Доступ к заданию изменился.");}
        catch(CommunityClientException){App.Toasts.Error("Не удалось сохранить готовность.");}
    }
    public void OpenSubjectHomeworks(LessonRowViewModel row){_shell.Section<HomeworkViewModel>(SectionKey.Homework).SubjectFilter=row.Row.Lesson.SubjectRaw;_shell.NavigateTo(SectionKey.Homework);}
    public void DiscussLesson(LessonRowViewModel row)
    {var l=row.Row.Lesson;var context=$"Пара {Date:dd.MM.yyyy} · {row.TimeStart}–{row.TimeEnd} · {row.DisplayName} · {row.RoomText}";OpenDiscussion(l.SubjectRaw,context);}
    private void DiscussHomework(string subject,string text,DateTime? due)=>OpenDiscussion(subject,$"Задание · {subject} · {text}"+(due is {} date?$" · срок {date:dd.MM.yyyy}":" · без срока"));
    private void OpenDiscussion(string subject,string context)
    {var group=_shell.Section<Features.Groups.GroupViewModel>(SectionKey.Group);group.RequestDiscussion(subject,context);_shell.NavigateTo(SectionKey.Group);}
}
public sealed partial class PlannerDeadlineRow : ObservableObject
{
    private Func<Task> open;
    private Func<PlannerDeadlineRow,Task> toggle;
    private Action discuss;
    public PlannerDeadlineRow(string key,Func<Task> open,Func<PlannerDeadlineRow,Task> toggle,Action discuss)
    {Key=key;this.open=open;this.toggle=toggle;this.discuss=discuss;OpenCommand=new AsyncRelayCommand(()=>this.open());ToggleCommand=new AsyncRelayCommand(()=>this.toggle(this));DiscussCommand=new RelayCommand(()=>this.discuss());}
    internal void SetActions(Func<Task> open,Func<PlannerDeadlineRow,Task> toggle,Action discuss){this.open=open;this.toggle=toggle;this.discuss=discuss;}
    internal Guid? CommunityId{get;set;}
    public string Key{get;} public DateTime? Due{get;private set;}
    [ObservableProperty] private string text="";
    [ObservableProperty] private string subject="";
    [ObservableProperty] private string deadline="";
    [ObservableProperty] private bool done;
    public string CompletionCaption=>Done?"Выполнено":"Выполнено: "+Text;
    partial void OnDoneChanged(bool value)=>OnPropertyChanged(nameof(CompletionCaption));
    public IAsyncRelayCommand OpenCommand{get;} public IAsyncRelayCommand ToggleCommand{get;} public IRelayCommand DiscussCommand{get;}
    internal void Update(string text,string subject,DateTime? due,bool done,bool overdue){Text=text;Subject=subject;Due=due;Done=done;Deadline=due is {} date?$"{date:dd.MM.yyyy}"+(overdue?" · Просрочено":""):"Без срока";}
}
