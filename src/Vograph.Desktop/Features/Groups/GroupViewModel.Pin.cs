using CommunityToolkit.Mvvm.Input;
using Zapara.Contracts.Communities;
namespace Vograph.Desktop.Features.Groups;
public sealed partial class GroupViewModel
{
    public bool CanPinSelected=>!PreviewMode && SelectedChannel is {TopicId:not null,Supported:true} row &&
        (row.Permissions.Contains("pin") || legacySpace && CanManageChannels);
    public string PinCaption=>SelectedChannel?.Pinned==true?"Открепить тему":"Закрепить тему";
    [RelayCommand] private Task ToggleTopicPin()
    {
        if(!CanPinSelected || SelectedChannel is not {} row || row.TopicId is not Guid topic)return Task.CompletedTask;
        var request=new GroupTopicRequest(row.Title,row.Icon,row.Kind,row.Description,row.Accent,!row.Pinned,row.WritePolicy,
            row.Template,row.CategoryId,row.Position,row.Subject,row.Revision);
        return SpaceAction(async(api,token,community,ct)=>
        {
            var result=await api.RenameTopicAsync(token,community,topic,request,ct);
            if(!CurrentSpace())return;
            ApplyChannels(result);NotifySpace();
        },true);
    }
}
