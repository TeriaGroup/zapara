namespace Zapara.Server.Social;

public static class SocialStickers
{
    private static readonly Dictionary<string, string> Titles = new(StringComparer.Ordinal)
    {
        ["hi"] = "Привет",
        ["ok"] = "Хорошо",
        ["love"] = "Люблю",
        ["laugh"] = "Смешно",
        ["think"] = "Думаю",
        ["sleep"] = "Сплю",
        ["coffee"] = "Кофе",
        ["book"] = "Учёба",
        ["late"] = "Опаздываю",
        ["done"] = "Готово",
        ["fire"] = "Огонь",
        ["sad"] = "Грустно",
        ["party"] = "Ура",
        ["question"] = "Вопрос",
        ["thanks"] = "Спасибо",
        ["cool"] = "Круто"
    };

    public static bool Known(string? id) => id is not null && Titles.ContainsKey(id);

    public static string Title(string? id) => id is not null && Titles.TryGetValue(id, out var title) ? title : "Стикер";
}
