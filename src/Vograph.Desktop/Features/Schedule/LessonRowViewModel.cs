using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Schedule;

public sealed partial class LessonRowViewModel : ObservableObject
{
    private readonly ScheduleViewModel _owner;

    /// <param name="index">Position in the day; drives the appear cascade (Appear.Index).</param>
    public LessonRowViewModel(LessonRow row, ScheduleViewModel owner, int index)
    {
        Row = row;
        showDetails = row.IsNext;
        _owner = owner;
        Index = index;
        RefreshRelated(row);
    }

    public LessonRow Row { get; private set; }
    public void Update(LessonRow row)
    {
        Row=row;
        RefreshRelated(row);
        foreach(var name in new[]{nameof(DisplayName),nameof(TeacherLine),nameof(CanOpenTeacher),nameof(Note),nameof(RoomText),nameof(TypeLabel),nameof(HasType),nameof(IsLectureType),nameof(IsPracticeType),nameof(IsLabType),nameof(IsConsultType),nameof(IsCreditType),nameof(IsExamType),nameof(IsCourseType),nameof(IsPast),nameof(IsNext),nameof(IsUpcoming),nameof(CanShowMap),nameof(PriorityCaption)})OnPropertyChanged(name);
    }
    private void RefreshRelated(LessonRow row)
    {
        Friends=row.Friends.Select(x=>new FriendMarkViewModel(x)).ToArray();OnPropertyChanged(nameof(Friends));OnPropertyChanged(nameof(HasFriends));
        var ordered=row.Homework.Select(item=>{var existing=Homework.FirstOrDefault(x=>x.Id==item.Id);if(existing is null)return new HomeworkItemViewModel(item,this);existing.Update(item);return existing;}).ToArray();
        for(var i=0;i<ordered.Length;i++){var old=Homework.IndexOf(ordered[i]);if(old<0)Homework.Insert(i,ordered[i]);else if(old!=i)Homework.Move(old,i);}
        while(Homework.Count>ordered.Length)Homework.RemoveAt(Homework.Count-1);
        OnPropertyChanged(nameof(HasHomework));OnPropertyChanged(nameof(ShowHomeworkDetails));
        var mark=row.Subgroup;
        var sourceScope=_owner.App.Profile.DatabasePath+":"+row.Lesson.GroupId;
        var renderEpoch=_owner.SubgroupRenderEpoch;
        var options=mark is {ShowChooser:true}?mark.Options.Select(x=>new SubgroupOptionViewModel(x.Id,x.Label,x.Id==mark.ChosenId,sourceScope,renderEpoch,
            new AsyncRelayCommand(()=>_owner.PickSubgroupAsync(row.Lesson.GroupId,sourceScope,renderEpoch,mark.StreamId,x.Id)))).ToArray():[];
        if(!SubgroupOptions.Select(x=>(x.Id,x.Label,x.IsChosen,x.SourceScope,x.RenderEpoch)).SequenceEqual(options.Select(x=>(x.Id,x.Label,x.IsChosen,x.SourceScope,x.RenderEpoch)))){SubgroupOptions=options;OnPropertyChanged(nameof(SubgroupOptions));}
        var chosen=mark?.Options.FirstOrDefault(x=>x.Id==mark.ChosenId)?.Label;
        SubgroupPrompt=mark is null?"":chosen is null?Loc.Current.T("subgroupPick"):Loc.Current.T("subgroupYours")+" · "+chosen;
        OnPropertyChanged(nameof(SubgroupPrompt));OnPropertyChanged(nameof(HasSubgroup));
    }
    public string PriorityCaption => IsNext ? Owner.DayPriorityCaption : "";
    [ObservableProperty] private bool showDetails;
    public bool ShowHomeworkDetails => ShowDetails && HasHomework;
    public string DetailsCaption => ShowDetails ? "Свернуть" : "Подробнее";
    partial void OnShowDetailsChanged(bool value){OnPropertyChanged(nameof(ShowHomeworkDetails));OnPropertyChanged(nameof(DetailsCaption));}
    [RelayCommand] private void ToggleDetails()=>ShowDetails=!ShowDetails;
    public int Index { get; }
    public ScheduleViewModel Owner => _owner;

