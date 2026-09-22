namespace Zapara.Server.Social;

public static class VoicePolicy
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxDurationMs = 180_000;

    public static (string Type, string Extension) Inspect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12) throw new SocialException(400, "invalid_request");
        if (bytes.Length > MaxBytes) throw new SocialException(413, "payload_too_large");
        if (bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3)
            return ("audio/webm", ".webm");
        if (bytes[0] == (byte)'O' && bytes[1] == (byte)'g' && bytes[2] == (byte)'g' && bytes[3] == (byte)'S')
            return ("audio/ogg", ".ogg");
        if (bytes[4] == (byte)'f' && bytes[5] == (byte)'t' && bytes[6] == (byte)'y' && bytes[7] == (byte)'p')
            return ("audio/mp4", ".m4a");
        if ((bytes[0] == (byte)'I' && bytes[1] == (byte)'D' && bytes[2] == (byte)'3') || (bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0))
            return ("audio/mpeg", ".mp3");
        throw new SocialException(400, "invalid_request");
    }

    public static int? Duration(int? value)
    {
        if (value is null) return null;
        if (value is < 1 or > MaxDurationMs) throw new SocialException(400, "invalid_request");
        return value;
    }
}
