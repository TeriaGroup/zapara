using Vograph.Desktop.Services;
using Vograph.Desktop.ViewModels;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ViewModelBaseTests
{
    private sealed class Probe(AppServices app) : ViewModelBase(app)
    {
        public Task<string?> Ok() => RunAsync(() => "42", "probe");
        public Task<string?> Fail() => RunAsync<string>(() => throw new InvalidOperationException("nope"), "probe");
        public Task<bool> FailAction() => RunAsync(() => throw new InvalidOperationException("nope"), "probe");
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
}
