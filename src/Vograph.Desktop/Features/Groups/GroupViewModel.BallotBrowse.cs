using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services.Accounts;
using Vograph.Core.Services.Communities;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel
{
    private IReadOnlyList<GroupBallotRow> filteredBallots = [];
    private int ballotRequestSerial;

    [ObservableProperty] private string ballotSearch = "";
    [ObservableProperty] private int ballotStatusIndex;
    [ObservableProperty] private int ballotSortIndex;
    [ObservableProperty] private bool showBallotComposer;
    [ObservableProperty] private bool ballotLoading;
    [ObservableProperty] private bool ballotLoadFailed;
    [ObservableProperty] private bool ballotLoaded;
    [ObservableProperty] private string ballotFeedback = "";

    public IReadOnlyList<GroupBallotRow> FilteredBallots => filteredBallots;
    public bool HasBallotFilters => BallotSearch.Trim().Length > 0 || BallotStatusIndex is >= 1 and <= 3
        || BallotSortIndex is >= 1 and <= 2;
    public string BallotResultCount => $"Показано {filteredBallots.Count} из {Ballots.Count} на текущей доске";
    public bool NoBallots => BallotLoaded && !BallotLoading && !BallotLoadFailed && Ballots.Count == 0
        && !HasBallotFilters;
    public bool NoBallotMatches => BallotLoaded && !BallotLoading && !BallotLoadFailed
        && HasBallotFilters && filteredBallots.Count == 0;
    public string BallotErrorText => Ballots.Count > 0
        ? "Не удалось обновить голосования. Показана последняя загруженная доска."
        : "Не удалось загрузить голосования.";
    public string BallotComposerCaption => ShowBallotComposer ? "Скрыть форму" : "Новое голосование";

    partial void OnBallotSearchChanged(string value) => RefreshBallotBrowse();
    partial void OnBallotStatusIndexChanged(int value) => RefreshBallotBrowse();
    partial void OnBallotSortIndexChanged(int value) => RefreshBallotBrowse();
    partial void OnShowBallotComposerChanged(bool value) => OnPropertyChanged(nameof(BallotComposerCaption));
    partial void OnBallotLoadingChanged(bool value) => RefreshBallotStates();
    partial void OnBallotLoadFailedChanged(bool value) => RefreshBallotStates();
    partial void OnBallotLoadedChanged(bool value) => RefreshBallotStates();

    [RelayCommand]
    private void ToggleBallotComposer() => ShowBallotComposer = !ShowBallotComposer;

    [RelayCommand]
    private void ResetBallotFilters()
    {
        BallotSearch = "";
        BallotStatusIndex = 0;
        BallotSortIndex = 0;
    }

    [RelayCommand]
    private async Task RetryBallots()
    {
        if (!ShowBallots || conversationId is not Guid id) return;
        var ticket = navigationGeneration;
        try
        {
            await LoadBallotsAsync(id, ticket);
            if (CurrentChat(id, ticket) && BallotLoaded && !BallotLoadFailed) Status = "";
        }
        catch (CommunityClientException) { if (CurrentChat(id, ticket)) Status = "Не удалось обновить голосования."; }
        catch (AccountClientException ex) { if (CurrentChat(id, ticket)) FailSession(ex); }
        catch (OperationCanceledException) { }
    }

    private void RefreshBallotBrowse()
    {
        filteredBallots = GroupBallotBrowse.Filter(Ballots, BallotSearch, BallotStatusIndex, BallotSortIndex);
        OnPropertyChanged(nameof(FilteredBallots));
        OnPropertyChanged(nameof(HasBallotFilters));
        OnPropertyChanged(nameof(BallotResultCount));
        RefreshBallotStates();
    }

    private void RefreshBallotStates()
    {
        OnPropertyChanged(nameof(NoBallots));
        OnPropertyChanged(nameof(NoBallotMatches));
        OnPropertyChanged(nameof(BallotErrorText));
    }

    private async Task CopyBallotSummaryAsync(GroupBallotRow row)
    {
        if (!ShowBallots || !Ballots.Contains(row)) return;
        try
        {
            if (clipboardWriter is null) throw new InvalidOperationException("Clipboard unavailable");
            await clipboardWriter(GroupBallotBrowse.Summary(row));
            BallotFeedback = "Сводка скопирована.";
        }
        catch (Exception)
        {
            BallotFeedback = "Не удалось скопировать сводку.";
        }
    }
}
