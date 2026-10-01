using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
namespace Vograph.Desktop.Features.Groups;
public sealed partial class GroupViewModel
{
    private string? requestedDiscussion;
    private string? requestedSubject;
    private Zapara.Contracts.Communities.GroupHomeworkCopyResponse? requestedHomework;
    [ObservableProperty] private bool showSingleHomework;
    public void RequestHomework(Zapara.Contracts.Communities.GroupHomeworkCopyResponse item) => requestedHomework=item;
    private void ApplyRequestedHomework()
    {
        if(requestedHomework is not {} item)return;
        requestedHomework=null;ShowSingleHomework=true;ChannelHomeworks.Clear();ChannelHomeworks.Add(new(item,!PreviewMode,ToggleChannelHomework,EditChannelHomework));NotifySpace();
    }
    partial void OnShowSingleHomeworkChanged(bool value)=>NotifySpace();
    private readonly Dictionary<(Guid,Guid?),string> discussionDrafts=[];
    [ObservableProperty] private string discussionContext="";
    public bool HasDiscussionContext=>DiscussionContext.Length>0;
    partial void OnDiscussionContextChanged(string value)
    {
        OnPropertyChanged(nameof(HasDiscussionContext));
        OnPropertyChanged(nameof(DraftLimitText));
        SendCommand.NotifyCanExecuteChanged();
        if(conversationId is Guid id){var key=DraftKey(id);if(value.Length==0)discussionDrafts.Remove(key);else discussionDrafts[key]=value;}
    }
    public void RequestDiscussion(string subject,string context){requestedSubject=subject;requestedDiscussion=context;}
    private async Task ApplyRequestedDiscussion()
    {
        if(requestedDiscussion is null)return;
        var target=Channels.FirstOrDefault(x=>x.CanPost && x.Kind=="chat" && string.Equals(x.Subject,requestedSubject,StringComparison.OrdinalIgnoreCase))
            ??Channels.FirstOrDefault(x=>x.CanPost && x.Kind=="chat");
        if(target is null){Status="Нет доступной темы для обсуждения. Контекст сохранён.";return;}
        await OpenChannelAsync(target);DiscussionContext=requestedDiscussion;requestedDiscussion=null;requestedSubject=null;
    }
    [RelayCommand] private void ClearDiscussionContext()=>DiscussionContext="";
}
