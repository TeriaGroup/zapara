using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Core.Models;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkTests : UiTest
{
    private static readonly DateTime Sun6 = new(2026, 9, 6, 12, 0, 0);

    private static Homework Hw(string? due, string status = "pending") =>
        new() { SubjectRawNormalized = "x", Text = "t", CreatedAt = Sun6, TargetNthOccurrence = 1, Status = status, DueDateComputed = due is null ? null : DateTime.Parse(due) };

    [Theory]
    [InlineData("2026-09-06", 0, "burning_urgent")]
    [InlineData("2026-09-07", 3, "burning")]
    [InlineData("2026-09-08", 1, "approaching")]
    [InlineData("2026-09-08", 0, "approaching")]   // ≤ 3 days, nothing in between
    [InlineData("2026-09-09", 0, "approaching")]   // exactly 3 days: the last day of «скоро»
    [InlineData("2026-09-10", 0, "far")]           // 4 days: over the threshold
    [InlineData("2026-09-16", 0, "far")]
    [InlineData("2026-09-16", 2, "far")]
    [InlineData("2026-09-05", 0, "overdue")]
    [InlineData(null, 0, "pending")]
    public void Status_Mirrors_Core_Thresholds_With_An_Explicit_Today(string? due, int lessonsBefore, string expected) =>
        Assert.Equal(expected, HomeworkStatus.Compute(Hw(due), Sun6, lessonsBefore));

    [Fact]
    public void Done_Wins_And_Badge_Counts_Urgent_Burning_Overdue()
    {
        Assert.Equal("done", HomeworkStatus.Compute(Hw("2026-09-05", "done"), Sun6, 0));
        var all = new[] { Hw("2026-09-06"), Hw("2026-09-07"), Hw("2026-09-01"), Hw("2026-09-20"), Hw("2026-09-06", "done"), Hw(null) };
        Assert.Equal(3, HomeworkStatus.BadgeCount(all, Sun6));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, new[] { "burning_urgent", "burning", "approaching", "far", "overdue", "done" }.Select(HomeworkStatus.GroupOrder));
    }

    [Fact]
    public void Composer_Groups_In_Spec_Order_With_Display_Names()
    {
        using var db = TestDb.Create();
        var hw = db.Services.Homework;
        var created = new DateTime(2026, 9, 5, 12, 0, 0);
        hw.AddHomework("пр ОСН РОС ГОС", "конспект", 1, created);   // odd Monday 14.09 → far on Sunday 06.09
        var history = hw.AddHomework("лек ИСТОРИЯ", "глава 1", 1, created); // odd Wednesday → 16.09 → far
        var doneId = hw.AddHomework("лек ФК И СПОРТ", "справка", 1, created);
        hw.MarkDone(doneId, true);

        var model = new HomeworkComposer(db.Services).Compose(Sun6.Date);

        Assert.True(model.HasGroup);
        Assert.Equal((3, 1), (model.Open, model.Done));
        Assert.Equal(new[] { "burning", "far", "done" }, model.Groups.Select(g => g.Status));
        Assert.Equal(new[] { "Горит", "Далеко", "Сдано" }, model.Groups.Select(g => g.Title));
        var burning = model.Groups[0].Items;
        Assert.Equal(new[] { "Матан" }, burning.Select(i => i.Subject).ToArray()); // fixture math is due Mon 07.09 (even)
        Assert.All(burning, i => Assert.Equal("горит завтра", i.Label));
        var far = model.Groups[1].Items.OrderBy(i => i.Subject).ToList();
        Assert.Equal(new[] { "История", "Основы российской государственности" }, far.Select(i => i.Subject));
        Assert.Equal(("История", "лек ИСТОРИЯ", history), (far[0].Subject, far[0].SubjectRaw, far[0].Homework.Id));
        Assert.Equal("срок 16.09", far[0].Label);
        Assert.Equal("сдано", Assert.Single(model.Groups[2].Items).Label);

        var subjects = new HomeworkComposer(db.Services).Subjects();
        Assert.Equal(6, subjects.Count);
        Assert.Contains(subjects, s => s.SubjectRaw == "лек ИСТОРИЯ" && s.Display == "История" && s.TypeLabel == "Лекция");
        Assert.Contains(subjects, s => s.SubjectRaw == "лек ВЫСШ. МАТЕМАТ" && s.Display == "Матан");
    }

    [Theory]
    [InlineData("лек физика", "ФИЗИКА")]
    [InlineData("пр осн рос гос", "ОСН РОС ГОС")]
    [InlineData("иностранный язык", "ИНОСТРАННЫЙ ЯЗЫК")]
    [InlineData("лек", "ЛЕК")]
    public void Orphan_Display_Drops_The_Type_Token_And_Upper_Cases(string normalized, string expected) =>
        Assert.Equal(expected, HomeworkComposer.OrphanDisplay(normalized));

    [Fact]
    public void Homework_Of_A_Subject_Not_In_The_Timetable_Is_Shown_Readably()
    {
        using var db = TestDb.Create();
        db.Services.Homework.AddHomework("лек ФИЗИКА", "задачи 1–3", 1, new DateTime(2026, 9, 5, 12, 0, 0)); // 3313 has no physics
        var model = new HomeworkComposer(db.Services).Compose(Sun6.Date);
        var orphan = model.Groups.SelectMany(g => g.Items).Single(i => i.Homework.Text == "задачи 1–3");
        Assert.Equal("ФИЗИКА", orphan.Subject);      // not the raw lower-cased key «лек физика»
        Assert.Equal("far", orphan.Status);           // no due date → pending → shown as far
        Assert.Equal("Срок: — (нет занятий)", orphan.Label);
    }

    [Fact]
    public async Task ViewModel_Add_Edit_Toggle_Delete_And_Badge()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services) { Clock = () => Sun6 };
        var changed = 0;
        shell.HomeworkChanged += () => changed++;
        var vm = new HomeworkViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        Assert.Equal("Горит", Assert.Single(vm.Groups).Title);   // the fixture homework: Math on Monday
        Assert.Contains("1", vm.Subtitle);

        // add: subject picker → homework dialog
        var add = vm.AddCommand.ExecuteAsync(null);
        var picker = await Waits.ForDialogAsync<SubjectPickerDialogViewModel>(shell);
        picker.Query = "истор";
        picker.Selected = Assert.Single(picker.Filtered);
        picker.ConfirmCommand.Execute(null);
        var dlg = await Waits.ForDialogAsync<HomeworkDialogViewModel>(shell);
        Assert.Equal("Срок: 16.09 (Ср)", dlg.DueText);
        dlg.Text = "глава 1";
        dlg.ConfirmCommand.Execute(null);
        await add;
        Assert.Equal(new[] { "Горит", "Далеко" }, vm.Groups.Select(g => g.Title));
        Assert.Equal(1, changed);

        // edit
        var row = vm.Groups[1].Items.Single();
        var edit = vm.EditAsync(row);
        dlg = await Waits.ForDialogAsync<HomeworkDialogViewModel>(shell);
        Assert.True(dlg.IsEdit);
        dlg.Text = "глава 2";
        dlg.ConfirmCommand.Execute(null);
        await edit;
        Assert.Equal("глава 2", vm.Groups[1].Items.Single().Text);

        // Done filter shows the completed task immediately; All keeps this group collapsed.
        await vm.ToggleDoneAsync(vm.Groups[1].Items.Single());
        vm.StatusFilter = 1;
        var done = vm.Groups.Single(g => g.IsDone);
        Assert.False(done.IsCollapsed);
        Assert.Equal(1, done.Count);
        done.ToggleCommand.Execute(null);
        Assert.True(done.IsCollapsed);
        done.ToggleCommand.Execute(null);
        Assert.False(done.IsCollapsed);

        // delete with confirmation
        var del = vm.DeleteAsync(done.Items.Single());
        var confirm = await Waits.ForDialogAsync<ConfirmDialogViewModel>(shell);
        confirm.ConfirmCommand.Execute(null);
        await del;
        vm.StatusFilter = 2;
        Assert.Single(vm.Groups);
        Assert.Equal(4, changed);

        await shell.UpdateHomeworkBadgeAsync();
        Assert.Equal("1", shell.ToolSections.Single(s => s.Key == SectionKey.Homework).Badge); // Math burns tomorrow
    }

    [Fact]
    public async Task A_Mutation_Reloads_The_Section_Once()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services) { Clock = () => Sun6 };
        var vm = new HomeworkViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        var row = vm.Groups.Single().Items.Single();
        var resets = 0;
        vm.Groups.CollectionChanged += (_, e) => { if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++; };

        await vm.ToggleDoneAsync(row);
        await Task.Delay(150, TestContext.Current.CancellationToken); // a second, event-driven reload would land here

        Assert.Equal(1, resets); // ChangedAsync reloads; its own HomeworkChanged echo must not reload again
        vm.StatusFilter = 1;
        Assert.True(vm.Groups.Single().IsDone);
    }

    [Fact]
    public async Task Add_Without_Lessons_Uses_Explicit_Manual_Subject_And_Unknown_Due_Date()
    {
        using var db = TestDb.Create();
        var s = db.Services.Db.GetSettings();
        s.MyGroupId = "9999"; // Е452Б: a group without lessons
        db.Services.Db.SaveSettings(s);
        var shell = new ShellViewModel(db.Services);
        var vm = new HomeworkViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        Assert.True(vm.HasGroup);

        var adding = vm.AddCommand.ExecuteAsync(null);
        var picker = await Waits.ForDialogAsync<SubjectPickerDialogViewModel>(shell);
        Assert.True(picker.ManualEntry);
        Assert.False(picker.ConfirmCommand.CanExecute(null));
        picker.ManualSubject = "  Физика  ";
        picker.ConfirmCommand.Execute(null);
        var editor = await Waits.ForDialogAsync<HomeworkDialogViewModel>(shell);
        Assert.Contains("нет занятий", editor.DueText);
        editor.Text = "Решить задачи";
        editor.ConfirmCommand.Execute(null);
        await adding;
        var saved = Assert.Single(db.Services.Homework.GetAll().Where(item => item.Text == "Решить задачи"));
        Assert.Equal("Решить задачи", saved.Text);
        Assert.Null(saved.DueDateComputed);
    }

    [Fact]
    public async Task No_Group_State_Appears_Only_After_The_First_Load()
    {
        using var db = TestDb.Create();
        var s = db.Services.Db.GetSettings();
        s.MyGroupId = "";
        db.Services.Db.SaveSettings(s);
        var shell = new ShellViewModel(db.Services);
        var vm = new HomeworkViewModel(db.Services, shell, () => Sun6);
        Assert.False(vm.ShowNoGroup); // nothing flashes before the first load
        Assert.False(vm.IsLoaded);

        await vm.LoadAsync();

        Assert.True(vm.IsLoaded);
        Assert.False(vm.HasGroup);
        Assert.True(vm.ShowNoGroup);
    }

    [Fact]
    public async Task Browse_filters_search_both_subject_and_text_and_recover_from_empty_results()
    {
        using var db = TestDb.Create();
        var doneId = db.Services.Homework.AddHomework("лек ИСТОРИЯ", "Глава о реформах", 1, Sun6);
        db.Services.Homework.MarkDone(doneId, true);
        var vm = new HomeworkViewModel(db.Services, new ShellViewModel(db.Services), () => Sun6);
        await vm.LoadAsync();

        Assert.Equal("Показано: 1 из 2", vm.BrowseSummary);
        vm.SearchQuery = "  МАТЕМАТ   задачи ";
        Assert.Equal("Показано: 1 из 2", vm.BrowseSummary);
        Assert.Equal("§5, задачи 1–12", Assert.Single(vm.Groups.Single().Items).Text);
        vm.SearchQuery = "реформа";
        Assert.True(vm.ShowBrowseEmpty);
        Assert.Equal("По выбранным фильтрам заданий нет", vm.BrowseEmptyTitle);
        vm.StatusFilter = 1;
        Assert.Equal("Глава о реформах", Assert.Single(vm.Groups.Single().Items).Text);
        Assert.False(vm.Groups.Single().IsCollapsed);
        vm.ClearBrowseFiltersCommand.Execute(null);
        Assert.Equal("", vm.SearchQuery);
        Assert.Equal(2, vm.StatusFilter);
        Assert.Equal("Показано: 2 из 2", vm.BrowseSummary);
    }

    [Fact]
    public async Task Done_only_homework_is_a_filtered_empty_state_until_reset_shows_it()
    {
        using var db = TestDb.Create();
        var only = Assert.Single(db.Services.Homework.GetAll());
        db.Services.Homework.MarkDone(only.Id, true);
        var vm = new HomeworkViewModel(db.Services, new ShellViewModel(db.Services), () => Sun6);
        await vm.LoadAsync();

        Assert.True(vm.ShowBrowseEmpty);
        Assert.Equal("По выбранным фильтрам заданий нет", vm.BrowseEmptyTitle);
        Assert.Equal("Показано: 0 из 1", vm.BrowseSummary);
        vm.ClearBrowseFiltersCommand.Execute(null);
        Assert.Equal(2, vm.StatusFilter);
        Assert.Equal("Показано: 1 из 1", vm.BrowseSummary);
        Assert.False(vm.ShowBrowseEmpty);
    }

    [Fact]
    public void Completion_undo_rejects_expiry_owner_change_and_changed_or_deleted_task()
    {
        var before = Hw("2026-10-01");
        before.Id = 7;
        before.Text = "Задачи";
        var after = Hw("2026-10-01", "done");
        after.Id = 7;
        after.Text = "Задачи";
        after.DoneAt = Sun6;
        var now = new DateTimeOffset(Sun6);
        var ticket = HomeworkCompletionUndo.Create(before, after, "profile/group", now);

        Assert.True(ticket.Allows(after, "profile/group", now.AddSeconds(4)));
        Assert.False(ticket.Allows(after, "profile/group", now.AddSeconds(5)));
        Assert.False(ticket.Allows(after, "other/group", now));
        Assert.False(ticket.Allows(null, "profile/group", now));
        after.Text = "Исправленные задачи";
        Assert.False(ticket.Allows(after, "profile/group", now));
    }

    [Fact]
    public async Task Completion_undo_restores_only_the_unchanged_task_and_group_change_resets_browse()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services) { Clock = () => Sun6 };
        var vm = new HomeworkViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();
        var row = Assert.Single(vm.Groups.Single().Items);

        await vm.ToggleDoneAsync(row);
        Assert.True(vm.HasCompletionUndo);
        Assert.Empty(vm.Groups);
        await vm.UndoCompletionCommand.ExecuteAsync(null);
        Assert.False(vm.HasCompletionUndo);
        Assert.False(Assert.Single(db.Services.Homework.GetAll()).Status == "done");

        vm.SearchQuery = "задачи";
        vm.SubjectFilter = TestDb.MathSubject;
        vm.StatusFilter = 2;
        var settings = db.Services.Db.GetSettings();
        settings.MyGroupId = "3314";
        db.Services.Db.SaveSettings(settings);
        await vm.LoadAsync();
        Assert.Equal("", vm.SearchQuery);
        Assert.Equal("", vm.SubjectFilter);
        Assert.Equal(0, vm.StatusFilter);
    }

    [AvaloniaFact]
    public async Task Renders_Both_Themes_And_Card_Flyout_Binds_Commands()
    {
        using var db = TestDb.Create();
        // The fixture seeds a single homework; the frames are supposed to show every group shape, so
        // three more give «Горит» a count, «Далеко» a «через N пар» label and a collapsed «Сдано».
        var seed = db.Services.Homework;
        var created = new DateTime(2026, 9, 5, 12, 0, 0);
        seed.AddHomework("пр ОСН РОС ГОС", "конспект по главе 3", 1, created);
        seed.AddHomework(TestDb.MathSubject, "решить вариант 7", 3, created);
        seed.AddHomework("лек ИСТОРИЯ", "реферат: реформы Петра I", 1, created);
        seed.MarkDone(seed.AddHomework("лек ФК И СПОРТ", "справка из поликлиники", 1, created), true);

        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services) { Clock = () => Sun6 };
        shell.Register(SectionKey.Schedule, () => new ScheduleViewModel(db.Services, shell, () => new DateTime(2026, 9, 7, 8, 0, 0)));
        shell.Register(SectionKey.Homework, () => new HomeworkViewModel(db.Services, shell, () => Sun6));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        Pump();

        // stage-1 debt: the homework block's MenuFlyout on a lesson card must bind to the row's commands
        var hwButton = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("hw"));
        var flyout = Assert.IsType<MenuFlyout>(hwButton.Flyout);
        flyout.ShowAt(hwButton);
        Pump();
        var items = flyout.Items.OfType<MenuItem>().ToList();
        Assert.Equal(3, items.Count);
        Assert.All(items, mi => Assert.NotNull(mi.Command));
        Assert.Equal("Сдано", items[0].Header);
        flyout.Hide();

        shell.NavigateTo(SectionKey.Homework);
        var vm = Assert.IsType<HomeworkViewModel>(shell.Current);
        await Waits.Until(() => vm.Groups.Count > 0, "homework groups");
        vm.StatusFilter = 2;
        Pump();
        SetTheme(ThemeVariant.Dark);
        Frames.Capture(window, "homework-dark");
        SetTheme(ThemeVariant.Light);
        Frames.Capture(window, "homework-light");

        // Expand «Сдано» through the group's own ToggleCommand, the way a user would, so the done row's
        // struck-through/dimmed text (not just the chip) is visible in a frame.
        var doneGroup = vm.Groups.Single(g => g.IsDone);
        doneGroup.ToggleCommand.Execute(null);
        Pump();
        Frames.Capture(window, "homework-done-light");
        doneGroup.ToggleCommand.Execute(null);
        Pump();

        // «＋ Добавить» step 1: nothing else renders SubjectPickerDialogView, so it gets a frame here.
        var add = vm.AddCommand.ExecuteAsync(null);
        await Waits.ForDialogAsync<SubjectPickerDialogViewModel>(shell);
        Pump();
        Frames.Capture(window, "dialog-subject-picker-light");
        shell.Dialogs.Current!.CancelCommand.Execute(null);
        await add;

        AssertNoBindingErrors();
    }
}
