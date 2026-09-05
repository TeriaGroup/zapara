using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Xunit;

namespace Vograph.Desktop.Tests;

/// <summary>Every {DynamicResource}/{StaticResource} key used in our XAML must resolve in both theme variants.</summary>
public class ResourceKeysTests
{
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Vograph.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Vograph.slnx not found above " + AppContext.BaseDirectory);
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
}
