using System.Text.RegularExpressions;

namespace Vograph.Core.Services;

/// <summary>
/// Нормализация строк расписания для показа (#12). Тот же алгоритм — в web/src/schedule-text.ts;
/// оба проверяются примерами из design/strings/schedule-cases.json. Ключи (SubjectRaw) не меняются — только показ.
/// </summary>
public static partial class ScheduleText
{
    private static readonly HashSet<string> TypeTokens = ["лек", "пр", "лаб", "конс", "зач", "экз", "курс"];

    /// <summary>Ключ словаря без пробелов и точек: «ОСН РОС ГОС» и «ОСН.РОС.ГОС» — один предмет.</summary>
    private static string DictionaryKey(string value) => new(value.ToUpperInvariant().Where(ch => ch != '.' && !char.IsWhiteSpace(ch)).ToArray());

    private static readonly Dictionary<string, (string Short, string Full)> Dictionary =
        SharedStrings.Subjects.ToDictionary(pair => DictionaryKey(pair.Key), pair => pair.Value);

    private static string Collapse(string? value) => Spaces().Replace(value ?? "", " ").Trim();

    private static bool FeedLike(string value)
    {
        var letters = value.Where(IsLetter).ToArray();
        if (letters.Length == 0) return false;
        return letters.Count(char.IsUpper) / (double)letters.Length >= 0.6;
    }

    private static bool IsLetter(char ch) => ch is >= 'А' and <= 'я' or 'Ё' or 'ё' or >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    /// <summary>«пр УПР.ПРОЕКТАМИ» → «УПР.ПРОЕКТАМИ»: префикс типа показывает чип.</summary>
    public static string StripTypePrefix(string? raw, string? typeRaw = null)
    {
        var value = Collapse(raw);
        var space = value.IndexOf(' ');
        if (space <= 0) return value;
        var head = value[..space].ToLowerInvariant();
        return head == Collapse(typeRaw).ToLowerInvariant() || TypeTokens.Contains(head) ? value[(space + 1)..] : value;
    }

    private static string WithYo(string word)
    {
        foreach (var (plain, dotted) in SharedStrings.Yo)
            if (word.StartsWith(plain, StringComparison.Ordinal)) return dotted + word[plain.Length..];
        return word;
    }

    private static bool HasVowel(string upper) => upper.IndexOfAny("АЕЁИОУЫЭЮЯAEIOUY".ToCharArray()) >= 0;

    private static string Readable(string value)
    {
        var spaced = Collapse(AfterPunct().Replace(value, "$1 "));
        var result = Word().Replace(spaced, match =>
        {
            var word = match.Value;
            var upper = word.ToUpperInvariant();
            var end = match.Index + word.Length;
            var dotted = end < spaced.Length && spaced[end] == '.';
            if (word.Length == 1) return dotted ? upper : word.ToLowerInvariant();
            if (word.Any(char.IsLower) && word.Skip(1).Any(char.IsUpper)) return word; // «МиР»: смешанный регистр — как в фиде
            if (SharedStrings.Abbreviations.Contains(upper) || match.Index == 0 && SharedStrings.LeadingAbbreviations.Contains(upper)) return upper;
            if (SharedStrings.LowerWords.Contains(upper)) return WithYo(word.ToLowerInvariant());
            if (!HasVowel(upper)) return upper;
            return WithYo(word.ToLowerInvariant());
        });
        return result.Length == 0 ? result : char.ToUpperInvariant(result[0]) + result[1..];
    }

    /// <summary>Короткое имя для строки пары и полное — для подробностей/подсказки.</summary>
    public static (string Short, string Full) Subject(string? raw, string? typeRaw = null)
    {
        var value = StripTypePrefix(raw, typeRaw);
        if (value.Length == 0 || !FeedLike(value)) return (value, value);
        if (Dictionary.TryGetValue(DictionaryKey(value), out var known)) return known;
        var shortName = Readable(value);
        return (shortName, shortName);
    }

    public static string SubjectShort(string? raw, string? typeRaw = null) => Subject(raw, typeRaw).Short;

