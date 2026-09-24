using Avalonia.Headless.XUnit;
using Vograph.Desktop.Features.Groups;
using Xunit;
using Zapara.Client.Domain;

namespace Vograph.Desktop.Tests;

public sealed class MessengerHoldTests
{
    [AvaloniaFact]
    public void Hold_opens_actions_and_a_tap_does_not()
    {
        Assert.Empty(MessengerHold.Actions("text", true, false, false));
        Assert.Empty(MessengerHold.Actions("video", true, false, false));
        Assert.Equal(["reply", "reaction", "edit", "delete"], MessengerHold.Actions("text", true, false, true));
        Assert.Equal(["reply", "reaction", "delete"], MessengerHold.Actions("image", true, false, true));
        Assert.Equal(["reply", "reaction", "delete"], MessengerHold.Actions("video", true, false, true));
        Assert.Equal(["reply", "reaction"], MessengerHold.Actions("file", false, false, true));
        Assert.Empty(MessengerHold.Actions("text", true, true, true));
        var called = new List<string>();
        var row = new GroupMessageRow(Guid.NewGuid(), "Аня", "текст", "сейчас", true, "text", false, called.Add);
        var box = new HoldBox { DataContext = row };
        box.Choose("reply");
        box.Choose("reaction:heart");
        box.Choose("edit");
        box.Choose("delete");
        Assert.Equal(["reply", "reaction:heart", "edit", "delete"], called);
        var photo = new GroupMessageRow(Guid.NewGuid(), "Аня", "фото", "сейчас", true, "image", false, called.Add);
        Assert.Equal("Фото", photo.Display);
        var media = new HoldBox { DataContext = photo };
        media.Choose("edit");
        Assert.Equal(4, called.Count);
        var guest = SupportChat.Submit(false, [], "Кнопка", "Не нажимается кнопка пары");
        Assert.Equal("Войдите в аккаунт, чтобы отправить сообщение и увидеть ответ.", guest.Error);
        var opened = SupportChat.Submit(true, [], "Кнопка", "Не нажимается кнопка пары");
        var followed = SupportChat.Submit(true, SupportChat.Reply(opened.Thread, "Поправили переключатель."), "уточнение", "Теперь нажимается.");
        Assert.Equal(["user", "operator", "user"], followed.Thread.Select(item => item.Author).ToArray());
    }
}
