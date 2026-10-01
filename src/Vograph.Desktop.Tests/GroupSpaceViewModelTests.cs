using System.Net;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Features.Groups;
using Vograph.Desktop.Services;
using Zapara.Contracts.Communities;
using Xunit;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;
// View models and fake HTTP only: these tests never create a view, window, frame or recorder.
public sealed class GroupSpaceViewModelTests
{
    [AvaloniaFact] public async Task Unknown_topics_do_not_request_messages_or_expose_a_composer()
    {
        using var fixture=new Fixture("unknown","futureTemplate");await fixture.Vm.ActivateAsync();
        Assert.True(fixture.Vm.ShowUnsupported);Assert.False(fixture.Vm.ShowComposer);Assert.Equal(0,fixture.MessageReads);
        fixture.Vm.Draft="Нельзя отправить";await fixture.Vm.SendCommand.ExecuteAsync(null);Assert.Equal(0,fixture.Writes);
    }
    [AvaloniaFact] public async Task Preview_preserves_read_access_but_disables_every_mutation()
    {
        using var fixture=new Fixture("chat","chat");await fixture.Vm.ActivateAsync();fixture.Vm.Draft="Мой текст";
        await fixture.Vm.PreviewPersonCommand.ExecuteAsync(null);Assert.True(fixture.Vm.PreviewMode);Assert.False(fixture.Vm.ShowComposer);
        var writes=fixture.Writes;await fixture.Vm.SendCommand.ExecuteAsync(null);await fixture.Vm.CreateTrustedRoleCommand.ExecuteAsync(null);await fixture.Vm.SaveCategoryCommand.ExecuteAsync(null);
        Assert.Equal(writes,fixture.Writes);Assert.Equal("Мой текст",fixture.Vm.Draft);
        await fixture.Vm.EndPreviewCommand.ExecuteAsync(null);Assert.False(fixture.Vm.PreviewMode);Assert.True(fixture.Vm.ShowComposer);
    }
    [AvaloniaFact] public async Task Discussion_context_can_be_removed_without_touching_the_channel_draft()
    {
        using var fixture=new Fixture("chat","chat");fixture.Vm.RequestDiscussion("Математика","Пара 29.09.2026 · 09:00 · Математика");await fixture.Vm.ActivateAsync();
        fixture.Vm.Draft="Давайте разберём задачу";Assert.True(fixture.Vm.HasDiscussionContext);fixture.Vm.ClearDiscussionContextCommand.Execute(null);
        Assert.False(fixture.Vm.HasDiscussionContext);Assert.Equal("Давайте разберём задачу",fixture.Vm.Draft);
    }
    [AvaloniaFact] public async Task Form_publication_appends_and_submission_restores_the_own_response()
    {
        using var fixture=new Fixture("forms","forms");await fixture.Vm.ActivateAsync();Assert.True(fixture.Vm.CanCreateForm);Assert.False(fixture.Vm.ShowComposer);
        for(var n=0;n<2;n++){fixture.Vm.FormTitle="Анкета "+n;fixture.Vm.AddFormQuestionCommand.Execute(null);fixture.Vm.FormQuestions[0].Title="Ваш ответ";await fixture.Vm.PublishFormCommand.ExecuteAsync(null);}
        Assert.Equal(2,fixture.Vm.Forms.Count);var first=fixture.Vm.Forms[0];first.Questions[0].Text="Сохраняемый ответ";
        await first.SubmitCommand.ExecuteAsync(null);Assert.Equal("Сохраняемый ответ",fixture.Vm.Forms[0].Questions[0].Text);Assert.NotNull(fixture.Vm.Forms[0].Form.OwnResponse);
    }
    [AvaloniaFact] public async Task Draft_questions_move_with_stable_ids_and_filled_delete_requires_exact_confirmation()
    {
        using var fixture=new Fixture("forms","forms");await fixture.Vm.ActivateAsync();
        fixture.Vm.AddFormQuestionCommand.Execute(null);fixture.Vm.AddFormQuestionCommand.Execute(null);
        var first=fixture.Vm.FormQuestions[0];var second=fixture.Vm.FormQuestions[1];
        first.Title="Первый";second.Title="Второй";second.Kind=SpaceQuestionEditor.Kinds[2];second.OptionsText="А\nБ";
        Assert.False(first.CanMoveUp);Assert.False(second.CanMoveDown);
        fixture.Vm.MoveFormQuestionUpCommand.Execute(second);
        Assert.Same(second,fixture.Vm.FormQuestions[0]);Assert.Same(first,fixture.Vm.FormQuestions[1]);
        Assert.Equal(second.Id,fixture.Vm.FormQuestions[0].Question().QuestionId);
        fixture.Vm.RemoveFormQuestionCommand.Execute(second);
        Assert.True(fixture.Vm.HasPendingRemoveFormQuestion);Assert.Equal(2,fixture.Vm.FormQuestions.Count);
        fixture.Vm.CancelRemoveFormQuestionCommand.Execute(null);Assert.Equal(2,fixture.Vm.FormQuestions.Count);
        fixture.Vm.RemoveFormQuestionCommand.Execute(second);fixture.Vm.ConfirmRemoveFormQuestionCommand.Execute(null);
        Assert.Same(first,Assert.Single(fixture.Vm.FormQuestions));
    }
    [AvaloniaFact] public async Task Materials_load_one_page_then_wait_for_explicit_load_more()
    {
        using var fixture=new Fixture("materials","materials") { MessageHasMore=true };
        await fixture.Vm.ActivateAsync();
        Assert.Equal(1,fixture.MessageReads);Assert.True(fixture.Vm.HasMore);Assert.True(fixture.Vm.MaterialsLoaded);
        await fixture.Vm.LoadOlderCommand.ExecuteAsync(null);
        Assert.Equal(2,fixture.MessageReads);
    }
    [AvaloniaFact] public async Task Materials_failure_offers_retry_and_never_looks_like_a_successful_empty_page()
    {
        using var fixture=new Fixture("materials","materials") { FailMessagesGet=true };
        await fixture.Vm.ActivateAsync();
        Assert.True(fixture.Vm.MaterialsLoadFailed);Assert.False(fixture.Vm.NoMaterials);
        fixture.FailMessagesGet=false;
        await fixture.Vm.RetryMaterialsCommand.ExecuteAsync(null);
        Assert.True(fixture.Vm.MaterialsLoaded);Assert.False(fixture.Vm.MaterialsLoadFailed);Assert.True(fixture.Vm.NoMaterials);
    }
    [AvaloniaFact] public async Task Materials_refresh_failure_keeps_the_last_loaded_page()
    {
        using var fixture=new Fixture("materials","materials") { MessageHasMore=true };
        await fixture.Vm.ActivateAsync();
        var prior=Assert.Single(fixture.Vm.Messages);
        fixture.FailMessagesGet=true;
        await fixture.Vm.RetryMaterialsCommand.ExecuteAsync(null);
        Assert.True(fixture.Vm.MaterialsLoadFailed);Assert.Same(prior,Assert.Single(fixture.Vm.Messages));
        Assert.Contains("сохранённая",fixture.Vm.MaterialsErrorText);
    }
    [AvaloniaFact] public async Task Role_editor_keeps_each_roles_draft_separate_across_selection()
    {
        using var fixture=new Fixture("chat","chat") { AdditionalRole=true };await fixture.Vm.ActivateAsync();
        var a=fixture.Vm.TrustedRoles.Single(row=>row.Name=="Помощник");
        var b=fixture.Vm.TrustedRoles.Single(row=>row.Name=="Вторая роль");
        fixture.Vm.SelectedTrustedRole=a;fixture.Vm.RoleEditName="Мой черновик A";
        fixture.Vm.SelectedTrustedRole=b;
        Assert.Equal("Вторая роль",fixture.Vm.RoleEditName);
        fixture.Vm.RoleEditName="Мой черновик B";
        fixture.Vm.SelectedTrustedRole=a;
        Assert.Equal("Мой черновик A",fixture.Vm.RoleEditName);
        fixture.Vm.SelectedTrustedRole=b;
        Assert.Equal("Мой черновик B",fixture.Vm.RoleEditName);
    }
    [AvaloniaFact] public async Task Category_delete_names_affected_topic_and_requires_exact_confirmation()
    {
        using var fixture=new Fixture("chat","chat");fixture.EnableCategory();await fixture.Vm.ActivateAsync();
        fixture.Vm.SelectedCategory=Assert.Single(fixture.Vm.Categories);
        fixture.Vm.DeleteCategoryCommand.Execute(null);
        Assert.True(fixture.Vm.HasPendingDeleteCategory);Assert.Contains("списке тем: 1",fixture.Vm.DeleteCategoryImpact);
        Assert.Equal(0,fixture.CategoryDeletes);
        fixture.Vm.CancelDeleteCategoryCommand.Execute(null);Assert.Equal(0,fixture.CategoryDeletes);
        fixture.Vm.DeleteCategoryCommand.Execute(null);await fixture.Vm.ConfirmDeleteCategoryCommand.ExecuteAsync(null);
        Assert.Equal(1,fixture.CategoryDeletes);Assert.Single(fixture.Vm.Channels.Where(item=>item.TopicId==fixture.Topic));
        Assert.Null(fixture.Vm.Channels.Single(item=>item.TopicId==fixture.Topic).CategoryId);
    }
    [AvaloniaTheory]
    [InlineData("chat","chat")][InlineData("announcements","chat")][InlineData("polls","ballots")][InlineData("forms","forms")]
    [InlineData("subject","chat")][InlineData("materials","materials")][InlineData("homework","homework")][InlineData("schedule","schedule")]
    public async Task All_eight_templates_use_their_real_contract(string template,string kind)
    {
        using var fixture=new Fixture("chat","chat");await fixture.Vm.ActivateAsync();fixture.Vm.ChannelTitle="Новая тема";fixture.Vm.ChannelSubject="Математика";fixture.Vm.SelectedTemplate=GroupViewModel.Templates.Single(x=>x.Code==template);
        await fixture.Vm.CreateChannelCommand.ExecuteAsync(null);
        Assert.Equal(template,fixture.CreatedTemplate);Assert.Equal(kind,fixture.CreatedKind);Assert.Equal(template=="subject"?"Математика":null,fixture.CreatedSubject);
        Assert.Equal("Новая тема",fixture.Vm.SelectedChannel!.Title);Assert.Equal(kind,fixture.Vm.SelectedChannel.Kind);
    }
    [AvaloniaFact] public async Task Access_draft_preview_requires_confirmation_and_is_invalidated_by_rule_edits()
    {
        using var fixture=new Fixture("chat","chat");await fixture.Vm.ActivateAsync();await fixture.Vm.LoadAccessCommand.ExecuteAsync(null);
        var read=fixture.Vm.AccessRules.Single(x=>x.RoleId is null&&x.Power=="read");read.State=SpaceAccessRow.States[1];
        await fixture.Vm.SaveAccessCommand.ExecuteAsync(null);Assert.Equal(0,fixture.AccessSaves);
        await fixture.Vm.PreviewProposedAccessCommand.ExecuteAsync(null);
        Assert.True(fixture.Vm.CanSaveProposedAccess);Assert.Contains("1 участников",fixture.Vm.AccessPreviewSummary);Assert.Single(fixture.Vm.AccessPreviewPeople);
        Assert.Contains("Базовое право",fixture.Vm.AccessPreviewPeople[0].Reasons[0].Source);
        read.State=SpaceAccessRow.States[2];Assert.False(fixture.Vm.AccessPreviewReady);Assert.Empty(fixture.Vm.AccessPreviewPeople);
        await fixture.Vm.SaveAccessCommand.ExecuteAsync(null);Assert.Equal(0,fixture.AccessSaves);
        await fixture.Vm.PreviewProposedAccessCommand.ExecuteAsync(null);await fixture.Vm.SaveAccessCommand.ExecuteAsync(null);
        Assert.Equal(1,fixture.AccessSaves);Assert.Equal(1,fixture.SavedAccess!.ExpectedRevision);Assert.Equal("deny",fixture.SavedAccess.Rules.Single(x=>x.RoleId is null&&x.Power=="read").State);
    }
    [AvaloniaFact] public async Task Access_save_conflict_refreshes_revision_but_preserves_the_unsaved_rules()
    {
        using var fixture=new Fixture("chat","chat");await fixture.Vm.ActivateAsync();await fixture.Vm.LoadAccessCommand.ExecuteAsync(null);
        var read=fixture.Vm.AccessRules.Single(x=>x.RoleId is null&&x.Power=="read");read.State=SpaceAccessRow.States[1];
        await fixture.Vm.PreviewProposedAccessCommand.ExecuteAsync(null);fixture.FailAccessSaveOnce=true;await fixture.Vm.SaveAccessCommand.ExecuteAsync(null);
        Assert.Equal("allow",read.State.Code);Assert.Contains(read,fixture.Vm.AccessRules);Assert.False(fixture.Vm.CanSaveProposedAccess);Assert.False(fixture.Vm.AccessPreviewReady);Assert.Equal(0,fixture.AccessSaves);
        Assert.Contains("черновик сохранён",fixture.Vm.Status);await fixture.Vm.SaveAccessCommand.ExecuteAsync(null);Assert.Equal(0,fixture.AccessSaves);
        await fixture.Vm.PreviewProposedAccessCommand.ExecuteAsync(null);Assert.Equal(2,fixture.PreviewRevision);await fixture.Vm.SaveAccessCommand.ExecuteAsync(null);
        Assert.Equal(1,fixture.AccessSaves);Assert.Equal(2,fixture.SavedAccess!.ExpectedRevision);Assert.Equal("allow",fixture.SavedAccess.Rules.Single(x=>x.RoleId is null&&x.Power=="read").State);
    }
    [AvaloniaFact] public async Task A_late_access_preview_cannot_confirm_a_newer_draft()
    {
        using var fixture=new Fixture("chat","chat");await fixture.Vm.ActivateAsync();await fixture.Vm.LoadAccessCommand.ExecuteAsync(null);
        fixture.HoldAccessPreview=true;var preview=fixture.Vm.PreviewProposedAccessCommand.ExecuteAsync(null);
        await fixture.PreviewStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.Vm.AccessRules[0].State=SpaceAccessRow.States[2];fixture.PreviewRelease.TrySetResult();await preview;
        Assert.False(fixture.Vm.AccessPreviewReady);Assert.False(fixture.Vm.CanSaveProposedAccess);Assert.Empty(fixture.Vm.AccessPreviewPeople);
    }
    [AvaloniaFact] public async Task Changing_topics_clears_the_access_draft_and_its_confirmation()
    {
        using var fixture=new Fixture("chat","chat");await fixture.Vm.ActivateAsync();await fixture.Vm.LoadAccessCommand.ExecuteAsync(null);await fixture.Vm.PreviewProposedAccessCommand.ExecuteAsync(null);
        Assert.True(fixture.Vm.AccessPreviewReady);fixture.Vm.ChannelTitle="Другая тема";await fixture.Vm.CreateChannelCommand.ExecuteAsync(null);
        Assert.False(fixture.Vm.AccessPreviewReady);Assert.Empty(fixture.Vm.AccessRules);Assert.Empty(fixture.Vm.AccessPreviewPeople);
    }
    [AvaloniaFact] public async Task Dirty_role_editor_survives_polling_and_requires_an_explicit_reload_after_conflict()
    {
        using var fixture=new Fixture("chat","chat");await fixture.Vm.ActivateAsync();fixture.Vm.RoleEditName="Мой вариант";fixture.ChangeRoleExternally("Чужая правка");fixture.Vm.Watch(true);
        await Waits.Until(()=>fixture.Vm.RoleEditConflict,"role conflict refreshed",6500);
        Assert.Equal("Мой вариант",fixture.Vm.RoleEditName);Assert.False(fixture.Vm.CanSaveRoleSettings);await fixture.Vm.SaveRoleSettingsCommand.ExecuteAsync(null);Assert.Null(fixture.SavedRole);
        fixture.Vm.ReloadRoleSettingsCommand.Execute(null);Assert.Equal("Чужая правка",fixture.Vm.RoleEditName);fixture.Vm.RoleEditName="Согласованный вариант";
        await fixture.Vm.SaveRoleSettingsCommand.ExecuteAsync(null);Assert.Equal(2,fixture.SavedRole!.ExpectedRevision);Assert.False(fixture.Vm.RoleEditConflict);Assert.Equal("Согласованный вариант",fixture.Vm.RoleEditName);
    }
    [AvaloniaFact] public async Task Role_save_sends_the_initial_revision_even_when_the_server_has_advanced()
    {
        using var fixture=new Fixture("chat","chat");await fixture.Vm.ActivateAsync();fixture.Vm.RoleEditName="Мой вариант";fixture.ChangeRoleExternally("Чужая правка");
        await fixture.Vm.SaveRoleSettingsCommand.ExecuteAsync(null);Assert.Equal(1,fixture.SavedRole!.ExpectedRevision);Assert.Equal("Мой вариант",fixture.Vm.RoleEditName);Assert.NotEmpty(fixture.Vm.Status);
    }
    [AvaloniaFact] public async Task Pin_permission_can_toggle_only_pinning_without_channel_management()
    {
        using var fixture=new Fixture("chat","chat");fixture.MakePinOnly();await fixture.Vm.ActivateAsync();Assert.False(fixture.Vm.CanManageChannels);Assert.True(fixture.Vm.CanPinSelected);
        var original=fixture.Vm.SelectedChannel!;await fixture.Vm.ToggleTopicPinCommand.ExecuteAsync(null);
        Assert.NotNull(fixture.RenamedTopic);Assert.True(fixture.RenamedTopic.Pinned);Assert.Equal("Тема",fixture.RenamedTopic.Title);Assert.Equal("chat",fixture.RenamedTopic.Kind);Assert.Equal(1,fixture.RenamedTopic.ExpectedRevision);
        Assert.True(fixture.Vm.SelectedChannel!.Pinned);Assert.False(fixture.Vm.CanManageChannels);
    }
    [AvaloniaFact] public async Task Archive_history_remains_selected_when_the_active_space_is_polled()
    {
        using var fixture=new Fixture("chat","chat");fixture.ArchiveTopic();await fixture.Vm.ActivateAsync();await fixture.Vm.LoadArchiveCommand.ExecuteAsync(null);
        Assert.Single(fixture.Vm.ArchivedChannels).OpenCommand.Execute(null);await Waits.Until(()=>fixture.Vm.SelectedChannel?.Archived==true && fixture.MessageReads>0,"archive opened");fixture.Vm.Watch(true);
        await Waits.Until(()=>fixture.ActiveSpaceReads>=2,"active space refreshed",6500);
        Assert.True(fixture.Vm.SelectedChannel!.Archived);Assert.Equal("Тема",fixture.Vm.ChatTitle);Assert.False(fixture.Vm.ShowComposer);
    }
    internal sealed class Fixture:IDisposable
    {
        private readonly ProfileTestDirectory directory=new();private readonly AppServices app;private readonly AccountClientHandler handler=new();private readonly HttpClient http;private readonly CommunityHttpClient client;
        private readonly Guid topic=Guid.NewGuid();private readonly Guid conversation=Guid.NewGuid();private readonly Guid role=Guid.NewGuid();private readonly Guid secondRole=Guid.NewGuid();private readonly Guid category=Guid.NewGuid();private readonly Guid person=Guid.NewGuid();
        private readonly List<GroupTopicResponse> topics=[];private readonly List<GroupFormResponse> forms=[];
        public GroupViewModel Vm{get;}public int MessageReads;public bool MessageHasMore;public bool FailMessagesGet;public int Writes;public string? CreatedKind;public string? CreatedTemplate;public string? CreatedSubject;
        public int ActiveSpaceReads;
        public bool AdditionalRole;
        private bool categoryActive;public int CategoryDeletes;
        public void EnableCategory()
        { categoryActive=true;var old=topics[0];topics[0]=new(topic,old.Title,old.Icon,null,null,null,0,true,old.Kind,template:old.Template,categoryId:category,permissions:["read","post","media","channels"]); }
        public void ArchiveTopic(){var row=topics[0];topics[0]=new(row.TopicId,row.Title,row.Icon,null,null,null,0,true,row.Kind,template:row.Template,archived:true,canPost:false,permissions:["read"]);}
        public bool FailAccessSaveOnce;private long accessRevision=1;public long PreviewRevision;
        public int AccessSaves;public GroupTopicAccessRequest? SavedAccess;public GroupRoleSettingsRequest? SavedRole;public GroupTopicRequest? RenamedTopic;
        public bool HoldAccessPreview;public TaskCompletionSource PreviewStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource PreviewRelease=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string roleName="Помощник";private long roleRevision=1;private bool pinOnly;
        public void ChangeRoleExternally(string name){roleName=name;roleRevision++;}
        public void MakePinOnly(){pinOnly=true;var row=topics[0];topics[0]=new(row.TopicId,row.Title,row.Icon,null,null,null,0,false,row.Kind,template:row.Template,permissions:["read","post","pin"]);}
        public Fixture(string kind,string template,string? apiBaseUrl=null)
        {
            app=AppServices.Create(directory.Root,()=>false,apiBaseUrl:apiBaseUrl);app.AllowNetwork=false;http=new(handler);client=new(http,Root);app.UseCommunities(client,_=>Task.FromResult<string?>(Access));
            topics.Add(new(topic,"Тема","user",null,null,null,0,true,kind,template:template,permissions:["read","post","media","vote","forms","formsRespond","homework","ballots","close","channels","access","pin"]));
            handler.Send=Route;Vm=new(app);
        }
        private GroupDeskResponse Desk()=>new(!pinOnly,AdditionalRole?[new(role,roleName,1,"user",roleRevision),new(secondRole,"Вторая роль",2,"user",1)]:[new(role,roleName,1,"user",roleRevision)],[],[],[],pinOnly?["pin"]:["channels","access","roles","grants"]);
        private GroupSpaceResponse Space()=>new(topics.Where(x=>!x.Archived).ToArray(),categoryActive?[new GroupCategoryResponse(category,"Учёба",0,1)]:[],new(Powers:["read","post","forms","homework","roles","grants","access"]),Desk());
        public AppServices Services=>app;
        public Guid Topic=>topic;public Guid Conversation=>conversation;public Guid Person=>person;
        public List<GroupTopicResponse> TopicRows=>topics;public List<GroupFormResponse> FormRecords=>forms;
        public Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage?>>? Intercept;
        private async Task<HttpResponseMessage> Route(HttpRequestMessage request,CancellationToken ct)
        {
            var path=request.RequestUri!.AbsolutePath;
            if(request.Method!=HttpMethod.Get)Writes++;
            if(Intercept is not null && await Intercept(request,ct) is {} intercepted)return intercepted;
            if(path.EndsWith("/communities",StringComparison.Ordinal))return Payload(new[]{Membership});
            if(path.EndsWith("/home",StringComparison.Ordinal))return Payload(new GroupHomeResponse(CommunityId,"Группа О3313","О3313",new(conversation,"group",CommunityId,"Группа О3313",null,null,null,0),[new(AccountClientTestSupport.UserId,"anya","Аня","headman",true),new(person,"boris","Борис","member",false)],[]));
            if(path.EndsWith("/space",StringComparison.Ordinal)){ActiveSpaceReads++;return Payload(Space());}
            if(path.EndsWith("/space/categories/"+category.ToString("D")+"/delete",StringComparison.Ordinal))
            { CategoryDeletes++;categoryActive=false;var old=topics[0];topics[0]=new(topic,old.Title,old.Icon,null,null,null,0,true,old.Kind,template:old.Template,permissions:["read","post","media","channels"]);return Payload(Space()); }
            if(path.EndsWith("/space/archive",StringComparison.Ordinal))return Payload(new GroupTopicListResponse(topics.Where(x=>x.Archived).ToArray(),true));
            if(path.EndsWith("/desk",StringComparison.Ordinal))return Payload(Desk());
            if(path.EndsWith("/messages",StringComparison.Ordinal))
            {
                MessageReads++;
                if(FailMessagesGet)return Problem(503,"unavailable");
                return MessageHasMore && !request.RequestUri.Query.Contains("before",StringComparison.Ordinal)
                    ? Payload(new ChatPageResponse([new(Guid.NewGuid(),conversation,person,"Борис","Материал",DateTimeOffset.UtcNow)],true))
                    : Payload(new ChatPageResponse([],false));
            }
            if(path.EndsWith("/read",StringComparison.Ordinal))return Payload(new ConversationResponse(conversation,"group",CommunityId,"Группа О3313",null,null,null,0));
            if(path.EndsWith("/space/preview",StringComparison.Ordinal))return Payload(new GroupPermissionPreviewResponse(topics.ToArray()));
            if(path.EndsWith("/access-preview",StringComparison.Ordinal))
            {
                var requestBody=JsonSerializer.Deserialize<GroupTopicAccessRequest>(await request.Content!.ReadAsStringAsync(ct),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                PreviewRevision=requestBody.ExpectedRevision;PreviewStarted.TrySetResult();if(HoldAccessPreview)await PreviewRelease.Task.WaitAsync(ct);
                return Payload(new GroupTopicAccessPreviewResponse(topic,requestBody.ExpectedRevision,1,[],[person],[new(person,[],["read"],new Dictionary<string,string>{{"read","Базовое право участника; предложенное правило разрешает чтение."}})]));
            }
            if(path.EndsWith("/access",StringComparison.Ordinal))
            {
                if(request.Method==HttpMethod.Get)return Payload(new GroupTopicAccessResponse(topic,accessRevision,[]));
                SavedAccess=JsonSerializer.Deserialize<GroupTopicAccessRequest>(await request.Content!.ReadAsStringAsync(ct),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;if(FailAccessSaveOnce){FailAccessSaveOnce=false;accessRevision++;return Problem(409,"revision_conflict");}AccessSaves++;return Payload(Space());
            }
            if(path.EndsWith("/space/roles/"+role.ToString("D"),StringComparison.Ordinal))
            {
                SavedRole=JsonSerializer.Deserialize<GroupRoleSettingsRequest>(await request.Content!.ReadAsStringAsync(ct),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                if(SavedRole.ExpectedRevision!=roleRevision)return Problem(409,"revision_conflict");roleName=SavedRole.Name;roleRevision++;return Payload(Desk());
            }
            if(path.EndsWith("/topics/"+topic.ToString("D"),StringComparison.Ordinal))
            {
                RenamedTopic=JsonSerializer.Deserialize<GroupTopicRequest>(await request.Content!.ReadAsStringAsync(ct),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;var body=RenamedTopic;
                topics[0]=new(topic,body.Title,body.Icon,null,null,null,0,!pinOnly,body.Kind,description:body.Description??"",accent:body.Accent??"default",pinned:body.Pinned??false,writePolicy:body.WritePolicy??"all",template:body.Template,categoryId:body.CategoryId,position:body.Position,subject:body.Subject,revision:2,permissions:pinOnly?["read","post","pin"]:["read","post","pin","channels"]);
                return Payload(new GroupTopicListResponse(topics.ToArray(),!pinOnly));
            }
            if(path.EndsWith("/topics",StringComparison.Ordinal))
            {
                if(request.Method==HttpMethod.Get)return Payload(new GroupTopicListResponse(topics,true));
                var body=JsonSerializer.Deserialize<GroupTopicRequest>(await request.Content!.ReadAsStringAsync(ct),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                CreatedKind=body.Kind;CreatedTemplate=body.Template;CreatedSubject=body.Subject;
                topics.Add(new(Guid.NewGuid(),body.Title,body.Icon,null,null,null,0,true,body.Kind,template:body.Template,subject:body.Subject,permissions:["read","post","media"]));return Payload(new GroupTopicListResponse(topics.ToArray(),true),HttpStatusCode.Created);
            }
            if(path.EndsWith("/forms",StringComparison.Ordinal))
            {
                if(request.Method==HttpMethod.Post){var body=JsonSerializer.Deserialize<GroupFormRequest>(await request.Content!.ReadAsStringAsync(ct),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;forms.Add(new(Guid.NewGuid(),topic,body.Title,body.Description,body.DeadlineAt,body.Anonymous,body.Questions,person,DateTimeOffset.UtcNow,true,true,null,0));}
                return Payload(new GroupFormListResponse(forms.ToArray()));
            }
            if(path.EndsWith("/response",StringComparison.Ordinal))
            {
                var id=Guid.Parse(path.Split('/')[^2]);var item=forms.Single(x=>x.FormId==id);var answer=JsonSerializer.Deserialize<GroupFormAnswerRequest>(await request.Content!.ReadAsStringAsync(ct),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                var changed=item with{OwnResponse=new(person,answer.Answers,DateTimeOffset.UtcNow),ResponseCount=1};forms[forms.IndexOf(item)]=changed;return Payload(changed);
            }
            if(path.EndsWith("/ballots",StringComparison.Ordinal))return Payload(new BallotBoardResponse(false,false,false,3,2,[]));
            if(path.EndsWith("/copies",StringComparison.Ordinal))return Payload(Array.Empty<GroupHomeworkCopyResponse>());
            return Problem(404,"not_found");
        }
        public void Dispose(){Vm.Detach();client.Dispose();http.Dispose();app.Dispose();directory.Dispose();}
    }
}
