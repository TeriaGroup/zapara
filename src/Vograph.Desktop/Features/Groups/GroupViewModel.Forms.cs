using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel
{
    public ObservableCollection<SpaceQuestionEditor> FormQuestions { get; } = [];
    public ObservableCollection<SpaceFormRow> Forms { get; } = [];
    public ObservableCollection<SpaceHomeworkRow> ChannelHomeworks { get; } = [];
    public ObservableCollection<string> ChannelSchedule { get; } = [];
    [ObservableProperty] private string formTitle = "";
    [ObservableProperty] private string formDescription = "";
    [ObservableProperty] private string formDeadline = "";
    [ObservableProperty] private bool formAnonymous;
    [ObservableProperty] private string sharedHomeworkTitle = "";
    [ObservableProperty] private string sharedHomeworkBody = "";
    [ObservableProperty] private string sharedHomeworkDeadline = "";
    [ObservableProperty] private DateTime? channelScheduleDate = DateTime.Today;
    private readonly Dictionary<(Guid Community,Guid Topic), List<SpaceQuestionEditor>> formDraftQuestions = [];
    private readonly Dictionary<(Guid Community,Guid Topic),(string Title,string Description,string Deadline,bool Anonymous)> formDrafts = [];
    private (Guid Community,Guid Topic)? activeFormDraft;
    private Guid? editingSharedHomework;
    private long sharedHomeworkRevision;
    private int homeworkEditorSerial;
    private int homeworkSerialCounter;
    private void SaveFormDraft()
    {
        if(activeFormDraft is not {} id)return;
        formDrafts[id]=(FormTitle,FormDescription,FormDeadline,FormAnonymous);
        formDraftQuestions[id]=FormQuestions.ToList();
    }
    private void SelectFormDraft(Guid? id)
    {
        SaveFormDraft(); activeFormDraft=id is {} topic && communityId is {} community?(community,topic):null; FormQuestions.Clear();
        var draft=activeFormDraft is {} key ? formDrafts.GetValueOrDefault(key) : default;
        FormTitle=draft.Title??""; FormDescription=draft.Description??""; FormDeadline=draft.Deadline??""; FormAnonymous=draft.Anonymous;
        if(activeFormDraft is {} savedKey && formDraftQuestions.TryGetValue(savedKey,out var questions)) foreach(var question in questions)FormQuestions.Add(question);
    }
    [RelayCommand] private void AddFormQuestion() { if(CanCreateForm && FormQuestions.Count<30)FormQuestions.Add(new()); }
    [RelayCommand] private void RemoveFormQuestion(SpaceQuestionEditor question) { if(CanCreateForm)FormQuestions.Remove(question); }
    internal static DateTimeOffset? ParseDeadline(string input)
    {
        if(string.IsNullOrWhiteSpace(input))return null;
        if(!DateTime.TryParseExact(input.Trim(),"dd.MM.yyyy HH:mm",CultureInfo.GetCultureInfo("ru-RU"),DateTimeStyles.None,out var date))throw new ArgumentException("Срок: дд.мм.гггг чч:мм");
        return new DateTimeOffset(DateTime.SpecifyKind(date,DateTimeKind.Local)).ToUniversalTime();
    }
    [RelayCommand] private Task PublishForm()
    {
        if(!CanCreateForm || selectedTopicId is not Guid topic)return Task.CompletedTask;
        GroupFormRequest request;
        try { request=new(FormTitle.Trim(),FormDescription.Trim(),ParseDeadline(FormDeadline),FormAnonymous,FormQuestions.Select(x=>x.Question()).ToArray());
            if(request.Title.Length==0||request.Questions.Count==0||request.Questions.Any(x=>x.Title.Length==0||x.Kind is "singleChoice" or "multipleChoice" && x.Options.Count<2))throw new ArgumentException(); }
        catch(ArgumentException) { Status="Укажите название, вопросы, варианты выбора и срок в формате дд.мм.гггг чч:мм (или оставьте срок пустым)."; return Task.CompletedTask; }
        return SpaceAction(async(api,t,c,ct)=>
        {
            var result=await api.CreateFormAsync(t,c,topic,request,ct);if(!CurrentSpace() || selectedTopicId!=topic)return;
            var unchanged=false;
            try{unchanged=System.Text.Json.JsonSerializer.Serialize(request)==System.Text.Json.JsonSerializer.Serialize(new GroupFormRequest(FormTitle.Trim(),FormDescription.Trim(),ParseDeadline(FormDeadline),FormAnonymous,FormQuestions.Select(x=>x.Question()).ToArray()));}catch(ArgumentException){}
            if(unchanged){FormTitle="";FormDescription="";FormDeadline="";FormQuestions.Clear();}SaveFormDraft();ApplyForms(result);
        },true);
    }
    private readonly Dictionary<(Guid Community,Guid Topic,Guid Form),SpaceFormRow> formRows=[];
    private readonly Dictionary<(Guid Community,Guid Topic,Guid Form),GroupFormAnswerRequest> answerDrafts=[];
    private (Guid Community,Guid Topic)? activeFormsScope;
    private void SaveAnswerDrafts()
    {
        if(activeFormsScope is not {} scope)return;var (community,topic)=scope;
        foreach(var row in Forms)
        {var key=(community,topic,row.Form.FormId);if(row.HasUnsavedAnswers)answerDrafts[key]=row.Answers();else answerDrafts.Remove(key);}
    }
    private void ApplyForms(GroupFormListResponse response)
    {
        if(!CurrentSpace() || communityId is not {} community || selectedTopicId is not {} topic)return;
        activeFormsScope=(community,topic);
        var ordered=new List<SpaceFormRow>();
        foreach(var form in response.Forms)
        {
            var key=(community,topic,form.FormId);
            if(!formRows.TryGetValue(key,out var row))
            {
                row=new(form,!PreviewMode,SubmitForm,ReadResponses,ExportResponses);formRows[key]=row;
                if(answerDrafts.TryGetValue(key,out var draft))row.RestoreAnswers(draft);
            }
            else row.Update(form,!PreviewMode);
            ordered.Add(row);
        }
        for(var i=0;i<ordered.Count;i++){var old=Forms.IndexOf(ordered[i]);if(old<0)Forms.Insert(i,ordered[i]);else if(old!=i)Forms.Move(old,i);}
        while(Forms.Count>ordered.Count)Forms.RemoveAt(Forms.Count-1);
    }
    private Task SubmitForm(SpaceFormRow row)
    {
        if(PreviewMode || !row.CanRespond || !Forms.Contains(row) || communityId is not {} community || selectedTopicId is not {} topic)return Task.CompletedTask;
        var request=row.Answers();var key=(community,topic,row.Form.FormId);
        return SpaceAction(async(api,t,c,ct)=>
        {
            if(PreviewMode || !row.CanRespond || !Forms.Contains(row))return;
            if(row.Form.Questions.Any(q=>q.Required && !request.Answers.Any(a=>a.QuestionId==q.QuestionId && (q.Kind is "shortText" or "longText"?!string.IsNullOrWhiteSpace(a.Text):a.Choices.Count>0))))throw new ArgumentException();
            var response=await api.SubmitFormAsync(t,c,row.Form.FormId,request,ct);
            if(!CurrentSpace() || !Forms.Contains(row))return;
            var unchanged=System.Text.Json.JsonSerializer.Serialize(row.Answers())==System.Text.Json.JsonSerializer.Serialize(request);
            row.Update(response,!PreviewMode,unchanged);if(unchanged)answerDrafts.Remove(key);else answerDrafts[key]=row.Answers();
        },true);
    }
    private Task ReadResponses(SpaceFormRow row)
    {
        if(!row.CanViewResponses || !Forms.Contains(row))return Task.CompletedTask;
        if(row.ResponseLoading && row.ResponseLoadTask is {} running)return running;
        row.ResponseLoading=true;row.ResponsesComplete=false;
        var task=ReadResponsePages(row);row.ResponseLoadTask=task;return task;
    }
    private async Task ReadResponsePages(SpaceFormRow row)
    {
        try
        {
            await SpaceAction(async(api,t,c,ct)=>
            {
                row.Responses.Clear();row.RawResponses.Clear();Guid? after=null;var cursors=new HashSet<Guid>();
                do
                {
                    var page=await api.FormResponsesAsync(t,c,row.Form.FormId,ct,after);
                    if(!CurrentSpace() || !Forms.Contains(row) || !row.CanViewResponses)return;
                    foreach(var response in page.Responses)
                    {
                        row.RawResponses.Add(response);
                        row.Responses.Add($"{(response.RespondentId is Guid id ? trustClassmates.FirstOrDefault(x=>x.UserId==id)?.DisplayName ?? "Участник" : "Анонимно")} · {response.UpdatedAt.ToLocalTime():dd.MM.yyyy HH:mm}\n"+string.Join("\n",response.Answers.Select(x=>(row.Form.Questions.FirstOrDefault(q=>q.QuestionId==x.QuestionId)?.Title??"Вопрос")+": "+(x.Text??string.Join(", ",x.Choices)))));
                    }
                    after=page.NextCursor;
                    if(after is {} cursor && !cursors.Add(cursor))throw new Vograph.Core.Services.Communities.CommunityClientException(Vograph.Core.Services.Communities.CommunityClientFailure.InvalidPayload);
                    if(after is null)row.ResponsesComplete=page.TotalResponses==row.RawResponses.Count;
                }while(after is not null && !ct.IsCancellationRequested);
            });
        }
        finally{row.ResponseLoading=false;}
    }
    private async Task ExportResponses(SpaceFormRow row)
    {
        using var operation=App.Work.Enter();var community=communityId;var topic=selectedTopicId;var ticket=navigationGeneration;
        if(!row.CanViewResponses || !Forms.Contains(row))return;
        await ReadResponses(row);
        if(!operation.IsCurrent || ticket!=navigationGeneration || communityId!=community || selectedTopicId!=topic || !row.CanViewResponses || !row.ResponsesComplete || !Forms.Contains(row))return;
        var path=await App.FileDialogs.SaveChatMediaAsync("Ответы анкеты.csv"); if(string.IsNullOrWhiteSpace(path))return;
        if(!operation.IsCurrent || ticket!=navigationGeneration || !row.CanViewResponses || !Forms.Contains(row) || !row.ResponsesComplete)return;
        if(File.Exists(path)){Status="Файл уже существует. Выберите новое имя.";return;}
        static string Cell(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
        var lines=new[]{string.Join(";",new[]{"Участник","Дата"}.Concat(row.Form.Questions.Select(x=>x.Title)).Select(Cell))}.Concat(row.RawResponses.Select(response=>string.Join(";",new[]{response.RespondentId?.ToString()??"Анонимно",response.UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")}.Concat(row.Form.Questions.Select(q=>{var a=response.Answers.FirstOrDefault(x=>x.QuestionId==q.QuestionId);return a?.Text??string.Join(", ",a?.Choices??[]);})).Select(Cell))));
        try{await File.WriteAllLinesAsync(path,lines,new System.Text.UTF8Encoding(true),operation.Token);Status="Ответы сохранены.";}catch(IOException){Status="Не удалось сохранить файл.";}catch(OperationCanceledException){}
    }
    private sealed record HomeworkDraft(int Serial,Guid? Id,long Revision,string Title,string Body,string Deadline,
        Features.Homeworks.HomeworkAudienceSnapshot Audience,Guid OperationId,bool Pending);
    private readonly Dictionary<(Guid Community,Guid Topic),HomeworkDraft> homeworkDrafts=[];
    private (Guid Community,Guid Topic)? activeHomeworkDraft;
    private HomeworkDraft CurrentHomeworkDraft()=>new(homeworkEditorSerial,editingSharedHomework,sharedHomeworkRevision,SharedHomeworkTitle,SharedHomeworkBody,SharedHomeworkDeadline,HomeworkRecipients.Snapshot,sharedHomeworkOperationId,sharedHomeworkPending);
    private void SaveHomeworkDraft(){if(activeHomeworkDraft is {} key && !updatingHomeworkRecipients)homeworkDrafts[key]=CurrentHomeworkDraft();NotifyChannelHomeworkState();}
    private static bool SameHomeworkInput(HomeworkDraft a,HomeworkDraft b) => a with{Pending=false} == b with{Pending=false};
    private void SelectHomeworkDraft(Guid? topic)
    {
        SaveHomeworkDraft();activeHomeworkDraft=topic is {} id && communityId is {} community?(community,id):null;
        var draft=activeHomeworkDraft is {} key?homeworkDrafts.GetValueOrDefault(key):null;
        updatingHomeworkRecipients=true;channelHomeworkLoaded=false;
        homeworkEditorSerial=draft?.Serial??++homeworkSerialCounter;editingSharedHomework=draft?.Id;sharedHomeworkRevision=draft?.Revision??0;SharedHomeworkTitle=draft?.Title??"";SharedHomeworkBody=draft?.Body??"";SharedHomeworkDeadline=draft?.Deadline??"";
        sharedHomeworkOperationId=draft?.OperationId??Guid.NewGuid();sharedHomeworkPending=draft?.Pending??false;
        RestoreHomeworkAudience(draft?.Audience??new(0,"",""));
    }
    private void ClearSpecializedDrafts()
    {
        foreach(var row in formRows.Values)row.Revoke();formRows.Clear();answerDrafts.Clear();homeworkDrafts.Clear();
        activeFormsScope=null;activeFormDraft=null;activeHomeworkDraft=null;editingSharedHomework=null;sharedHomeworkRevision=0;
        FormQuestions.Clear();FormTitle="";FormDescription="";FormDeadline="";FormAnonymous=false;
        SharedHomeworkTitle="";SharedHomeworkBody="";SharedHomeworkDeadline="";
        sharedHomeworkPending=false;ClearHomeworkRecipients();
    }
    private async Task LoadSpecializedAsync(Guid conversation,int ticket)
    {
        RefreshHomeworkRecipients();
        if(ShowMaterials)
        {
            await LoadLatestAsync(conversation,ticket);
            while(HasMore && CurrentChat(conversation,ticket))
            {var count=Messages.Count;await LoadOlder();if(Messages.Count<=count)break;}
        }
        else if(ShowForms && selectedTopicId is Guid topic) await SpaceAction(async(api,t,c,ct)=> { var result=await api.FormsAsync(t,c,topic,ct);if(CurrentChat(conversation,ticket))ApplyForms(result); });
        else if(ShowChannelHomework && selectedTopicId is Guid homeworkTopic) await SpaceAction(async(api,t,c,ct)=> {var result=await api.ListHomeworkCopiesAsync(t,c,ct,homeworkTopic);if(CurrentChat(conversation,ticket))ApplyChannelHomeworks(result);});
        else if(ShowChannelSchedule) await RefreshChannelSchedule();
    }
    private void ApplyChannelHomeworks(IReadOnlyList<GroupHomeworkCopyResponse> result)
    { if(!CurrentSpace())return;channelHomeworkLoaded=true;ChannelHomeworks.Clear();foreach(var item in result)ChannelHomeworks.Add(new(item,!PreviewMode && item.CanComplete,ToggleChannelHomework,EditChannelHomework,
        !PreviewMode && (space?.Capabilities.HomeworkAudience==true?item.CanEdit:CanCreateChannelHomework),HomeworkAudienceLabel(item.Audience)));NotifyChannelHomeworkState(); }
    private Task ToggleChannelHomework(SpaceHomeworkRow row) => PreviewMode || !row.Writable || IsBusy ? Task.CompletedTask : SpaceAction(async(api,t,c,ct)=>
    {
        var topic=selectedTopicId;var single=ShowSingleHomework;
        CompletionResponse changed;
        try { changed=await api.UpsertCompletionAsync(t,c,row.Item.HomeworkId,new(!row.Item.Completed,row.Item.CompletionRevision),ct); }
        catch(Vograph.Core.Services.Communities.CommunityClientException ex) when(ex.Failure==Vograph.Core.Services.Communities.CommunityClientFailure.RevisionConflict)
        { if(CurrentSpace()){var latest=await api.ListHomeworkCopiesAsync(t,c,ct,topic);if(CurrentSpace()){ApplyChannelHomeworks(single?latest.Where(x=>x.HomeworkId==row.Item.HomeworkId).ToArray():latest);Status="Отметка изменилась на другом устройстве. Показано актуальное состояние.";}}return; }
        if(!CurrentSpace())return;
        if(single){var item=row.Item;ApplyChannelHomeworks([new(item.HomeworkId,item.Title,item.Body,item.Revision,changed.Completed,changed.Revision,item.DeadlineAt,item.TopicId,item.Audience,item.CanEdit,item.CanComplete)]);}
        else ApplyChannelHomeworks(await api.ListHomeworkCopiesAsync(t,c,ct,topic));
    },true);
    private void EditChannelHomework(SpaceHomeworkRow row)
    {if(!row.Editable||IsBusy||sharedHomeworkPending)return;activeHomeworkDraft ??= communityId is {} c?(c,row.Item.TopicId??Guid.Empty):null;
        updatingHomeworkRecipients=true;homeworkEditorSerial=++homeworkSerialCounter;editingSharedHomework=row.Item.HomeworkId;sharedHomeworkRevision=row.Item.Revision;SharedHomeworkTitle=row.Item.Title;SharedHomeworkBody=row.Item.Body;SharedHomeworkDeadline=row.Item.DeadlineAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm")??"";
        sharedHomeworkOperationId=Guid.NewGuid();RestoreHomeworkAudience(new(row.Item.Audience.Kind=="selected"?1:0,string.Join(',',row.Item.Audience.RoleIds),string.Join(',',row.Item.Audience.UserIds)));SaveHomeworkDraft();}
    [RelayCommand] private void NewSharedHomework() {if(IsBusy||sharedHomeworkPending)return;ResetSharedHomework();}
    private void ResetSharedHomework() {updatingHomeworkRecipients=true;homeworkEditorSerial=++homeworkSerialCounter;editingSharedHomework=null;sharedHomeworkRevision=0;SharedHomeworkTitle=SelectedChannel?.Subject??"";SharedHomeworkBody="";SharedHomeworkDeadline="";sharedHomeworkOperationId=Guid.NewGuid();sharedHomeworkPending=false;RestoreHomeworkAudience(new(0,"",""));SaveHomeworkDraft();}
    [RelayCommand] private Task SaveSharedHomework()
    {
        if(!CanSaveSharedHomework || activeHomeworkDraft is not {} key)return Task.CompletedTask;
        var submitted=CurrentHomeworkDraft();var serial=homeworkEditorSerial;homeworkDrafts[key]=submitted;HomeworkUpsert request;
        try{request=new(submitted.Title.Trim(),submitted.Body.Trim(),submitted.Revision,ParseDeadline(submitted.Deadline),key.Topic==Guid.Empty?null:key.Topic,
            Features.Homeworks.HomeworkAudienceSelection.PayloadFor(submitted.Audience,HomeworkRecipients.Supported),submitted.Id is null&&HomeworkRecipients.Supported?submitted.OperationId:null);}
        catch(Exception ex) when(ex is ArgumentException or InvalidOperationException){Status="Проверьте название, текст, получателей и срок: дд.мм.гггг чч:мм.";return Task.CompletedTask;}
        return SpaceAction(async(api,t,c,ct)=>
        {
            HomeworkResponse saved;
            if(submitted.Id is null&&request.OperationId is not null){sharedHomeworkPending=true;SaveHomeworkDraft();}
            saved=submitted.Id is {} id?await api.UpdateHomeworkAsync(t,c,id,request,ct):await api.ShareHomeworkAsync(t,c,request,ct);
            if(homeworkDrafts.TryGetValue(key,out var stored) && stored.Serial==submitted.Serial && stored.Id==submitted.Id && stored.Revision==submitted.Revision)
            {
                if(SameHomeworkInput(stored,submitted))homeworkDrafts.Remove(key);
                else homeworkDrafts[key]=stored with{Id=saved.HomeworkId,Revision=saved.Revision};
            }
            if(!CurrentSpace() || activeHomeworkDraft!=key)return;
            var copies=await api.ListHomeworkCopiesAsync(t,c,ct,key.Topic);
            if(!CurrentSpace() || activeHomeworkDraft!=key)return;ApplyChannelHomeworks(copies);
            if(homeworkEditorSerial==serial){if(SameHomeworkInput(CurrentHomeworkDraft(),submitted)){sharedHomeworkPending=false;ResetSharedHomework();}else{editingSharedHomework=saved.HomeworkId;sharedHomeworkRevision=saved.Revision;sharedHomeworkPending=false;}}SaveHomeworkDraft();
        },true);
    }
    private int channelScheduleGeneration;
    public ObservableCollection<SpaceScheduleDate> ChannelScheduleDates{get;}=[];
    [ObservableProperty] private string channelScheduleSummary="";
    [ObservableProperty] private string channelScheduleNextCaption="";
    private DateTime? channelScheduleNext;
    partial void OnChannelScheduleDateChanged(DateTime? value) {if(ShowChannelSchedule)_=RefreshChannelSchedule();}
    [RelayCommand] private void ChannelToday()=>ChannelScheduleDate=DateTime.Today;
    [RelayCommand] private void ChannelTomorrow()=>ChannelScheduleDate=DateTime.Today.AddDays(1);
    [RelayCommand] private void ChannelAfterTomorrow()=>ChannelScheduleDate=DateTime.Today.AddDays(2);
    [RelayCommand] private void ChannelPrevious(){if(ChannelScheduleDate is {} date && date>DateTime.MinValue.Date)ChannelScheduleDate=date.AddDays(-1);}
    [RelayCommand] private void ChannelNext(){if(ChannelScheduleDate is {} date && date<DateTime.MaxValue.Date)ChannelScheduleDate=date.AddDays(1);}
    [RelayCommand] private void ChannelNextStudy(){if(channelScheduleNext is {} date)ChannelScheduleDate=date;}
    [RelayCommand] private async Task RefreshChannelSchedule()
    {
        var day=ChannelScheduleDate??DateTime.Today;var community=communityId;var topic=selectedTopicId;var ticket=navigationGeneration;
        var generation=++channelScheduleGeneration;var title=HomeTitle;
        var model=await RunAsync(()=>
        {
            var group=App.Db.GetAllGroups().FirstOrDefault(x=>x.Name==title||"Группа "+x.Name==title);
            return group is null?null!:new Features.Schedule.ScheduleComposer(App,group.Id).Compose((day.Date-DateTime.Today).Days,DateTime.Now);
        },"group schedule");
        if(!CanPublish || communityId!=community || selectedTopicId!=topic || navigationGeneration!=ticket || generation!=channelScheduleGeneration || !ShowChannelSchedule || ChannelScheduleDate!=day)return;
        ChannelSchedule.Clear();ChannelScheduleDates.Clear();channelScheduleNext=model?.NextStudyDate;
        ChannelScheduleNextCaption=channelScheduleNext is {} next?$"К занятиям {next:dd.MM.yyyy}":"";
        if(model is null){ChannelScheduleSummary="Расписание этой группы ещё не сохранено.";return;}
        ChannelScheduleSummary=$"{model.Date:dddd, dd.MM.yyyy} · {model.Summary} · {model.SourceSummary}";
        foreach(var date in model.Dates??[])ChannelScheduleDates.Add(new(date.Date,date.LessonCount,date.Date.Date==day.Date,()=>{ChannelScheduleDate=date.Date;_=RefreshChannelSchedule();}));
        if(model.IsUnavailable || model.Rows.Count==0){ChannelSchedule.Add(model.EmptyTitle??"Нет данных");return;}
        var gapIndex=0;var gaps=(model.Breaks??[]).OrderBy(x=>x.End).ToArray();
        foreach(var row in model.Rows)
        {
            if(App.Prefs.ShowFreeTime && TimeSpan.TryParse(row.TimeStart,out var start))while(gapIndex<gaps.Length && gaps[gapIndex].End<=start)ChannelSchedule.Add(new Features.Schedule.PlannerBreak(gaps[gapIndex++]).Label);
            ChannelSchedule.Add((row.IsNext?(day.Date>DateTime.Today?"Первая пара · ":"Текущая или следующая · "):"")+$"{row.TimeStart}–{row.TimeEnd} · {row.DisplayName}\n{row.TypeLabel} · {row.RoomText} · {row.Teacher}"+(row.HasConflict?" · Пересечение времени":""));
        }
    }

}

public sealed partial class SpaceQuestionEditor : ObservableObject
{
    public Guid Id{get;}=Guid.NewGuid();
    public static IReadOnlyList<SpaceChoice> Kinds{get;}=[new("shortText","Короткий текст"),new("longText","Развёрнутый текст"),new("singleChoice","Один вариант"),new("multipleChoice","Несколько вариантов")];
    public IReadOnlyList<SpaceChoice> QuestionKinds=>Kinds;
    [ObservableProperty] private string title="";
    [ObservableProperty] private SpaceChoice kind=Kinds[0];
    [ObservableProperty] private bool required=true;
    [ObservableProperty] private string optionsText="";
    public bool HasChoices=>Kind.Code is "singleChoice" or "multipleChoice";
    partial void OnKindChanged(SpaceChoice value)=>OnPropertyChanged(nameof(HasChoices));
    public GroupFormQuestion Question()=>new(Id,Title.Trim(),Kind.Code,Required,HasChoices?OptionsText.Split('\n',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct().ToArray():[]);
}
public sealed class SpaceFormRow : ObservableObject
{
    private string answerBaseline;
    private bool canRespond;
    private bool responseLoading;
    private bool responsesComplete;
    public SpaceFormRow(GroupFormResponse form,bool writable,Func<SpaceFormRow,Task> submit,Func<SpaceFormRow,Task> read,Func<SpaceFormRow,Task> export)
    {Form=form;canRespond=writable&&form.CanRespond;Questions=form.Questions.Select(q=>new SpaceAnswerRow(q,form.OwnResponse?.Answers.FirstOrDefault(x=>x.QuestionId==q.QuestionId))).ToArray();answerBaseline=Fingerprint();SubmitCommand=new AsyncRelayCommand(()=>submit(this));ResponsesCommand=new AsyncRelayCommand(()=>read(this));ExportCommand=new AsyncRelayCommand(()=>export(this));}
    public GroupFormResponse Form{get;private set;} public string Title=>Form.Title;public string Description=>Form.Description;
    public string Summary=>$"Ответов: {Form.ResponseCount} · "+(Form.Anonymous?"Анонимная · ":"")+(Form.DeadlineAt is {} deadline?$"До {deadline.ToLocalTime():dd.MM.yyyy HH:mm}":"Без срока")+(Form.OwnResponse is null?"":" · Ваш ответ сохранён");
    public bool CanRespond=>canRespond;public bool CanViewResponses=>Form.CanViewResponses;
    public bool HasUnsavedAnswers=>Fingerprint()!=answerBaseline;
    public bool ResponseLoading{get=>responseLoading;set=>SetProperty(ref responseLoading,value);}
    public bool ResponsesComplete{get=>responsesComplete;set=>SetProperty(ref responsesComplete,value);}
    internal Task? ResponseLoadTask{get;set;}
    public IReadOnlyList<SpaceAnswerRow> Questions{get;}
    public ObservableCollection<string> Responses{get;}=[];public List<GroupFormAnswerResponse> RawResponses{get;}=[];
    public IAsyncRelayCommand SubmitCommand{get;}public IAsyncRelayCommand ResponsesCommand{get;}public IAsyncRelayCommand ExportCommand{get;}
    public GroupFormAnswerRequest Answers()=>new(Questions.Select(x=>x.Answer()).ToArray());
    private string Fingerprint()=>System.Text.Json.JsonSerializer.Serialize(Answers());
    internal void RestoreAnswers(GroupFormAnswerRequest request){foreach(var row in Questions)row.Restore(request.Answers.FirstOrDefault(x=>x.QuestionId==row.Id));}
    internal void Update(GroupFormResponse form,bool writable,bool forceOwnResponse=false)
    {
        var dirty=HasUnsavedAnswers;Form=form;canRespond=writable&&form.CanRespond;
        if(forceOwnResponse || !dirty)RestoreAnswers(new(form.OwnResponse?.Answers??[]));
        answerBaseline=System.Text.Json.JsonSerializer.Serialize(new GroupFormAnswerRequest(form.Questions.Select(q=>new SpaceAnswerRow(q,form.OwnResponse?.Answers.FirstOrDefault(x=>x.QuestionId==q.QuestionId)).Answer()).ToArray()));
        if(!form.CanViewResponses){Responses.Clear();RawResponses.Clear();ResponsesComplete=false;}
        foreach(var name in new[]{nameof(Title),nameof(Description),nameof(Summary),nameof(CanRespond),nameof(CanViewResponses),nameof(HasUnsavedAnswers)})OnPropertyChanged(name);
    }
    internal void CopyUnsavedAnswers(SpaceFormRow old){if(old.HasUnsavedAnswers)RestoreAnswers(old.Answers());}
    internal void Revoke(){canRespond=false;Form=Form with{CanRespond=false,CanViewResponses=false,OwnResponse=null};RestoreAnswers(new([]));Responses.Clear();RawResponses.Clear();ResponsesComplete=false;}
}
public sealed partial class SpaceAnswerRow : ObservableObject
{
    public SpaceAnswerRow(GroupFormQuestion question,GroupFormAnswer? answer)
    {Id=question.QuestionId;Title=question.Title+(question.Required?" *":"");Required=question.Required;IsText=question.Kind is "shortText" or "longText";IsLong=question.Kind=="longText";text=answer?.Text??"";Options=question.Options.Select(x=>new SpaceAnswerChoice(x,answer?.Choices.Contains(x)==true,()=>SelectSingle(x),question.Kind=="singleChoice")).ToArray();}
    public Guid Id{get;} public string Title{get;}public bool Required{get;}public bool IsText{get;}public bool IsLong{get;}
    [ObservableProperty] private string text;
    public IReadOnlyList<SpaceAnswerChoice> Options{get;}
    private void SelectSingle(string label){foreach(var choice in Options.Where(x=>x.Label!=label))choice.Selected=false;}
    internal void Restore(GroupFormAnswer? answer){Text=answer?.Text??"";foreach(var option in Options)option.Selected=answer?.Choices.Contains(option.Label)==true;}
    public GroupFormAnswer Answer()=>new(Id,IsText?Text.Trim():null,IsText?[]:Options.Where(x=>x.Selected).Select(x=>x.Label).ToArray());
}
public sealed partial class SpaceAnswerChoice(string label,bool selected,Action changed,bool single) : ObservableObject
{
    public string Label{get;}=label;
    [ObservableProperty] private bool selected=selected;
    partial void OnSelectedChanged(bool value){if(value&&single)changed();}
}
public sealed class SpaceHomeworkRow(GroupHomeworkCopyResponse item,bool writable,Func<SpaceHomeworkRow,Task> toggle,Action<SpaceHomeworkRow> edit,bool editable=false,string audience="")
{
    public GroupHomeworkCopyResponse Item{get;}=item;public string Title=>Item.Title;public string Body=>Item.Body;public bool Completed=>Item.Completed;
    public string Deadline=>Item.DeadlineAt is {} date?$"Срок: {date.ToLocalTime():dd.MM.yyyy HH:mm}":"Без срока";
    public string CompletionLabel=>Item.Completed?"Готово у меня · снять отметку":"Отметить готово у меня";
    public bool Writable=>writable&&Item.CanComplete;
    public bool Editable=>editable;
    public string AudienceLabel=>audience.Length>0?audience:Item.Audience.Kind=="all"?"Вся учебная группа":$"Подгрупп: {Item.Audience.RoleIds.Count} · участников: {Item.Audience.UserIds.Count}";
    public string CompletionHint=>Item.CanComplete?"Отметка видна только вам":"Задание назначено другим участникам";
    public IAsyncRelayCommand ToggleCommand{get;}=new AsyncRelayCommand(()=>toggle(new SpaceHomeworkRow(item,writable,toggle,edit,editable,audience)),()=>writable&&item.CanComplete);
    public IRelayCommand EditCommand{get;}=new RelayCommand(()=>edit(new SpaceHomeworkRow(item,writable,toggle,edit,editable,audience)),()=>editable);
}

public sealed class SpaceScheduleDate(DateTime date,int? count,bool selected,Action select)
{
    public string Label=>$"{date:ddd dd.MM} · "+(count is null?"Нет данных":count==0?"Без пар":$"{count} пар");
    public bool Selected=>selected;
    public IRelayCommand SelectCommand{get;}=new RelayCommand(select);
}
