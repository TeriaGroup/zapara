using System.Buffers.Binary;

namespace Zapara.Server.Accounts;

internal sealed record AccountDeviceCursor(DateTimeOffset CreatedAt, Guid FamilyId)
{
    internal string Encode(Guid userId)
    {
        var bytes = new byte[41];
        bytes[0] = 1;
        userId.TryWriteBytes(bytes.AsSpan(1, 16));
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(17, 8), CreatedAt.Ticks);
        FamilyId.TryWriteBytes(bytes.AsSpan(25, 16));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static AccountDeviceCursor? Parse(string? cursor, Guid actor)
    {
        if (cursor is null) return null;
        try
        {
            if (cursor.Length != 55 || cursor.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) throw new FormatException();
            var bytes = Convert.FromBase64String(cursor.Replace('-', '+').Replace('_', '/') + "=");
            if (bytes.Length != 41 || bytes[0] != 1 || new Guid(bytes.AsSpan(1, 16)) != actor) throw new FormatException();
            var result = new AccountDeviceCursor(new(BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(17, 8)), TimeSpan.Zero), new(bytes.AsSpan(25, 16)));
            if (result.FamilyId == Guid.Empty || result.CreatedAt == default || result.Encode(actor) != cursor) throw new FormatException();
            return result;
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            throw new AccountServiceException(AccountFailure.InvalidRequest);
        }
    }
}
