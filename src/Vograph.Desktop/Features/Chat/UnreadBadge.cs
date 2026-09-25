namespace Vograph.Desktop.Features.Chat;

public static class UnreadBadge
{
    public static string Label(int count) => count <= 0 ? "" : count > 99 ? "99+" : count.ToString();
    public static string Description(int count) => count <= 0 ? "" : $"Непрочитанных сообщений: {count}";
}
