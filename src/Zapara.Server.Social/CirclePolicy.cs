namespace Zapara.Server.Social;

public static class CirclePolicy
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public const int MaxDurationMs = 60_000;

    public static (string Type, string Extension) Inspect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12) throw new SocialException(400, "invalid_request");
        if (bytes.Length > MaxBytes) throw new SocialException(413, "payload_too_large");
        if (bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3)
            return ("video/webm", ".webm");
        if (bytes[4] == (byte)'f' && bytes[5] == (byte)'t' && bytes[6] == (byte)'y' && bytes[7] == (byte)'p')
        {
            var brand = System.Text.Encoding.ASCII.GetString(bytes.Slice(8, 4));
            if (brand is "M4A " or "M4B ") throw new SocialException(400, "invalid_request");
            return ("video/mp4", ".mp4");
        }
        throw new SocialException(400, "invalid_request");
    }

    public static int? Duration(int? value)
    {
        if (value is null) return null;
        if (value is < 1 or > MaxDurationMs) throw new SocialException(400, "invalid_request");
        return value;
    }
}
