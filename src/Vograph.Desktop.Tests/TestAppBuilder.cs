using Avalonia;
using Avalonia.Headless;
using Vograph.Desktop;
using Vograph.Desktop.Tests;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]
// Headless Avalonia and the static Loc.Current are not safe to share between parallel tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Vograph.Desktop.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHarfBuzz()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        // Motion off for the whole session, once. App.axaml ships Theme/Motion.axaml included and headless tests
        // never run App's startup switch, so every [AvaloniaFact] would otherwise capture frames mid-transition —
        // including the ones in classes that do not derive from UiTest (SmokeTests, ThemeTests, ResourceKeysTests)
        // and therefore never reach its ctor guard. MotionTests turn motion on for themselves and back off.
        .AfterSetup(b => ((App)b.Instance!).SetMotion(false));
}
