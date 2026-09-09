using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>Every {DynamicResource}/{StaticResource} key used in our XAML must resolve in both theme variants.</summary>
public class ResourceKeysTests
{
    public static string RepoRoot([CallerFilePath] string? thisFile = null)
    {
        foreach (var start in new[] { thisFile is { Length: > 0 } ? Path.GetDirectoryName(thisFile) : null, Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            if (string.IsNullOrEmpty(start)) continue;
            var dir = new DirectoryInfo(start);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Vograph.slnx"))) dir = dir.Parent;
            if (dir != null) return dir.FullName;
        }
        throw new InvalidOperationException("Vograph.slnx not found above " + AppContext.BaseDirectory);
    }

    [AvaloniaFact]
    public void All_Resource_Keys_Resolve_In_Both_Variants()
    {
        var root = Path.Combine(RepoRoot(), "src", "Vograph.Desktop");
        var keys = Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\{(?:Dynamic|Static)Resource ((?:Brush|Icon|Radius|Shadow)\.[A-Za-z0-9_.]+)\}").Select(m => m.Groups[1].Value))
            .Distinct()
            .OrderBy(k => k)
            .ToList();

        Assert.NotEmpty(keys);
        var missing = new List<string>();
        foreach (var key in keys)
        {
            if (!Application.Current!.TryFindResource(key, ThemeVariant.Dark, out _)) missing.Add(key + " (Dark)");
            if (!Application.Current!.TryFindResource(key, ThemeVariant.Light, out _)) missing.Add(key + " (Light)");
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void Every_Loc_Key_Used_In_Markup_Has_Russian_Value_And_LangEn_Is_Not_Shown()
    {
        var root = Path.Combine(RepoRoot(), "src", "Vograph.Desktop");
        var storedEn = new I18nService("en");
        var ru = new I18nService("ru");
        var keys = Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\{loc:TU?\s+([A-Za-z0-9_]+)\}").Select(m => m.Groups[1].Value))
            .Distinct()
            .OrderBy(k => k)
            .ToList();

        Assert.NotEmpty(keys);
        Assert.DoesNotContain("langEn", keys);
        Assert.DoesNotContain("langRu", keys);
        Assert.DoesNotContain("language", keys);
        var missing = keys.Where(k => ru.T(k) == k).ToList();
        var notRussian = keys.Where(k => storedEn.T(k) != ru.T(k)).Select(k => k + "=" + storedEn.T(k)).ToList();
        Assert.True(missing.Count == 0, "missing Russian values: " + string.Join(", ", missing));
        Assert.True(notRussian.Count == 0, "stored en still ships English chrome: " + string.Join(", ", notRussian));
    }

    [Fact]
    public void I18nService_Does_Not_Ship_An_English_Catalog()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Vograph.Core", "Services", "I18nService.cs"));
        Assert.DoesNotContain("[\"en\"] = new", source);
    }
}
