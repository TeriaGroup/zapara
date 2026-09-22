using Zapara.Contracts.Accounts;
using Zapara.Contracts.Communities;
using Xunit;

namespace Zapara.Client.Domain.Tests;

public class ContractRulesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Chat_message_rejects_blank_allows_newlines_and_limits_the_body()
    {
        Assert.Equal("строка\nдва", new SendMessageRequest("строка\r\nдва").Body);
        Assert.Equal("строка\nдва", new ChatMessageResponse(Id, Id, Id, "Имя", "строка\r\nдва", At).Body);
        Assert.Equal(2000, new ChatMessageResponse(Id, Id, Id, "Имя", new string('я', 2000), At).Body.Length);
        Assert.Throws<ArgumentException>(() => new ChatMessageResponse(Id, Id, Id, "Имя", " \n\t ", At));
        Assert.Throws<ArgumentException>(() => new ChatMessageResponse(Id, Id, Id, "Имя", "hello\rworld", At));
        Assert.Throws<ArgumentException>(() => new SendMessageRequest(new string('я', 2001)));
    }

    [Fact]
    public void Classmate_uses_the_account_username_and_display_name_rules()
    {
        var person = new ClassmateResponse(Id, "User.Name", new string('я', 80), "member", false);
        Assert.Equal("User.Name", person.Username);
        Assert.Equal(80, person.DisplayName!.Length);
        Assert.Null(new ClassmateResponse(Id, "abc", null, "member", true).DisplayName);
        Assert.Throws<ArgumentException>(() => new ClassmateResponse(Id, "ab", null, "member", false));
        Assert.Throws<ArgumentException>(() => new ClassmateResponse(Id, "абв", null, "member", false));
        Assert.Throws<ArgumentException>(() => new ClassmateResponse(Id, "abc", "name\n", "member", false));
        Assert.Throws<ArgumentException>(() => new ClassmateResponse(Id, "abc", "", "member", false));
        Assert.Throws<ArgumentException>(() => AccountValidation.Password(new string('я', 11)));
        Assert.Equal(128, AccountValidation.Password(new string('я', 128)).Length);
    }
}
