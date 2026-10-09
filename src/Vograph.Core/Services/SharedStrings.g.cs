// Сгенерировано scripts/design/strings.mjs из design/strings/ru.json — не править вручную.
namespace Vograph.Core.Services;

public static class SharedStrings
{
    /// <summary>Ключ каталога → строка.</summary>
    public static readonly IReadOnlyDictionary<string, string> Catalog = new Dictionary<string, string>
    {
        ["productName"] = "Расписание военмех",
        ["scheduleTitle"] = "Расписание",
        ["parityOdd"] = "нечётная",
        ["parityEven"] = "чётная",
        ["parityOddTitle"] = "Нечётная",
        ["parityEvenTitle"] = "Чётная",
        ["parityOddShort"] = "нечёт.",
        ["parityEvenShort"] = "чёт.",
        ["parityWeek"] = "{0} неделя",
        ["deadlinesTitle"] = "Ближайшие сроки",
        ["toDeadlines"] = "К срокам домашки",
        ["openMap"] = "Карта",
        ["discussInGroupChat"] = "Обсудить в чате группы",
        ["addHomework"] = "Добавить задание",
        ["countOpen"] = "открыто {0}",
        ["countDone"] = "выполнено {0}",
        ["doneFilter"] = "Выполненные",
        ["noDeadline"] = "Без срока",
        ["floorN"] = "{0} этаж",
        ["floor"] = "Этаж",
        ["swipeWeekHint"] = "Проведите влево или вправо, чтобы сменить неделю",
        ["loading"] = "Загрузка…",
        ["typeLecture"] = "Лекция",
        ["typePractice"] = "Практика",
        ["typeLab"] = "Лабораторная",
        ["typeConsult"] = "Консультация",
        ["typeCredit"] = "Зачёт",
        ["typeExam"] = "Экзамен",
        ["typeCourse"] = "Курсовая",
    };

