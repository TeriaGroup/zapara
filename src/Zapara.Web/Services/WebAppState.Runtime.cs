using Microsoft.JSInterop;
using System.Text.Json;
using System.Threading.Channels;

namespace Zapara.Web.Services;

public sealed partial class WebAppState
{
    public string? RuntimeMessage { get; private set; }
    private readonly SemaphoreSlim runtimeLifecycle = new(1, 1);
    private RuntimeRun? runtime;

    public async Task StartRuntimeAsync(IJSRuntime? js = null, TimeProvider? clock = null, CancellationToken ct = default)
    {
        await runtimeLifecycle.WaitAsync(ct);
        try
        {
            if (runtime is not null) return;
            ct.ThrowIfCancellationRequested();
            var run = new RuntimeRun(clock ?? TimeProvider.System, CancellationTokenSource.CreateLinkedTokenSource(ct));
            runtime = run;
            if (js is not null)
            {
                try
                {
                    run.Reference = DotNetObjectReference.Create(this);
                    run.Module = await js.InvokeAsync<IJSObjectReference>("import", ct, "./js/runtime.js");
                    await run.Module.InvokeVoidAsync("start", ct, run.Reference);
                }
                catch (JSException) { RuntimeMessage = "События браузера недоступны. Периодическое обновление продолжит работать."; }
            }
            run.Loop = RuntimeLoopAsync(run);
            run.Timer = RuntimeTimerAsync(run);
            run.Requests.Writer.TryWrite(new(false, false, null));
        }
        catch
        {
            if (runtime is { } failed)
            {
                runtime = null; failed.Lifetime.Cancel();
                if (failed.Module is not null)
                    try { await failed.Module.InvokeVoidAsync("stop"); await failed.Module.DisposeAsync(); } catch (Exception) { }
                failed.Reference?.Dispose(); failed.Lifetime.Dispose();
            }
            throw;
        }
        finally { runtimeLifecycle.Release(); }
    }

    public void WakeRuntime() => runtime?.Requests.Writer.TryWrite(new(false, false, null));

