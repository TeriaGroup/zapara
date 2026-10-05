using Vograph.Desktop.Features.Communities;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class CommunityBrowseTests
{
    private sealed record Card(string Id, string Name, string Description);

    [Fact]
    public void SearchMatchesNamesAndDescriptionsWithoutMutatingSource()
    {
        Card[] source =
        [
            new("one", "Н162С", "Лабораторные и проекты"),
            new("two", "А4313", "Объявления"),
            new("three", "Третья группа", "Проекты")
        ];
        Assert.Equal(["one"], CommunityBrowse.Filter(source, "  н162с ", row => row.Name, row => row.Description).Select(row => row.Id));
        Assert.Equal(["one", "three"], CommunityBrowse.Filter(source, "ПРОЕКТ", row => row.Name, row => row.Description).Select(row => row.Id));
        Assert.Equal(source, CommunityBrowse.Filter(source, "", row => row.Name, row => row.Description));
        Assert.Equal(["one", "two", "three"], source.Select(row => row.Id));
    }

    [Fact]
    public void NoMatchStaysEmpty()
    {
        var source = new[] { new Card("one", "Н162С", "Лабораторные") };
        Assert.Empty(CommunityBrowse.Filter(source, "не существует", row => row.Name, row => row.Description));
        Assert.Empty(CommunityBrowse.Filter(Array.Empty<Card>(), "группа", row => row.Name, row => row.Description));
    }
}
