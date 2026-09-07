using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Vograph.Core.Models;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Dialogs;
using Xunit;

namespace Vograph.Desktop.Tests;

public class GroupPickerTests
{
    private static readonly List<Group> Groups = new()
    {
        new Group { Id = "3313", Name = "А863С" },
        new Group { Id = "3031", Name = "09С31" },
        new Group { Id = "9999", Name = "Е452Б" },
        new Group { Id = "1", Name = "О3313" },
    };

    [Theory]
    [InlineData("A863", "А863С", true)]    // Latin A typed for Cyrillic А
    [InlineData("а863", "А863С", true)]    // lower-case Cyrillic
    [InlineData("09c", "09С31", true)]     // Latin c for Cyrillic С
    [InlineData("3313", "О3313", true)]
    [InlineData("3313", "А863С", false)]
    public void Matches_Ignores_Case_And_Latin_Lookalikes(string query, string name, bool expected) =>
        Assert.Equal(expected, GroupSearch.Matches(name, query));

    [Fact]
    public void Filter_Follows_Query_And_Preselects_Current_Group()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        Xunit.Assert.NotNull(db); // fixture only initializes Loc.Current

        var vm = new GroupPickerDialogViewModel(Groups, currentId: "3313");
        Assert.Equal(4, vm.Filtered.Count);
        Assert.Equal("3313", vm.Selected!.Id);
        Assert.True(vm.ConfirmCommand.CanExecute(null));

        vm.Query = "e45"; // Latin e → Е
        Assert.Single(vm.Filtered);
        Assert.Equal("Е452Б", vm.Filtered[0].Name);

        vm.Selected = null;
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Groups_Are_Sorted_By_Name()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        var vm = new GroupPickerDialogViewModel(Groups, null);
        Assert.Equal(new[] { "09С31", "А863С", "Е452Б", "О3313" }, vm.Filtered.Select(g => g.Name));
    }

    [Theory]
    [InlineData("09С31", "9c", 1, 2)]       // Latin c for Cyrillic С
    [InlineData("А863С", "a86", 0, 3)]
    [InlineData("О3313", "3313", 1, 4)]
    [InlineData("А863С", "  86 ", 1, 2)]    // the query is trimmed, the name is not moved
    public void MatchRange_Points_Into_The_Original_Name(string name, string query, int start, int length) =>
        Assert.Equal((start, length), GroupSearch.MatchRange(name, query));

    [Fact]
    public void MatchRange_Is_Null_Without_A_Match_Or_A_Query()
    {
        Assert.Null(GroupSearch.MatchRange("А863С", ""));
        Assert.Null(GroupSearch.MatchRange("А863С", "   "));
        Assert.Null(GroupSearch.MatchRange("А863С", "zzz"));
    }

    [AvaloniaFact]
    public void Picker_Highlights_The_Match_And_Down_Moves_Into_The_List()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        var vm = new GroupPickerDialogViewModel(Groups, null);
        var view = new GroupPickerDialogView { DataContext = vm };
        var window = new Window { Width = 520, Height = 480, Content = view };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.Query = "9c";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var text = window.GetVisualDescendants().OfType<HighlightText>().Single(t => t.Source == "09С31");
        var runs = text.Inlines!.OfType<Run>().ToList();
        Assert.Equal(new[] { "0", "9С", "31" }, runs.Select(r => r.Text));
        Assert.Equal(FontWeight.SemiBold, runs[1].FontWeight);

        Assert.Null(vm.Selected); // narrowing to the one match is not a pick
        var search = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SearchBox");
        search.Focus();
        window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("09С31", vm.Selected!.Name);
        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        Assert.True(list.IsKeyboardFocusWithin);
    }

    /// <summary>T11-R1: a query that narrows Filtered to one match must not pick it — Confirm (and the Enter
    /// key, which DialogHostViewModel.ConfirmCurrent routes through CanExecute) stays gated until the user
    /// actually picks a group, by a click or by ↓ into the list.</summary>
    [Fact]
    public void Confirm_Waits_For_An_Explicit_Pick_Even_When_Filtering_Leaves_One_Match()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        var vm = new GroupPickerDialogViewModel(Groups, null);
        var host = new DialogHostViewModel();
        _ = host.ShowAsync(vm);

        vm.Query = "9c"; // narrows Filtered to the one match ("09С31"), but must not pick it
        Assert.Single(vm.Filtered);
        Assert.Null(vm.Selected);
        Assert.False(vm.ConfirmCommand.CanExecute(null));

        host.ConfirmCurrentCommand.Execute(null); // Enter: gated by CanConfirm, must do nothing
        Assert.False(vm.Completion.IsCompleted);

        vm.Selected = vm.Filtered[0]; // the explicit pick (a click, or ↓ into the list)
        Assert.True(vm.ConfirmCommand.CanExecute(null));

        host.ConfirmCurrentCommand.Execute(null); // Enter again: now it confirms, with the picked group
        Assert.True(vm.Completion.IsCompleted);
        Assert.Equal("09С31", vm.Selected!.Name);
    }
}