    public Task WakeRuntimeAsync(bool recheckSession = false, bool refreshPublic = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var run = runtime;
        if (run is null || run.Lifetime.IsCancellationRequested) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!run.Requests.Writer.TryWrite(new(recheckSession, refreshPublic, completion))) return Task.CompletedTask;
        return completion.Task.WaitAsync(ct);
    }

    [JSInvokable]
    public Task BrowserRuntimeWake(string reason) => reason is "online" or "focus" or "visible"
        ? WakeRuntimeAsync(recheckSession: true, refreshPublic: reason == "online") : Task.CompletedTask;

    public async Task StopRuntimeAsync()
    {
        await runtimeLifecycle.WaitAsync();
        try
        {
            var run = runtime;
            if (run is null) return;
            runtime = null;
            run.Lifetime.Cancel(); run.Requests.Writer.TryComplete();
            if (run.Module is not null)
            {
                try { await run.Module.InvokeVoidAsync("stop"); await run.Module.DisposeAsync(); }
                catch (Exception error) when (error is JSException or ObjectDisposedException) { }
            }
            await Task.WhenAll(run.Loop, run.Timer);
            run.Reference?.Dispose(); run.Lifetime.Dispose();
        }
        finally { runtimeLifecycle.Release(); }
    }

    private async Task RuntimeLoopAsync(RuntimeRun run)
    {
        var ct = run.Lifetime.Token;
        try
        {
            while (await run.Requests.Reader.WaitToReadAsync(ct))
            {
                var batch = new List<RuntimeWake>();
                while (run.Requests.Reader.TryRead(out var wake)) batch.Add(wake);
                try { await RuntimeCycleAsync(run, batch.Any(item => item.RecheckSession), batch.Any(item => item.RefreshPublic), ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                { foreach (var item in batch) item.Completion?.TrySetCanceled(ct); break; }
                catch (BrowserApiException error) { RuntimeMessage = error.Message; }
                catch (Exception) { RuntimeMessage = "Автоматическое обновление не завершилось. Сохранённые данные не удалены."; }
                finally
                {
                    if (!ct.IsCancellationRequested)
                    {
                        try { Notify(); } catch (Exception) { RuntimeMessage = "Не удалось обновить экран."; }
                    }
                    foreach (var item in batch) item.Completion?.TrySetResult();
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { RuntimeMessage = "Автоматическое обновление приостановлено. Обновите приложение."; }
        finally
        {
            run.Requests.Writer.TryComplete();
            while (run.Requests.Reader.TryRead(out var remaining)) remaining.Completion?.TrySetResult();
        }
    }

    private async Task RuntimeTimerAsync(RuntimeRun run)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), run.Clock);
            while (await timer.WaitForNextTickAsync(run.Lifetime.Token))
                if (!run.Requests.Writer.TryWrite(new(false, false, null))) break;
        }
        catch (OperationCanceledException) when (run.Lifetime.IsCancellationRequested) { }
        catch (Exception) { RuntimeMessage = "Таймер обновления недоступен. Обновляйте данные вручную."; }
    }

    private async Task RuntimeCycleAsync(RuntimeRun run, bool recheckSession, bool refreshPublic, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (api?.Transitioning == true || api?.ExternalTransitionPending == true) return;
        var now = run.Clock.GetUtcNow();
        var networkAvailable = true;
        RuntimeMessage = null;
        run.PublicRequested |= refreshPublic;
        if (api is not null && (recheckSession || now - run.LastSession >= TimeSpan.FromMinutes(1)))
        {
            run.LastSession = now;
            try { await api.RefreshSessionAsync(ct); }
            catch (BrowserApiException error) { RuntimeMessage = error.Message; networkAvailable = false; }
        }
        if (api?.Transitioning == true || api?.ExternalTransitionPending == true) return;
        await ObserveRuntimeTaskAsync(WaitForStorageAsync(ct), ct);
        var owner = ProfileKey; var generation = Generation;
        var family = api?.Session.FamilyId; var confirmed = api?.ConfirmedSessionGeneration; var revision = api?.Revision;
        bool Current() => !ct.IsCancellationRequested && owner == ProfileKey && generation == Generation
            && api?.Session.FamilyId == family && api?.ConfirmedSessionGeneration == confirmed && api?.Revision == revision
            && api?.Transitioning != true && api?.ExternalTransitionPending != true;
        var beforeGroup = GroupId;
        var latest = await ObserveRuntimeReadAsync(storage.ReadAsync<WebProfile>("profiles", owner), ct);
        if (!Current()) return;
        await gate.WaitAsync(ct);
        try
        {
            if (!Current()) return;
            if (latest is not null)
            {
                if (latest.Owner != owner || latest.StorageRevision < 0 || latest.Records is null || latest.Outbox is null)
                    throw new InvalidDataException("Сохранённый профиль повреждён.");
                // A local commit may finish while the disk read waits. Never replace
                // it with an older (or equal-version) snapshot captured by that read.
                if (latest.StorageRevision > Profile.StorageRevision)
                {
                    if (latest.ResetEpoch != Profile.ResetEpoch) Generation++;
                    Profile = latest;
                }
            }
        }
        finally { gate.Release(); }
        if (!Current()) return;
        if (networkAvailable && api?.Available == true && Authenticated) await SynchronizeAsync(ct, refreshPublic: false);
        if (!Current()) return;
        var changedGroup = beforeGroup != GroupId && !string.IsNullOrEmpty(GroupId);
        var publicDue = now - run.LastPublic >= TimeSpan.FromMinutes(15)
            || run.PublicRequested && now - run.LastPublic >= TimeSpan.FromMinutes(1)
            || changedGroup && !HasGroupData(GroupId);
        if (networkAvailable && publicDue && !Refreshing)
        {
            run.LastPublic = now; run.PublicRequested = false;
            await RefreshAsync(ct);
        }
    }

    private static async Task<T> ObserveRuntimeReadAsync<T>(Task<T> read, CancellationToken ct)
    {
        try { return await read.WaitAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { _ = ObserveAbandonedRuntimeTask(read); throw; }
    }
    private static async Task ObserveRuntimeTaskAsync(Task task, CancellationToken ct)
    {
        try { await task.WaitAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { _ = ObserveAbandonedRuntimeTask(task); throw; }
    }
    private static async Task ObserveAbandonedRuntimeTask(Task task) { try { await task; } catch (Exception) { } }

    private sealed record RuntimeWake(bool RecheckSession, bool RefreshPublic, TaskCompletionSource? Completion);
    private sealed class RuntimeRun(TimeProvider clock, CancellationTokenSource lifetime)
    {
        internal TimeProvider Clock { get; } = clock;
        internal CancellationTokenSource Lifetime { get; } = lifetime;
        internal Channel<RuntimeWake> Requests { get; } = Channel.CreateUnbounded<RuntimeWake>(new() { SingleReader = true, AllowSynchronousContinuations = false });
        internal Task Loop { get; set; } = Task.CompletedTask;
        internal Task Timer { get; set; } = Task.CompletedTask;
        internal IJSObjectReference? Module { get; set; }
        internal DotNetObjectReference<WebAppState>? Reference { get; set; }
        internal DateTimeOffset LastSession { get; set; } = clock.GetUtcNow();
        internal DateTimeOffset LastPublic { get; set; } = clock.GetUtcNow();
        internal bool PublicRequested { get; set; }
    }
}
