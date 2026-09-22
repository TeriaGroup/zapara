using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;
using Zapara.Server.Social;

namespace Zapara.Server.Tests;

public sealed class SocialMediaTests
{
    [Fact]
    public void Photo_is_reencoded_to_webp_and_large_edge_is_capped()
    {
        using var image = new Image<Rgba32>(2000, 500);
        var random = new Random(7);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            }
        });
        using var png = new MemoryStream();
        image.SaveAsPng(png);
        var source = png.ToArray();
        var (bytes, width, height) = PhotoCompressor.Compress(source);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.Equal(1920, width);
        Assert.Equal(480, height);
        Assert.True(bytes.Length < source.Length);
        Assert.InRange(bytes.Length, 12, 700_000);
    }

    [Fact]
    public void Tiny_png_and_garbage_and_oversized_header_follow_the_rules()
    {
        var tiny = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var (bytes, width, height) = PhotoCompressor.Compress(tiny);
        Assert.Equal(1, width);
        Assert.Equal(1, height);
        Assert.StartsWith("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));

        var garbage = Assert.Throws<SocialException>(() => PhotoCompressor.Compress(new byte[64]));
        Assert.Equal(400, garbage.Status);
        Assert.Equal("invalid_image", garbage.Code);

        var bomb = Assert.Throws<SocialException>(() => PhotoCompressor.Compress(Png(8000, 8000)));
        Assert.Equal(413, bomb.Status);
    }

    [Fact]
    public void Documents_keep_one_safe_extension()
    {
        Assert.Equal("отчёт.pdf", DocumentPolicy.CleanName(@"C:\temp\отчёт.pdf", 128));
        Assert.Equal("application/pdf", DocumentPolicy.ContentType("отчёт.pdf"));
        Assert.Contains("filename*=UTF-8''", DocumentPolicy.Header("отчёт.pdf", inline: false));
        Assert.StartsWith("attachment;", DocumentPolicy.Header("отчёт.pdf", inline: false));
        Assert.StartsWith("inline;", DocumentPolicy.Header("Фото.webp", inline: true));

        var script = Assert.Throws<SocialException>(() => DocumentPolicy.CleanName("report.pdf.exe", 20));
        Assert.Equal(400, script.Status);
        var empty = Assert.Throws<SocialException>(() => DocumentPolicy.CleanName("notes.txt", 0));
        Assert.Equal(400, empty.Status);
        var huge = Assert.Throws<SocialException>(() => DocumentPolicy.CleanName("notes.txt", DocumentPolicy.MaxBytes + 1));
        Assert.Equal(413, huge.Status);
    }

    [Fact]
    public void Voice_accepts_compressed_containers_and_rejects_other_bytes()
    {
        Assert.Equal("audio/webm", VoicePolicy.Inspect(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 1, 2, 3, 4, 5, 6, 7, 8 }).Type);
        Assert.Equal(".ogg", VoicePolicy.Inspect("OggSrestofhead"u8).Extension);
        var mp4 = new byte[16];
        "ftyp"u8.CopyTo(mp4.AsSpan(4));
        Assert.Equal("audio/mp4", VoicePolicy.Inspect(mp4).Type);
        Assert.Equal(1500, VoicePolicy.Duration(1500));
        Assert.Null(VoicePolicy.Duration(null));
        Assert.Equal(400, Assert.Throws<SocialException>(() => VoicePolicy.Inspect("<html>...."u8)).Status);
        Assert.Equal(400, Assert.Throws<SocialException>(() => VoicePolicy.Duration(0)).Status);
        Assert.Equal(413, Assert.Throws<SocialException>(() => VoicePolicy.Inspect(new byte[VoicePolicy.MaxBytes + 1])).Status);
        Assert.True(SocialStickers.Known("coffee"));
        Assert.Equal("Кофе", SocialStickers.Title("coffee"));
        var movie = new byte[16];
        "ftypisom"u8.CopyTo(movie.AsSpan(4));
        Assert.Equal("video/mp4", CirclePolicy.Inspect(movie).Type);
        Assert.Equal("video/webm", CirclePolicy.Inspect(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 1, 2, 3, 4, 5, 6, 7, 8 }).Type);
        Assert.Equal(60_000, CirclePolicy.Duration(60_000));
        Assert.Equal(400, Assert.Throws<SocialException>(() => CirclePolicy.Duration(60_001)).Status);
        Assert.Equal(400, Assert.Throws<SocialException>(() => CirclePolicy.Inspect("OggSnotavideo!!"u8)).Status);
        Assert.False(SocialStickers.Known("telegram"));
        Assert.Equal("Стикер", SocialStickers.Title(null));
        var homework = ChatCards.Canonical("""{"v":1,"type":"homework","subject":"Базы данных","text":"Доделать лабу","done":false}""");
        Assert.Contains("Базы данных", homework);
        Assert.Equal("Домашка · Базы данных", ChatCards.Preview(homework));
        var day = ChatCards.Canonical("""{"v":1,"type":"schedule","date":"2026-09-22","group":"09С33","title":"22 сентября","items":[{"time":"09:00–10:35","lesson":"лек","subject":"Базы","place":"ГК 401","teacher":"Иванов"}]}""");
        Assert.StartsWith("Расписание", ChatCards.Preview(day));
        Assert.Equal(400, Assert.Throws<SocialException>(() => ChatCards.Canonical("""{"v":1,"type":"schedule","date":"вчера","title":"День","items":[{"time":"09:00","subject":"Базы"}]}""")).Status);
        Assert.Equal(400, Assert.Throws<SocialException>(() => ChatCards.Canonical("""{"v":1,"type":"nope"}""")).Status);
    }

    private static byte[] Png(int width, int height)
    {
        var data = new byte[13];
        Write(data, 0, width);
        Write(data, 4, height);
        data[8] = 8;
        data[9] = 2;
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var chunk = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes("IHDR").CopyTo(chunk, 0);
        data.CopyTo(chunk, 4);
        var crc = Crc(chunk);
        Write(output, data.Length);
        output.Write(chunk);
        Write(output, unchecked((int)crc));
        return output.ToArray();
    }

    private static void Write(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        Write(bytes, 0, value);
        stream.Write(bytes);
    }

    private static void Write(Span<byte> target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint Crc(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var item in data)
        {
            crc ^= item;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return crc ^ 0xFFFFFFFF;
    }
}
