using Avalonia.Headless.XUnit;
using Vograph.Desktop.Features.Groups;
using Zapara.Contracts.Communities;
using Xunit;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public class GroupHistoryUx300Tests
{
    private static async Task<(GroupSpaceViewModelTests.Fixture Fixture, Guid Parent, Guid Child)> Open()
    {
        var fixture = new GroupSpaceViewModelTests.Fixture("chat", "chat");
        var parent = Guid.NewGuid(); var child = Guid.NewGuid();
        fixture.Intercept = (request, _) =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/messages", StringComparison.Ordinal))
                return Task.FromResult<HttpResponseMessage?>(null);
            var earlier = request.RequestUri.Query.Contains("before", StringComparison.Ordinal);
            ChatMessageResponse[] rows = earlier
                ? [new(parent, fixture.Conversation, fixture.Person, "Борис", "Ранний текст", Now)]
                : [new(child, fixture.Conversation, fixture.Person, "Борис", "Ответ", Now, replyTo: parent)];
            return Task.FromResult<HttpResponseMessage?>(Payload(new ChatPageResponse(rows, !earlier)));
        };
        await fixture.Vm.ActivateAsync();
        Assert.Equal(child, Assert.Single(fixture.Vm.Messages).Id);
        return (fixture, parent, child);
    }

    [AvaloniaFact]
    public async Task Filter_searches_older_pages_without_resetting_query()
    {
        var (fixture, parent, _) = await Open(); using (fixture)
        {
            fixture.Vm.MessageSearch = "Ранний";
            Assert.Empty(fixture.Vm.FilteredMessages);
            await fixture.Vm.SearchOlderMessagesCommand.ExecuteAsync(null);
            Assert.Equal(parent, Assert.Single(fixture.Vm.FilteredMessages).Id);
            Assert.Equal("Ранний", fixture.Vm.MessageSearch);
            Assert.Contains("Найдено", fixture.Vm.OlderSearchFeedback);
        }
    }

    [AvaloniaFact]
    public async Task Quote_loads_parent_by_cursor_and_focuses_exact_message()
    {
        var (fixture, parent, child) = await Open(); using (fixture)
        {
            GroupMessageRow? focused = null; fixture.Vm.QuoteTargetRequested += row => focused = row;
            await fixture.Vm.JumpQuoteCommand.ExecuteAsync(fixture.Vm.Messages.Single(row => row.Id == child));
            Assert.Equal(parent, focused?.Id);
            Assert.True(fixture.Vm.Messages.Single(row => row.Id == parent).IsQuoteTarget);
        }
    }
}
