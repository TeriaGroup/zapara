using Zapara.Contracts.Accounts;

namespace Zapara.Server.Accounts;

public sealed partial class AccountService
{
    public Task<DevicesResponse> ListDevicesAsync(string accessToken, int limit = 20, string? cursor = null, CancellationToken ct = default)
    {
        if (limit is < 1 or > 100) throw new AccountServiceException(AccountFailure.InvalidRequest);
        return AuthorizedAsync(accessToken, async (db, user, family) =>
        {
            var position = AccountDeviceCursor.Parse(cursor, user.User.UserId);
            var values = position is null ? new object?[] { user.User.UserId, db.Now, limit + 1 }
                : [user.User.UserId, db.Now, limit + 1, position.CreatedAt, position.FamilyId];
            await using var command = db.Command($"""
                SELECT family_id,device_id,device_name,platform,created_at,last_seen_at,expires_at
                FROM {schema}.session_families WHERE user_id=@p0 AND revoked_at IS NULL AND expires_at>@p1
                {(position is null ? "" : "AND (created_at,family_id)<(@p3,@p4)")}
                ORDER BY created_at DESC,family_id DESC LIMIT @p2
                """, values);
            var devices = new List<DeviceResponse>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                devices.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4), reader.GetFieldValue<DateTimeOffset>(5),
                    reader.GetFieldValue<DateTimeOffset>(6), reader.GetGuid(0) == family.Id));
            string? next = null;
            if (devices.Count > limit)
            {
                devices.RemoveAt(limit);
                next = new AccountDeviceCursor(devices[^1].CreatedAt, devices[^1].FamilyId).Encode(user.User.UserId);
            }
            return new DevicesResponse(devices.AsReadOnly(), next);
        }, ct);
    }
}
