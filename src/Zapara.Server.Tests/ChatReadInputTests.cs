using System.Text;
using Microsoft.AspNetCore.Http;
using Xunit;
using Zapara.Contracts.Communities;
using Zapara.Server.Communities;

namespace Zapara.Server.Tests;

public sealed class ChatReadInputTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    public async Task Empty_body_preserves_legacy_explicit_read(string body)
        => Assert.Null(await CommunityHttpInput.ReadThroughMessage(Context(body)));

    [Fact]
    public async Task Bound_read_accepts_its_own_request_without_changing_response_contract()
    {
        var id = Guid.NewGuid();
        var bytes = CommunityJson.Serialize(new MarkChatReadRequest(id));
        var context = Context(Encoding.UTF8.GetString(bytes));
        Assert.Equal(id, await CommunityHttpInput.ReadThroughMessage(context));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"throughMessageId\":null}")]
    [InlineData("{\"throughMessageId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"throughMessageId\":\"later\"}")]
    [InlineData("{\"throughMessageId\":\"de71524f-bd9c-429e-a6e1-ed342b2227af\",\"userId\":\"de71524f-bd9c-429e-a6e1-ed342b2227af\"}")]
    [InlineData("{\"throughMessageId\":\"de71524f-bd9c-429e-a6e1-ed342b2227af\",\"throughMessageId\":\"de71524f-bd9c-429e-a6e1-ed342b2227af\"}")]
    public async Task Malformed_target_cannot_fall_back_to_mark_all(string body)
    {
        var failure = await Assert.ThrowsAsync<CommunityInputException>(() => CommunityHttpInput.ReadThroughMessage(Context(body)));
        Assert.Equal(400, failure.Status);
    }

    [Fact]
    public async Task Nonempty_body_requires_json_and_is_bounded()
    {
        var wrongType = Context("{\"throughMessageId\":\"de71524f-bd9c-429e-a6e1-ed342b2227af\"}");
        wrongType.Request.ContentType = "text/plain";
        Assert.Equal(415, (await Assert.ThrowsAsync<CommunityInputException>(() => CommunityHttpInput.ReadThroughMessage(wrongType))).Status);
        Assert.Equal(413, (await Assert.ThrowsAsync<CommunityInputException>(() => CommunityHttpInput.ReadThroughMessage(Context(new string(' ', CommunityValidation.RequestBytes + 1))))).Status);
    }

    [Fact]
    public void Explicit_read_header_requires_supported_value()
    {
        var context = Context("");
        Assert.False(CommunityHttpInput.ExplicitReadCursor(context));
        context.Request.Headers["X-Zapara-Read-Cursor"] = "1";
        Assert.True(CommunityHttpInput.ExplicitReadCursor(context));
        context.Request.Headers["X-Zapara-Read-Cursor"] = "true";
        Assert.Throws<CommunityInputException>(() => CommunityHttpInput.ExplicitReadCursor(context));
        context.Request.Headers["X-Zapara-Read-Cursor"] = new Microsoft.Extensions.Primitives.StringValues(["1", "1"]);
        Assert.Throws<CommunityInputException>(() => CommunityHttpInput.ExplicitReadCursor(context));
    }

    private static DefaultHttpContext Context(string body)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.RequestAborted = TestContext.Current.CancellationToken;
        return context;
    }
}
