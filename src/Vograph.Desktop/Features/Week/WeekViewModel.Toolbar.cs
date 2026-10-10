using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Features.Week;

/// <summary>#19 (D-03): тулбар недели в одну строку — ‹ «5–11 окт. · чётная» › «Сегодня», поиск и «⋯».
/// Сравнение недель и обзор зачётов открываются из «⋯» и не стоят между тулбаром и неделей.</summary>
public sealed partial class WeekViewModel
{
    [ObservableProperty] private bool searchOpen;
    [ObservableProperty] private bool showComparePicker;
    [ObservableProperty] private bool showAssessmentPicker;

    public string WeekHeading => WeekRange.Length == 0 ? Title : $"{WeekRange} · {App.I18n.FormatParity(ParityIndex == 0)}";
    public bool ShowSearch => SearchOpen || SearchQuery.Trim().Length > 0;

    partial void OnWeekRangeChanged(string value) => OnPropertyChanged(nameof(WeekHeading));
    partial void OnSearchOpenChanged(bool value) => OnPropertyChanged(nameof(ShowSearch));

    [RelayCommand] private void ToggleSearch()
    {
        if (ShowSearch) { SearchQuery = ""; SearchOpen = false; }
        else SearchOpen = true;
        OnPropertyChanged(nameof(ShowSearch));
    }
    [RelayCommand] private void OpenComparePicker() => ShowComparePicker = !ShowComparePicker;
    [RelayCommand] private void OpenAssessmentPicker() => ShowAssessmentPicker = !ShowAssessmentPicker;
    [RelayCommand] private void ToggleOnlyDaysWithClasses() => OnlyDaysWithClasses = !OnlyDaysWithClasses;
}
