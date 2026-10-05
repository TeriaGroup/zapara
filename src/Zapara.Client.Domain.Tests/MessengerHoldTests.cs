using Xunit;
using Zapara.Client.Domain;

namespace Zapara.Client.Domain.Tests;

public sealed class MessengerHoldTests
{
    [Fact]
    public void Hold_opens_actions_and_a_tap_does_not()
    {
        Assert.Empty(MessengerHold.Actions("text", true, false, false));
        Assert.Empty(MessengerHold.Actions("image", true, false, false));
        Assert.Equal(["reply", "reaction", "edit", "delete"], MessengerHold.Actions("text", true, false, true));
        Assert.Equal(["reply", "reaction", "delete"], MessengerHold.Actions("image", true, false, true));
        Assert.Equal(["reply", "reaction", "delete"], MessengerHold.Actions("video", true, false, true));
        Assert.Equal(["reply", "reaction", "delete"], MessengerHold.Actions("file", true, false, true));
        Assert.Equal(["reply", "reaction"], MessengerHold.Actions("image", false, false, true));
        Assert.Empty(MessengerHold.Actions("text", true, true, true));
        var called = new List<string>();
        MessengerHold.Perform("reply", () => called.Add("reply"), () => called.Add("reaction"), () => called.Add("edit"), () => called.Add("delete"));
        MessengerHold.Perform("reaction", () => called.Add("reply"), () => called.Add("reaction"), () => called.Add("edit"), () => called.Add("delete"));
        MessengerHold.Perform("edit", () => called.Add("reply"), () => called.Add("reaction"), () => called.Add("edit"), () => called.Add("delete"));
        MessengerHold.Perform("delete", () => called.Add("reply"), () => called.Add("reaction"), () => called.Add("edit"), () => called.Add("delete"));
        Assert.Equal(["reply", "reaction", "edit", "delete"], called);
    }
}