    /// <summary>Ключ I18nService → строка каталога; I18nService берёт эти строки поверх своих.</summary>
    public static readonly IReadOnlyDictionary<string, string> Desktop = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["navSchedule"] = "Расписание",
        ["odd"] = "нечётная",
        ["even"] = "чётная",
        ["weekOdd"] = "Нечётная",
        ["weekEven"] = "Чётная",
        ["parityWeek"] = "{0} неделя",
        ["deadlinesTitle"] = "Ближайшие сроки",
        ["toDeadlines"] = "К срокам домашки",
        ["openMap"] = "Карта",
        ["discussInGroupChat"] = "Обсудить в чате группы",
        ["hwAddShort"] = "Добавить задание",
        ["countOpen"] = "открыто {0}",
        ["hwDoneCount"] = "выполнено {0}",
        ["doneFilter"] = "Выполненные",
        ["noDeadline"] = "Без срока",
        ["mapFloorN"] = "{0} этаж",
        ["mapFloor"] = "Этаж",
        ["swipeWeekHint"] = "Проведите влево или вправо, чтобы сменить неделю",
        ["loading"] = "Загрузка…",
        ["typeLek"] = "Лекция",
        ["typePr"] = "Практика",
        ["typePraktika"] = "Практика",
        ["typeLab"] = "Лабораторная",
        ["typeKons"] = "Консультация",
        ["typeZach"] = "Зачёт",
        ["typeEkz"] = "Экзамен",
        ["typeKurs"] = "Курсовая",
    };

    public static readonly IReadOnlyDictionary<string, string> LessonTypes = new Dictionary<string, string>
    {
        ["лек"] = "lecture",
        ["лекция"] = "lecture",
        ["пр"] = "practice",
        ["практика"] = "practice",
        ["лаб"] = "lab",
        ["лабораторная"] = "lab",
        ["лабораторная работа"] = "lab",
        ["конс"] = "consult",
        ["консультация"] = "consult",
        ["зач"] = "credit",
        ["зачёт"] = "credit",
        ["зачет"] = "credit",
        ["экз"] = "exam",
        ["экзамен"] = "exam",
        ["курс"] = "course",
        ["курсовая"] = "course",
    };

    public static readonly IReadOnlyDictionary<string, (string Short, string Full)> Subjects = new Dictionary<string, (string Short, string Full)>
    {
        ["ВЫСШ. МАТ."] = ("Высшая математика", "Высшая математика"),
        ["ВЫСШ. МАТЕМАТ"] = ("Высшая математика", "Высшая математика"),
        ["ВЫЧ. МАТ."] = ("Вычислительная математика", "Вычислительная математика"),
        ["ИН. ЯЗ."] = ("Иностранный язык", "Иностранный язык"),
        ["ИН.ЯЗ. В ПД"] = ("Иностранный язык в ПД", "Иностранный язык в профессиональной деятельности"),
        ["ОСН.РОС.ГОС"] = ("Основы российской государственности", "Основы российской государственности"),
        ["НАЧЕРТ. ГЕОМ."] = ("Начертательная геометрия", "Начертательная геометрия"),
        ["СОПР.МАТЕРИАЛОВ"] = ("Сопротивление материалов", "Сопротивление материалов"),
        ["ТЕОР. МЕХАНИКА"] = ("Теоретическая механика", "Теоретическая механика"),
        ["БЖД"] = ("БЖД", "Безопасность жизнедеятельности"),
        ["ИНЖ.И КОМП. ГРАФ"] = ("Инженерная и компьютерная графика", "Инженерная и компьютерная графика"),
        ["ФК И СПОРТ"] = ("Физкультура и спорт", "Физическая культура и спорт"),
        ["ЭК ПО ФК И СПОРТУ"] = ("Элективные курсы по физкультуре", "Элективные курсы по физической культуре и спорту"),
        ["ФИЗ.К-РА./АДАП."] = ("Физкультура (адаптивная)", "Физическая культура / адаптивная физическая культура"),
        ["УПР.ПРОЕКТАМИ"] = ("Управление проектами", "Управление проектами"),
        ["ЭЛ-ТЕХ И ЭЛЕКТР"] = ("Электротехника и электроника", "Электротехника и электроника"),
        ["ТВ И МАТ.СТАТИСТ"] = ("ТВ и матстатистика", "Теория вероятностей и математическая статистика"),
        ["МАТЕРИАЛОВЕД."] = ("Материаловедение", "Материаловедение"),
        ["ДЕТ.МАШ."] = ("Детали машин", "Детали машин"),
        ["СЕТИ ЭВМ И Т-КОМ"] = ("Сети ЭВМ и телекоммуникации", "Сети ЭВМ и телекоммуникации"),
        ["ОПЕРАЦ. СИСТЕМЫ"] = ("Операционные системы", "Операционные системы"),
        ["ПСИХ-Я.ПРОФ.ДЕЯТ."] = ("Психология проф. деятельности", "Психология профессиональной деятельности"),
        ["ОСН.ФИЛОСОФ."] = ("Основы философии", "Основы философии"),
    };

    public static readonly IReadOnlySet<string> Abbreviations = new HashSet<string> { "ИС", "ИТ", "НИ", "НИР", "ОВП", "ЭВМ", "ЛА", "УП", "ЭК", "ОС", "САПР", "МИРТС", "АСУ", "СУ", "МИРТУ" };
    public static readonly IReadOnlySet<string> LowerWords = new HashSet<string> { "ГР", "СХ" };
    /// <summary>Аббревиатуры, совпадающие с предлогом: заглавными только в начале названия («ПО мех. роб. сист.»).</summary>
    public static readonly IReadOnlySet<string> LeadingAbbreviations = new HashSet<string> { "ПО" };

    public static readonly IReadOnlyDictionary<string, string> Yo = new Dictionary<string, string>
    {
        ["четн"] = "чётн",
        ["нечетн"] = "нечётн",
        ["зачет"] = "зачёт",
        ["отчет"] = "отчёт",
        ["расчет"] = "расчёт",
        ["учет"] = "учёт",
        ["счетчик"] = "счётчик",
        ["еще"] = "ещё",
        ["ученый"] = "учёный",
    };
}
