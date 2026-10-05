namespace Zapara.Server.Operator;

public sealed record SupportAttachment(Guid Id, string Kind, string Name);

public sealed record SupportLine(string Author, string Body, DateTimeOffset At, IReadOnlyList<SupportAttachment>? Attachments = null);

public sealed class SupportTicket
{
    public SupportTicket(Guid id, string userId, string subject, IReadOnlyList<SupportLine> messages)
    {
        Id = id;
        UserId = userId;
        Subject = subject;
        Messages = messages;
    }

    public Guid Id { get; }
    public string UserId { get; }
    public string Subject { get; }
    public IReadOnlyList<SupportLine> Messages { get; private set; }

    public void Append(SupportLine line) => Messages = SupportThread.Append(Messages, line);
}

public static class SupportThread
{
    public static IReadOnlyList<SupportLine> Append(IReadOnlyList<SupportLine> messages, SupportLine line)
        => messages.Append(line).OrderBy(item => item.At).ThenBy(item => item.Author == "operator" ? 1 : 0).ToArray();
}

public sealed class SupportDesk
{
    private readonly List<SupportTicket> tickets = [];

    public SupportTicket Open(string userId, string subject, string body, DateTimeOffset at)
    {
        var ticket = new SupportTicket(Guid.NewGuid(), userId, subject.Trim(), [new SupportLine("user", body.Trim(), at)]);
        tickets.Add(ticket);
        return ticket;
    }

    public IReadOnlyList<SupportTicket> List() => tickets;

    public SupportTicket Reply(Guid id, string body, DateTimeOffset at)
    {
        var ticket = tickets.Single(item => item.Id == id);
        ticket.Append(new SupportLine("operator", body.Trim(), at));
        return ticket;
    }
}
