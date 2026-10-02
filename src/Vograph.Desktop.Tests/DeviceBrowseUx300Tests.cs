using Vograph.Desktop.Features.Account;
using Zapara.Contracts.Accounts;
using Xunit;

namespace Vograph.Desktop.Tests;

public class DeviceBrowseUx300Tests
{
    [Fact]
    public void Device_search_and_other_scope_include_name_platform_and_id_suffix()
    {
        var now = DateTimeOffset.UtcNow;
        DeviceResponse[] devices =
        [
            new(Guid.NewGuid(), Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaa1234"), "Этот ноутбук", "windows", now, now, now.AddDays(30), true),
            new(Guid.NewGuid(), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb5678"), "Телефон", "android", now, now, now.AddDays(30), false)
        ];

        Assert.Equal(["Телефон"], DeviceBrowse.Filter(devices, "android", 1).Select(device => device.DeviceName));
        Assert.Equal(["Телефон"], DeviceBrowse.Filter(devices, "5678", 1).Select(device => device.DeviceName));
        Assert.Empty(DeviceBrowse.Filter(devices, "ноутбук", 1));
    }
}
