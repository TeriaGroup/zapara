using System.Threading;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Zapara.Server.Admin.UiTests;

[CollectionDefinition("admin-ui")]
public sealed class AdminUiCollection : ICollectionFixture<AdminUiGate>;

public sealed class AdminUiGate : IAsyncLifetime
{
    private Mutex? mutex;
    private bool held;
    public Microsoft.Playwright.IPlaywright? Playwright { get; private set; }
    public Microsoft.Playwright.IBrowser? Browser { get; private set; }
    public string? BrowsersPath { get; private set; }
    public string? PlaywrightError { get; private set; }
    public bool PlaywrightReady => Browser is not null;

    public async ValueTask InitializeAsync()
    {
        mutex = new Mutex(false, @"Local\ZaparaServerBuildGate");
        try { held = mutex.WaitOne(TimeSpan.FromMinutes(10)); }
        catch (AbandonedMutexException) { held = true; }
        if (!held) throw new InvalidOperationException("Local\\ZaparaServerBuildGate занят.");
        await TryStartPlaywrightAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Browser is not null) await Browser.DisposeAsync();
        }
        catch { }
        try { Playwright?.Dispose(); }
        catch { }
        Browser = null;
        Playwright = null;
        if (held)
        {
            try { mutex!.ReleaseMutex(); }
            catch { }
            held = false;
        }
        mutex?.Dispose();
    }

    internal void RequirePlaywright()
    {
        if (PlaywrightReady) return;
        Assert.Skip("Playwright Chromium недоступен: " + (PlaywrightError ?? "не установлен"));
    }

    private async Task TryStartPlaywrightAsync()
    {
        BrowsersPath = Path.Combine(Path.GetTempPath(), "opencode", "zapara-playwright-chromium");
        Directory.CreateDirectory(BrowsersPath);
        Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", BrowsersPath);
        try
        {
            var code = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (code != 0)
            {
                PlaywrightError = "install chromium exit=" + code;
                return;
            }
            Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            Browser = await Playwright.Chromium.LaunchAsync(new() { Headless = true });
        }
        catch (Exception exception)
        {
            PlaywrightError = exception.GetType().Name + ": " + exception.Message;
            if (Browser is not null) await Browser.DisposeAsync();
            Playwright?.Dispose();
            Browser = null;
            Playwright = null;
        }
    }
}
