using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Core.Models;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class DialogTests : UiTest
{
    [Fact]
    public async Task ShowAsync_Completes_True_On_Confirm_And_Clears_Current()
    {
        var host = new DialogHostViewModel();
        var dialog = new ConfirmDialogViewModel("Удалить?", "Точно?", "Удалить", danger: true);

        var task = host.ShowAsync(dialog);
        Assert.True(host.HasDialog);
        Assert.Same(dialog, host.Current);

        dialog.ConfirmCommand.Execute(null);

        Assert.True(await task);
        Assert.False(host.HasDialog);
    }

    [Fact]
    public async Task Dismiss_Completes_False()
    {
        var host = new DialogHostViewModel();
        var task = host.ShowAsync(new ConfirmDialogViewModel("t", "m", "ok", false));
        host.DismissCommand.Execute(null);
        Assert.False(await task);
    }

    [AvaloniaFact]
    public async Task Escape_Closes_Dialog_In_Window_And_Renders()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        window.Focus();
        SetTheme(ThemeVariant.Dark);

        var task = shell.Dialogs.ShowAsync(new ConfirmDialogViewModel("Удалить домашку?", "«§5, задачи 1–12»", "Удалить", danger: true));
        Pump();
        var host = window.GetVisualDescendants().OfType<DialogHostView>().Single();
        Assert.True(host.IsEffectivelyVisible);
        Frames.Capture(window, "dialog-confirm-dark");

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Pump();

        Assert.False(await task);
        Assert.False(shell.Dialogs.HasDialog);
        AssertNoBindingErrors();
    }

    /// <summary>
    /// A freshly-opened dialog must respond to Enter immediately, without the user first Tabbing or
    /// clicking into it: DialogHostView moves focus into itself (or ConfirmDialogView focuses its own
    /// confirm button) as soon as the dialog appears, so nothing needs to steal focus for this to work.
    /// </summary>
    [AvaloniaFact]
    public async Task Enter_Confirms_Freshly_Opened_Confirm_Dialog()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        window.Focus();

        var task = shell.Dialogs.ShowAsync(new ConfirmDialogViewModel("Удалить домашку?", "«§5, задачи 1–12»", "Удалить", danger: true));
        Pump();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Pump();

        Assert.True(await task);
        Assert.False(shell.Dialogs.HasDialog);
        AssertNoBindingErrors();
    }

    /// <summary>
    /// The window-level Enter KeyBinding must not steal Enter away from a multi-line text box: a real
    /// AcceptsReturn box (Task 12's homework editor) consumes Enter to insert a newline, and the dialog
    /// must stay open. Task 9 has no multi-line field of its own, so this flips the group picker's search
    /// box into AcceptsReturn mode to probe how this Avalonia version routes the key.
    /// </summary>
    [AvaloniaFact]
    public async Task Enter_In_MultiLine_TextBox_Does_Not_Confirm_Dialog()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        window.Focus();

        var groups = new List<Group> { new() { Id = "1", Name = "А1" } };
        var task = shell.Dialogs.ShowAsync(new GroupPickerDialogViewModel(groups, "1"));
        Pump();

        var searchBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SearchBox");
        searchBox.AcceptsReturn = true; // simulate a multi-line field
        searchBox.Focus();
        Pump();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Pump();

        Assert.True(shell.Dialogs.HasDialog); // still open: Enter must not have bubbled out of the text box
        Assert.False(task.IsCompleted);
        AssertNoBindingErrors(); // this is also the only test that renders GroupPickerDialogView in a real window

        shell.Dialogs.DismissCommand.Execute(null);
        await task;
    }

    /// <summary>
    /// A dialog shown while another is open replaces it (T10-R4): the one being replaced completes as cancelled,
    /// the replacement becomes Current, and the host stays visible and keyboard-reachable — so Escape closes it and
    /// its own ShowAsync returns. Before the fix the second Show could not raise IsOpen (already true), the first
    /// dialog's continuation then faded the host out over the second one, and the second ShowAsync never completed.
    /// </summary>
    [AvaloniaFact]
    public async Task Showing_A_Dialog_Over_An_Open_One_Replaces_It()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        var shell = new ShellViewModel(db.Services);
        var window = new MainWindow { DataContext = shell };
        window.Show();
        window.Focus();

        var first = new ConfirmDialogViewModel("Удалить домашку?", "m", "Удалить", danger: true);
        var second = new ConfirmDialogViewModel("Переименовать?", "m", "ok", danger: false);
        var firstTask = shell.Dialogs.ShowAsync(first);
        Pump();
        var secondTask = shell.Dialogs.ShowAsync(second);
        Pump();

        Assert.False(await firstTask);                   // the replaced dialog completes as cancelled
        Assert.Same(second, shell.Dialogs.Current);
        Assert.True(shell.Dialogs.IsOpen);               // …and the host still belongs to the replacement
        Assert.True(shell.Dialogs.HasDialog);
        var root = window.GetVisualDescendants().OfType<DialogHostView>().Single()
            .GetVisualDescendants().OfType<Panel>().First(p => p.Name == "Root");
        Assert.True(root.IsVisible);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Pump();

        Assert.False(await secondTask);                  // Escape reaches it, so its ShowAsync returns
        Assert.False(shell.Dialogs.HasDialog);
        Assert.Null(shell.Dialogs.Current);
        Assert.False(root.IsVisible);
        AssertNoBindingErrors();
    }

    [Fact]
    public async Task Host_Without_Motion_Closes_Instantly()
    {
        var host = new DialogHostViewModel();
        Assert.Same(MotionSettings.Off, host.Motion);
        var task = host.ShowAsync(new ConfirmDialogViewModel("t", "m", "ok", false));
        Assert.True(host.IsOpen);
        host.DismissCommand.Execute(null);
        Assert.False(await task);
        Assert.False(host.IsOpen);
        Assert.Null(host.Current);
    }
}
