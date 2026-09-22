using Xunit;
using Zapara.Contracts.Accounts;

namespace Zapara.Server.Tests;

public sealed class WebAccountContractTests
{
    [Fact]
    public void BrowserDeviceIsAcceptedWithoutMasqueradingAsNative()
    {
        var device = new DeviceInput(Guid.NewGuid(), "Браузер", "web");
        Assert.Equal("web", device.Platform);
    }
}
