using Avalonia.Headless.XUnit;
using Vograph.Desktop.Features.Friends;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class FriendsSettingsLifetimeTests
{
    [AvaloniaFact]
    public async Task Settings_change_finishes_its_own_reload_before_notifying_other_sections()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new FriendsViewModel(db.Services, shell, () => new DateTime(2026, 9, 6, 12, 0, 0));
        await vm.LoadAsync();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(10));
        var writeFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notified = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reloaded = 0;
        var takeGate = true;
        var held = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FriendsViewModel.TickLabels)) reloaded++;
            if (e.PropertyName == nameof(FriendsViewModel.IsBusy) && !vm.IsBusy && takeGate)
            {
                takeGate = false;
                // The settings SQL has released CoreGate; hold the next read until this
                // dispatcher callback returns. No sleeps or thread-pool scheduling assumptions.
                held = db.Services.CoreGate.Wait(0);
                writeFinished.TrySetResult();
            }
        };
        shell.ScheduleChanged += () => notified.TrySetResult(reloaded);
        try
        {
            vm.Strictness = 100;
            var refresh = vm.RefreshPreviewAsync();
            await writeFinished.Task.WaitAsync(stop.Token);
            Assert.True(held);
            db.Services.CoreGate.Release(); held = false;
            await refresh.WaitAsync(stop.Token);
            await db.Services.Work.WhenIdleAsync(stop.Token);
            Assert.Equal(1, await notified.Task.WaitAsync(stop.Token));
            Assert.Equal(100, db.Services.Db.GetSettings().IntersectionStrictness);
        }
        finally
        {
            if (held) db.Services.CoreGate.Release();
            vm.Detach();
            shell.Stop();
        }
    }
}
