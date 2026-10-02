using Vograph.Desktop.Features.Groups;
using Xunit;

namespace Vograph.Desktop.Tests;

public class GroupAuditBrowseUx300Tests
{
    [Fact]
    public void Audit_filter_combines_action_kind_and_text_without_mutation()
    {
        string[] entries =
        [
            "01.10.2026 · Создана тема · object-a",
            "02.10.2026 · Назначена роль · object-b",
            "03.10.2026 · Изменён доступ · object-c"
        ];

        Assert.Equal(["01.10.2026 · Создана тема · object-a"], GroupAuditBrowse.Filter(entries, "object-a", 1));
        Assert.Equal(["02.10.2026 · Назначена роль · object-b"], GroupAuditBrowse.Filter(entries, "", 2));
        Assert.Equal(3, entries.Length);
    }
}
