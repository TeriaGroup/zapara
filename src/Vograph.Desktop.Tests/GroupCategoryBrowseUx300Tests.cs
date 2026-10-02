using Vograph.Desktop.Features.Groups;
using Xunit;

namespace Vograph.Desktop.Tests;

public class GroupCategoryBrowseUx300Tests
{
    [Fact]
    public void Category_title_search_keeps_matching_bucket()
    {
        SpaceCategoryBucket[] categories =
        [
            new(Guid.NewGuid()) { Title = "Учёба" },
            new(Guid.NewGuid()) { Title = "Объявления" }
        ];

        Assert.Equal("Учёба", Assert.Single(GroupCategoryBrowse.Filter(categories, "учеба")).Title);
        Assert.Equal(2, GroupCategoryBrowse.Filter(categories, "").Count);
    }
}
