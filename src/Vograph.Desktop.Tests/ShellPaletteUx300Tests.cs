using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class ShellPaletteUx300Tests : UiTest
{
    [Fact]
    public void Search_and_enter_open_the_named_section_and_close_palette()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);

        shell.OpenCommandPaletteCommand.Execute(null);
        shell.CommandQuery = "карты";
        var map = Assert.Single(shell.PaletteSections);
        shell.OpenPaletteSectionCommand.Execute(map);

        Assert.Equal(SectionKey.Maps, shell.CurrentKey);
        Assert.False(shell.ShowCommandPalette);
    }
}
