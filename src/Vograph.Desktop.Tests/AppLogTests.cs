using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class AppLogTests
{
    [Fact]
    public void Error_Writes_Context_Type_And_Message()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vograph-tests", Guid.NewGuid().ToString("N"));
        var log = new AppLog(dir);

        log.Info("started");
        log.Error("schedule", new InvalidOperationException("boom"));

        var text = File.ReadAllText(log.CurrentFile);
        Assert.Contains("INFO started", text);
        Assert.Contains("ERROR schedule: InvalidOperationException: boom", text);
        Assert.StartsWith("desktop-", Path.GetFileName(log.CurrentFile));
    }
}
