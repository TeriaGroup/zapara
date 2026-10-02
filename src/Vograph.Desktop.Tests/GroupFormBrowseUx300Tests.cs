using Vograph.Desktop.Features.Groups;
using Zapara.Contracts.Communities;
using Xunit;

namespace Vograph.Desktop.Tests;

public class GroupFormBrowseUx300Tests
{
    private static SpaceFormRow Row(string title, bool canRespond, bool answered)
    {
        var response = answered ? new GroupFormAnswerResponse(Guid.NewGuid(), [], DateTimeOffset.UtcNow) : null;
        var form = new GroupFormResponse(Guid.NewGuid(), Guid.NewGuid(), title, "Описание", null, false,
            [], Guid.NewGuid(), DateTimeOffset.UtcNow, canRespond, false, response, answered ? 1 : 0);
        return new SpaceFormRow(form, true, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask);
    }

    [Fact]
    public void Form_search_and_answer_status_are_combined()
    {
        SpaceFormRow[] rows = [Row("Лабораторная", true, false), Row("Практика", true, true), Row("Экзамен", false, false)];

        Assert.Equal(["Лабораторная"], GroupFormBrowse.Filter(rows, "лабо", 1).Select(row => row.Title));
        Assert.Equal(["Практика"], GroupFormBrowse.Filter(rows, "", 2).Select(row => row.Title));
        Assert.Equal(["Экзамен"], GroupFormBrowse.Filter(rows, "", 3).Select(row => row.Title));
    }
}
