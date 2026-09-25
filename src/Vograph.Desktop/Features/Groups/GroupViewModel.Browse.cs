using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel
{
    [ObservableProperty] private string groupSearch = "";
    [ObservableProperty] private string memberSearch = "";
    [ObservableProperty] private string channelSearch = "";
    [ObservableProperty] private int channelKindIndex;
    [ObservableProperty] private bool unreadOnly;
    [ObservableProperty] private bool showChannelManagement;

    public IReadOnlyList<GroupCommunityRow> FilteredCommunities => GroupBrowse.Communities(Communities, GroupSearch);
    public IReadOnlyList<GroupPersonRow> FilteredPeople => GroupBrowse.People(People, MemberSearch);
    public IReadOnlyList<GroupChannelRow> FilteredChannels => GroupBrowse.Channels(Channels, ChannelSearch,
        ChannelKindIndex switch { 1 => "chat", 2 => "ballots", _ => "all" }, UnreadOnly);
    public string ChannelResultCount => $"Показано {FilteredChannels.Count(row => !row.IsGlobalBallots)} из {Channels.Count(row => !row.IsGlobalBallots)}";
    public string CommunityResultCount => $"Показано {FilteredCommunities.Count} из {Communities.Count}";
    public bool NoCommunitySearchResults => GroupSearch.Trim().Length > 0 && FilteredCommunities.Count == 0;
    public bool NoMemberSearchResults => MemberSearch.Trim().Length > 0 && FilteredPeople.Count == 0;
    public bool NoChannelSearchResults => HasHome && FilteredChannels.Count == 0;
    public bool HasChannelFilters => ChannelSearch.Trim().Length > 0 || ChannelKindIndex != 0 || UnreadOnly;
    public string ManagementCaption => ShowChannelManagement ? "Закрыть управление" : "Управлять каналами";
    public bool ShowSelectedChannelManagement => ShowChannelManagement && CanManageSelectedChannel;
    public bool ShowTrustedManagement => ShowChannelManagement && IsHeadman;
    public bool HasUnreadChannel => GroupBrowse.NextUnread(GroupBrowse.Channels(Channels, "", "all", false),
        IsDirect ? null : SelectedChannel) is not null;

    partial void OnGroupSearchChanged(string value) => RefreshCommunityBrowse();
    partial void OnMemberSearchChanged(string value) => RefreshPeopleBrowse();
    partial void OnChannelSearchChanged(string value) => RefreshChannelBrowse();
    partial void OnChannelKindIndexChanged(int value) => RefreshChannelBrowse();
    partial void OnUnreadOnlyChanged(bool value) => RefreshChannelBrowse();
    partial void OnShowChannelManagementChanged(bool value)
    {
        OnPropertyChanged(nameof(ManagementCaption));
        OnPropertyChanged(nameof(ShowSelectedChannelManagement));
        OnPropertyChanged(nameof(ShowTrustedManagement));
    }

    private void RefreshCommunityBrowse()
    {
        OnPropertyChanged(nameof(FilteredCommunities));
        OnPropertyChanged(nameof(CommunityResultCount));
        OnPropertyChanged(nameof(NoCommunitySearchResults));
    }

    private void RefreshPeopleBrowse()
    {
        OnPropertyChanged(nameof(FilteredPeople));
        OnPropertyChanged(nameof(NoMemberSearchResults));
    }

    private void RefreshChannelBrowse()
    {
        OnPropertyChanged(nameof(FilteredChannels));
        OnPropertyChanged(nameof(ChannelResultCount));
        OnPropertyChanged(nameof(NoChannelSearchResults));
        OnPropertyChanged(nameof(HasChannelFilters));
        OnPropertyChanged(nameof(HasUnreadChannel));
    }

    [RelayCommand]
    private void ResetChannelFilters()
    {
        ChannelSearch = "";
        ChannelKindIndex = 0;
        UnreadOnly = false;
    }

    [RelayCommand]
    private void ToggleChannelManagement() => ShowChannelManagement = !ShowChannelManagement;

    [RelayCommand]
    private Task NextUnreadChannel()
    {
        var next = GroupBrowse.NextUnread(GroupBrowse.Channels(Channels, "", "all", false),
            IsDirect ? null : SelectedChannel);
        return next is null ? Task.CompletedTask : OpenChannelAsync(next);
    }

    [RelayCommand]
    private void ClearGroupSearch() => GroupSearch = "";

    [RelayCommand]
    private void ClearMemberSearch() => MemberSearch = "";

    [RelayCommand]
    private async Task RetryBrowseAsync()
    {
        if (communityId is Guid id && HasHome) await OpenAsync(id);
        else await LoadAsync();
    }
}
