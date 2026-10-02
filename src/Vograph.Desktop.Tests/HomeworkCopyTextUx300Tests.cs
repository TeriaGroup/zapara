using Vograph.Desktop.Features.Homeworks;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkCopyTextUx300Tests
{
    [Fact]
    public void Copied_homework_contains_subject_text_and_due_without_internal_identifier()
    {
        var text = HomeworkCopyText.Format("Матан", "Задачи 1–12", new DateTime(2026, 10, 5));

        Assert.Contains("Матан", text);
        Assert.Contains("Задачи 1–12", text);
        Assert.Contains("05.10.2026", text);
        Assert.DoesNotContain("Id", text);
    }
}
