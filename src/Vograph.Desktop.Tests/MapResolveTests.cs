using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class MapResolveTests
{
    [Fact]
    public void Letter_suffix_stays_on_the_room_key()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        var info = db.Services.Maps.Resolve("219А;")!;
        Assert.Equal("ГК", info.Building);
        Assert.Equal(2, info.Floor);
        Assert.Equal("219А", info.RoomRaw);
        var ulk = db.Services.Maps.Resolve("507*а;")!;
        Assert.Equal("УЛК", ulk.Building);
        Assert.Equal(5, ulk.Floor);
        Assert.Equal("507а", ulk.RoomRaw);
    }

    [Fact]
    public void GetCoords_prefers_lettered_key_over_digits_only_neighbour()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        db.Services.Maps.SaveCoords("ГК", 2, "219", 0.10, 0.10, 0.05, 0.05);
        db.Services.Maps.SaveCoords("ГК", 2, "219а", 0.80, 0.80, 0.05, 0.05);
        var hit = db.Services.Maps.GetCoords("ГК", 2, "219А")!;
        Assert.Equal(0.80, hit.x);
        Assert.Equal(0.80, hit.y);
    }

    [Fact]
    public void Vc_keeps_letter_suffix_and_does_not_strip_ke1_to_one()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        var lettered = db.Services.Maps.Resolve("ВЦ 219А;")!;
        Assert.Equal("ВЦ", lettered.Building);
        Assert.Equal(2, lettered.Floor);
        Assert.Equal("219А", lettered.RoomRaw);
        var ke = db.Services.Maps.Resolve("ВЦ КЕ1;")!;
        Assert.Equal("ВЦ", ke.Building);
        Assert.Equal("КЕ1", ke.RoomRaw);
        Assert.Equal(1, ke.Floor);
    }
}
