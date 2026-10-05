using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel
{
    [ObservableProperty] private int selectedBallotOptionIndex = 2;
    [ObservableProperty] private bool showRemoveBallotOption;
    private (int Index, string Text)? pendingRemoveBallotOption;
    private string[] BallotValues() => [BallotOptionA, BallotOptionB, BallotOptionC,
        BallotOptionD, BallotOptionE, BallotOptionF];
    public IReadOnlyList<string> BallotOptionLabels => BallotValues().Select((value, index) =>
        $"{index + 1}. {(value.Trim().Length > 0 ? value.Trim() : "пусто")}").ToArray();
    public bool CanMoveBallotOptionUp => CanMoveBallotOption(-1);
    public bool CanMoveBallotOptionDown => CanMoveBallotOption(1);
    public bool CanRemoveBallotOption => CanEditBallotOptions() && SelectedBallotOptionIndex >= 2 &&
        SelectedBallotOptionIndex < 6 && BallotValues()[SelectedBallotOptionIndex].Trim().Length > 0;
    private bool CanEditBallotOptions() => CanCreateBallot && ShowBallotComposer && !IsBusy && !loadingBallotDraft;
    partial void OnSelectedBallotOptionIndexChanged(int value) => NotifyBallotOptionEditor();
    private void NotifyBallotOptionEditor()
    {
        OnPropertyChanged(nameof(BallotOptionLabels));
        OnPropertyChanged(nameof(CanMoveBallotOptionUp));
        OnPropertyChanged(nameof(CanMoveBallotOptionDown));
        OnPropertyChanged(nameof(CanRemoveBallotOption));
    }
    private bool CanMoveBallotOption(int delta)
    {
        var next = SelectedBallotOptionIndex + delta;
        var values = BallotValues();
        return CanEditBallotOptions() && SelectedBallotOptionIndex is >= 0 and < 6 && next is >= 0 and < 6 &&
            values[SelectedBallotOptionIndex].Trim().Length > 0 && values[next].Trim().Length > 0;
    }
    private void SetBallotValues(string[] values)
    {
        loadingBallotDraft = true;
        try
        {
            BallotOptionA = values[0]; BallotOptionB = values[1]; BallotOptionC = values[2];
            BallotOptionD = values[3]; BallotOptionE = values[4]; BallotOptionF = values[5];
        }
        finally { loadingBallotDraft = false; }
        SaveBallotDraft(); NotifyBallotOptionEditor();
    }
    [RelayCommand] private void MoveBallotOptionUp() => MoveBallotOption(-1);
    [RelayCommand] private void MoveBallotOptionDown() => MoveBallotOption(1);
    private void MoveBallotOption(int delta)
    {
        if (!CanMoveBallotOption(delta)) return;
        var values = BallotValues(); var at = SelectedBallotOptionIndex;
        (values[at], values[at + delta]) = (values[at + delta], values[at]);
        SetBallotValues(values); SelectedBallotOptionIndex = at + delta;
    }
    [RelayCommand] private void RequestRemoveBallotOption()
    {
        if (!CanRemoveBallotOption) return;
        pendingRemoveBallotOption = (SelectedBallotOptionIndex, BallotValues()[SelectedBallotOptionIndex]);
        ShowRemoveBallotOption = true;
    }
    [RelayCommand] private void CancelRemoveBallotOption()
    { pendingRemoveBallotOption = null; ShowRemoveBallotOption = false; }
    [RelayCommand] private void ConfirmRemoveBallotOption()
    {
        var pending = pendingRemoveBallotOption; CancelRemoveBallotOption();
        if (pending is not { } choice || !CanEditBallotOptions()) return;
        var values = BallotValues();
        if (choice.Index < 2 || choice.Index >= values.Length || values[choice.Index] != choice.Text)
        { BallotValidation = "Вариант изменился. Проверьте его и подтвердите удаление ещё раз."; return; }
        for (var index = choice.Index; index < values.Length - 1; index++) values[index] = values[index + 1];
        values[^1] = "";
        SetBallotValues(values);
    }
}
