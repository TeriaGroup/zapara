using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Features.Schedule;

public sealed partial class ScheduleViewModel
{
    [ObservableProperty] private string lessonJumpStatus = "";
    public bool HasLessons => Lessons.Count > 0;
    public event Action? DeadlineFocusRequested;
    [RelayCommand] private void JumpNearestLesson()
    {
        var next = Lessons.FirstOrDefault(row => row.IsNext);
        if (next is null)
        { LessonJumpStatus = Lessons.Count == 0 ? "В выбранном дне пар нет." : "На сегодня пары закончились."; return; }
        LessonJumpStatus = "";
        next.ShowDetails = true;
        LessonFocusRequested?.Invoke(next);
    }
    [RelayCommand] private void JumpDeadlines() => DeadlineFocusRequested?.Invoke();
}
