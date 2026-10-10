using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Vograph.Desktop.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>#38: поля дат не зависят от системной локали.</summary>
public class AppCultureTests : UiTest
{
    [AvaloniaTheory]
    [InlineData("en-US")]
    [InlineData("")] // Invariant: запуск без LANG
    public void Date_fields_show_russian_dates_under_any_system_locale(string system)
    {
        var (culture, ui, defCulture, defUi) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture,
            CultureInfo.DefaultThreadCurrentCulture, CultureInfo.DefaultThreadCurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(system);
            var before = new DateTime(2026, 10, 9).ToShortDateString();
            Assert.NotEqual("09.10.2026", before); // так поле выглядело до исправления

            AppCulture.Apply(); // Program.Main делает это до старта Avalonia

            Assert.Equal("ru-RU", CultureInfo.CurrentCulture.Name);
            Assert.Equal("ru-RU", CultureInfo.DefaultThreadCurrentCulture!.Name);
            var picker = new CalendarDatePicker { SelectedDate = new DateTime(2026, 10, 9) };
            var window = new Window { Content = picker, Width = 400, Height = 200 };
            window.Show(); Pump();
            var box = picker.GetVisualDescendants().OfType<TextBox>().First();
            Assert.Equal("09.10.2026", box.Text);
            Assert.Equal("октябрь", CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(10).ToLowerInvariant());
            Assert.Equal(DayOfWeek.Monday, CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
            window.Close();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = ui;
            CultureInfo.DefaultThreadCurrentCulture = defCulture; CultureInfo.DefaultThreadCurrentUICulture = defUi;
        }
    }

    [Fact]
    public void Program_applies_the_culture_before_avalonia_starts()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "Vograph.Desktop"))) root = root.Parent;
        var program = File.ReadAllText(Path.Combine(root!.FullName, "src", "Vograph.Desktop", "Program.cs"));
        Assert.True(program.IndexOf("AppCulture.Apply()", StringComparison.Ordinal) < program.IndexOf("StartWithClassicDesktopLifetime", StringComparison.Ordinal));
        Assert.Contains("AppCulture.Apply()", program);
    }
}