    public string TimeStart => Row.TimeStart;
    public string TimeEnd => Row.TimeEnd;
    public string? NextDateText => Row.NextDateText;
    public bool HasNextDate => Row.NextDateText is not null;
    public string DisplayName => Row.DisplayName;
    public string TeacherLine => Row.OriginalName is null ? Row.Teacher : $"{Row.Teacher} · {Loc.Current.T("originalLabel", Row.OriginalName)}";
    public bool CanOpenTeacher => _owner.CanOpenTeacher(this);
    public string? Note => Row.Note;
    public bool HasNote => Row.Note is not null;
    public string TypeLabel => Row.TypeLabel;
    public bool HasType => !string.IsNullOrWhiteSpace(Row.TypeLabel);
    private LessonTypeBadgeKind TypeKind => LessonTypeBadge.KindOf(Row.Lesson.TypeRaw);
    public bool IsLectureType => TypeKind == LessonTypeBadgeKind.Lecture;
    public bool IsPracticeType => TypeKind == LessonTypeBadgeKind.Practice;
    public bool IsLabType => TypeKind == LessonTypeBadgeKind.Lab;
    public bool IsConsultType => TypeKind == LessonTypeBadgeKind.Consult;
    public bool IsCreditType => TypeKind == LessonTypeBadgeKind.Credit;
    public bool IsExamType => TypeKind == LessonTypeBadgeKind.Exam;
    public bool IsCourseType => TypeKind == LessonTypeBadgeKind.Course;
    public string RoomText => Row.RoomText;
    public string? BuildingTag => Row.BuildingTag;
    public bool HasBuildingTag => Row.BuildingTag is not null;
    public bool IsRemote => Row.IsRemote;
    public bool HasConflict=>Row.HasConflict;
    public bool IsPast => Row.IsPast;
    public bool IsNext => Row.IsNext;
    public bool IsUpcoming => Row.IsUpcoming;
    public IReadOnlyList<FriendMarkViewModel> Friends { get; private set; } = [];
    public bool HasFriends => Friends.Count > 0;
    public ObservableCollection<HomeworkItemViewModel> Homework { get; } = [];
    public bool HasHomework => Homework.Count > 0;
    public bool CanShowMap => Row.Map is { HasMap: true } && !Row.IsRemote;
    public bool HasSubgroup => Row.Subgroup is { ShowChooser: true };
    public string SubgroupPrompt { get; private set; } = "";
    public IReadOnlyList<SubgroupOptionViewModel> SubgroupOptions { get; private set; } = [];

    [RelayCommand]
    private void ShowMap() => _owner.ShowMap(this);

    [RelayCommand] private Task Rename() => _owner.RenameAsync(this);
    [RelayCommand] private void OpenHomeworks() => _owner.OpenSubjectHomeworks(this);
    [RelayCommand] private Task OpenTeacher() => _owner.OpenTeacherAsync(this);
    [RelayCommand] private void Discuss() => _owner.DiscussLesson(this);
    [RelayCommand] private Task AddHomework() => _owner.AddHomeworkAsync(this);
}

public sealed class SubgroupOptionViewModel
{
    public SubgroupOptionViewModel(string id, string label, bool chosen, string sourceScope, int renderEpoch, IAsyncRelayCommand selectCommand)
    {
        Id = id;
        Label = label;
        IsChosen = chosen;
        SourceScope = sourceScope;
        RenderEpoch = renderEpoch;
        SelectCommand = selectCommand;
    }

    public string Id { get; }
    public string Label { get; }
    public bool IsChosen { get; }
    public string SourceScope { get; }
    public int RenderEpoch { get; }
    public IAsyncRelayCommand SelectCommand { get; }
}

public sealed class FriendMarkViewModel
{
    private readonly FriendMark _mark;
    public FriendMarkViewModel(FriendMark mark) => _mark = mark;
    public int ColorIndex => _mark.ColorIndex;
    public DotFill Fill => _mark.Fill;
    public string Tooltip => _mark.Tooltip;
    public string GroupName => _mark.GroupName;
    public bool ShowLessonStatus => _mark.ShowLessonStatus;
    public string LessonStatusCaption => _mark.HasLesson switch
    {
        true => "Пара в это время", false => "Нет пары в это время", null => "Нет данных"
    };
    public double Opacity => _mark.Fill == DotFill.Off ? 0.6 : 1.0;
}

public sealed partial class HomeworkItemViewModel : ObservableObject
{
    public HomeworkItemViewModel(HomeworkItem item, LessonRowViewModel row)
    {
        Item = item;
        Row = row;
    }

    public HomeworkItem Item { get; private set; }
    internal void Update(HomeworkItem item)
    {
        Item=item;
        foreach(var name in new[]{nameof(Text),nameof(Label),nameof(Status),nameof(IsDone),nameof(IsApproaching),nameof(IsBurning),nameof(IsUrgent),nameof(IsOverdue),nameof(DoneLabel)})OnPropertyChanged(name);
    }
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
    public string DoneLabel => Loc.Current.T(IsDone ? "hwUndo" : "hwMarkDone");

    [RelayCommand] private Task ToggleDone() => Row.Owner.ToggleDoneAsync(this);
    [RelayCommand] private Task Edit() => Row.Owner.EditHomeworkAsync(this);
    [RelayCommand] private Task Delete() => Row.Owner.DeleteHomeworkAsync(this);
}
