using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Features.Schedule;

public sealed partial class ScheduleViewModel
{
    [ObservableProperty] private string lessonSearch = "";
    public bool HasLessonSearch => LessonSearch.Trim().Length > 0;
    /// <summary>#19 (D-03): поле поиска дня открывается кнопкой в тулбаре.</summary>
    [ObservableProperty] private bool lessonSearchOpen;
    public bool ShowLessonSearch => LessonSearchOpen || HasLessonSearch;
    partial void OnLessonSearchOpenChanged(bool value) => OnPropertyChanged(nameof(ShowLessonSearch));
    [RelayCommand] private void ToggleLessonSearch()
    {
        if (ShowLessonSearch) { LessonSearch = ""; LessonSearchOpen = false; }
        else LessonSearchOpen = true;
        OnPropertyChanged(nameof(ShowLessonSearch));
    }
    public IReadOnlyList<LessonRowViewModel> MatchingLessons
    {
        get
        {
            var words = LessonSearch.Trim().ToLowerInvariant().Replace('ё', 'е')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return words.Length == 0 ? [] : Lessons.Where(row =>
            {
                var text = string.Join(' ', row.DisplayName, row.Row.Lesson.SubjectRaw, row.Row.Teacher,
                    row.RoomText, row.TimeStart).ToLowerInvariant().Replace('ё', 'е');
                return words.All(word => text.Contains(word, StringComparison.Ordinal));
            }).ToArray();
        }
    }
    public bool NoLessonMatches => HasLessonSearch && Lessons.Count > 0 && MatchingLessons.Count == 0;
    partial void OnLessonSearchChanged(string value) => RefreshLessonSearch();
    internal void RefreshLessonSearch()
    { OnPropertyChanged(nameof(HasLessonSearch)); OnPropertyChanged(nameof(ShowLessonSearch)); OnPropertyChanged(nameof(MatchingLessons)); OnPropertyChanged(nameof(NoLessonMatches)); }
    [RelayCommand] private void ClearLessonSearch() => LessonSearch = "";
    [RelayCommand] private void OpenSearchResult(LessonRowViewModel? row)
    {
        if (row is null || !Lessons.Contains(row)) return;
        row.Reveal();
        LessonFocusRequested?.Invoke(row);
    }
}
