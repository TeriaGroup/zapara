using Vograph.Desktop.Features.Groups;
using Xunit;

namespace Vograph.Desktop.Tests;

public class GroupMaterialBrowseUx300Tests
{
    [Fact]
    public void Material_search_matches_name_or_author_in_loaded_page()
    {
        GroupMessageRow[] rows =
        [
            new(Guid.NewGuid(), "Куратор", "Лекция.pdf", "01.10 10:00", false, "file"),
            new(Guid.NewGuid(), "Староста", "https://example.org/plan", "01.10 11:00", false, "text")
        ];

        Assert.Equal("Лекция.pdf", Assert.Single(GroupMaterialBrowse.Filter(rows, "лекция")).Body);
        Assert.Equal("https://example.org/plan", Assert.Single(GroupMaterialBrowse.Filter(rows, "староста")).Body);
    }
}
