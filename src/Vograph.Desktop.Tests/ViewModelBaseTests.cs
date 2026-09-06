using Vograph.Desktop.Services;
using Vograph.Desktop.ViewModels;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ViewModelBaseTests
{
    private sealed class Probe(AppServices app) : ViewModelBase(app)
    {
        public Task<string?> Ok() => RunAsync(() => "42", "probe");
        public Task<string?> Run(Func<string> f) => RunAsync(f, "probe");
        // Explicit delegate type: a throw-only lambda fits both RunAsync overloads (Func<string> and Func<Task<string>>) and would be ambiguous.
        public Task<string?> Fail() => RunAsync(new Func<string>(() => throw new InvalidOperationException("nope")), "probe");
        public Task<bool> FailAction() => RunAsync(() => throw new InvalidOperationException("nope"), "probe");
        public Task<string?> RunTask(Func<Task<string>> f) => RunAsync(f, "probe");
    }

    private sealed class ProbeVm : ViewModelBase
    {
        public ProbeVm(AppServices app) : base(app) { }
        public Task<string?> Run(Func<string> work) => RunAsync(work, "probe");
        public Task<bool> RunTask(Func<Task> work) => RunAsync(work, "probe");
        public int Detached;
        public override void Detach() => Detached++;
    }

    [Fact]
    public async Task RunAsync_After_Dispose_Returns_Null_Without_Throwing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"));
        var services = AppServices.Create(dir);
        var vm = new ProbeVm(services);
        services.Dispose(); // the gate is gone

        var result = await vm.Run(() => "never");

        Assert.Null(result);
        Assert.False(vm.IsBusy);
        Assert.Empty(services.Toasts.Items); // shutdown is not an error
        try { Directory.Delete(dir, recursive: true); } catch (IOException ex) { Console.Error.WriteLine($"temp dir left behind ({dir}): {ex.Message}"); }
    }

    [Fact]
    public async Task Dispose_Waits_For_The_Gated_Call_Before_Closing_The_Database()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"));
        var services = AppServices.Create(dir);
        var vm = new ProbeVm(services);
        var inGate = new ManualResetEventSlim();

        var work = vm.Run(() => { inGate.Set(); Thread.Sleep(200); return "done"; });
        inGate.Wait(TestContext.Current.CancellationToken);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        services.Dispose(); // must not close SQLite under a running query
        sw.Stop();

        Assert.Equal("done", await work);
        Assert.True(sw.ElapsedMilliseconds >= 150, $"Dispose returned after {sw.ElapsedMilliseconds} ms — it did not wait for the gated call");
        Assert.Null(await vm.Run(() => "never")); // gate gone: queued callers get null, not an exception
        Assert.Empty(services.Toasts.Items);      // shutdown is not an error
        try { Directory.Delete(dir, recursive: true); } catch (IOException ex) { Console.Error.WriteLine($"temp dir left behind ({dir}): {ex.Message}"); }
    }

    [Fact]
    public async Task RunAsync_Task_Overload_Reports_Failure_As_False()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        var vm = new ProbeVm(db.Services);

        Assert.True(await vm.RunTask(() => Task.CompletedTask));
        Assert.False(await vm.RunTask(() => throw new InvalidOperationException("boom")));
        Assert.Single(db.Services.Toasts.Items, t => t.Text.Contains("boom"));
        Assert.Equal(1, db.Services.CoreGate.CurrentCount); // released after both calls
    }

    [Fact]
    public async Task RunAsync_Task_Overload_Awaits_Work_And_Reports_Failures()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        var vm = new Probe(db.Services);

        Assert.Equal("x", await vm.RunTask(async () => { await Task.Delay(10); return "x"; }));
        Assert.Null(await vm.RunTask(() => Task.FromException<string>(new InvalidOperationException("net down"))));
        Assert.Contains("net down", db.Services.Toasts.Items[0].Text);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task RunAsync_Returns_Value_And_Toggles_Busy()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        var vm = new Probe(db.Services);

        var task = vm.Ok();
        var result = await task;

        Assert.Equal("42", result);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task RunAsync_Failure_Toasts_And_Logs_Instead_Of_Throwing()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        var vm = new Probe(db.Services);

        Assert.Null(await vm.Fail());
        Assert.False(await vm.FailAction());

        var toast = db.Services.Toasts.Items[0];
        Assert.Equal(ToastKind.Bad, toast.Kind);
        Assert.Contains("nope", toast.Text);
        Assert.Contains("ERROR probe: InvalidOperationException: nope", File.ReadAllText(db.Services.Log.CurrentFile));
    }

    [Fact]
    public async Task RunAsync_Calls_Are_Serialized_Through_The_Core_Gate()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        var vm = new Probe(db.Services);
        var firstStarted = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var firstRunning = 0;
        var overlapped = false;

        var first = vm.Run(() =>
        {
            Interlocked.Exchange(ref firstRunning, 1);
            firstStarted.Set();
            release.Wait();
            Interlocked.Exchange(ref firstRunning, 0);
            return "a";
        });
        firstStarted.Wait(TestContext.Current.CancellationToken);

        var second = vm.Run(() =>
        {
            overlapped = Volatile.Read(ref firstRunning) == 1;
            return "b";
        });
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted); // queued behind the first

        release.Set();
        Assert.Equal("a", await first);
        Assert.Equal("b", await second);
        Assert.False(overlapped);
    }
}
