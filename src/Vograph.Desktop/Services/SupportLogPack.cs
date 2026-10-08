using System.Text;
using Vograph.Core.Services.Accounts;

namespace Vograph.Desktop.Services;

public static class SupportLogPack
{
    public const int MaxFiles = 3;
    public const int MaxBytes = 512 * 1024;

    public static IReadOnlyList<SupportUpload> FromLog(AppLog log)
    {
        try { return Pack(log.ReadFiles()); }
        catch (Exception) { return []; }
    }

    public static IReadOnlyList<SupportUpload> Pack(IReadOnlyList<(string Name, byte[] Bytes)> sources)
    {
        var text = new StringBuilder();
        foreach (var source in sources)
        {
            var body = Decode(source.Bytes);
            if (body.Length == 0) continue;
            if (text.Length > 0) text.Append('\n');
            text.Append("----- ").Append(CleanName(source.Name)).Append(" -----\n");
            text.Append(body);
            if (!body.EndsWith('\n')) text.Append('\n');
        }
        if (text.Length == 0) return [];
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text.ToString());
        var parts = Split(bytes);
        var uploads = new SupportUpload[parts.Count];
        for (var i = 0; i < parts.Count; i++)
            uploads[i] = new SupportUpload("log", "desktop-" + (i + 1) + ".log", "text/plain", parts[i]);
        return uploads;
    }

    private static string CleanName(string name)
    {
        var file = Path.GetFileName(name.Replace('\\', '/')).Trim();
        if (file.Length == 0 || file.Any(char.IsControl)) return "log";
        return file.Length <= 60 ? file : file[..60];
    }

    private static string Decode(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Replace("\0", "");
    }

    internal static List<byte[]> Split(byte[] bytes)
    {
        if (bytes.Length == 0) return [];
        var budget = MaxFiles * MaxBytes;
        var start = bytes.Length > budget ? bytes.Length - budget : 0;
        while (start < bytes.Length && (bytes[start] & 0xC0) == 0x80) start++;
        if (start >= bytes.Length) return [];
        if (start > 0)
        {
            var newline = start;
            var window = Math.Min(bytes.Length, start + 4096);
            while (newline < window && bytes[newline] != (byte)'\n') newline++;
            if (newline < bytes.Length && bytes[newline] == (byte)'\n' && newline + 1 < bytes.Length)
                start = newline + 1;
        }
        var newestFirst = new List<byte[]>();
        var end = bytes.Length;
        while (end > start && newestFirst.Count < MaxFiles)
        {
            var chunkStart = Math.Max(start, end - MaxBytes);
            if (chunkStart > start)
            {
                while (chunkStart < end && (bytes[chunkStart] & 0xC0) == 0x80) chunkStart++;
                var newline = chunkStart;
                var window = Math.Min(end, chunkStart + 4096);
                while (newline < window && bytes[newline] != (byte)'\n') newline++;
                if (newline < end && bytes[newline] == (byte)'\n' && newline + 1 < end)
                    chunkStart = newline + 1;
            }
            if (chunkStart >= end) break;
            var chunk = new byte[end - chunkStart];
            Buffer.BlockCopy(bytes, chunkStart, chunk, 0, chunk.Length);
            newestFirst.Add(chunk);
            end = chunkStart;
        }
        newestFirst.Reverse();
        return newestFirst;
    }
}
