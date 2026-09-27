using System.Text.Json;
using Vograph.Core.Models;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class IntersectionPrecisionTests
{
    [Fact]
    public void New_settings_start_with_building_precision()
        => Assert.Equal(50, new Settings().IntersectionStrictness);

    [Theory]
    [InlineData(int.MinValue, 50)]
    [InlineData(0, 50)]
    [InlineData(25, 50)]
    [InlineData(50, 50)]
    [InlineData(62, 50)]
    [InlineData(63, 75)]
    [InlineData(75, 75)]
    [InlineData(87, 75)]
    [InlineData(88, 100)]
    [InlineData(100, 100)]
    [InlineData(int.MaxValue, 100)]
    public void Assigned_precision_uses_one_of_the_remaining_levels(int input, int expected)
        => Assert.Equal(expected, new Settings { IntersectionStrictness = input }.IntersectionStrictness);

    [Fact]
    public void Legacy_import_retains_other_settings_when_university_level_becomes_building()
    {
        var settings = JsonSerializer.Deserialize<Settings>("""
            {"IntersectionStrictness":25,"MyGroupId":"3313","AlwaysShowAllTrafficLights":true,"ParityInvert":true,"NotifyTime1":"20:00"}
            """)!;
        Assert.Equal(50, settings.IntersectionStrictness);
        Assert.Equal("3313", settings.MyGroupId);
        Assert.True(settings.AlwaysShowAllTrafficLights);
        Assert.True(settings.ParityInvert);
        Assert.Equal("20:00", settings.NotifyTime1);
    }
}
