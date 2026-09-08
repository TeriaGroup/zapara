namespace Zapara.Server.Timetable;

public sealed class IngestService(SnapshotStore store, TimetableInput input)
{
    public async Task<int> IngestFileAsync(string path, CancellationToken ct = default)
        => (await IngestFileResultAsync(path, ct)).ExitCode;

    public async Task<int> IngestFetchAsync(HttpClient client, CancellationToken ct = default)
        => (await IngestFetchResultAsync(client, ct)).ExitCode;

    public Task<IngestResult> IngestFileResultAsync(string path, CancellationToken ct = default)
        => IngestAsync(token => input.FromFileAsync(path, token), ct);

    public Task<IngestResult> IngestFetchResultAsync(HttpClient client, CancellationToken ct = default)
        => IngestAsync(token => input.FetchFixedAsync(client, token), ct);

    private async Task<IngestResult> IngestAsync(Func<CancellationToken, Task<SourceDocument>> read, CancellationToken ct)
    {
        IngestResult? result = null;
        try
        {
            await using var lease = await store.TryAcquireAsync(ct);
            if (lease is null) return new IngestResult(3);
            result = await PublishUnderLeaseAsync(lease, read, ct);
            return result;
        }
        catch (StoreException error)
        {
            // Disposal cannot erase an already established publication/failure outcome.
            return result is null ? IngestResult.Failed(error.FailureCode, error.AttemptId)
                : result with { CleanupFailureCode = error.FailureCode };
        }
        catch (OperationCanceledException)
        {
            return result ?? IngestResult.Failed(FailureCode.Cancelled);
        }
    }

    private async Task<IngestResult> PublishUnderLeaseAsync(RefreshLease lease,
        Func<CancellationToken, Task<SourceDocument>> read, CancellationToken ct)
    {
        FailureCode failure;
        try
        {
            var source = await read(ct);
            ct.ThrowIfCancellationRequested();
            var snapshot = input.Validate(source);
            ct.ThrowIfCancellationRequested();
            var id = await store.PublishAsync(lease, snapshot, ct);
            return new IngestResult(0, lease.AttemptId, id, new(snapshot.Groups.Length, snapshot.Lessons.Length));
        }
        catch (StoreException error) when (error.FailureCode == FailureCode.PublicationUnknown)
        {
            // Terminal lease: neither failure recording nor blind retry is safe.
            return IngestResult.Failed(FailureCode.PublicationUnknown, lease.AttemptId);
        }
        catch (StoreException error) { failure = error.FailureCode; }
        catch (TimetableInputException error) { failure = error.FailureCode; }
        catch (OperationCanceledException) { failure = FailureCode.Cancelled; }

        var result = IngestResult.Failed(failure, lease.AttemptId);
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await store.RecordFailedAttemptAsync(lease, failure, cleanup.Token); }
        catch (StoreException error) { result = result with { CleanupFailureCode = error.FailureCode }; }
        catch (OperationCanceledException) { result = result with { CleanupFailureCode = FailureCode.Cancelled }; }
        return result;
    }
}
