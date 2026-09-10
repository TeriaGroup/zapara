using System.Reflection;
using Vograph.Desktop.Services;
using Vograph.Desktop.Services.Profiles;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ToastServiceTests
{
    private sealed class FakeScheduler
    {
        public readonly List<(TimeSpan Delay, Action Fire)> Pending = new();
        public Action? DuringRegistration { get; set; }
        public IDisposable Schedule(TimeSpan delay, Action fire)
        {
            Pending.Add((delay, fire));
            DuringRegistration?.Invoke();
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
        Assert.Equal(3, sched.Pending.Count);
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
        var sched = new FakeScheduler();
        var toasts = new ToastService(sched.Schedule);
        toasts.Error("x");
        toasts.Dismiss(toasts.Items[0]);
        Assert.Empty(toasts.Items);
        Assert.Empty(sched.Pending);
    }

    [Fact]
    public void Dispose_Cancels_Pending_And_Ignores_Late_Fires_And_Shows()
    {
        var sched = new FakeScheduler();
        var toasts = new ToastService(sched.Schedule);
        toasts.Show("paused");
        toasts.Items[0].IsPaused = true;
        toasts.Show("another");
        var queued = sched.Pending.Select(p => p.Fire).ToArray();
        var snapshot = toasts.Items.ToArray();

        var lifetime = Assert.IsAssignableFrom<IDisposable>(toasts);
        lifetime.Dispose();
        lifetime.Dispose();
        foreach (var fire in queued) fire();
        toasts.Show("too late");

        Assert.Equal(snapshot, toasts.Items);
        Assert.Empty(sched.Pending);
    }

    [Fact]
    public void Dismiss_Cancels_The_Rearmed_Paused_Timer()
    {
        var sched = new FakeScheduler();
        var toasts = new ToastService(sched.Schedule);
        toasts.Show("paused");
        var item = toasts.Items.Single();
        item.IsPaused = true;
        var firstFire = sched.Pending.Single().Fire;
        firstFire();
        var rearmedFire = sched.Pending.Single().Fire;

        toasts.Dismiss(item);
        Assert.Empty(sched.Pending);
        rearmedFire();

        Assert.Empty(toasts.Items);
        Assert.Empty(sched.Pending);
    }

    [Fact]
    public void Dispose_During_Paused_Rearm_Cancels_The_Returned_Registration()
    {
        var sched = new FakeScheduler();
        var toasts = new ToastService(sched.Schedule);
        toasts.Show("paused");
        var item = toasts.Items.Single();
        item.IsPaused = true;
        var lifetime = Assert.IsAssignableFrom<IDisposable>(toasts);
        sched.DuringRegistration = lifetime.Dispose;

        sched.Pending.Single().Fire();

        Assert.Same(item, Assert.Single(toasts.Items));
        Assert.Empty(sched.Pending);
    }

    [Fact]
    public void AppServices_Dispose_Closes_Toast_Lifetime()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        services.Toasts.Show("pending", ms: Timeout.Infinite);
        var item = services.Toasts.Items.Single();

        services.Dispose();
        services.Toasts.Show("too late", ms: Timeout.Infinite);

        Assert.Same(item, Assert.Single(services.Toasts.Items));
    }

    [Fact]
    public async Task Worker_Dispose_Cancels_Without_Notifying_The_Bound_Collection()
    {
        var sched = new FakeScheduler();
        var toasts = new ToastService(sched.Schedule);
        toasts.Show("pending");
        var item = toasts.Items.Single();
        var fire = sched.Pending.Single().Fire;
        var changes = 0;
        toasts.Items.CollectionChanged += (_, _) => Interlocked.Increment(ref changes);

        await Task.Run(toasts.Dispose, TestContext.Current.CancellationToken);
        fire();
        toasts.Show("too late");

        Assert.Empty(sched.Pending);
        Assert.Same(item, Assert.Single(toasts.Items));
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task Profile_Retirement_Closes_Toasts_Without_Notifying_The_Bound_Collection()
    {
        using var directory = new ProfileTestDirectory();
        using var services = AppServices.Create(directory.Root, () => false);
        services.AllowNetwork = false;
        var sched = new FakeScheduler();
        var toasts = ReplaceToastsWithScheduledService(services, sched);
        toasts.Show("paused", ms: Timeout.Infinite);
        var item = toasts.Items.Single();
        item.IsPaused = true;
        var fire = sched.Pending.Single().Fire;
        var changes = 0;
        toasts.Items.CollectionChanged += (_, _) => Interlocked.Increment(ref changes);
        var cancellation = TestContext.Current.CancellationToken;

        await Task.Run(async () =>
        {
            services.Work.Suspend();
            using var lease = await ExclusiveProfileLease.AcquireAsync(services, cancellation);
            await services.CloseQuiescedAsync(lease);
        }, cancellation);
        fire();
        toasts.Show("too late");

        Assert.Empty(sched.Pending);
        Assert.Same(item, Assert.Single(toasts.Items));
        Assert.Equal(0, changes);
    }

    private static ToastService ReplaceToastsWithScheduledService(AppServices services, FakeScheduler scheduler)
    {
        // AppServices has no toast-scheduler injection. Replace only this dependency in the test
        // rather than adding a production API solely to observe cancellation during real retirement.
        services.Toasts.Dispose();
        var toasts = new ToastService(scheduler.Schedule);
        var field = typeof(AppServices).GetField("<Toasts>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(services, toasts);
        return toasts;
    }
}
