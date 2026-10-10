using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Vograph.Desktop.Features.Week;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>R2-13: пара без аудитории («Доп. подготовка») — без пустого серого чипа «—».</summary>
public class RoomChipTests : UiTest
{
    private static readonly DateTime Mon = new(2026, 9, 14, 8, 0, 0);

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" — ", false)]
    [InlineData("267 (К.кл)", true)]
    public void Room_chip_only_for_a_known_room(string? room, bool visible) => Assert.Equal(visible, RoomChip.HasRoom(room));

    [AvaloniaFact]
    public async Task Week_hides_the_dash_chip_for_a_lesson_without_a_room()
    {
        using var db = TestDb.Create();
        db.Services.Theme = ThemeService.ForApplication(Application.Current!, db.Services.Prefs);
        db.Services.Db.InsertLesson(new() { GroupId = TestDb.MyGroupId, DayOfWeek = 1, Parity = 0, Index = 9,
            TimeStart = "20:00", TimeEnd = "21:30", SubjectRaw = "пр ДОП. ПОДГОТОВКА", SubjectNormalized = "доп. подготовка", RoomRaw = "", ClassroomRaw = "" });
        var shell = new ShellViewModel(db.Services) { Clock = () => Mon };
        shell.Register(SectionKey.Week, () => new WeekViewModel(db.Services, shell, () => Mon));
        await shell.StartAsync(allowNetwork: false);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        window.Show();
        SetTheme(ThemeVariant.Light, db.Services.Theme);
        shell.NavigateTo(SectionKey.Week);
        Pump(); await Task.Delay(300); Pump();

        var chipTexts = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("chip") && b.IsEffectivelyVisible)
            .SelectMany(b => b.GetVisualDescendants().OfType<TextBlock>()).Select(t => t.Text?.Trim() ?? "").ToArray();
        Assert.True(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && (t.Text ?? "").Contains("ПОДГОТОВКА", StringComparison.OrdinalIgnoreCase)),
            "пара без аудитории должна быть в неделе");
        Assert.DoesNotContain("—", chipTexts);
        Assert.Contains(chipTexts, t => t.Length > 0 && char.IsDigit(t[0])); // у остальных пар чип аудитории на месте
        window.Close();
    }
}
