using CommunityToolkit.Mvvm.ComponentModel;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    protected ViewModelBase(AppServices app) => App = app;

    public AppServices App { get; }

    [ObservableProperty] private bool _isBusy;

    protected string T(string key, params object[] args) => App.Loc.T(key, args);

    /// <summary>Sections subscribe to shell/Loc events in their constructor and drop them here. The shell
    /// calls Detach whenever it replaces or rebuilds a section, so an orphaned section never keeps reloading.</summary>
    public virtual void Detach() { }

    /// <summary>Called by the shell every time the section is navigated to. Sections that need fresh data on
    /// entry override it; implementations use RunAsync and therefore never throw.</summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;

    /// <summary>
    /// Runs synchronous Core work off the UI thread, one call at a time across the whole app
    /// (App.CoreGate — Core's SqliteConnection is not thread-safe). Failures become a toast + log
    /// entry and a null result — callers never see exceptions and the UI thread never blocks on SQLite.
    /// A gate disposed at shutdown (ObjectDisposedException) is not an error: the app is going away.
    /// </summary>
    protected Task<T?> RunAsync<T>(Func<T> work, string context) where T : class =>
        GatedAsync(() => Task.Run(work), context);

    /// <summary>Async Core work (parse + SQLite writes) under the same gate. Network belongs OUTSIDE the gate — fetch first, then call this with the result.</summary>
    protected Task<T?> RunAsync<T>(Func<Task<T>> work, string context) where T : class =>
        GatedAsync(() => Task.Run(work), context);

    protected async Task<bool> RunAsync(Action work, string context) =>
        await GatedAsync(async () => { await Task.Run(work); return Done.Instance; }, context) is not null;

    protected async Task<bool> RunAsync(Func<Task> work, string context) =>
        await GatedAsync(async () => { await Task.Run(work); return Done.Instance; }, context) is not null;

    /// <summary>
    /// Acquires App.CoreGate, runs the work, and releases the gate — the whole sequence on a pool thread,
    /// via one Task.Run with ConfigureAwait(false) on every await inside it. That is deliberate: Avalonia
    /// raises desktop.Exit on the UI thread, and AppServices.Dispose() blocks there, synchronously, inside
    /// CoreGate.Wait(2s). A plain `await CoreGate.WaitAsync(); ... finally { CoreGate.Release(); }` called
    /// from the UI thread captures the dispatcher's SynchronizationContext, so the continuation that
    /// releases the gate — the one after `await run()` — gets posted back to the dispatcher instead of
    /// running immediately. A UI thread stuck inside Dispose() never pumps that dispatcher, so the posted
    /// release could never run, and Dispose() would deadlock into its own 2 s timeout on every gated call
    /// that happened to start on the UI thread. Doing the acquire/work/release entirely inside Task.Run
    /// means the release never depends on the UI thread at all, so a UI-thread Dispose() unblocks as soon
    /// as the work itself finishes.
    /// </summary>
    private async Task<T?> GatedAsync<T>(Func<Task<T>> run, string context) where T : class
    {
        IsBusy = true;
        try
        {
            return await Task.Run(async () =>
            {
                await App.CoreGate.WaitAsync().ConfigureAwait(false); // acquired on the pool
                try { return await run().ConfigureAwait(false); } // the work (already Task.Run-wrapped by the overloads above)
                finally { App.CoreGate.Release(); } // released on the pool — a UI-thread Dispose() can proceed
            });
        }
        catch (ObjectDisposedException ex)
        {
            App.Log.Warn($"{context}: {ex.GetType().Name}: {ex.Message}"); // shutdown raced a queued call
            return null;
        }
        catch (Exception ex)
        {
            Report(context, ex);
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private sealed class Done { public static readonly Done Instance = new(); }

    private void Report(string context, Exception ex)
    {
        App.Log.Error(context, ex);
        App.Toasts.Error($"{T("errorTitle")}: {ex.Message}");
    }
}