    /// <summary>«268*(фесто);» → «268 (Фесто)», «ВЦ 281; ВЦ 283;» → «ВЦ 281, ВЦ 283».</summary>
    public static string Room(string? raw) => string.Join(", ", (raw ?? "").Split(';')
        .Select(part => Collapse(part.Replace("*", "")))
        .Select(part => OpenParen().Replace(BeforeParen().Replace(part, "$1 ("), m => "(" + m.Groups[1].Value.ToUpperInvariant()))
        .Where(part => part.Length > 0));

    private static string OneTeacher(string raw)
    {
        var tokens = Collapse(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count == 0) return "";
        var at = tokens.Count;
        while (at > 1 && Initials().IsMatch(tokens[at - 1])) at--;
        var tail = Initial().Matches(string.Concat(tokens.Skip(at))).Select(m => m.Value).ToList();
        var surname = tokens.Take(at).ToList();
        if (tail.Count == 1 && surname.Count >= 2)
        {
            tail.Insert(0, char.ToUpperInvariant(surname[^1][0]) + ".");
            surname.RemoveAt(surname.Count - 1);
        }
        return tail.Count > 0 ? $"{string.Join(' ', surname)} {string.Join(' ', tail)}" : string.Join(' ', surname);
    }

    /// <summary>«Кондратьев Сергей А.» → «Кондратьев С. А.»; несколько преподавателей — через запятую.</summary>
    public static string Teacher(string? raw) => string.Join(", ", (raw ?? "").Split(';').Select(OneTeacher).Where(x => x.Length > 0));

    /// <summary>Строка метаданных без пустых полей и висящих «·».</summary>
    public static string MetaLine(params string?[] parts) =>
        string.Join(" · ", parts.Select(Collapse).Where(part => part.Length > 0 && part is not "—" and not "·"));

    /// <summary>«лек» → "lecture" или "" для незнакомого типа.</summary>
    public static string KindOf(string? typeRaw) => SharedStrings.LessonTypes.TryGetValue(Collapse(typeRaw).ToLowerInvariant(), out var kind) ? kind : "";

    /// <summary>Подпись чипа типа пары, с заглавной: «Практика».</summary>
    public static string TypeLabel(string? typeRaw)
    {
        var key = KindOf(typeRaw) switch
        {
            "lecture" => "typeLecture", "practice" => "typePractice", "lab" => "typeLab", "consult" => "typeConsult",
            "credit" => "typeCredit", "exam" => "typeExam", "course" => "typeCourse", _ => null
        };
        if (key is not null) return SharedStrings.Catalog[key];
        var value = Collapse(typeRaw);
        return value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static readonly string[] ShortMonths = ["янв.", "февр.", "мар.", "апр.", "мая", "июн.", "июл.", "авг.", "сент.", "окт.", "нояб.", "дек."];

    /// <summary>«9 окт.»; год — только если он не текущий.</summary>
    public static string Day(DateTime date, DateTime now)
    {
        var text = $"{date.Day} {ShortMonths[date.Month - 1]}";
        return date.Year == now.Year ? text : $"{text} {date.Year}";
    }

    /// <summary>«9 окт., 18:31».</summary>
    public static string DateTimeText(DateTime value, DateTime now) => $"{Day(value, now)}, {value:HH\\:mm}";

    /// <summary>«5–11 окт.», «28 сент.–4 окт.» — короткое тире без пробелов.</summary>
    public static string Range(DateTime from, DateTime to, DateTime now) =>
        from.Month == to.Month && from.Year == to.Year ? $"{from.Day}–{Day(to, now)}" : $"{Day(from, now)}–{Day(to, now)}";

    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"([.,])(?=[А-ЯЁа-яёA-Za-z])")] private static partial Regex AfterPunct();
    [GeneratedRegex(@"[А-ЯЁа-яёA-Za-z]+")] private static partial Regex Word();
    [GeneratedRegex(@"(\S)\(")] private static partial Regex BeforeParen();
    [GeneratedRegex(@"\((\S)")] private static partial Regex OpenParen();
    [GeneratedRegex(@"^(?:[А-ЯЁA-Z]\.)+$")] private static partial Regex Initials();
    [GeneratedRegex(@"[А-ЯЁA-Z]\.")] private static partial Regex Initial();
}
