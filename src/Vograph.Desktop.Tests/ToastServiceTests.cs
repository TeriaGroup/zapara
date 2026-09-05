using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ToastServiceTests
{
    private sealed class FakeScheduler
    {
        public readonly List<(TimeSpan Delay, Action Fire)> Pending = new();
        public IDisposable Schedule(TimeSpan delay, Action fire)
        {
            Pending.Add((delay, fire));
            return new Disposer(() => Pending.RemoveAll(p => p.Fire == fire));
        }
        private sealed class Disposer(Action a) : IDisposable { public void Dispose() => a(); }
    }

    [Fact]
    public void Show_Adds_Newest_First_And_Keeps_At_Most_Three()
    {
        var sched = new FakeScheduler();
        var toasts = new ToastService(sched.Schedule);

        toasts.Show("a"); toasts.Show("b"); toasts.Show("c"); toasts.Show("d");

        Assert.Equal(new[] { "d", "c", "b" }, toasts.Items.Select(t => t.Text));
        Assert.Equal(TimeSpan.FromMilliseconds(4000), toasts.Items[0].Duration);
    }

    [Fact]
    public void Toast_Is_Removed_When_Timer_Fires_Unless_Paused()
    {
        var sched = new FakeScheduler();
        var toasts = new ToastService(sched.Schedule);
        toasts.Ok("saved");
        var item = toasts.Items.Single();

        item.IsPaused = true;
        sched.Pending.Single().Fire();          // paused: stays, re-armed
        Assert.Single(toasts.Items);
        Assert.Single(sched.Pending);

        item.IsPaused = false;
        sched.Pending.Single().Fire();
        Assert.Empty(toasts.Items);
        Assert.Equal(ToastKind.Ok, item.Kind);
    }

    [Fact]
    public void Dismiss_Removes_Immediately()
    {
        var toasts = new ToastService(new FakeScheduler().Schedule);
        toasts.Error("x");
        toasts.Dismiss(toasts.Items[0]);
        Assert.Empty(toasts.Items);
    }
}
