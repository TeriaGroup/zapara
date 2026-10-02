using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Communities;
using Vograph.Core.Services.Accounts;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel
{
    private GroupSpaceResponse? space;
    private GroupTopicAccessResponse? accessBaseline;
    private bool legacySpace;
    private bool modernSpaceKnown;
    private readonly AsyncLocal<(Guid Community,int Generation)?> spaceScope=new();
    private bool CurrentSpace()=>CanPublish && (spaceScope.Value is not {} scope || communityId==scope.Community && navigationGeneration==scope.Generation);
    private static bool ReadDenied(CommunityClientException ex)=>ex.Failure is CommunityClientFailure.InvalidSession or CommunityClientFailure.Forbidden or CommunityClientFailure.NotFound;
    private void ClearRevokedContent()
    {
        ReleaseVisibleAvatars();
        navigationGeneration++;contextRequestGeneration++;ballotRequestSerial++;archiveRequestVersion++;archiveLoaded=false;
        conversationId=null;selectedTopicId=null;groupConversationId=null;SelectedChannel=null;
        ClearSubjectPanels();
        Messages.Clear();Ballots.Clear();ShowBallots=false;BallotLoaded=false;BallotLoading=false;BallotLoadFailed=false;
        Forms.Clear();ChannelHomeworks.Clear();ChannelSchedule.Clear();SubjectHomeworks.Clear();SubjectLesson="";
        ArchivedChannels.Clear();AuditEvents.Clear();Categories.Clear();Channels.Clear();People.Clear();Directs.Clear();Communities.Clear();RefreshCommunityBrowse();
        channelDrafts.Clear();discussionDrafts.Clear();ballotDrafts.Clear();ballotDraftRevisions.Clear();activeBallotDraftKey=null;
        roleEditDrafts.Clear();
        BallotQuestion="";BallotOptionA="";BallotOptionB="";BallotOptionC="";BallotOptionD="";BallotOptionE="";BallotOptionF="";BallotDays="3";
        SelectedCategory=null;CategoryTitle="";ChannelTitle="";ChannelDescription="";ChannelSubject="";TrustedRoleName="";
        formDrafts.Clear();formDraftQuestions.Clear();creationDrafts.Clear();activeCreationCommunity=null;CreationRoles.Clear();
        ClearSpecializedDrafts();ClearAccessEditor();ClearDesk();ResetGroupContext();RefreshCategories();RefreshChannelBrowse();RefreshPeopleBrowse();
        ClearPreviews();ClearRecordedDraft();_=CancelRecordingAsync();Draft="";DiscussionContext="";ShowSingleHomework=false;
        HomeTitle="";ChatTitle="";HasHome=false;IsDirect=false;IsEmpty=true;HasMore=false;PreviewMode=false;CanManageChannels=false;ShowBallotComposer=false;ShowChannelManagement=false;replyTo=null;editing=null;HoldCaption="";
        CancelDeleteMessage();CancelCloseBallot();requestedDiscussion=null;requestedHomework=null;
        lastNavigation=null;lastGroupChannels.Clear();Status="Доступ к каналу изменился";RestartTimer();NotifySpace();
    }
    private void PurgeTopicPrivate(Guid topic,bool clearCurrent=true)
    {
        if(clearCurrent){navigationGeneration++;contextRequestGeneration++;ballotRequestSerial++;}
        foreach(var closed in ArchivedChannels.Where(x=>x.TopicId==topic).ToArray())ArchivedChannels.Remove(closed);
        if(communityId is {} community)
        {
            foreach(var key in formRows.Keys.Where(x=>x.Community==community&&x.Topic==topic).ToArray()){formRows[key].Revoke();formRows.Remove(key);answerDrafts.Remove(key);}
            formDrafts.Remove((community,topic));formDraftQuestions.Remove((community,topic));homeworkDrafts.Remove((community,topic));
            if(activeFormDraft==(community,topic))activeFormDraft=null;if(activeHomeworkDraft==(community,topic))activeHomeworkDraft=null;if(activeFormsScope==(community,topic))activeFormsScope=null;
            foreach(var key in channelDrafts.Keys.Where(x=>x.TopicId==topic).ToArray()){channelDrafts.Remove(key);discussionDrafts.Remove(key);}
        }
        if(!clearCurrent)return;
        ClearSubjectPanels();
        Messages.Clear();Ballots.Clear();Forms.Clear();ChannelHomeworks.Clear();ChannelSchedule.Clear();ChatTitle="";Draft="";DiscussionContext="";
        FormTitle="";FormDescription="";FormQuestions.Clear();SharedHomeworkTitle="";SharedHomeworkBody="";SharedHomeworkDeadline="";editingSharedHomework=null;
        ClearPreviews();ClearRecordedDraft();_=CancelRecordingAsync();ClearAccessEditor();Status="Доступ к каналу изменился";
    }
    private async Task<GroupSpaceResponse> ReadSpace(string token,Guid community,CancellationToken ct)
    {
        try{var result=await Api!.SpaceAsync(token,community,ct);legacySpace=false;modernSpaceKnown=true;return result;}
        catch(CommunityClientException ex) when(ex.Failure==CommunityClientFailure.NotFound && !modernSpaceKnown)
        {
            legacySpace=true;
            GroupTopicListResponse legacy;
            try{legacy=await Api!.TopicsAsync(token,community,ct);}
            catch(CommunityClientException old)when(old.Failure==CommunityClientFailure.NotFound)
            {legacy=new([new(null,"Общий","💬",null,null,null,0,false)],false);}
            return new(legacy.Topics,[],new(),new(legacy.CanManageChannels,[],[],[],[],legacy.CanManageChannels?["channels"]:[]));
        }
    }
    [ObservableProperty] private bool previewMode;
    [ObservableProperty] private bool showSpaceTools;
    [ObservableProperty] private string categoryTitle = "";
    [ObservableProperty] private int categoryPosition;
    [ObservableProperty] private GroupCategoryResponse? selectedCategory;
    private (Guid Community, Guid Category)? pendingDeleteCategory;
    public bool HasPendingDeleteCategory => pendingDeleteCategory is not null;
    public string DeleteCategoryImpact => pendingDeleteCategory is { } pending
        ? $"Удалить категорию «{Categories.FirstOrDefault(item=>item.CategoryId==pending.Category)?.Title}»? В текущем списке тем: {Channels.Count(item=>item.CategoryId==pending.Category)}. Темы останутся в сообществе без категории."
        : "";
    [ObservableProperty] private int channelPosition;
    [ObservableProperty] private int renamePosition;
    [ObservableProperty] private string channelSubject = "";
    [ObservableProperty] private string renameSubject = "";
    [ObservableProperty] private SpaceChoice selectedTemplate = Templates[0];
    [ObservableProperty] private string roleEditName = "";
    [ObservableProperty] private string roleEditIcon = "user";
    [ObservableProperty] private int roleEditPosition;
    [ObservableProperty] private string roleImpact = "";
    [ObservableProperty] private bool confirmRemoveRole;
    public static IReadOnlyList<SpaceChoice> Templates { get; } = [new("chat","Чат"),new("announcements","Объявления"),new("polls","Опросы"),new("forms","Анкеты"),new("subject","Предмет"),new("materials","Материалы"),new("homework","Домашка"),new("schedule","Расписание")];
    public IReadOnlyList<SpaceChoice> TopicTemplates => Templates;
    public ObservableCollection<GroupCategoryResponse> Categories { get; } = [];
    public ObservableCollection<GroupChannelRow> ArchivedChannels { get; } = [];
    [ObservableProperty] private string archiveSearch = "";
    public IReadOnlyList<GroupChannelRow> VisibleArchivedChannels => ArchivedChannels.Where(row =>
    {
        var words = ArchiveSearch.Trim().ToLowerInvariant().Replace('ё', 'е')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var text = (row.Title + " " + row.Description).ToLowerInvariant().Replace('ё', 'е');
        return words.All(word => text.Contains(word, StringComparison.Ordinal));
    }).ToArray();
    public bool NoArchiveMatches => ArchivedChannels.Count > 0 && VisibleArchivedChannels.Count == 0 && ArchiveSearch.Trim().Length > 0;
    public string ArchiveResultCount => $"Показано {VisibleArchivedChannels.Count} из {ArchivedChannels.Count} загруженных";
    partial void OnArchiveSearchChanged(string value) => RefreshArchiveBrowse();
    [RelayCommand] private void ClearArchiveSearch() => ArchiveSearch = "";
    private void RefreshArchiveBrowse()
    { OnPropertyChanged(nameof(VisibleArchivedChannels)); OnPropertyChanged(nameof(NoArchiveMatches)); OnPropertyChanged(nameof(ArchiveResultCount)); }
    public ObservableCollection<SpaceAccessRow> AccessRules { get; } = [];
    public ObservableCollection<SpacePowerRow> RolePowers { get; } = [];
    public ObservableCollection<string> AuditEvents { get; } = [];
    [ObservableProperty] private string auditSearch = "";
    [ObservableProperty] private int auditKindIndex;
    public IReadOnlyList<string> AuditKinds { get; } = ["Все действия", "Темы", "Роли", "Доступ"];
    public IReadOnlyList<string> VisibleAuditEvents => GroupAuditBrowse.Filter(AuditEvents, AuditSearch, AuditKindIndex);
    public bool HasAuditFilters => AuditSearch.Trim().Length > 0 || AuditKindIndex != 0;
    public bool NoAuditMatches => AuditEvents.Count > 0 && VisibleAuditEvents.Count == 0 && HasAuditFilters;
    partial void OnAuditSearchChanged(string value) => RefreshAuditBrowse();
    partial void OnAuditKindIndexChanged(int value) => RefreshAuditBrowse();
    [RelayCommand] private void ResetAuditFilters() { AuditSearch = ""; AuditKindIndex = 0; }
    private void RefreshAuditBrowse()
    { OnPropertyChanged(nameof(VisibleAuditEvents)); OnPropertyChanged(nameof(HasAuditFilters)); OnPropertyChanged(nameof(NoAuditMatches)); }
    public ObservableCollection<string> SubjectChoices { get; } = [];
    public bool CanManageGroupAccess=>!PreviewMode && (IsHeadman || desk?.Mine.Contains("access")==true);
    public bool CanManageAccess => !PreviewMode && (SelectedChannel?.Permissions.Contains("access")==true || legacySpace && CanManageGroupAccess);
    public bool CanManageRoles => !PreviewMode && (IsHeadman || desk?.Mine.Contains("roles") == true);
    public bool CanManageGrants => !PreviewMode && (IsHeadman || desk?.Mine.Contains("grants") == true);
    public string CapabilitySummary=>$"Тем: {Channels.Count(x=>x.TopicId is not null)} из {space?.Capabilities.MaxTopics??24} · ролей: {desk?.Roles.Count??0} из {space?.Capabilities.MaxRoles??12} · ролей у человека: до {space?.Capabilities.MaxRolesPerMember??3}";
    public bool CanCreateTopic=>!PreviewMode && CanManageChannels && Channels.Count(x=>x.TopicId is not null)<(space?.Capabilities.MaxTopics??24);
    public bool CanCreateRole=>!IsBusy && CanManageRoles && (desk?.Roles.Count??0)<RoleLimit && (IsHeadman || GroupRoleManagement.Position(desk,me)>0);
    public bool CanEditSelectedRole
    {
        get
        {
            return !IsBusy && CanManageRoles && SelectedTrustedRole is {} selected
                && GroupRoleManagement.RoleReason(desk,me,selected.RoleId,"roles").Length==0;
        }
    }
    public bool ShowMaterials => !IsDirect && SelectedChannel is { Kind: "materials", Supported: true };
    [ObservableProperty] private string materialSearch = "";
    public IReadOnlyList<GroupMessageRow> VisibleMaterials => GroupMaterialBrowse.Filter(Messages, MaterialSearch);
    public bool NoMaterialMatches => ShowMaterials && Messages.Count > 0 && VisibleMaterials.Count == 0 && MaterialSearch.Trim().Length > 0;
    public string MaterialResultCount => $"Показано {VisibleMaterials.Count} из {Messages.Count} загруженных";
    partial void OnMaterialSearchChanged(string value) => RefreshMaterialBrowse();
    [RelayCommand] private void ClearMaterialSearch() => MaterialSearch = "";
    private void RefreshMaterialBrowse()
    { OnPropertyChanged(nameof(VisibleMaterials)); OnPropertyChanged(nameof(NoMaterialMatches)); OnPropertyChanged(nameof(MaterialResultCount)); }
    [ObservableProperty] private bool materialsLoading;
    [ObservableProperty] private bool materialsLoaded;
    [ObservableProperty] private bool materialsLoadFailed;
    public bool NoMaterials => ShowMaterials && MaterialsLoaded && !MaterialsLoading && !MaterialsLoadFailed && !HasMore && Messages.Count == 0;
    public string MaterialsErrorText => Messages.Count > 0
        ? "Материалы не обновились. Показана сохранённая страница."
        : "Материалы не загрузились. Повторите попытку.";
    partial void OnMaterialsLoadingChanged(bool value) => OnPropertyChanged(nameof(NoMaterials));
    partial void OnMaterialsLoadedChanged(bool value) => OnPropertyChanged(nameof(NoMaterials));
    partial void OnMaterialsLoadFailedChanged(bool value) => OnPropertyChanged(nameof(NoMaterials));
    [RelayCommand] private Task RetryMaterials() => ShowMaterials && conversationId is Guid id
        ? LoadMaterialsAsync(id, navigationGeneration) : Task.CompletedTask;
    private async Task LoadMaterialsAsync(Guid conversation, int ticket)
    {
        if(!CurrentChat(conversation,ticket) || MaterialsLoading)return;
        MaterialsLoading=true;MaterialsLoadFailed=false;
        try
        {
            await LoadLatestAsync(conversation,ticket);
            if(CurrentChat(conversation,ticket))MaterialsLoaded=true;
        }
        catch(CommunityClientException ex) when(!ReadDenied(ex))
        { if(CurrentChat(conversation,ticket))MaterialsLoadFailed=true; }
        catch(AccountClientException)
        { if(CurrentChat(conversation,ticket))MaterialsLoadFailed=true; }
        finally { if(CurrentChat(conversation,ticket))MaterialsLoading=false; }
    }
    public bool ShowSubject => !IsDirect && SelectedChannel is { Template: "subject", Supported: true };
    public bool ShowForms => !IsDirect && SelectedChannel is { Kind: "forms", Supported: true };
    public bool ShowChannelHomework => ShowSingleHomework || !IsDirect && SelectedChannel is { Kind: "homework", Supported: true };
    public bool ShowChannelSchedule => !IsDirect && SelectedChannel is { Kind: "schedule", Supported: true };
    public bool ShowUnsupported => !IsDirect && SelectedChannel is { Supported: false };
    public bool ShowSpecialized => ShowMaterials || ShowForms || ShowChannelHomework || ShowChannelSchedule || ShowUnsupported;
    public bool CanCreateForm => ShowForms && !PreviewMode && SelectedChannel!.Permissions.Contains("forms");
    public bool CanCreateChannelHomework => !ShowSingleHomework && ShowChannelHomework && !PreviewMode && SelectedChannel!.Permissions.Contains("homework");
    public string SpecializedHint => ShowUnsupported ? "Этот тип темы не поддерживается. Обновите приложение, чтобы открыть её." : PreviewMode ? "Просмотр от лица участника: изменения отключены." : "";
    partial void OnPreviewModeChanged(bool value) { NotifySpace(); SendCommand.NotifyCanExecuteChanged(); }
    partial void OnSelectedCategoryChanged(GroupCategoryResponse? value)
    { CancelDeleteCategory(); CategoryTitle = value?.Title ?? ""; CategoryPosition = value?.Position ?? 0; }
    private void NotifySpace()
    {
        foreach (var name in new[] { nameof(CanPinSelected), nameof(PinCaption), nameof(CanSaveRoleSettings), nameof(CanSaveProposedAccess), nameof(CanCreateTopic), nameof(CanCreateRole), nameof(CanEditSelectedRole), nameof(CapabilitySummary), nameof(CanRestoreTopic), nameof(CanSetInitialAccess), nameof(InitialAccessHint), nameof(CanManageGroupAudit), nameof(CanManageGroupAccess), nameof(CanOpenChannelManagement), nameof(ShowNewChannelManagement), nameof(CanManageAccess), nameof(CanManageRoles), nameof(CanManageGrants), nameof(ShowMaterials), nameof(ShowSubject), nameof(ShowForms), nameof(ShowFormEditor), nameof(ShowFormPreview), nameof(ShowChannelHomework), nameof(ShowChannelSchedule), nameof(ShowUnsupported), nameof(ShowSpecialized), nameof(CanCreateForm), nameof(CanCreateChannelHomework), nameof(SpecializedHint), nameof(ShowComposer), nameof(ShowMessages), nameof(CanAttachMedia), nameof(CanManageSelectedChannel) }) OnPropertyChanged(name);
        RefreshRoleManager();
    }
    private void ApplySpace(GroupSpaceResponse response)
    {
        if(!CurrentSpace())return;
        space = response;
        var categories = response.Categories.OrderBy(x=>x.Position).ToArray();
        if(!Categories.SequenceEqual(categories)){Categories.Clear(); foreach(var item in categories)Categories.Add(item);}
        ApplyDesk(response.Desk);
        ApplyChannels(new(response.Topics, response.Desk.Headman || response.Desk.Mine.Contains("channels")));
        NotifySpace();
    }
    private async Task SpaceAction(Func<CommunityHttpClient,string,Guid,CancellationToken,Task> action, bool mutation = false, Func<bool>? resultCurrent = null)
    {
        if (mutation && PreviewMode || communityId is not Guid community || Api is null || Access is null || mutation && IsBusy) return;
        using var operation = App.Work.Enter();
        var previousScope=spaceScope.Value;spaceScope.Value=(community,navigationGeneration);
        Busy(true);
        try
        {
            var token = await Access(operation.Token);
            if(!CurrentSpace() || resultCurrent?.Invoke()==false)return;
            if (string.IsNullOrWhiteSpace(token)) { ShowAccount(); return; }
            if(CurrentSpace())Status="";
            await action(Api,token,community,operation.Token);
        }
        catch (CommunityClientException ex) when(!mutation && ReadDenied(ex)){if(CurrentSpace()&&resultCurrent?.Invoke()!=false)ClearRevokedContent();}
        catch (CommunityClientException) { if(CurrentSpace()&&resultCurrent?.Invoke()!=false)Status = "Действие недоступно или данные изменились. Обновите тему и повторите."; }
        catch (ArgumentException) { if(CurrentSpace()&&resultCurrent?.Invoke()!=false)Status = "Проверьте введённые значения."; }
        catch (AccountClientException ex) { if(CurrentSpace()&&resultCurrent?.Invoke()!=false)FailSession(ex); }
        catch (OperationCanceledException) { }
        finally { spaceScope.Value=previousScope;if (operation.IsCurrent) Busy(false); }
    }
    [RelayCommand] private void ToggleSpaceTools() => ShowSpaceTools = !ShowSpaceTools;
    [RelayCommand] private void NewCategory() { SelectedCategory = null; CategoryTitle = ""; CategoryPosition = Categories.Count; }
    [RelayCommand] private void RevertCategoryDraft()
    { if(IsBusy || !CanManageChannels)return; CategoryTitle=SelectedCategory?.Title??"";CategoryPosition=SelectedCategory?.Position??Categories.Count; }
    [RelayCommand] private Task SaveCategory()
    {
        if(!CanManageChannels || string.IsNullOrWhiteSpace(CategoryTitle))return Task.CompletedTask;
        var request=new GroupCategoryRequest(SelectedCategory?.CategoryId,CategoryTitle.Trim(),CategoryPosition,SelectedCategory?.Revision??0);
        return SpaceAction(async(api,t,c,ct)=>ApplySpace(await api.SaveCategoryAsync(t,c,request,ct)),true);
    }
    [RelayCommand] private void DeleteCategory()
    {
        if(!CanManageChannels || SelectedCategory is not { } category || communityId is not Guid community)return;
        pendingDeleteCategory=(community,category.CategoryId);
        OnPropertyChanged(nameof(HasPendingDeleteCategory));OnPropertyChanged(nameof(DeleteCategoryImpact));
    }
    [RelayCommand] private void CancelDeleteCategory()
    { pendingDeleteCategory=null;OnPropertyChanged(nameof(HasPendingDeleteCategory));OnPropertyChanged(nameof(DeleteCategoryImpact)); }
    [RelayCommand] private Task ConfirmDeleteCategory()
    {
        var pending=pendingDeleteCategory;CancelDeleteCategory();
        if(pending is not { } chosen || communityId!=chosen.Community || SelectedCategory?.CategoryId!=chosen.Category || !CanManageChannels)return Task.CompletedTask;
        return SpaceAction(async(api,t,c,ct)=>
        {
            var result=await api.DeleteCategoryAsync(t,c,chosen.Category,ct);
            if(CurrentSpace() && communityId==chosen.Community)
            { if(SelectedCategory?.CategoryId==chosen.Category)SelectedCategory=null;ApplySpace(result); }
        },true,resultCurrent:()=>communityId==chosen.Community);
    }
    [RelayCommand] private Task ArchiveChannel()
    {
        if(!CanManageSelectedChannel || SelectedChannel is not {} row || row.TopicId is not Guid id)return Task.CompletedTask;
        var request=new GroupArchiveRequest(true,row.Revision);
        return SpaceAction(async(api,t,c,ct)=>{var result=await api.ArchiveTopicAsync(t,c,id,request,ct);if(!CurrentSpace())return;ApplySpace(result);await OpenChannelAsync(Channels.FirstOrDefault()!);},true);
    }
    private Guid? archiveCommunity;
    private bool archiveLoaded;
    private int archiveRequestVersion;
    private void ApplyArchiveList(GroupTopicListResponse list,Guid community)
    {
        archiveLoaded=true;
        if(archiveCommunity!=community){ArchivedChannels.Clear();archiveCommunity=community;}
        var ordered=new List<GroupChannelRow>();
        foreach(var topic in list.Topics)
        {
            var row=ArchivedChannels.FirstOrDefault(x=>x.TopicId==topic.TopicId);
            if(row is null){GroupChannelRow created=null!;created=new(topic,new RelayCommand(()=>_=OpenArchivedTopic(created)));row=created;}
            else row.Update(topic);
            ordered.Add(row);
        }
        for(var i=0;i<ordered.Count;i++){var old=ArchivedChannels.IndexOf(ordered[i]);if(old<0)ArchivedChannels.Insert(i,ordered[i]);else if(old!=i)ArchivedChannels.Move(old,i);}
        while(ArchivedChannels.Count>ordered.Count)ArchivedChannels.RemoveAt(ArchivedChannels.Count-1);
    }
    [RelayCommand] private Task LoadArchive()
    {
        if(legacySpace)return Task.CompletedTask;var version=++archiveRequestVersion;
        return SpaceAction(async(api,t,c,ct)=>
        {
            var active=await ReadSpace(t,c,ct);if(!CurrentSpace()||version!=archiveRequestVersion)return;
            var result=await api.ArchivedTopicsAsync(t,c,ct);if(!CurrentSpace()||version!=archiveRequestVersion)return;
            var selected=SelectedChannel;ReconcileArchiveAuthority(result,c,active.Topics);
            if(selected?.TopicId is {} id && active.Topics.FirstOrDefault(x=>x.TopicId==id) is {} restored)selected.Update(restored);
            if(CurrentSpace())ApplySpace(active);
        },resultCurrent:()=>version==archiveRequestVersion);
    }
    private void ReconcileArchiveAuthority(GroupTopicListResponse result,Guid community,IReadOnlyList<GroupTopicResponse>? freshActive=null)
    {
        if(archiveCommunity==community)
        {
            var visible=result.Topics.Select(x=>x.TopicId).Concat((freshActive??[]).Select(x=>x.TopicId)).ToHashSet();
            foreach(var old in ArchivedChannels.Where(x=>!visible.Contains(x.TopicId)).ToArray())if(old.TopicId is {} id)PurgeTopicPrivate(id,selectedTopicId==id);
        }
        ApplyArchiveList(result,community);
    }
    private Task OpenArchivedTopic(GroupChannelRow row)
    {if(groupConversationId is not Guid id || archiveCommunity!=communityId || !ArchivedChannels.Contains(row))return Task.CompletedTask;SelectedChannel=row;return OpenConversationAsync(id,row.Title,false,row);}
    public bool CanRestoreTopic=>!PreviewMode && SelectedChannel is {Archived:true} row && (row.Permissions.Contains("channels") || legacySpace && CanManageChannels);
    [RelayCommand] private Task RestoreChannel()
    {
        if(!CanRestoreTopic || SelectedChannel is not {} row || row.TopicId is not Guid id)return Task.CompletedTask;
        var request=new GroupArchiveRequest(false,row.Revision);
        return SpaceAction(async(api,t,c,ct)=>{var result=await api.ArchiveTopicAsync(t,c,id,request,ct);if(!CurrentSpace())return;ApplySpace(result);ArchivedChannels.Clear();var restored=Channels.FirstOrDefault(x=>x.TopicId==id);if(restored is not null)await OpenChannelAsync(restored);},true);
    }
    [RelayCommand] private Task LoadAccess() => !CanManageAccess || SelectedChannel?.TopicId is not Guid id ? Task.CompletedTask : SpaceAction(async (api,t,c,ct) =>
    {
        var result=await api.TopicAccessAsync(t,c,id,ct); if(!CurrentSpace() || SelectedChannel?.TopicId!=id) return; ClearAccessEditor();accessBaseline=result;
        foreach(var role in new GroupRoleResponse?[] { null }.Concat(desk!.Roles.Select(x => (GroupRoleResponse?)x)))
        foreach(var power in (space?.Capabilities.Powers ?? KnownPowers).Where(x=>!GroupOnlyPowers.Contains(x)))
        {
            var row=new SpaceAccessRow(role?.RoleId,role?.Name ?? "Все участники",power,result.Rules.FirstOrDefault(x=>x.RoleId==role?.RoleId && x.Power==power)?.State ?? "inherit");
            row.PropertyChanged+=AccessRuleChanged;AccessRules.Add(row);
        }
        SyncSimpleAccess();
    });
    [RelayCommand] private Task SaveAccess()
    {
        if(!CanSaveProposedAccess || accessBaseline is not {} baseline || ProposedAccess() is not {} request)return Task.CompletedTask;
        AccessPreviewReady=false;
        return SpaceAction(async (api,t,c,ct) =>
        {
            try
            {
                var result=await api.SetTopicAccessAsync(t,c,baseline.TopicId,request,ct);
                if(!CurrentSpace())return;
                ApplySpace(result);ClearAccessEditor();
            }
            catch(CommunityClientException ex)when(ex.Failure==CommunityClientFailure.RevisionConflict)
            {
                if(!CurrentSpace() || SelectedChannel?.TopicId!=baseline.TopicId)return;
                InvalidateAccessPreview();
                var latest=await api.TopicAccessAsync(t,c,baseline.TopicId,ct);
                if(!CurrentSpace() || SelectedChannel?.TopicId!=baseline.TopicId)return;
                if(accessBaseline is null || latest.Revision>=accessBaseline.Revision)accessBaseline=latest;
                Status="Правила на сервере изменились. Ваш черновик сохранён; проверьте его последствия заново перед сохранением.";
            }
        },true);
    }
    [RelayCommand] private Task PreviewPerson() => !CanManageGroupAccess || SelectedTrustCandidate is not { } person ? Task.CompletedTask : Preview(new(person.UserId,null));
    [RelayCommand] private Task PreviewRole() => !CanManageGroupAccess || SelectedTrustedRole is not { } role ? Task.CompletedTask : Preview(new(null,role.RoleId));
    private Task Preview(GroupPermissionPreviewRequest request) => SpaceAction(async (api,t,c,ct) =>
    { var result=await api.PreviewPermissionsAsync(t,c,request,ct); if(!CurrentSpace())return; PreviewMode=true; ApplyChannels(new(result.Topics,false)); await OpenChannelAsync(Channels.FirstOrDefault()!); });
    [RelayCommand] private Task EndPreview() => SpaceAction(async (api,t,c,ct) => { PreviewMode=false; ApplySpace(await api.SpaceAsync(t,c,ct)); await OpenChannelAsync(Channels.FirstOrDefault()!); });
    public bool CanManageGroupAudit=>!PreviewMode && (IsHeadman || desk?.Mine.Any(x=>x is "channels" or "access" or "roles")==true);
    [RelayCommand] private Task LoadAudit() => !CanManageGroupAudit ? Task.CompletedTask : SpaceAction(async (api,t,c,ct) =>
    { var result=await api.GroupAuditAsync(t,c,ct); if(!CurrentSpace())return; AuditEvents.Clear(); foreach(var x in result.Events) AuditEvents.Add($"{x.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm} · {AuditLabel(x.Action)} · {x.ObjectId}"); });
    private static string AuditLabel(string action) => action switch { "topic.create"=>"Создана тема", "topic.update"=>"Изменена тема", "topic.archive"=>"Тема в архиве", "topic.restore"=>"Тема восстановлена", "topic.access"=>"Изменён доступ", "role.create"=>"Создана роль", "role.delete"=>"Удалена роль", "role.grant"=>"Назначена роль", "role.revoke"=>"Снята роль", _=>"Изменение структуры группы" };
    [RelayCommand] private Task RemoveRole() => !CanEditSelectedRole || !ConfirmRemoveRole || SelectedTrustedRole is not { } role ? Task.CompletedTask : SpaceAction(async(api,t,c,ct) =>
    {
        if(PreviewMode || !ConfirmRemoveRole || SelectedTrustedRole?.RoleId!=role.RoleId || GroupRoleManagement.RoleReason(desk,me,role.RoleId,"roles").Length>0)return;
        ApplyDesk(await api.DeleteRoleAsync(t,c,role.RoleId,ct)); ConfirmRemoveRole=false;
    },true);
    private static readonly string[] GroupOnlyPowers=["joins","exclude","roles","grants"];
    private static readonly string[] KnownPowers = ["read","post","media","vote","formsRespond","ballots","forms","close","pin","moderate","homework","mentionAll","joins","exclude","channels","access","roles","grants"];
    internal static string PowerLabel(string power) => power switch { "read"=>"Читать", "post"=>"Писать", "media"=>"Вложения", "vote"=>"Голосовать", "formsRespond"=>"Заполнять анкеты", "ballots"=>"Создавать опросы", "forms"=>"Создавать анкеты", "close"=>"Завершать опросы", "pin"=>"Закреплять", "moderate"=>"Удалять сообщения", "homework"=>"Общая домашка", "mentionAll"=>"Упоминать всех", "joins"=>"Принимать участников", "exclude"=>"Исключать участников", "channels"=>"Управлять темами", "access"=>"Настраивать доступ", "roles"=>"Управлять ролями", "grants"=>"Назначать роли", _=>"Неизвестное право" };
    private GroupRoleResponse? roleEditBaseline;
    private Guid? roleEditBaselineCommunity;
    private sealed record RoleEditDraft(string Name, string Icon, int Position, long Revision);
    private readonly Dictionary<(Guid Community,Guid Role),RoleEditDraft> roleEditDrafts=[];
    private void StashRoleEditorDraft()
    {
        if(roleEditBaselineCommunity is not Guid community || roleEditBaseline is not { } baseline)return;
        var key=(community,baseline.RoleId);
        if(RoleEditorDirty)roleEditDrafts[key]=new(RoleEditName,RoleEditIcon,RoleEditPosition,baseline.Revision);
        else roleEditDrafts.Remove(key);
    }
    [ObservableProperty] private bool roleEditConflict;
    public bool CanSaveRoleSettings=>CanEditSelectedRole && !RoleEditConflict && RoleEditPosition is >= 0 and <= 10000 && !string.IsNullOrWhiteSpace(RoleEditName)
        && roleEditBaseline?.RoleId==SelectedTrustedRole?.RoleId && RoleSettingsHint.Length==0;
    partial void OnRoleEditConflictChanged(bool value)=>OnPropertyChanged(nameof(CanSaveRoleSettings));
    private bool RoleEditorDirty=>roleEditBaseline is {} baseline &&
        (RoleEditName!=baseline.Name || RoleEditIcon!=baseline.Icon || RoleEditPosition!=baseline.Position);
    private void LoadRoleEditor()
    {
        var role=desk?.Roles.FirstOrDefault(x=>x.RoleId==SelectedTrustedRole?.RoleId);
        if(role is not null && roleEditBaseline?.RoleId==role.RoleId && roleEditBaselineCommunity==communityId && RoleEditorDirty)
        {
            RoleEditConflict=roleEditBaseline.Revision!=role.Revision;
            RefreshRolePowerRows(role);
            OnPropertyChanged(nameof(CanSaveRoleSettings));return;
        }
        roleEditBaseline=role;roleEditBaselineCommunity=communityId;RoleEditConflict=false;
        if(role is not null && communityId is Guid community && roleEditDrafts.TryGetValue((community,role.RoleId),out var draft))
        {RoleEditName=draft.Name;RoleEditIcon=draft.Icon;RoleEditPosition=draft.Position;RoleEditConflict=draft.Revision!=role.Revision;}
        else {RoleEditName=role?.Name ?? "";RoleEditIcon=role?.Icon ?? "user";RoleEditPosition=role?.Position ?? 0;}
        ConfirmRemoveRole=false;RoleImpact="";RefreshRolePowerRows(role);OnPropertyChanged(nameof(CanSaveRoleSettings));
    }
    private void RefreshRolePowerRows(GroupRoleResponse? role)
    {
        RolePowers.Clear();if(role is null)return;
        foreach(var power in space?.Capabilities.Powers ?? desk?.Capabilities.Powers ?? KnownPowers)
        {
            var enabled=desk!.Powers.Any(x=>x.RoleId==role.RoleId&&x.Power==power);
            var reason=PreviewMode?"В режиме просмотра изменения отключены.":IsBusy?"Дождитесь завершения действия."
                :GroupRoleManagement.RoleReason(desk,me,role.RoleId,"roles",addedPower:enabled?null:power);
            RolePowers.Add(new(power,enabled,reason.Length==0,reason));
        }
    }
    [RelayCommand] private void ReloadRoleSettings(){if(IsBusy)return;if(communityId is Guid community && SelectedTrustedRole is { } selected)roleEditDrafts.Remove((community,selected.RoleId));roleEditBaseline=null;LoadRoleEditor();}
    [RelayCommand] private void RevertRoleDraft(){if(!IsBusy && CanEditSelectedRole)ReloadRoleSettings();}
    [RelayCommand] private Task SaveRoleSettings()
    {
        if(!CanSaveRoleSettings || roleEditBaseline is not {} baseline)return Task.CompletedTask;
        var request=new GroupRoleSettingsRequest(RoleEditName.Trim(),RoleEditIcon.Trim(),RoleEditPosition,baseline.Revision);
        return SpaceAction(async(api,t,c,ct)=>
        {
            if(PreviewMode || SelectedTrustedRole?.RoleId!=baseline.RoleId || GroupRoleManagement.RoleReason(desk,me,baseline.RoleId,"roles",newPosition:request.Position).Length>0)return;
            var result=await api.SaveRoleSettingsAsync(t,c,baseline.RoleId,request,ct);
            if(!CurrentSpace())return;
            if(communityId is Guid community)
            {
                var key=(community,baseline.RoleId);
                if(roleEditDrafts.TryGetValue(key,out var stored) && stored.Name==request.Name && stored.Icon==request.Icon && stored.Position==request.Position && stored.Revision==baseline.Revision)
                    roleEditDrafts.Remove(key);
                else if(SelectedTrustedRole?.RoleId==baseline.RoleId && RoleEditName==request.Name && RoleEditIcon==request.Icon && RoleEditPosition==request.Position)
                    roleEditDrafts.Remove(key);
            }
            if(SelectedTrustedRole?.RoleId==baseline.RoleId)roleEditBaseline=result.Roles.FirstOrDefault(x=>x.RoleId==baseline.RoleId);
            ApplyDesk(result);LoadRoleEditor();
        },true);
    }
    [RelayCommand] private Task ToggleRolePower(SpacePowerRow row) => !CanEditSelectedRole || SelectedTrustedRole is not { } role
        || GroupRoleManagement.RoleReason(desk,me,role.RoleId,"roles",addedPower:row.Enabled?null:row.Power).Length>0 ? Task.CompletedTask : SpaceAction(async(api,t,c,ct)=>
    {
        if(PreviewMode || SelectedTrustedRole?.RoleId!=role.RoleId || GroupRoleManagement.RoleReason(desk,me,role.RoleId,"roles",addedPower:row.Enabled?null:row.Power).Length>0)return;
        var result=await api.SetRolePowerAsync(t,c,role.RoleId,new(row.Power,!row.Enabled),ct);
        if(!CurrentSpace())return;
        var latest=result.Roles.FirstOrDefault(x=>x.RoleId==role.RoleId);
        if(roleEditBaseline is {} baseline && latest is not null && baseline.RoleId==latest.RoleId && baseline.Name==latest.Name && baseline.Icon==latest.Icon && baseline.Position==latest.Position)
            roleEditBaseline=latest;
        ApplyDesk(result);
    },true);
    [RelayCommand] private Task InspectRoleImpact() => !CanEditSelectedRole || SelectedTrustedRole is not { } role ? Task.CompletedTask : SpaceAction(async(api,t,c,ct)=>
    { if(PreviewMode || GroupRoleManagement.RoleReason(desk,me,role.RoleId,"roles").Length>0)return;var impact=await api.RoleImpactAsync(t,c,role.RoleId,ct); if(SelectedTrustedRole?.RoleId!=role.RoleId || !CurrentSpace())return; RoleImpact=$"Назначений: {impact.Assignments} · правил доступа: {impact.AccessRules}. Удаление изменит права этих участников."; ConfirmRemoveRole=true; });
}

public sealed record SpaceChoice(string Code,string Label) { public override string ToString()=>Label; }
public sealed partial class SpaceAccessRow : ObservableObject
{
    public SpaceAccessRow(Guid? roleId,string role,string power,string state) { RoleId=roleId; Label=role+" · "+GroupViewModel.PowerLabel(power); Power=power; this.state=States.FirstOrDefault(x=>x.Code==state)??States[0]; }
    public Guid? RoleId {get;} public string Power{get;} public string Label{get;}
    public static IReadOnlyList<SpaceChoice> States {get;}=[new("inherit","По умолчанию"),new("allow","Разрешить"),new("deny","Запретить")];
    public IReadOnlyList<SpaceChoice> Choices=>States;
    [ObservableProperty] private SpaceChoice state;
}
public sealed record SpacePowerRow(string Power,bool Enabled,bool CanToggle=true,string DisabledReason="") { public string Label=>GroupViewModel.PowerLabel(Power)+(Enabled?" · разрешено":" · выключено"); }
