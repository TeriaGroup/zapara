namespace Vograph.Core.Services;

/// <summary>#39: корневая папка данных клиента. Путь всегда абсолютный: если система не дала
/// LocalApplicationData (Linux без HOME/XDG_DATA_HOME), берём XDG_DATA_HOME, затем ~/.local/share,
/// затем папку рядом с программой. Иначе получался относительный путь «Vograph», который в папке сборки
/// совпадает с исполняемым файлом «Vograph». Имя папки не меняется, чтобы у пользователей остались данные.</summary>
public static class VographDataRoot
{
    public const string FolderName = "Vograph";

    /// <summary>Папка данных по умолчанию (без учёта VOGRAPH_DATA_DIR).</summary>
    public static string DefaultDir => Resolve(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
        Environment.GetEnvironmentVariable("HOME"),
        AppContext.BaseDirectory);

    public static string Resolve(string? localAppData, string? xdgDataHome, string? home, string baseDirectory)
    {
        if (IsRooted(localAppData)) return Path.Combine(localAppData!, FolderName);
        if (IsRooted(xdgDataHome)) return Path.Combine(xdgDataHome!, FolderName);
        if (IsRooted(home)) return Path.Combine(home!, ".local", "share", FolderName);
        // Последний вариант — рядом с программой, но не под именем исполняемого файла.
        return Path.Combine(Path.GetFullPath(baseDirectory), "data");
    }

    private static bool IsRooted(string? path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);
}
