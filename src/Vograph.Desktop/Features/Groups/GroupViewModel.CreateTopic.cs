using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zapara.Contracts.Communities;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Communities;
namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel
{
    [ObservableProperty] private SpaceChoice creationReadPreset=ReadPresets[0];
    [ObservableProperty] private SpaceChoice creationPostPreset=PostPresets[0];
    [ObservableProperty] private GroupCategoryResponse? newChannelCategory;
    public IReadOnlyList<SpaceChoice> CreationReadChoices=>ReadPresets.Where(x=>x.Code!="custom").ToArray();
    public IReadOnlyList<SpaceChoice> CreationPostChoices=>PostPresets.Where(x=>x.Code!="custom").ToArray();
    public ObservableCollection<SimpleAccessRole> CreationRoles{get;}=[];
    public bool CanSetInitialAccess=>!PreviewMode && !legacySpace && (IsHeadman || desk?.Mine.Contains("access")==true);
    public string InitialAccessHint=>legacySpace?"Этот сервер поддерживает создание без начальных правил доступа. Закрытая тема требует обновления сервера.":CanSetInitialAccess?"Просмотр и публикация будут сохранены вместе с новой темой. Ограничения типа и политики публикации проверяет сервер.":"Для закрытой темы нужно групповое право настройки доступа или статус старосты. Обычная тема доступна для создания.";
    private Guid creationEditorId=Guid.NewGuid();
    private Guid? activeCreationCommunity;
    private sealed record CreationDraft(Guid EditorId,string Title,string Icon,string Description,bool Pinned,string Accent,string Policy,string Template,Guid? Category,int Position,string Subject,string Read,string Post,Guid[] Readers,Guid[] Posters);
    private readonly Dictionary<Guid,CreationDraft> creationDrafts=[];
    private CreationDraft CaptureCreation()=>new(creationEditorId,ChannelTitle,ChannelIcon,ChannelDescription,ChannelPinned,NewChannelAccent.Code,NewChannelPolicy.Code,SelectedTemplate.Code,NewChannelCategory?.CategoryId,ChannelPosition,ChannelSubject,CreationReadPreset.Code,CreationPostPreset.Code,CreationRoles.Where(x=>x.Read).Select(x=>x.RoleId).Order().ToArray(),CreationRoles.Where(x=>x.Post).Select(x=>x.RoleId).Order().ToArray());
    private static bool SameCreation(CreationDraft a,CreationDraft b)=>JsonSerializer.Serialize(a)==JsonSerializer.Serialize(b);
    private void SaveCreationDraft(){if(activeCreationCommunity is {} community)creationDrafts[community]=CaptureCreation();}
    private void RestoreCreationDraft(Guid community)
    {
        activeCreationCommunity=community;
        if(!creationDrafts.TryGetValue(community,out var draft)){ResetCreation();return;}
        creationEditorId=draft.EditorId;ChannelTitle=draft.Title;ChannelIcon=draft.Icon;ChannelDescription=draft.Description;ChannelPinned=draft.Pinned;
        NewChannelAccent=AccentChoices.FirstOrDefault(x=>x.Code==draft.Accent)??AccentChoices[0];NewChannelPolicy=PolicyChoices.FirstOrDefault(x=>x.Code==draft.Policy)??PolicyChoices[0];SelectedTemplate=Templates.FirstOrDefault(x=>x.Code==draft.Template)??Templates[0];
        NewChannelCategory=Categories.FirstOrDefault(x=>x.CategoryId==draft.Category);ChannelPosition=draft.Position;ChannelSubject=draft.Subject;
        CreationReadPreset=ReadPresets.FirstOrDefault(x=>x.Code==draft.Read)??ReadPresets[0];CreationPostPreset=PostPresets.FirstOrDefault(x=>x.Code==draft.Post)??PostPresets[0];
        ReconcileCreationRoles(draft.Readers,draft.Posters);
    }
    private void ReconcileCreationRoles(IEnumerable<Guid>? readers=null,IEnumerable<Guid>? posters=null)
    {
        var read=(readers??CreationRoles.Where(x=>x.Read).Select(x=>x.RoleId)).ToHashSet();var post=(posters??CreationRoles.Where(x=>x.Post).Select(x=>x.RoleId)).ToHashSet();
        CreationRoles.Clear();foreach(var role in desk?.Roles??[])CreationRoles.Add(new(role.RoleId,role.Name,read.Contains(role.RoleId),post.Contains(role.RoleId)));
        OnPropertyChanged(nameof(CanSetInitialAccess));OnPropertyChanged(nameof(InitialAccessHint));
    }
    private void ResetCreation()
    {
        creationEditorId=Guid.NewGuid();ChannelTitle="";ChannelIcon="💬";ChannelDescription="";ChannelPinned=false;ChannelPosition=0;ChannelSubject="";NewChannelCategory=null;
        NewChannelAccent=AccentChoices[0];NewChannelPolicy=PolicyChoices[0];SelectedTemplate=Templates[0];CreationReadPreset=ReadPresets[0];CreationPostPreset=PostPresets[0];ReconcileCreationRoles([],[]);
    }
    [RelayCommand] private void NewTopicDraft(){ResetCreation();SaveCreationDraft();}
    private static IReadOnlyList<GroupAccessRule>? InitialRules(CreationDraft draft)
    {
        var rules=new List<GroupAccessRule>();
        if(draft.Read=="roles"){rules.Add(new(null,"read","deny"));rules.AddRange(draft.Readers.Select(x=>new GroupAccessRule(x,"read","allow")));}
        if(draft.Post is "roles" or "headman"){rules.Add(new(null,"post","deny"));if(draft.Post=="roles")rules.AddRange(draft.Posters.Select(x=>new GroupAccessRule(x,"post","allow")));}
        return rules.Count==0?null:rules;
    }
    private async Task CreateTopicFromDraft(string? requestedKind)
    {
        if(!CanCreateTopic || IsBusy || communityId is not {} community || Api is null || Access is null)return;
        if(requestedKind is "chat" or "ballots")SelectedTemplate=Templates.First(x=>x.Code==(requestedKind=="ballots"?"polls":"chat"));
        var submitted=CaptureCreation();
        var rules=InitialRules(submitted);
        if(rules is not null && !CanSetInitialAccess){Status=InitialAccessHint;return;}
        if(legacySpace && submitted.Template is not ("chat" or "polls")){Status="Этот тип темы требует обновления сервера.";return;}
        var kind=submitted.Template switch{"polls"=>"ballots","announcements" or "subject"=>"chat",_=>submitted.Template};
        var request=new GroupTopicRequest(submitted.Title.Trim(),submitted.Icon.Trim(),kind,submitted.Description.Trim(),submitted.Accent,submitted.Pinned,submitted.Policy,
            legacySpace?null:submitted.Template,legacySpace?null:submitted.Category,legacySpace?0:submitted.Position,legacySpace?null:submitted.Template=="subject"?submitted.Subject:null,initialAccessRules:rules);
        var ticket=navigationGeneration;var before=Channels.Select(x=>x.TopicId).ToHashSet();creationDrafts[community]=CaptureCreation();
        using var operation=App.Work.Enter();Busy(true);
        try
        {
            var token=await Access(operation.Token);if(string.IsNullOrWhiteSpace(token)){ShowAccount();return;}
            if(!operation.IsCurrent || communityId!=community || ticket!=navigationGeneration)return;
            if(!CanCreateTopic || rules is not null && !CanSetInitialAccess){Status="Права изменились. Проверьте настройки создания темы.";return;}
            var result=await Api.CreateTopicAsync(token,community,request,operation.Token);
            if(creationDrafts.TryGetValue(community,out var stored) && SameCreation(stored,submitted))creationDrafts.Remove(community);
            if(!operation.IsCurrent || communityId!=community || ticket!=navigationGeneration)return;
            var unchanged=SameCreation(CaptureCreation(),submitted);ApplyChannels(result);
            if(unchanged){ResetCreation();SaveCreationDraft();}
            var created=Channels.FirstOrDefault(x=>!before.Contains(x.TopicId) && x.Kind==kind && x.Title==request.Title);
            if(created is not null)await OpenChannelAsync(created);
            Status="Тема создана. Доступные темы обновлены.";
        }
        catch(CommunityClientException){if(operation.IsCurrent && communityId==community)Status="Тема не создана. Проверьте название и текущие права; черновик сохранён.";}
        catch(AccountClientException ex){if(operation.IsCurrent && communityId==community)FailSession(ex);}
        catch(ArgumentException){Status="Проверьте название и параметры темы.";}
        catch(OperationCanceledException){}
        finally{if(operation.IsCurrent)Busy(false);}
    }
}
