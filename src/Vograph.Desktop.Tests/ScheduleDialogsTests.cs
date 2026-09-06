using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ScheduleDialogsTests : UiTest
{
    private static readonly DateTime Mon8 = new(2026, 9, 7, 8, 0, 0);

    private static async Task<(ShellViewModel Shell, ScheduleViewModel Vm)> Make(TestDb db)
    {
        var shell = new ShellViewModel(db.Services);
        var vm = new ScheduleViewModel(db.Services, shell, () => Mon8);
        shell.Register(SectionKey.Schedule, () => vm);
        shell.NavigateTo(SectionKey.Schedule);
        await vm.InitializeAsync();
        return (shell, vm);
    }

    /// <summary>The action opens its dialog after a background Core call, so the dialog appears a
    /// few continuations later — poll instead of racing it with a single Task.Yield.</summary>
    private static async Task<T> WaitForDialogAsync<T>(ShellViewModel shell) where T : DialogViewModelBase
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (shell.Dialogs.Current is not T && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
        return Assert.IsType<T>(shell.Dialogs.Current);
    }

    [Fact]
    public async Task Rename_Saves_Weekday_Override_And_Reloads()
    {
        using var db = TestDb.Create();
        var (shell, vm) = await Make(db);

        var task = vm.RenameAsync(vm.Lessons[1]); // ОСН РОС ГОС — no override yet
        var dlg = await WaitForDialogAsync<RenameDialogViewModel>(shell);
        Assert.False(dlg.HasExisting);
        Assert.Equal("Оригинал: ОСН РОС ГОС", dlg.OriginalLine);

        dlg.DisplayName = "Основы гос.";
        dlg.ScopeIndex = 1;
        Assert.Equal("Предпросмотр: Основы гос.", dlg.Preview);
        dlg.ConfirmCommand.Execute(null);
        await task;

        Assert.Equal("Основы гос.", db.Services.Overrides.GetDisplayName("пр ОСН РОС ГОС", 1));
        Assert.Equal("пр ОСН РОС ГОС", db.Services.Overrides.GetDisplayName("пр ОСН РОС ГОС", 2)); // weekday-only: other days still return the raw name
        Assert.Equal("Основы гос.", vm.Lessons[1].DisplayName);
    }

    [Fact]
    public async Task Rename_Reset_Removes_Existing_Override()
    {
        using var db = TestDb.Create();
        var (shell, vm) = await Make(db);

        var task = vm.RenameAsync(vm.Lessons[0]); // Матан (global override from the fixture)
        var dlg = await WaitForDialogAsync<RenameDialogViewModel>(shell);
        Assert.True(dlg.HasExisting);
        Assert.Equal("Матан", dlg.DisplayName);
        Assert.Equal(0, dlg.ScopeIndex);

        dlg.ResetCommand.Execute(null);
        await task;

        Assert.Equal(TestDb.MathSubject, db.Services.Overrides.GetDisplayName(TestDb.MathSubject, 1)); // raw (full) name again
        Assert.Equal("ВЫСШ. МАТЕМАТ", vm.Lessons[0].DisplayName);
    }

    [Fact]
    public async Task Rename_Cancel_Changes_Nothing()
    {
        using var db = TestDb.Create();
        var (shell, vm) = await Make(db);
        var task = vm.RenameAsync(vm.Lessons[0]);
        var dlg = await WaitForDialogAsync<RenameDialogViewModel>(shell);
        dlg.CancelCommand.Execute(null);
        await task;
        Assert.Equal("Матан", vm.Lessons[0].DisplayName);
    }

    [Fact]
    public async Task Note_Only_Override_Keeps_The_Raw_Name()
    {
        using var db = TestDb.Create();
        var (shell, vm) = await Make(db);

        var task = vm.RenameAsync(vm.Lessons[1]); // пр ОСН РОС ГОС — no override yet
        var dlg = await WaitForDialogAsync<RenameDialogViewModel>(shell);
        dlg.Note = "зачёт в декабре";
        Assert.Equal("Предпросмотр: ОСН РОС ГОС", dlg.Preview); // preview stays in display form
        dlg.ConfirmCommand.Execute(null);
        await task;

        var o = db.Services.Overrides.GetOverride("пр ОСН РОС ГОС", "global");
        Assert.NotNull(o);
        Assert.Equal("пр ОСН РОС ГОС", o!.DisplayName);       // legacy shape: a note, not a rename
        Assert.Equal("зачёт в декабре", o.Note);
        Assert.Equal("ОСН РОС ГОС", vm.Lessons[1].DisplayName);
        Assert.Null(vm.Lessons[1].Row.OriginalName);          // no «оригинал: …» line — nothing was renamed

        // Reopening shows the note and an empty name field (also covers overrides written by the WPF client).
        task = vm.RenameAsync(vm.Lessons[1]);
        dlg = await WaitForDialogAsync<RenameDialogViewModel>(shell);
        Assert.True(dlg.HasExisting);
        Assert.Equal("", dlg.DisplayName);
        Assert.Equal("зачёт в декабре", dlg.Note);
        dlg.CancelCommand.Execute(null);
        await task;
    }

    [AvaloniaFact]
    public async Task Group_Change_Reapplies_Smart_Start()
    {
        using var db = TestDb.Create();
        var s = db.Services.Db.GetSettings();
        s.MyGroupId = "";                       // first run: no group yet
        db.Services.Db.SaveSettings(s);
        var sunday = new DateTime(2026, 9, 6, 12, 0, 0);
        var shell = new ShellViewModel(db.Services);
        var vm = new ScheduleViewModel(db.Services, shell, () => sunday);
        shell.Register(SectionKey.Schedule, () => vm);
        shell.NavigateTo(SectionKey.Schedule);
        await vm.InitializeAsync();
        Assert.Equal(0, vm.DayOffset);          // no group → today
        Assert.True(vm.IsEmpty);

        // The user picks a group through the real picker path (save under the gate, refresh card, notify).
        var pick = shell.OpenGroupPickerCommand.ExecuteAsync(null);
        var dlg = await WaitForDialogAsync<GroupPickerDialogViewModel>(shell);
        dlg.Selected = dlg.Filtered.Single(g => g.Id == TestDb.MyGroupId);
        dlg.ConfirmCommand.Execute(null);
        await pick;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (vm.IsEmpty && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.Equal(1, vm.DayOffset);                      // Sunday → smart start lands on Monday, not on the empty Sunday
        Assert.Equal(new DateTime(2026, 9, 7), vm.Date);
        Assert.Equal(2, vm.SegmentIndex);
        Assert.True(vm.ShowGoToday);
        Assert.Equal(2, vm.Lessons.Count);
        Assert.Equal("А863С", shell.GroupName);
    }

    [Fact]
    public async Task Homework_Add_Edit_Toggle_Delete()
    {
        using var db = TestDb.Create();
        var (shell, vm) = await Make(db);
        var law = vm.Lessons[1];
        Assert.Empty(law.Homework);

        // add
        var add = vm.AddHomeworkAsync(law);
        var dlg = await WaitForDialogAsync<HomeworkDialogViewModel>(shell);
        Assert.False(dlg.IsEdit);
        Assert.False(dlg.ConfirmCommand.CanExecute(null));
        Assert.Equal("Срок: 21.09 (Пн)", dlg.DueText);   // ОСН РОС ГОС is Monday/odd only: 07.09 → next is 21.09
        dlg.IncCommand.Execute(null);
        Assert.Equal(2, dlg.Nth);
        Assert.Equal("Срок: 05.10 (Пн)", dlg.DueText);
        dlg.DecCommand.Execute(null);
        dlg.Text = "прочитать главу 2";
        Assert.True(dlg.ConfirmCommand.CanExecute(null));
        dlg.ConfirmCommand.Execute(null);
        await add;
        law = vm.Lessons[1];
        var hw = Assert.Single(law.Homework);
        Assert.Equal("прочитать главу 2", hw.Text);
        Assert.Equal(new DateTime(2026, 9, 21), hw.Item.Due);
        Assert.Equal("срок 21.09", hw.Label); // status/label now come from the section's own clock (Mon 07.09), no ОСН РОС ГОС in between
        Assert.False(hw.IsDone);

        // edit
        var edit = vm.EditHomeworkAsync(hw);
        dlg = await WaitForDialogAsync<HomeworkDialogViewModel>(shell);
        Assert.True(dlg.IsEdit);
        Assert.Equal("прочитать главу 2", dlg.Text);
        dlg.Text = "глава 3";
        dlg.ConfirmCommand.Execute(null);
        await edit;
        hw = Assert.Single(vm.Lessons[1].Homework);
        Assert.Equal("глава 3", hw.Text);

        // done / undo
        await vm.ToggleDoneAsync(hw);
        hw = Assert.Single(vm.Lessons[1].Homework);
        Assert.True(hw.IsDone);
        Assert.Equal("сдано", hw.Label);
        await vm.ToggleDoneAsync(hw);
        Assert.False(Assert.Single(vm.Lessons[1].Homework).IsDone);

        // delete with confirmation
        var del = vm.DeleteHomeworkAsync(Assert.Single(vm.Lessons[1].Homework));
        var confirm = await WaitForDialogAsync<ConfirmDialogViewModel>(shell);
        Assert.Contains("глава 3", confirm.Message);
        confirm.ConfirmCommand.Execute(null);
        await del;
        Assert.Empty(vm.Lessons[1].Homework);
        Assert.Empty(db.Services.Homework.GetForSubject("пр ОСН РОС ГОС"));
    }

    [AvaloniaFact]
    public async Task Dialogs_Render_In_Window()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var (shell, vm) = await Make(db);
        var window = new MainWindow { DataContext = shell };
        window.Show();

        SetTheme(ThemeVariant.Dark);
        var rename = vm.RenameAsync(vm.Lessons[0]);
        await WaitForDialogAsync<RenameDialogViewModel>(shell);
        Pump();
        Frames.Capture(window, "dialog-rename-dark");
        shell.Dialogs.Current!.CancelCommand.Execute(null);
        await rename;

        SetTheme(ThemeVariant.Light);
        var hw = vm.AddHomeworkAsync(vm.Lessons[0]);
        await WaitForDialogAsync<HomeworkDialogViewModel>(shell);
        Pump();
        Frames.Capture(window, "dialog-homework-light");
        shell.Dialogs.Current!.CancelCommand.Execute(null);
        await hw;

        AssertNoBindingErrors();
    }

    /// <summary>Regression: a lesson card's own IsPast (its time slot already ended today) must not strike
    /// through a NOT-done homework's text. Border.card.past is shared with the Homework section's row card,
    /// whose .past means "this homework is done" — the two must stay visually independent.</summary>
    [AvaloniaFact]
    public async Task Past_Lesson_Does_Not_Strike_Through_NotDone_Homework_Text()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        var afterFirstLesson = new DateTime(2026, 9, 7, 12, 0, 0); // Mon 07.09, after the 09:00-10:35 math lecture ends
        var vm = new ScheduleViewModel(db.Services, shell, () => afterFirstLesson);
        shell.Register(SectionKey.Schedule, () => vm);
        shell.NavigateTo(SectionKey.Schedule);
        await vm.InitializeAsync();

        var math = vm.Lessons[0];
        Assert.True(math.IsPast);                // the lesson's own time slot already ended
        var hw = Assert.Single(math.Homework);
        Assert.False(hw.IsDone);                 // the fixture homework is not done

        var window = new MainWindow { DataContext = shell };
        window.Show();
        Pump();

        var pastCard = window.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("past"));
        var text = pastCard.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("hwtext"));
        Assert.Equal("§5, задачи 1–12", text.Text);
        Assert.True(text.TextDecorations is null || text.TextDecorations.Count == 0);

        AssertNoBindingErrors();
    }
}
