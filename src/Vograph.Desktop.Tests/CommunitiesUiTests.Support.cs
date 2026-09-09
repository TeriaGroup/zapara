using System.Net;
using Vograph.Core.Services.Communities;
using Vograph.Desktop.Features.Communities;
using Vograph.Desktop.Services;
using Zapara.Contracts.Communities;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;

namespace Vograph.Desktop.Tests;

internal sealed class CommunitiesUiHarness : IDisposable
{
    private readonly ProfileTestDirectory directory = new();
    private readonly HttpClient http;
    private readonly CommunityHttpClient client;
    public AccountClientHandler Handler { get; } = new();
    public AppServices Services { get; }
    public CommunitiesViewModel Vm { get; }
    public int Calls => Handler.Calls;
    public List<CommunityResponse> Memberships { get; } = [];
    public List<CommunityResponse> Catalog { get; } = [];
    public List<JoinRequestResponse> Joins { get; } = [];
    public List<HomeworkResponse> Homework { get; } = [];
    public Dictionary<Guid, CompletionResponse> Completions { get; } = [];
    public List<AnnouncementResponse> Announcements { get; } = [];
    public List<PollResponse> Polls { get; } = [];
    public List<MemberResponse> People { get; } = [.. Members];
    public PollResultsResponse? Results { get; set; } = CommunityClientTestSupport.Results;
    public (int Status, string Code)? Force { get; set; }
    public List<(string Method, string Path)> Requests { get; } = [];

    public CommunitiesUiHarness(bool guest = false, bool guestWithClient = false, string? groupId = null)
    {
        Services = AppServices.Create(directory.Root, () => false);
        Services.AllowNetwork = false;
        http = new HttpClient(Handler);
        client = new CommunityHttpClient(http, Root);
        Handler.Send = Respond;
        if (guest)
            Vm = new CommunitiesViewModel(Services);
        else if (guestWithClient)
            Vm = new CommunitiesViewModel(Services, client, _ => Task.FromResult<string?>(null));
        else
            Vm = new CommunitiesViewModel(Services, client, _ => Task.FromResult<string?>(Access),
                groupId is null ? null : () => groupId);
    }

    public void Dispose()
    {
        Vm.Detach();
        client.Dispose();
        http.Dispose();
        Services.Dispose();
        directory.Dispose();
    }

    private Task<HttpResponseMessage> Respond(HttpRequestMessage request, CancellationToken _)
    {
        var method = request.Method.Method;
        var uri = request.RequestUri!;
        var marker = "/api/v1/communities";
        var at = uri.AbsolutePath.IndexOf(marker, StringComparison.Ordinal);
        var rest = at < 0 ? uri.AbsolutePath : uri.AbsolutePath[(at + marker.Length)..];
        Requests.Add((method, rest + uri.Query));
        if (Force is { } forced) return Task.FromResult(Problem(forced.Status, forced.Code));
        var parts = rest.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (method == "GET" && parts.Length == 0)
            return Task.FromResult(Payload((uri.Query.Contains("groupId=", StringComparison.Ordinal) ? Catalog : Memberships).ToArray()));
        if (parts.Length == 0) return Task.FromResult(Problem(404, "not_found"));
        if (parts.Length == 1 && method == "GET")
            return Task.FromResult(Payload(Memberships.Concat(Catalog).First(c => c.CommunityId.ToString("D") == parts[0])));
        return Task.FromResult(Route(method, parts) ?? Problem(404, "not_found"));
    }

    private HttpResponseMessage? Route(string method, string[] parts)
    {
        if (parts.Length == 2 && parts[1] == "join-requests")
        {
            if (method == "POST")
            {
                Joins.Add(Pending);
                return Payload(Pending, HttpStatusCode.Created);
            }
            return method == "GET" ? Payload(Joins.ToArray()) : null;
        }
        if (parts.Length == 4 && parts[1] == "join-requests")
        {
            var found = Joins.FirstOrDefault(j => j.RequestId.ToString("D") == parts[2]);
            if (found is null) return Problem(404, "not_found");
            if (parts[3] == "accept" && method == "POST")
            {
                Joins.Remove(found);
                return Payload(Accepted);
            }
            if (parts[3] == "reject" && method == "POST")
            {
                Joins.Remove(found);
                return Payload(Rejected);
            }
            return null;
        }
        if (parts.Length == 2 && parts[1] == "members" && method == "GET") return Payload(People.ToArray());
        if (parts.Length == 2 && parts[1] == "staff" && method == "GET") return Payload(Staff);
        if (parts.Length == 2 && parts[1] == "homework")
        {
            if (method == "GET") return Payload(Homework.ToArray());
            if (method == "POST")
            {
                Homework.Add(CommunityClientTestSupport.Homework);
                return Payload(CommunityClientTestSupport.Homework, HttpStatusCode.Created);
            }
        }
        if (parts.Length == 3 && parts[1] == "homework")
        {
            var hw = Homework.FirstOrDefault(h => h.HomeworkId.ToString("D") == parts[2]);
            if (hw is null) return Problem(404, "not_found");
            if (method == "GET") return Payload(hw);
            if (method == "PUT")
            {
                var updated = new HomeworkResponse(hw.HomeworkId, hw.CommunityId, "ДЗ+", "Текст+", hw.Revision + 1, hw.CreatedAt, CommunityClientTestSupport.Now);
                Homework[Homework.IndexOf(hw)] = updated;
                return Payload(updated);
            }
        }
        if (parts.Length == 4 && parts[1] == "homework" && parts[3] == "completion")
        {
            var id = Guid.Parse(parts[2]);
            if (method == "GET")
                return Payload(Completions.GetValueOrDefault(id) ?? new CompletionResponse(id, false, 0, null));
            if (method == "PUT")
            {
                var done = new CompletionResponse(id, true, 1, CommunityClientTestSupport.Now);
                Completions[id] = done;
                return Payload(done);
            }
        }
        if (parts.Length == 2 && parts[1] == "announcements")
        {
            if (method == "GET") return Payload(Announcements.ToArray());
            if (method == "POST")
            {
                Announcements.Add(Announcement);
                return Payload(Announcement, HttpStatusCode.Created);
            }
        }
        if (parts.Length == 3 && parts[1] == "announcements" && method == "PUT")
        {
            var item = Announcements.FirstOrDefault(a => a.AnnouncementId.ToString("D") == parts[2]);
            if (item is null) return Problem(404, "not_found");
            var updated = new AnnouncementResponse(item.AnnouncementId, item.CommunityId, "Собрание+", "Текст+", item.Revision + 1, item.CreatedAt, CommunityClientTestSupport.Now);
            Announcements[Announcements.IndexOf(item)] = updated;
            return Payload(updated);
        }
        if (parts.Length == 2 && parts[1] == "polls")
        {
            if (method == "GET") return Payload(Polls.ToArray());
            if (method == "POST")
            {
                Polls.Add(Poll);
                return Payload(Poll, HttpStatusCode.Created);
            }
        }
        if (parts.Length == 3 && parts[1] == "polls" && method == "GET")
            return Payload(Polls.First(p => p.PollId.ToString("D") == parts[2]));
        if (parts.Length == 4 && parts[1] == "polls" && parts[3] == "votes" && method == "POST")
            return Payload(Vote, HttpStatusCode.Created);
        if (parts.Length == 4 && parts[1] == "polls" && parts[3] == "results" && method == "GET")
            return Payload(Results!);
        return null;
    }
}
