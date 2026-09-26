using Vograph.Core.Services.Communities;
using Xunit;
using Zapara.Contracts.Communities;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class CommunitySpaceClientTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Form_pages_over_64KiB_require_negotiated_modern_response(bool modern)
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        var form = Guid.NewGuid();
        var question = Guid.NewGuid();
        var answers = Enumerable.Range(0, 26).Select(_ => new GroupFormAnswerResponse(null,
            [new(question, new string('x', 4000), [])], Now)).ToArray();
        var page = new GroupFormResponsesResponse(form, answers, null, answers.Length);
        Assert.True(CommunityJson.Serialize(page).Length > CommunityValidation.RequestBytes);
        handler.Send = (request, _) =>
        {
            Assert.Equal("1", Assert.Single(request.Headers.GetValues("X-Zapara-Group-Space")));
            var response = Payload(page);
            if (modern) response.Headers.Add("X-Zapara-Group-Space", "1");
            return Task.FromResult(response);
        };
        if (modern)
        {
            var actual = await client.FormResponsesAsync(Access, CommunityId, form, TestContext.Current.CancellationToken);
            Assert.Equal(26, actual.Responses.Count);
            Assert.Equal(4000, actual.Responses[0].Answers[0].Text!.Length);
        }
        else
        {
            var error = await Assert.ThrowsAsync<CommunityClientException>(() => client.FormResponsesAsync(Access, CommunityId, form, TestContext.Current.CancellationToken));
            Assert.Equal(CommunityClientFailure.BodyTooLarge, error.Failure);
        }
    }
}
