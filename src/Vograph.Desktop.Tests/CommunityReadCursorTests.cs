using System.Text.Json;
using Vograph.Core.Services.Communities;
using Xunit;
using Zapara.Contracts.Communities;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class CommunityReadCursorTests
{
    private static readonly Guid Conversation = Guid.Parse("88888888-8888-4888-8888-888888888888");
    private static readonly Guid Message = Guid.Parse("99999999-9999-4999-8999-999999999999");

    [Fact]
    public async Task Paged_get_suppresses_implicit_read_and_explicit_read_has_a_bounded_target()
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        var posts = 0;
        handler.Send = async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("1", Assert.Single(request.Headers.GetValues("X-Zapara-Read-Cursor")));
                return Payload(new ChatPageResponse([], false));
            }
            posts++;
            if (posts == 1)
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal(Message.ToString("D"), json.RootElement.GetProperty("throughMessageId").GetString());
            }
            else Assert.Null(request.Content);
            return Payload(new ConversationResponse(Conversation, "direct", CommunityId, "Друг", PromotedId, null, null, 0));
        };
        await client.MessagesAsync(Access, Conversation, before: Message, ct: TestContext.Current.CancellationToken);
        await client.MarkReadAsync(Access, Conversation, Message, TestContext.Current.CancellationToken);
        await client.MarkReadAsync(Access, Conversation, TestContext.Current.CancellationToken);
        Assert.Equal(2, posts);
    }
}
