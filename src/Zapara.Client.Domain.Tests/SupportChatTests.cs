using Xunit;

namespace Zapara.Client.Domain.Tests;

public sealed class SupportChatTests
{
    [Fact]
    public void Guest_does_not_open_a_thread_and_a_signed_in_exchange_stays_one_thread()
    {
        var guest = SupportChat.Submit(false, [], "Кнопка", "Не нажимается кнопка пары");
        Assert.Equal("Войдите в аккаунт, чтобы отправить сообщение и увидеть ответ.", guest.Error);
        Assert.Empty(guest.Thread);
        var opened = SupportChat.Submit(true, [], "Кнопка", "Не нажимается кнопка пары");
        Assert.Null(opened.Error);
        var replied = SupportChat.Reply(opened.Thread, "Поправили переключатель.");
        var followed = SupportChat.Submit(true, replied, "уточнение", "Теперь нажимается.");
        Assert.Null(followed.Error);
        Assert.Equal(["user", "operator", "user"], followed.Thread.Select(item => item.Author).ToArray());
        Assert.Equal("Не нажимается кнопка пары", followed.Thread[0].Body);
        Assert.Equal("Теперь нажимается.", followed.Thread[2].Body);
    }
}
