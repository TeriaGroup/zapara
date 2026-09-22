using System.Net;
using Vograph.Core.Services.Communities;
using Xunit;
using Zapara.Contracts.Communities;
using static Vograph.Desktop.Tests.AccountClientTestSupport;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

public sealed class CommunityClientVersionTests
{
    [Fact]
    public async Task Unsupported_v2_route_falls_back_once_and_remembers_v1()
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        var paths = new List<string>();
        handler.Send = (request, _) =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(paths.Count == 1 ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("") } : Payload(Array.Empty<CommunityResponse>()));
        };
        await client.ListAsync(Access, ct: TestContext.Current.CancellationToken);
        await client.ListAsync(Access, ct: TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "/root/api/v2/communities", "/root/api/v1/communities", "/root/api/v1/communities" }, paths);
    }

    [Theory]
    [InlineData(401, "invalid_session")]
    [InlineData(403, "forbidden")]
    [InlineData(404, "not_found")]
    [InlineData(503, "db_unavailable")]
    public async Task Typed_errors_never_replay_a_request_on_v1(int status, string code)
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        handler.Send = (_, _) => Task.FromResult(Payload(new CommunityError("Ошибка", status, code), (HttpStatusCode)status));
        await Assert.ThrowsAsync<CommunityClientException>(() => client.ListAsync(Access, ct: TestContext.Current.CancellationToken));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task V2_homework_reader_preserves_paragraphs()
    {
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var client = new CommunityHttpClient(http, Root);
        var record = new HomeworkResponse(Homework.HomeworkId, Homework.CommunityId, Homework.Title, "Первый\n\tВторой", Homework.Revision, Homework.CreatedAt, Homework.UpdatedAt);
        handler.Send = (request, _) =>
        {
            Assert.Contains("/api/v2/communities/", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Payload(record));
        };
        Assert.Equal("Первый\n\tВторой", (await client.GetHomeworkAsync(Access, record.CommunityId, record.HomeworkId, TestContext.Current.CancellationToken)).Body);
    }
}
