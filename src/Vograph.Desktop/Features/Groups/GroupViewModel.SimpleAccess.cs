using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace Vograph.Desktop.Features.Groups;
public sealed partial class GroupViewModel
{
    public static IReadOnlyList<SpaceChoice> ReadPresets{get;}=[new("all","Все участники"),new("roles","Выбранные роли"),new("custom","Настроено отдельно")];
    public static IReadOnlyList<SpaceChoice> PostPresets{get;}=[new("inherit","Все с нужным правом"),new("roles","Выбранные роли"),new("headman","Только староста"),new("custom","Настроено отдельно")];
    public IReadOnlyList<SpaceChoice> SimpleReadChoices=>ReadPresets;
    public IReadOnlyList<SpaceChoice> SimplePostChoices=>PostPresets;
    [ObservableProperty] private SpaceChoice simpleReadPreset=ReadPresets[2];
    [ObservableProperty] private SpaceChoice simplePostPreset=PostPresets[3];
    public ObservableCollection<SimpleAccessRole> SimpleAccessRoles{get;}=[];
    private bool applyingSimpleAccess;
    private void SyncSimpleAccess()
    {
        if(applyingSimpleAccess)return;
        var reads=AccessRules.Where(x=>x.Power=="read").ToArray();var posts=AccessRules.Where(x=>x.Power=="post").ToArray();
        var everyoneRead=reads.FirstOrDefault(x=>x.RoleId is null)?.State.Code;
        SimpleReadPreset=ReadPresets[everyoneRead=="allow"&&reads.Where(x=>x.RoleId is not null).All(x=>x.State.Code=="inherit")?0:
            everyoneRead=="deny"&&reads.Where(x=>x.RoleId is not null).All(x=>x.State.Code is "inherit" or "allow")?1:2];
        var everyonePost=posts.FirstOrDefault(x=>x.RoleId is null)?.State.Code;
        SimplePostPreset=PostPresets[posts.Length>0&&posts.All(x=>x.State.Code=="inherit")?0:
            everyonePost=="deny"&&posts.Where(x=>x.RoleId is not null).All(x=>x.State.Code=="inherit")?2:
            everyonePost=="deny"&&posts.Where(x=>x.RoleId is not null).All(x=>x.State.Code is "inherit" or "allow")?1:3];
        SimpleAccessRoles.Clear();
        foreach(var role in desk?.Roles??[])SimpleAccessRoles.Add(new(role.RoleId,role.Name,reads.Any(x=>x.RoleId==role.RoleId&&x.State.Code=="allow"),posts.Any(x=>x.RoleId==role.RoleId&&x.State.Code=="allow")));
    }
    [RelayCommand] private void ApplySimpleAccess()
    {
        if(!CanManageAccess || accessBaseline is null)return;
        var read=SimpleReadPreset.Code;var post=SimplePostPreset.Code;var roles=SimpleAccessRoles.Select(x=>(x.RoleId,x.Read,x.Post)).ToArray();
        applyingSimpleAccess=true;
        try
        {
            foreach(var row in AccessRules)
            {
                string? state=null;
                if(row.Power=="read" && read!="custom")state=row.RoleId is null?(read=="all"?"allow":"deny"):
                    read=="roles"&&roles.Any(x=>x.RoleId==row.RoleId&&x.Read)?"allow":"inherit";
                if(row.Power=="post" && post!="custom")state=row.RoleId is null?(post=="inherit"?"inherit":"deny"):
                    post=="roles"&&roles.Any(x=>x.RoleId==row.RoleId&&x.Post)?"allow":"inherit";
                if(state is not null)row.State=SpaceAccessRow.States.First(x=>x.Code==state);
            }
        }
        finally{applyingSimpleAccess=false;SyncSimpleAccess();SimpleReadPreset=ReadPresets.First(x=>x.Code==read);SimplePostPreset=PostPresets.First(x=>x.Code==post);InvalidateAccessPreview();}
    }
}
public sealed partial class SimpleAccessRole(Guid roleId,string name,bool read,bool post):ObservableObject
{
    public Guid RoleId{get;}=roleId;public string Name{get;}=name;
    [ObservableProperty] private bool read=read;
    [ObservableProperty] private bool post=post;
}
