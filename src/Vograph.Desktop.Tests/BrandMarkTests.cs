using Avalonia.Headless.XUnit;
using Vograph.Desktop.Features.Account;

namespace Vograph.Desktop.Tests;

public class BrandMarkTests
{
    [AvaloniaFact]
    public void Official_marks_parse()
    {
        YandexMark.Touch();
        VkMark.Touch();
    }
}
