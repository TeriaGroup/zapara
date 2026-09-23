namespace Zapara.Client.Domain;

public sealed record SupportNote(string Author, string Body);

public static class SupportChat
{
    public static (IReadOnlyList<SupportNote> Thread, string? Error) Submit(bool signedIn, IReadOnlyList<SupportNote> thread, string subject, string body)
    {
        if (!signedIn) return (thread, "Войдите в аккаунт, чтобы отправить сообщение и увидеть ответ.");
        var theme = subject.Trim();
        var text = body.Trim();
        if (theme.Length < 3 || text.Length < 3) return (thread, "Опишите тему и что случилось.");
        return (thread.Append(new SupportNote("user", text)).ToArray(), null);
    }

    public static IReadOnlyList<SupportNote> Reply(IReadOnlyList<SupportNote> thread, string body)
        => thread.Append(new SupportNote("operator", body.Trim())).ToArray();
}
