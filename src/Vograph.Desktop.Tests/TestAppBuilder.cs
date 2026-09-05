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
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
