using System.Text;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class SupportLogPackTests
{
    [Fact]
    public void Daily_log_is_packed_without_a_file_dialog()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"));
        var log = new AppLog(dir);
        log.Info("support-marker");
        log.Error("schedule", new InvalidOperationException("boom"));

        var packed = SupportLogPack.FromLog(log);

        var file = Assert.Single(packed);
        Assert.Equal("log", file.Kind);
        Assert.Equal("desktop-1.log", file.Name);
        Assert.Equal("text/plain", file.ContentType);
        var text = Encoding.UTF8.GetString(file.Bytes);
        Assert.Contains("support-marker", text);
        Assert.Contains("InvalidOperationException: boom", text);
        Assert.True(file.Bytes.Length <= SupportLogPack.MaxBytes);
    }

    [Fact]
    public void Oversized_log_keeps_the_newest_tail_in_three_files()
    {
        var marker = "END-OF-LOG\n";
        var body = "\0" + new string('я', 900_000) + "\n" + marker;
        var packed = SupportLogPack.Pack([("desktop-old.log", Encoding.UTF8.GetBytes(body))]);

        Assert.InRange(packed.Count, 1, SupportLogPack.MaxFiles);
        Assert.All(packed, file =>
        {
            Assert.True(file.Bytes.Length <= SupportLogPack.MaxBytes);
            Assert.EndsWith(".log", file.Name);
            _ = new UTF8Encoding(false, true).GetString(file.Bytes);
        });
        var joined = string.Concat(packed.Select(file => Encoding.UTF8.GetString(file.Bytes)));
        Assert.EndsWith(marker, joined);
        Assert.Equal(-1, joined.IndexOf('\0'));
    }

    [Fact]
    public void Empty_log_directory_adds_nothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Assert.Empty(SupportLogPack.FromLog(new AppLog(dir)));
        Assert.Empty(SupportLogPack.Pack([]));
    }
}
