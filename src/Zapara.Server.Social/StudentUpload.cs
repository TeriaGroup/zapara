using Zapara.Server.Accounts;

namespace Zapara.Server.Social;

public sealed record StoredUpload(byte[] Bytes, QuotaReservation Reservation);

public sealed class StudentUpload(IObjectStore store, QuotaLedger ledger) : IUploadQuota
{
    public async Task<byte[]> Accept(IAccountUnitOfWork accounts, string token, string? groupId, string key, byte[] bytes, CancellationToken ct)
        => (await AcceptTrackedAsync(accounts, token, groupId, key, bytes, ct)).Bytes;

    public async Task<StoredUpload> AcceptTrackedAsync(IAccountUnitOfWork accounts, string token, string? groupId, string key, byte[] bytes, CancellationToken ct)
    {
        var reservation = await ledger.ReserveTrackedAsync(accounts, token, bytes.LongLength, groupId, ct);
        try
        {
            store.Put(key, bytes);
            var stored = store.Get(key);
            if (stored is null || !stored.AsSpan().SequenceEqual(bytes))
                throw new SocialException(503, "storage_unavailable");
            ct.ThrowIfCancellationRequested();
            return new(stored, reservation);
        }
        catch
        {
            try { store.Delete(key); } catch (Exception) { /* The reservation is released even if the object is already gone. */ }
            await ledger.ReleaseFailedAsync(reservation);
            throw;
        }
    }

}
