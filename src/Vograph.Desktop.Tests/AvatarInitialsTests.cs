using Vograph.Desktop.Features.Chat;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class AvatarInitialsTests
{
    [Theory]
    [InlineData("Анна Петрова", "АП")]
    [InlineData("  Иван  ", "ИВ")]
    [InlineData("", "?")]
    [InlineData("😊 Alex", "AL")]
    [InlineData("Максим Бова", "МБ")]
    [InlineData("dufa14", "DU")]
    [InlineData("Анна Мария Иванова", "АИ")]
    [InlineData("😀 Анна Иванова", "АИ")]
    public void Uses_first_two_words_without_exposing_identifiers(string name, string expected)
        => Assert.Equal(expected, AvatarInitials.FromName(name));
}
