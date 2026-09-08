using Zapara.Contracts.Accounts;

namespace Zapara.Server.Accounts;

public sealed partial class ExternalAuthService
{
    private async Task<ExternalTransaction> ReadTransaction(AccountRepository db, Guid id, bool locked, CancellationToken ct)
    {
        await using var command = db.Command($"""
            SELECT transaction_id,purpose,provider,native_challenge,status,expires_at,subject,display_name,
              handoff_hash,handoff_expires_at,initiator_user_id,initiator_family_id,security_version,proof_hash,proof_purpose,
              return_kind,return_port,device_id,device_name,platform
            FROM {schema}.oauth_transactions WHERE transaction_id=@p0 AND owner_id=@p1 {(locked ? "FOR UPDATE" : "")}
            """, id, secrets.Owner);
        await using var r = await command.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) throw ExternalAuthException.Gone();
        return new(r.GetGuid(0), r.GetString(1), r.GetString(2), (byte[])r[3], r.GetString(4), r.GetFieldValue<DateTimeOffset>(5),
            r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : (byte[])r[8],
            r.IsDBNull(9) ? null : r.GetFieldValue<DateTimeOffset>(9), r.IsDBNull(10) ? null : r.GetGuid(10),
            r.IsDBNull(11) ? null : r.GetGuid(11), r.IsDBNull(12) ? null : r.GetInt64(12), r.IsDBNull(13) ? null : (byte[])r[13],
            r.IsDBNull(14) ? null : r.GetString(14), r.GetString(15), r.IsDBNull(16) ? null : r.GetInt32(16),
            new(r.GetGuid(17), r.GetString(18), r.GetString(19)));
    }
}

internal sealed record ExternalTransaction(Guid Id, string Purpose, string Provider, byte[] NativeChallenge,
    string Status, DateTimeOffset Expires, string? Subject, string? DisplayName, byte[]? HandoffHash,
    DateTimeOffset? HandoffExpires, Guid? UserId, Guid? FamilyId, long? Version, byte[]? ProofHash,
    string? ProofPurpose, string ReturnKind, int? ReturnPort, DeviceInput Device)
{
    public override string ToString() => "ExternalTransaction { [REDACTED] }";
}
