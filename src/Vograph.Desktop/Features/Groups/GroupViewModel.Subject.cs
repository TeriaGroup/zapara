using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;
using Vograph.Desktop.Shell;

namespace Vograph.Desktop.Features.Groups;
public sealed partial class GroupViewModel
{
    private ShellViewModel? shell;
    [ObservableProperty] private string subjectLesson="";
    public ObservableCollection<string> SubjectHomeworks{get;}=[];
    private DateTime? subjectLessonDate;
    private int subjectRequestVersion;
    [ObservableProperty] private string subjectPanel="";
    [ObservableProperty] private DateTime? subjectScheduleDate=DateTime.Today;
    [ObservableProperty] private string subjectScheduleSummary="";
    public bool ShowSubjectSchedule=>ShowSubject && SubjectPanel=="schedule";
    public bool ShowSubjectHomework=>ShowSubject && SubjectPanel=="homework";
    public ObservableCollection<string> SubjectScheduleRows{get;}=[];
    public ObservableCollection<SpaceHomeworkRow> SubjectSharedTasks{get;}=[];
    private sealed record SubjectKey(Guid Community,Guid? Topic,string Subject,string Group,int Navigation,bool Preview);
    private SubjectKey? SubjectScope()=>ShowSubject && communityId is {} community && SelectedChannel?.Subject is {} subject?new(community,selectedTopicId,subject,HomeTitle,navigationGeneration,PreviewMode):null;
    private bool SubjectCurrent(SubjectKey key)=>CanPublish && SubjectScope()==key;
    partial void OnSubjectPanelChanged(string value){OnPropertyChanged(nameof(ShowSubjectSchedule));OnPropertyChanged(nameof(ShowSubjectHomework));}
    partial void OnSubjectScheduleDateChanged(DateTime? value){if(ShowSubjectSchedule)_=RefreshSubjectSchedule();}
    private async Task LoadSubjectContext()
    {
        var key=SubjectScope();if(key is null)return;var version=++subjectRequestVersion;
        var context=await RunAsync(()=>
        {
            var group=App.Db.GetAllGroups().FirstOrDefault(x=>x.Name==key.Group||"Группа "+x.Name==key.Group);
            if(group is null)return new SubjectContext("Расписание этой группы ещё не сохранено.",null,[]);
            var normalized=ParityService.NormalizeSubject(key.Subject);var now=DateTime.Now;DateTime? next=null;var caption="Ближайшая пара пока неизвестна.";
            for(var offset=0;offset<32;offset++)
            {
                var date=now.Date.AddDays(offset);var day=new Features.Schedule.ScheduleComposer(App,group.Id).Compose(offset,now);
                if(day.IsUnavailable){caption=day.EmptyTitle??"Нет данных расписания";break;}
                var lesson=day.Rows.FirstOrDefault(x=>ParityService.NormalizeSubject(x.Lesson.SubjectRaw)==normalized && (offset>0||TimeSpan.TryParse(x.TimeEnd,out var end)&&end>now.TimeOfDay));
                if(lesson is null)continue;next=date;caption=$"{date:dd.MM.yyyy} · {lesson.TimeStart}–{lesson.TimeEnd} · {lesson.RoomText}";break;
            }
            var personal=!key.Preview && group.Id==App.Settings.MyGroupId?App.Homework.GetForSubject(key.Subject).Select(x=>$"{x.Text} · "+(x.DueDateComputed is {} date?$"срок {date:dd.MM.yyyy}":"без срока")+(x.Status=="done"?" · готово у меня":"")).ToArray():[];
            return new SubjectContext(caption,next,personal);
        },"subject context");
        if(context is null||!SubjectCurrent(key)||version!=subjectRequestVersion)return;
        SubjectLesson=context.Caption;subjectLessonDate=context.Date;SubjectHomeworks.Clear();foreach(var item in context.Homeworks)SubjectHomeworks.Add(item);
        await LoadSubjectShared(key,version);
    }
    private sealed record SubjectContext(string Caption,DateTime? Date,IReadOnlyList<string> Homeworks);
    private bool CopyMatchesSubject(Zapara.Contracts.Communities.GroupHomeworkCopyResponse item,SubjectKey key)
    {
        var normalized=ParityService.NormalizeSubject(key.Subject);
        if(item.TopicId is {} linked)
        {
            var topic=Channels.FirstOrDefault(x=>x.TopicId==linked && (x.Permissions.Contains("read")||legacySpace));
            if(topic is null)return false;
            return linked==key.Topic || topic.Subject is {} subject && ParityService.NormalizeSubject(subject)==normalized || ParityService.NormalizeSubject(item.Title)==normalized;
        }
        return ParityService.NormalizeSubject(item.Title)==normalized;
    }
    private Task LoadSubjectShared(SubjectKey key,int version)=>SpaceAction(async(api,token,community,ct)=>
    {
        var copies=await api.ListHomeworkCopiesAsync(token,key.Community,ct);
        if(!CurrentSpace()||!SubjectCurrent(key)||version!=subjectRequestVersion)return;
        SubjectSharedTasks.Clear();
        foreach(var original in copies.Where(x=>CopyMatchesSubject(x,key)))
        {
            var item=key.Preview?new Zapara.Contracts.Communities.GroupHomeworkCopyResponse(original.HomeworkId,original.Title,original.Body,original.Revision,false,0,original.DeadlineAt,original.TopicId):original;
            var writable=!key.Preview && (item.TopicId is null || Channels.First(x=>x.TopicId==item.TopicId).Archived==false);
            SubjectSharedTasks.Add(new(item,writable,row=>ToggleSubjectHomework(key,row),_=>{}));
        }
    });
    private Task ToggleSubjectHomework(SubjectKey key,SpaceHomeworkRow row)
    {
        if(key.Preview||!SubjectCurrent(key)||!SubjectSharedTasks.Any(x=>x.Item.HomeworkId==row.Item.HomeworkId))return Task.CompletedTask;
        return SpaceAction(async(api,t,c,ct)=>{await api.UpsertCompletionAsync(t,key.Community,row.Item.HomeworkId,new(!row.Item.Completed,row.Item.CompletionRevision),ct);if(CurrentSpace()&&SubjectCurrent(key))await LoadSubjectContext();},true);
    }
    [RelayCommand] private async Task OpenSubjectSchedule()
    {
        if(SubjectScope() is null)return;SubjectPanel="schedule";SubjectScheduleDate=subjectLessonDate??DateTime.Today;await RefreshSubjectSchedule();
    }
    [RelayCommand] private async Task OpenSubjectHomework(){if(SubjectScope() is null)return;SubjectPanel="homework";await LoadSubjectContext();}
    [RelayCommand] private void CloseSubjectPanel()=>SubjectPanel="";
    [RelayCommand] private async Task RefreshSubjectSchedule()
    {
        var key=SubjectScope();if(key is null||!ShowSubjectSchedule)return;var date=SubjectScheduleDate??DateTime.Today;var version=++subjectRequestVersion;
        var day=await RunAsync(()=>
        {
            var group=App.Db.GetAllGroups().FirstOrDefault(x=>x.Name==key.Group||"Группа "+x.Name==key.Group);
            return group is null?null!:new Features.Schedule.ScheduleComposer(App,group.Id).Compose((date.Date-DateTime.Today).Days,DateTime.Now);
        },"subject schedule");
        if(!SubjectCurrent(key)||!ShowSubjectSchedule||SubjectScheduleDate!=date||version!=subjectRequestVersion)return;
        SubjectScheduleRows.Clear();
        if(day is null){SubjectScheduleSummary="Расписание этой группы ещё не сохранено.";return;}
        SubjectScheduleSummary=$"{date:dd.MM.yyyy} · {key.Subject} · {day.SourceSummary}";
        if(day.IsUnavailable){SubjectScheduleRows.Add(day.EmptyTitle??"Нет данных");return;}
        var lessons=day.Rows.Where(x=>ParityService.NormalizeSubject(x.Lesson.SubjectRaw)==ParityService.NormalizeSubject(key.Subject)).ToArray();
        if(lessons.Length==0)SubjectScheduleRows.Add("По предмету пар нет");
        foreach(var lesson in lessons)SubjectScheduleRows.Add($"{lesson.TimeStart}–{lesson.TimeEnd} · {lesson.DisplayName} · {lesson.RoomText} · {lesson.Teacher}");
    }
    private void ClearSubjectPanels()
    {
        subjectRequestVersion++;SubjectPanel="";SubjectLesson="";SubjectHomeworks.Clear();SubjectSharedTasks.Clear();SubjectScheduleRows.Clear();SubjectScheduleSummary="";subjectLessonDate=null;
    }
    [RelayCommand] private Task AttachLessonCard()
    {
        if(!ShowComposer||PreviewMode||shell is null)return Task.CompletedTask;
        var day=shell.Section<Features.Schedule.ScheduleViewModel>(SectionKey.Schedule);
        var row=day.Lessons.FirstOrDefault(x=>x.IsNext)??day.Lessons.FirstOrDefault();
        if(row is null){Status="Откройте учебный день с парой, чтобы прикрепить её карточку.";return Task.CompletedTask;}
        DiscussionContext=$"Пара {day.Date:dd.MM.yyyy} · {row.TimeStart}–{row.TimeEnd} · {row.DisplayName} · {row.RoomText}";
        return Task.CompletedTask;
    }
    [RelayCommand] private Task OpenTopicPolls()
    {var topic=Channels.FirstOrDefault(x=>x.Kind=="ballots");if(topic is null){Status="В группе пока нет доступной темы опросов.";return Task.CompletedTask;}return OpenChannelAsync(topic);}
}
