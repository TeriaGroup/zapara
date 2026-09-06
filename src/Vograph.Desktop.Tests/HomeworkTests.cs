using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Core.Models;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkTests : UiTest
{
    private static readonly DateTime Sun6 = new(2026, 9, 6, 12, 0, 0);

    private static async Task<T> WaitForDialogAsync<T>(ShellViewModel shell) where T : DialogViewModelBase
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (shell.Dialogs.Current is not T && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
        return Assert.IsType<T>(shell.Dialogs.Current);
    }

    private static Homework Hw(string? due, string status = "pending") =>
        new() { SubjectRawNormalized = "x", Text = "t", CreatedAt = Sun6, TargetNthOccurrence = 1, Status = status, DueDateComputed = due is null ? null : DateTime.Parse(due) };

    [Theory]
    [InlineData("2026-09-06", 0, "burning_urgent")]
    [InlineData("2026-09-07", 3, "burning")]
    [InlineData("2026-09-08", 1, "approaching")]
    [InlineData("2026-09-08", 0, "approaching")]   // ≤ 3 days, nothing in between
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
        hw.AddHomework("пр ОСН РОС ГОС", "конспект", 1, created);   // Mon 07.09 → burning on Sunday
        var history = hw.AddHomework("лек ИСТОРИЯ", "глава 1", 1, created); // odd Wednesday → 16.09 → far
        var doneId = hw.AddHomework("лек ФК И СПОРТ", "справка", 1, created);
        hw.MarkDone(doneId, true);

        var model = new HomeworkComposer(db.Services).Compose(Sun6.Date);

        Assert.True(model.HasGroup);
        Assert.Equal((3, 1), (model.Open, model.Done));
        Assert.Equal(new[] { "burning", "far", "done" }, model.Groups.Select(g => g.Status));
        Assert.Equal(new[] { "Горит", "Далеко", "Сдано" }, model.Groups.Select(g => g.Title));
        var burning = model.Groups[0].Items;
        Assert.Equal(new[] { "Матан", "ОСН РОС ГОС" }, burning.Select(i => i.Subject).OrderBy(s => s)); // renamed + stripped
        Assert.All(burning, i => Assert.Equal("горит завтра", i.Label));
        var far = Assert.Single(model.Groups[1].Items);
        Assert.Equal(("ИСТОРИЯ", "лек ИСТОРИЯ", history), (far.Subject, far.SubjectRaw, far.Homework.Id));
        Assert.Equal("срок 16.09", far.Label);
        Assert.Equal("сдано", Assert.Single(model.Groups[2].Items).Label);

        var subjects = new HomeworkComposer(db.Services).Subjects();
        Assert.Equal(6, subjects.Count);
        Assert.Contains(subjects, s => s.SubjectRaw == "лек ИСТОРИЯ" && s.Display == "ИСТОРИЯ" && s.TypeLabel == "лекция");
        Assert.Contains(subjects, s => s.SubjectRaw == "лек ВЫСШ. МАТЕМАТ" && s.Display == "Матан");
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
        var picker = await WaitForDialogAsync<SubjectPickerDialogViewModel>(shell);
        picker.Query = "истор";
        picker.Selected = Assert.Single(picker.Filtered);
        picker.ConfirmCommand.Execute(null);
        var dlg = await WaitForDialogAsync<HomeworkDialogViewModel>(shell);
        Assert.Equal("Срок: 16.09 (Ср)", dlg.DueText);
        dlg.Text = "глава 1";
        dlg.ConfirmCommand.Execute(null);
        await add;
        Assert.Equal(new[] { "Горит", "Далеко" }, vm.Groups.Select(g => g.Title));
        Assert.Equal(1, changed);

        // edit
        var row = vm.Groups[1].Items.Single();
        var edit = vm.EditAsync(row);
        dlg = await WaitForDialogAsync<HomeworkDialogViewModel>(shell);
        Assert.True(dlg.IsEdit);
        dlg.Text = "глава 2";
        dlg.ConfirmCommand.Execute(null);
        await edit;
        Assert.Equal("глава 2", vm.Groups[1].Items.Single().Text);

        // done → collapsed «Сдано» group with a count
        await vm.ToggleDoneAsync(vm.Groups[1].Items.Single());
        var done = vm.Groups.Single(g => g.IsDone);
        Assert.True(done.IsCollapsed);
        Assert.Equal(1, done.Count);
        done.ToggleCommand.Execute(null);
        Assert.False(done.IsCollapsed);

        // delete with confirmation
        var del = vm.DeleteAsync(done.Items.Single());
        var confirm = await WaitForDialogAsync<ConfirmDialogViewModel>(shell);
        confirm.ConfirmCommand.Execute(null);
        await del;
        Assert.Single(vm.Groups);
        Assert.Equal(4, changed);

        await shell.UpdateHomeworkBadgeAsync();
        Assert.Equal("1", shell.ToolSections.Single(s => s.Key == SectionKey.Homework).Badge); // Math burns tomorrow
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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (vm.Groups.Count == 0 && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
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
        await WaitForDialogAsync<SubjectPickerDialogViewModel>(shell);
        Pump();
        Frames.Capture(window, "dialog-subject-picker-light");
        shell.Dialogs.Current!.CancelCommand.Execute(null);
        await add;

        AssertNoBindingErrors();
    }
}
