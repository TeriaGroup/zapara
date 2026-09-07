using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>The one polling helper of the suite: bounded (2 s), cancellable (xUnit1051), and it names what it waited for.</summary>
public static class Waits
{
    public static async Task Until(Func<bool> done, string what = "condition", int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(done(), $"{what} not met within {timeoutMs} ms");
    }

    /// <summary>Actions open their dialog after a gated Core call, a few continuations later — poll for it.</summary>
    public static async Task<T> ForDialogAsync<T>(ShellViewModel shell) where T : DialogViewModelBase
    {
        await Until(() => shell.Dialogs.Current is T, $"dialog {typeof(T).Name}");
        return Assert.IsType<T>(shell.Dialogs.Current);
    }
}
