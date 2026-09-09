using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Zapara.Contracts.Communities;
using static Vograph.Desktop.Tests.AccountClientTestSupport;

namespace Vograph.Desktop.Tests;

internal static class CommunityClientTestSupport
{
    internal static readonly Guid CommunityId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    internal static readonly Guid RequestId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    internal static readonly Guid HomeworkId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    internal static readonly Guid AnnouncementId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    internal static readonly Guid PollId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    internal static readonly Guid OptionYes = Guid.Parse("11111111-1111-1111-1111-111111111111");
    internal static readonly Guid OptionNo = Guid.Parse("22222222-2222-2222-2222-222222222222");
    internal static readonly Guid StaffId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    internal static readonly Guid PromotedId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    internal static DateTimeOffset Now => AccountClientTestSupport.Now;
    internal static DateTimeOffset Deadline => Now.AddMinutes(10);
    internal static string Access => Token("za_");
    internal static Uri Root => new("https://example.invalid/root");
    internal static CommunityResponse Membership => new(CommunityId, "О3313", "Сообщество учебной группы", 1, "member");
    internal static CommunityResponse Catalog => new(CommunityId, "О3313", "Сообщество учебной группы", 1, null);
    internal static JoinRequestResponse Pending => new(RequestId, CommunityId, UserId, "pending", Now);
    internal static JoinRequestResponse Accepted => new(RequestId, CommunityId, PromotedId, "accepted", Now);
    internal static JoinRequestResponse Rejected => new(RequestId, CommunityId, UserId, "rejected", Now);
    internal static MemberResponse[] Members => [new(StaffId, "headman"), new(PromotedId, "curator")];
    internal static MemberResponse[] Staff => [new(StaffId, "headman"), new(PromotedId, "curator")];
    internal static HomeworkUpsert HomeworkWrite => new("ДЗ", "Текст", 0);
    internal static HomeworkResponse Homework => new(HomeworkId, CommunityId, "ДЗ", "Текст", 1, Now, Now);
    internal static CompletionUpsert CompletionWrite => new(true, 0);
    internal static CompletionResponse Completion => new(HomeworkId, true, 1, Now);
    internal static AnnouncementUpsert AnnouncementWrite => new("Собрание", "Текст", 0);
    internal static AnnouncementResponse Announcement => new(AnnouncementId, CommunityId, "Собрание", "Текст", 1, Now, Now);
    internal static PollUpsert PollWrite => new("Придете?", Deadline, ["Да", "Нет"], 0);
    internal static PollResponse Poll => new(PollId, CommunityId, "Придете?", Deadline, 1,
        [new(OptionYes, "Да", 1), new(OptionNo, "Нет", 2)]);
    internal static VoteRequest VoteWrite => new(OptionYes);
    internal static VoteResponse Vote => new(PollId, OptionYes, Now);
    internal static PollResultsResponse Results => new(PollId, 2,
        [new(OptionYes, "Да", 1), new(OptionNo, "Нет", 1)]);
    internal static HttpResponseMessage Payload(object body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, CommunityJson.CreateOptions()));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return new HttpResponseMessage(status) { Content = content };
    }
    internal static HttpResponseMessage Problem(int status, string code, string? title = null)
    {
        var content = new ByteArrayContent(CommunityJson.Serialize(new CommunityError(title ?? Password, status, code)));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/problem+json");
        return new HttpResponseMessage((HttpStatusCode)status) { Content = content };
    }
    internal static HttpResponseMessage TextPayload(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    internal static void Equal<T>(T expected, T actual)
        => Xunit.Assert.Equal(CommunityJson.Serialize(expected), CommunityJson.Serialize(actual));
}
