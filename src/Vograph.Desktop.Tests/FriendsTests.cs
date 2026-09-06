using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Vograph.Core.Models;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Friends;
using Vograph.Desktop.Features.Schedule;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class FriendsTests : UiTest
{
    private static readonly DateTime Sun6 = new(2026, 9, 6, 12, 0, 0);

    private static async Task<T> WaitForDialogAsync<T>(ShellViewModel shell) where T : DialogViewModelBase
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (shell.Dialogs.Current is not T && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
        return Assert.IsType<T>(shell.Dialogs.Current);
    }

    [Fact]
    public void Marks_Are_Computed_By_The_Shared_Helper()
    {
        using var db = TestDb.Create();
        var lesson = db.Services.Db.GetLessons("3313", 1, 1).Single(l => l.TimeStart == "09:00");
        var marks = FriendMarks.Compute(db.Services, lesson, new DateTime(2026, 9, 7), db.Services.Db.GetFriends(), db.Services.Db.GetSettings(), db.Services.Loc);
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
        var picker = await WaitForDialogAsync<GroupPickerDialogViewModel>(shell);
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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (db.Services.Db.GetFriends().Single(f => f.GroupName == "09С31").Enabled && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.False(db.Services.Db.GetFriends().Single(f => f.GroupName == "09С31").Enabled);
        Assert.True(changed >= 3);

        // remove with confirmation
        var remove = vm.RemoveAsync(second);
        var confirm = await WaitForDialogAsync<ConfirmDialogViewModel>(shell);
        Assert.Contains("Е452Б", confirm.Message);
        confirm.ConfirmCommand.Execute(null);
        await remove;
        Assert.Single(vm.Friends);
        Assert.Single(db.Services.Db.GetFriends());
    }

    [Fact]
    public async Task Strictness_And_Always_Show_Persist_And_Drive_The_Preview()
    {
        using var db = TestDb.Create();
        db.Services.Db.InsertFriend(new FriendGroup { GroupName = "Е452Б", ColorHex = FriendPalette.Hex[1], Enabled = true, MemberNames = "" }); // no lessons → never present
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => Sun6);
        await vm.LoadAsync();

        Assert.Equal(25, vm.Strictness);
        Assert.Equal(new[] { "в вузе", "корпус", "этаж", "аудитория" }, vm.TickLabels);
        Assert.True(vm.HasPreview);
        Assert.StartsWith("Пн 07.09 · 09:00 · Матан", vm.PreviewLine);   // the nearest lesson with a friend around
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
        Assert.Equal(DotFill.Full, vm.PreviewMarks[0].Fill);              // same room still qualifies at 100
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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!vm.HasPreview && sw.ElapsedMilliseconds < 2000) await Task.Delay(10, TestContext.Current.CancellationToken);
        Pump();
        SetTheme(ThemeVariant.Dark);
        Frames.Capture(window, "friends-dark");
        SetTheme(ThemeVariant.Light);
        Frames.Capture(window, "friends-light");
        AssertNoBindingErrors();
    }
}
