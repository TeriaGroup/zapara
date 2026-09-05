using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Schedule;

public sealed partial class LessonRowViewModel : ObservableObject
{
    private readonly ScheduleViewModel _owner;

    public LessonRowViewModel(LessonRow row, ScheduleViewModel owner)
    {
        Row = row;
        _owner = owner;
        Friends = row.Friends.Select(f => new FriendMarkViewModel(f)).ToList();
        Homework = new ObservableCollection<HomeworkItemViewModel>(row.Homework.Select(h => new HomeworkItemViewModel(h, this)));
    }

    public LessonRow Row { get; }
    public ScheduleViewModel Owner => _owner;

    public string TimeStart => Row.TimeStart;
    public string TimeEnd => Row.TimeEnd;
    public string? NextDateText => Row.NextDateText;
    public bool HasNextDate => Row.NextDateText is not null;
    public string DisplayName => Row.DisplayName;
    public string TeacherLine => Row.OriginalName is null ? Row.Teacher : $"{Row.Teacher} · {Loc.Current.T("originalLabel", Row.OriginalName)}";
    public string? Note => Row.Note;
    public bool HasNote => Row.Note is not null;
    public string TypeLabel => Row.TypeLabel;
    public bool HasType => Row.TypeLabel.Length > 0;
    public string RoomText => Row.RoomText;
    public string? BuildingTag => Row.BuildingTag;
    public bool HasBuildingTag => Row.BuildingTag is not null;
    public bool IsRemote => Row.IsRemote;
    public bool IsPast => Row.IsPast;
    public bool IsNext => Row.IsNext;
    public IReadOnlyList<FriendMarkViewModel> Friends { get; }
    public bool HasFriends => Friends.Count > 0;
    public ObservableCollection<HomeworkItemViewModel> Homework { get; }
    public bool HasHomework => Homework.Count > 0;
    public bool CanShowMap => Row.Map is { HasMap: true } && !Row.IsRemote;

    [RelayCommand]
    private void ShowMap() => _owner.ShowMap(this);
}

public sealed class FriendMarkViewModel
{
    private readonly FriendMark _mark;
    public FriendMarkViewModel(FriendMark mark) => _mark = mark;
    public int ColorIndex => _mark.ColorIndex;
    public DotFill Fill => _mark.Fill;
    public string Tooltip => _mark.Tooltip;
    public double Opacity => _mark.Fill == DotFill.Off ? 0.6 : 1.0;
}

public sealed partial class HomeworkItemViewModel : ObservableObject
{
    public HomeworkItemViewModel(HomeworkItem item, LessonRowViewModel row)
    {
        Item = item;
        Row = row;
    }

    public HomeworkItem Item { get; }
    public LessonRowViewModel Row { get; }
    public long Id => Item.Id;
    public string Text => Item.Text;
    public string Label => Item.Label;
    public string Status => Item.Status;
    public bool IsDone => Item.IsDone;
    public bool IsApproaching => Item.Status == "approaching";
    public bool IsBurning => Item.Status == "burning";
    public bool IsUrgent => Item.Status == "burning_urgent";
    public bool IsOverdue => Item.Status == "overdue";
}
