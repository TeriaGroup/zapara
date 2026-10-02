using System.Text.RegularExpressions;

namespace Vograph.Desktop.Features.Preferences;

public sealed record SupportDiagnostics(string Version, int CachedGroups, int TotalGroups,
    int CachedMaps, int TotalMaps, int PendingChanges)
{
    public string Format()
    {
        var safeVersion = Regex.IsMatch(Version, @"^v?[0-9A-Za-z.+-]{1,32}$", RegexOptions.CultureInvariant)
            ? Version : "неизвестно";
        return $"Расписание военмех · диагностика без личных данных\n" +
            $"Версия: {safeVersion}\nПлатформа: Windows\n" +
            $"Копии расписания: {Math.Max(0, CachedGroups)} из {Math.Max(0, TotalGroups)} групп\n" +
            $"Офлайн-планы: {Math.Max(0, CachedMaps)} из {Math.Max(0, TotalMaps)}\n" +
            $"Изменений в очереди: {Math.Max(0, PendingChanges)}";
    }
}
