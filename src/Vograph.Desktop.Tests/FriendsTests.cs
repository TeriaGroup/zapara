using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Core.Models;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Features.Friends;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class FriendsTests : UiTest
{
    private static readonly DateTime Sun6 = new(2026, 9, 6, 12, 0, 0);

    [Fact]
    public void Marks_Are_Computed_By_The_Shared_Helper()
    {
        using var db = TestDb.Create();
        var lesson = db.Services.Db.GetLessons("3313", 1, 1).Single(l => l.TimeStart == "09:00");
        var marks = FriendMarks.Compute(db.Services.Intersections, lesson, new DateTime(2026, 9, 14), db.Services.Db.GetFriends(), db.Services.Db.GetSettings(), db.Services.Loc);
        var m = Assert.Single(marks);
        Assert.Equal(("09С31", 0, DotFill.Full), (m.GroupName, m.ColorIndex, m.Fill)); // physics in 493 at the same time
        Assert.Contains("Иван", m.Tooltip);
    }

    [Fact]
    public async Task Load_Add_Color_Names_Toggle_Remove()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var changed = 0;
        shell.ScheduleChanged += () => changed++;
        var vm = new FriendsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();

        var first = Assert.Single(vm.Friends);
        Assert.Equal(("09С31", "Иван", true, 0), (first.GroupName, first.MemberNames, first.Enabled, first.ColorIndex));
        Assert.True(vm.CanAdd);
        Assert.Equal(5, first.ColorOptions.Count); // nobody else uses a color yet

        // add: the picker offers neither my group nor existing friends
        var add = vm.AddCommand.ExecuteAsync(null);
        var picker = await Waits.ForDialogAsync<GroupPickerDialogViewModel>(shell);
        Assert.Equal(new[] { "Е452Б" }, picker.Filtered.Select(g => g.Name));
        picker.Selected = picker.Filtered[0];
        picker.ConfirmCommand.Execute(null);
        await add;
        Assert.Equal(2, vm.Friends.Count);
        var second = vm.Friends[1];
        Assert.Equal(("Е452Б", 1), (second.GroupName, second.ColorIndex)); // first free color
        Assert.Equal(1, changed);
        Assert.Contains(db.Services.Toasts.Items, t => t.Text == "Группа Е452Б добавлена");
        Assert.DoesNotContain(first.ColorOptions, o => o.Index == 1); // taken by the second friend

        // color, names, enabled
        await vm.SetColorAsync(second, 4);
        Assert.Equal(FriendPalette.Hex[4], db.Services.Db.GetFriends().Single(f => f.GroupName == "Е452Б").ColorHex);
        second.MemberNames = "Петя";
        await second.CommitNamesCommand.ExecuteAsync(null);
        Assert.Equal("Петя", db.Services.Db.GetFriends().Single(f => f.GroupName == "Е452Б").MemberNames);
        first.Enabled = false;
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await Waits.Until(() => !db.Services.Db.GetFriends().Single(f => f.GroupName == "09С31").Enabled, "09С31 disabled");
        Assert.False(db.Services.Db.GetFriends().Single(f => f.GroupName == "09С31").Enabled);
        Assert.True(changed >= 3);

        // remove with confirmation
        var remove = vm.RemoveAsync(second);
        var confirm = await Waits.ForDialogAsync<ConfirmDialogViewModel>(shell);
        Assert.Contains("Е452Б", confirm.Message);
        confirm.ConfirmCommand.Execute(null);
        await remove;
        Assert.Single(vm.Friends);
        Assert.Single(db.Services.Db.GetFriends());
    }

    /// <summary>T7 #5: a reload reconciles the list in place — the item the view holds survives, no Reset is raised.</summary>
    [Fact]
    public async Task Reload_Keeps_Item_Instances_And_Never_Resets()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        var first = Assert.Single(vm.Friends);
        var resets = 0;
        vm.Friends.CollectionChanged += (_, e) => { if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++; };

        db.Services.Db.InsertFriend(new FriendGroup { GroupName = "Е452Б", ColorHex = FriendPalette.Hex[1], Enabled = true, MemberNames = "" });
        await vm.LoadAsync();

        Assert.Equal(0, resets);
        Assert.Same(first, vm.Friends[0]);
        Assert.Equal("Е452Б", vm.Friends[1].GroupName);

        db.Services.Db.DeleteFriend(first.Model.Id);
        await vm.LoadAsync();
        Assert.Equal(0, resets);
        Assert.Equal("Е452Б", Assert.Single(vm.Friends).GroupName);
    }

    /// <summary>T7 #7: group numbers are compared case-insensitively when the picker hides existing friends.</summary>
    [Fact]
    public async Task Add_Hides_Existing_Friends_Regardless_Of_Case()
    {
        using var db = TestDb.Create();
        db.Services.Db.InsertFriend(new FriendGroup { GroupName = "е452б", ColorHex = FriendPalette.Hex[1], Enabled = true, MemberNames = "" });
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();

        var add = vm.AddCommand.ExecuteAsync(null);
        var picker = await Waits.ForDialogAsync<GroupPickerDialogViewModel>(shell);
        Assert.Empty(picker.Filtered); // 09С31 and е452б are friends already, 3313 is mine
        picker.CancelCommand.Execute(null);
        await add;
    }

    /// <summary>T7 #6: Add and Remove reload once — their own ScheduleChanged echo must not reload a second time.</summary>
    [Fact]
    public async Task Add_Reloads_Once()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        var loads = 0;
        // Every LoadAsync assigns a fresh TickLabels array, so its PropertyChanged counts the loads themselves —
        // the collection would not show a second reload once SyncFriends reconciles in place.
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FriendsViewModel.TickLabels)) loads++; };

        var add = vm.AddCommand.ExecuteAsync(null);
        var picker = await Waits.ForDialogAsync<GroupPickerDialogViewModel>(shell);
        picker.Selected = picker.Filtered[0];
        picker.ConfirmCommand.Execute(null);
        await add;
        await Task.Delay(150, TestContext.Current.CancellationToken); // an event-driven second reload would land here

        Assert.Equal(1, loads);
        Assert.Equal(2, vm.Friends.Count);
    }

    /// <summary>T7 #4: a name edit that lands while a reload swaps the model must not be lost — the write happens
    /// from a copy, and the in-memory model is updated only after the write succeeded.</summary>
    [Fact]
    public async Task Save_Writes_The_Edited_Values_Not_The_Model_Reference()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        var item = Assert.Single(vm.Friends);
        var stale = item.Model;

        item.MemberNames = "Иван, Пётр";
        var save = vm.SaveAsync(item);
        Assert.Equal("Иван", stale.MemberNames); // untouched until the write is through
        await save;

        Assert.Equal("Иван, Пётр", db.Services.Db.GetFriends().Single().MemberNames);
        Assert.Equal("Иван, Пётр", item.Model.MemberNames);
    }

    [Fact]
    public async Task Friend_editor_cancel_and_conflict_keep_saved_values_and_draft_separate()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        var item = Assert.Single(vm.Friends);
        item.BeginEditCommand.Execute(null);
        item.DraftGroupName = "Е452Б";
        item.DraftMemberNames = "Петя";
        item.DraftColorIndex = 4;
        Assert.Equal("09С31", db.Services.Db.GetFriends().Single().GroupName);
        var discard = item.CancelEditCommand.ExecuteAsync(null);
        var discardDialog = await Waits.ForDialogAsync<ConfirmDialogViewModel>(shell);
        discardDialog.ConfirmCommand.Execute(null);
        await discard;
        Assert.Equal(("09С31", "Иван", FriendPalette.Hex[0]),
            (db.Services.Db.GetFriends().Single().GroupName, db.Services.Db.GetFriends().Single().MemberNames, db.Services.Db.GetFriends().Single().ColorHex));

        item.BeginEditCommand.Execute(null);
        item.DraftGroupName = "Е452Б";
        item.DraftMemberNames = "Петя";
        var remote = db.Services.Db.GetFriends().Single();
        remote.MemberNames = "Обновлено другим действием";
        db.Services.Db.UpdateFriend(remote);
        await item.SaveDraftCommand.ExecuteAsync(null);
        Assert.True(item.IsEditing);
        Assert.Equal("Петя", item.DraftMemberNames);
        Assert.Contains("изменились", item.DraftError);
        Assert.Equal("09С31", db.Services.Db.GetFriends().Single().GroupName);
        Assert.Equal("Обновлено другим действием", db.Services.Db.GetFriends().Single().MemberNames);
    }

    [Fact]
    public async Task Dirty_friend_editor_requires_explicit_discard_and_keeps_draft_when_cancelled()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        var item = Assert.Single(vm.Friends);
        item.BeginEditCommand.Execute(null);
        item.DraftMemberNames = "Несохранённое имя";

        var pending = item.CancelEditCommand.ExecuteAsync(null);
        var confirm = await Waits.ForDialogAsync<ConfirmDialogViewModel>(shell);
        confirm.CancelCommand.Execute(null);
        await pending;

        Assert.True(item.IsEditing);
        Assert.Equal("Несохранённое имя", item.DraftMemberNames);
        Assert.Equal("Иван", db.Services.Db.GetFriends().Single().MemberNames);
    }

    [Fact]
    public async Task Friend_editor_saves_group_names_and_color_together()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        var item = Assert.Single(vm.Friends);
        item.BeginEditCommand.Execute(null);
        item.DraftGroupName = "Е452Б";
        item.DraftMemberNames = "Петя";
        item.DraftColorIndex = 4;
        await item.SaveDraftCommand.ExecuteAsync(null);
        var saved = Assert.Single(db.Services.Db.GetFriends());
        Assert.Equal(("Е452Б", "Петя", FriendPalette.Hex[4]), (saved.GroupName, saved.MemberNames, saved.ColorHex));
        Assert.False(item.IsEditing);
    }

    [Fact]
    public async Task Pending_friend_save_prevents_cancel_or_remove_from_replacing_its_snapshot()
    {
        using var db = TestDb.Create(); var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => Sun6); await vm.LoadAsync();
        var item = Assert.Single(vm.Friends); item.BeginEditCommand.Execute(null);
        item.DraftMemberNames = "Новый черновик"; item.IsSavingDraft = true;
        item.CancelEditCommand.Execute(null); await vm.RemoveAsync(item);
        Assert.True(item.IsEditing); Assert.Single(db.Services.Db.GetFriends());
        Assert.Equal("Новый черновик", item.DraftMemberNames);
        item.IsSavingDraft = false;
    }

    [Fact]
    public async Task Strictness_And_Always_Show_Persist_And_Drive_The_Preview()
    {
        using var db = TestDb.Create();
        db.Services.Db.InsertFriend(new FriendGroup { GroupName = "Е452Б", ColorHex = FriendPalette.Hex[1], Enabled = true, MemberNames = "" }); // no lessons → never present
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();

        Assert.Equal(50, vm.Strictness);
        Assert.Equal(new[] { "корпус", "этаж", "аудитория" }, vm.TickLabels);
        Assert.True(vm.HasPreview);
        Assert.StartsWith("Пн 14.09 · 09:00 · Матан", vm.PreviewLine);   // nearest odd Monday: friend sits in 493 too
        Assert.Single(vm.PreviewMarks);                                   // the absent friend is hidden
        Assert.Equal(DotFill.Full, vm.PreviewMarks[0].Fill);

        vm.AlwaysShowAll = true;
        await vm.RefreshPreviewAsync();
        Assert.True(db.Services.Db.GetSettings().AlwaysShowAllTrafficLights);
        Assert.Equal(2, vm.PreviewMarks.Count);
        Assert.Equal(DotFill.Off, vm.PreviewMarks[1].Fill);

        vm.Strictness = 100;
        await vm.RefreshPreviewAsync();
        Assert.Equal(100, db.Services.Db.GetSettings().IntersectionStrictness);
        Assert.Equal("аудитория", vm.StrictnessLabel);
        var axaml = File.ReadAllText(Path.Combine(ResourceKeysTests.RepoRoot(), "src", "Vograph.Desktop", "Features", "Friends", "FriendsView.axaml"));
        Assert.DoesNotContain("Text=\"{Binding StrictnessLabel}\"", axaml);
        Assert.Equal(DotFill.Full, vm.PreviewMarks[0].Fill);              // same room still qualifies at 100
    }

    [Fact]
    public void Always_Show_Does_Not_Describe_An_Unloaded_Friend_As_Absent()
    {
        using var db = TestDb.Create();
        db.Services.Db.InsertFriend(new FriendGroup { GroupName = "Нет в каталоге", ColorHex = FriendPalette.Hex[1], Enabled = true });
        var settings = db.Services.Db.GetSettings();
        settings.AlwaysShowAllTrafficLights = true;
        var lesson = db.Services.Db.GetLessons("3313", 1, 1).First(l => l.TimeStart == "09:00");

        var marks = FriendMarks.Compute(db.Services.Intersections, lesson, new DateTime(2026, 9, 14),
            db.Services.Db.GetFriends(), settings, db.Services.Loc);

        Assert.Equal("09С31", Assert.Single(marks).GroupName);
    }

    [Fact]
    public async Task Forecast_Shows_The_Ongoing_Pair_With_Friend_And_Skips_Finished_Today()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var now = new DateTime(2026, 9, 14, 9, 30, 0);
        var vm = new FriendsViewModel(db.Services, shell, () => now);
        await vm.LoadAsync();

        var ongoing = Assert.Single(vm.Encounters, row => row.When.Contains("09:00"));
        Assert.Equal("09С31", ongoing.GroupName);
        Assert.Contains("Иван", ongoing.GroupDisplay);
        Assert.Contains("аудитории", ongoing.Place);
        Assert.Equal(0, vm.StrictnessIndex);

        now = new DateTime(2026, 9, 14, 14, 30, 0);
        await vm.LoadAsync();
        Assert.Empty(vm.Encounters);
        Assert.Equal("В ближайшие две недели пересечений нет", vm.ForecastStatus);
    }

    [Fact]
    public async Task Encounter_opens_its_absolute_day_and_exact_pair_without_changing_own_group()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => new DateTime(2026, 9, 14, 9, 30, 0));
        await vm.LoadAsync();
        var encounter = Assert.Single(vm.Encounters, row => row.TimeStart == "09:00");
        var originalGroup = db.Services.Db.GetSettings().MyGroupId;
        vm.OpenEncounterCommand.Execute(encounter);
        var schedule = Assert.IsType<Vograph.Desktop.Features.Schedule.ScheduleViewModel>(shell.Current);
        await Waits.Until(() => schedule.Date.Date == encounter.Date.Date &&
            schedule.Lessons.Any(row => row.Row.Lesson.SubjectRaw == encounter.SubjectRaw && row.TimeStart == encounter.TimeStart && row.ShowDetails),
            "encounter exact pair focused");
        Assert.Equal(originalGroup, db.Services.Db.GetSettings().MyGroupId);
    }

    [Fact]
    public async Task Encounter_from_previous_group_cannot_open_current_groups_schedule()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => new DateTime(2026, 9, 14, 9, 30, 0));
        await vm.LoadAsync();
        var stale = Assert.Single(vm.Encounters, row => row.TimeStart == "09:00");
        shell.NavigateTo(SectionKey.Settings);
        var before = shell.CurrentKey;
        var settings = db.Services.Db.GetSettings(); settings.MyGroupId = "Е452Б"; db.Services.Db.SaveSettings(settings);
        vm.OpenEncounterCommand.Execute(stale);
        Assert.Equal(before, shell.CurrentKey);
        settings.MyGroupId = stale.OwnGroupId; db.Services.Db.SaveSettings(settings); shell.RaiseGroupChanged();
        vm.OpenEncounterCommand.Execute(stale);
        Assert.Equal(before, shell.CurrentKey);
    }

    [Fact]
    public async Task Forecast_Rejects_An_Imported_Friend_Entry_For_The_Selected_Group()
    {
        using var db = TestDb.Create();
        foreach (var friend in db.Services.Db.GetFriends()) db.Services.Db.DeleteFriend(friend.Id);
        db.Services.Db.InsertFriend(new FriendGroup { GroupName = "А863С", ColorHex = FriendPalette.Hex[0], Enabled = true });
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => new DateTime(2026, 9, 14, 8, 0, 0));

        await vm.LoadAsync();

        Assert.Empty(vm.Encounters);
    }

    [AvaloniaFact]
    public async Task Friends_Render_Both_Themes()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        shell.Register(SectionKey.Friends, () => new FriendsViewModel(db.Services, shell, () => Sun6));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        shell.NavigateTo(SectionKey.Friends);
        var vm = Assert.IsType<FriendsViewModel>(shell.Current);
        await Waits.Until(() => vm.HasPreview, "friends preview");
        Pump();
        SetTheme(ThemeVariant.Dark);
        Frames.Capture(window, "friends-dark");

        var friend = Assert.Single(vm.Friends);
        friend.BeginEditCommand.Execute(null);
        Pump();
        Frames.Capture(window, "friends-color-editor-dark");
        friend.CancelEditCommand.Execute(null);
        Pump();

        SetTheme(ThemeVariant.Light);
        Frames.Capture(window, "friends-light");
        AssertNoBindingErrors();
    }

    [Fact]
    public async Task Five_Friends_Disable_Add()
    {
        using var db = TestDb.Create();
        foreach (var (name, i) in new[] { ("А1", 1), ("А2", 2), ("А3", 3), ("А4", 4) })
            db.Services.Db.InsertFriend(new FriendGroup { GroupName = name, ColorHex = FriendPalette.Hex[i], Enabled = true, MemberNames = "" });
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        Assert.Equal(5, vm.Friends.Count);
        Assert.False(vm.CanAdd);
        Assert.Equal("5 из 5", vm.CountText);

        await vm.AddCommand.ExecuteAsync(null);
        Assert.Null(shell.Dialogs.Current); // no picker at the cap
    }

    [Fact]
    public async Task Removing_The_Last_Friend_Empties_The_Preview()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        Assert.True(vm.HasPreview);

        var remove = vm.RemoveAsync(vm.Friends.Single());
        var confirm = await Waits.ForDialogAsync<ConfirmDialogViewModel>(shell);
        confirm.ConfirmCommand.Execute(null);
        await remove;

        Assert.Empty(vm.Friends);
        Assert.True(vm.CanAdd);
        Assert.False(vm.HasPreview);
        Assert.Equal("В ближайшие две недели пересечений нет", vm.PreviewLine);
    }

    [Fact]
    public async Task Detach_Stops_Reloads()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        vm.Detach();

        db.Services.Db.InsertFriend(new FriendGroup { GroupName = "Е452Б", ColorHex = FriendPalette.Hex[1], Enabled = true, MemberNames = "" });
        shell.RaiseScheduleChanged();
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Single(vm.Friends); // a live section would show two
    }

    /// <summary>The production path of the switch: save → ScheduleChanged → reload → preview, without calling RefreshPreviewAsync by hand.</summary>
    [Fact]
    public async Task Always_Show_All_Reaches_The_Preview_Through_The_Production_Path()
    {
        using var db = TestDb.Create();
        db.Services.Db.InsertFriend(new FriendGroup { GroupName = "Е452Б", ColorHex = FriendPalette.Hex[1], Enabled = true, MemberNames = "" }); // no lessons → never present
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        Assert.Single(vm.PreviewMarks);

        vm.AlwaysShowAll = true;

        await Waits.Until(() => vm.PreviewMarks.Count == 2, "absent friend appears in the preview");
        Assert.Equal(DotFill.Off, vm.PreviewMarks[1].Fill);
        Assert.True(db.Services.Db.GetSettings().AlwaysShowAllTrafficLights);
    }
}
