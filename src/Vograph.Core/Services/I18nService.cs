namespace Vograph.Core.Services;

public class I18nService
{
    private string _lang = "ru";
    public string Language => _lang;
    public event Action? LanguageChanged;

    private readonly Dictionary<string, Dictionary<string, string>> _dict = new()
    {
        ["ru"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Header
            ["appTitle"] = "Военмех - расписание и карты",
            ["headerHint"] = "Группа {0} · {1} неделя",
            ["headerSub"] = "Расписание Военмеха · завтра по умолчанию",
            ["odd"] = "нечетная",
            ["even"] = "четная",
            ["oddShort"] = "нечет",
            ["evenShort"] = "чет",
            ["oddBadge"] = "НЕЧЕТНАЯ",
            ["evenBadge"] = "ЧЕТНАЯ",
            // Days full
            ["mon"] = "Понедельник", ["tue"] = "Вторник", ["wed"] = "Среда", ["thu"] = "Четверг", ["fri"] = "Пятница", ["sat"] = "Суббота", ["sun"] = "Воскресенье",
            ["monShort"] = "Пн", ["tueShort"] = "Вт", ["wedShort"] = "Ср", ["thuShort"] = "Чт", ["friShort"] = "Пт", ["satShort"] = "Сб", ["sunShort"] = "Вс",
            // Tabs
            ["yesterday"] = "Вчера", ["today"] = "Сегодня", ["tomorrow"] = "Завтра", ["week"] = "Неделя",
            ["noLessons"] = "Нет занятий",
            ["noLessonsShort"] = "Пар нет",
            // Table
            ["colNo"] = "№", ["colTime"] = "Время", ["colSubject"] = "Предмет", ["colTeacher"] = "Преподаватель", ["colRoom"] = "Ауд./Корп.", ["colType"] = "Тип",
            // Settings
            ["settings"] = "НАСТРОЙКИ",
            ["myGroup"] = "Моя группа",
            ["invertParity"] = "Инвертировать четность недели",
            ["invertHint"] = "Если вуз сдвинул неделю, включите инверсию.",
            ["friends"] = "ДРУЗЬЯ (до 5)",
            ["friendsHint"] = "Цвет — один из 5, иконка внутри ячейки.",
            ["strictness"] = "Строгость пересечений",
            ["strict0"] = "0 — любое время", ["strict40"] = "40 — корпус", ["strict100"] = "100 — аудитория",
            ["notifications"] = "УВЕДОМЛЕНИЯ",
            ["notifHint"] = "Текст использует переименованные названия и помечает горящее ДЗ.",
            ["time1"] = "Время 1", ["time2"] = "Время 2", ["saveTimes"] = "Сохранить времена",
            ["sync"] = "СИНХРОНИЗАЦИЯ", ["export"] = "Экспорт", ["import"] = "Импорт", ["refresh"] = "Обновить расписание", ["updated"] = "Обновлено: {0}", ["lastAutoCheck"] = "Автопроверка: {0}",
            ["language"] = "Язык", ["langRu"] = "Русский", ["langEn"] = "English",
            ["auto"] = "авто", ["invert"] = "инвертировать",
            ["parity"] = "Четность",
            ["group"] = "ГРУППА",
            ["onlyCurrentWeek"] = "Только текущая неделя",
            ["weekLabel"] = "Неделя:",
            ["weekOdd"] = "Нечетная", ["weekEven"] = "Четная",
            ["emptyWeek"] = "Нет занятий",
            // Dialogs
            ["renameTitle"] = "ПЕРЕИМЕНОВАНИЕ", ["original"] = "Оригинал: {0}", ["newName"] = "Новое название", ["footnote"] = "Примечание (сноска)", ["scope"] = "Область", ["global"] = "Глобально (все вхождения предмета)", ["weekdayOnly"] = "Только в этот день", ["preview"] = "Предпросмотр: {0}", ["reset"] = "Сбросить", ["cancel"] = "Отмена", ["save"] = "Сохранить",
            ["hwTitle"] = "ДОМАШНЕЕ ЗАДАНИЕ", ["hwSubject"] = "Предмет: {0}", ["hwText"] = "Текст задания", ["hwN"] = "Через сколько занятий этого предмета сдать (1..10)", ["hwDue"] = "Срок: {0}", ["hwNoDate"] = "Срок: — (нет занятий)", ["hwStatusHint"] = "Статус: far (скрыт) → approaching (серый) → burning (яркий)",
            // Notifications
            ["notifNoLessons"] = "Сегодня пар нет",
            ["notifBurning"] = "[ДЗ!]",
            // Intersections tooltip
            ["room"] = "ауд.",
            // Parity note
            ["semesterOddNote"] = "Обратите внимание! Семестр начинается с нечетной недели!",
            ["stale"] = " · устаревшие данные",
            ["ready"] = "Готово",
            ["loading"] = "Загрузка...",
            ["updatedOk"] = "Готово — расписание обновлено",
            ["exportOk"] = "Экспорт сохранен {0} + QR {1}",
            ["importOk"] = "Импорт: {0} переименований, {1} ДЗ, {2} друзей",
            // Map
            ["mapTitle"] = "КАРТА",
            ["mapNext"] = "Куда идти — следующая пара",
            ["mapNoNext"] = "Нет предстоящих занятий",
            ["mapBuilding"] = "Корпус",
            ["mapFloor"] = "Этаж",
            ["mapRoom"] = "Ауд.",
            ["mapOpen"] = "Открыть полностью",
            ["mapOpenSite"] = "Открыть на сайте",
            ["mapDownload"] = "Скачать карты",
            ["mapRemote"] = "Дистанционно — карта не требуется",
            ["mapNoRoom"] = "Аудитория не указана",
            ["mapAll"] = "Все карты",
            ["mapHint"] = "Карты с voenmeh.ru/openmap — ГК 1-4, УЛК 1-5; кэш локально",
            ["mapWhere"] = "Куда: {0}",
            ["mapWhen"] = "Когда: {0}",
            ["mapCacheDir"] = "Кэш: {0}",
            ["mapDownloading"] = "Загрузка карт...",
            ["blockWidth"] = "ШИРИНА БЛОКОВ",
            ["blockWidthHint"] = "Тяните разделитель между расписанием и картой или двигайте ползунок. Все блоки подстраиваются.",
            ["blockWidthReset"] = "Сбросить 300",
            ["blockWidthWide"] = "На всю ширину",
            ["summaryTitle"] = "СВОДКА",
            ["summaryBoth"] = "Обе недели (2 недели)",
            ["summaryHint"] = "Сводка по всем парам группы: типы, предметы, преподаватели, аудитории",
            ["teachers"] = "Преподаватели",
            ["teachersHint"] = "Список всех преподавателей по предметам студента — где и когда ведут",
            ["nextPair"] = "След. пара",
            ["nextPairHint"] = "Дата следующей пары по этому предмету",
            ["weekNum"] = "неделя {0}",
            // Self-update (GitHub releases)
            ["autoUpdate"] = "Автообновление с GitHub",
            ["updTitle"] = "Обновление",
            ["updDownloading"] = "Скачивание обновления {0}...",
            ["updReady"] = "Обновление {0} скачано. Перезапустить сейчас для установки?",
            ["updNone"] = "У вас последняя версия {0}",
            ["updFail"] = "Не удалось проверить обновление",
            // Weekday names for API parity (already above)
            // ---- Desktop v2 (Avalonia) ----
            ["navSchedule"] = "Расписание", ["navWeek"] = "Неделя", ["navSummary"] = "Сводка", ["navTools"] = "Инструменты",
            ["navTeachers"] = "Преподаватели", ["navMaps"] = "Карты", ["navFriends"] = "Друзья", ["navHomework"] = "Домашка", ["navSettings"] = "Настройки",
            ["goToday"] = "К сегодня", ["prevDay"] = "Предыдущий день", ["nextDay"] = "Следующий день",
            ["lessons1"] = "{0} пара", ["lessons2"] = "{0} пары", ["lessons5"] = "{0} пар",
            ["weekOf"] = "неделя {0}", ["parityWeek"] = "{0} неделя", ["nextShort"] = "след. {0}",
            ["noLessonsDay"] = "Пар нет", ["noLessonsSunday"] = "Воскресенье — пар нет", ["nextLessonHint"] = "следующая пара — {0}, {1}",
            ["typeLek"] = "лекция", ["typePr"] = "практика", ["typeLab"] = "лабораторная", ["typeKons"] = "консультация",
            ["typeZach"] = "зачёт", ["typeEkz"] = "экзамен", ["typeKurs"] = "курсовая", ["typePraktika"] = "практика",
            ["remote"] = "дистанционно", ["originalLabel"] = "оригинал: {0}",
            ["hwLabel"] = "Домашка", ["hwBurningTomorrow"] = "горит завтра", ["hwBurningToday"] = "горит сегодня", ["hwOverdue"] = "просрочено {0}",
            ["hwDone"] = "сдано", ["hwDueOn"] = "срок {0}", ["hwInLessons1"] = "через {0} пару", ["hwInLessons2"] = "через {0} пары", ["hwInLessons5"] = "через {0} пар",
            ["hwMarkDone"] = "Сдано", ["hwUndo"] = "Вернуть", ["hwEdit"] = "Изменить", ["hwDelete"] = "Удалить", ["hwAdd"] = "Добавить домашку",
            ["hwDeleteConfirm"] = "Удалить домашку «{0}»?", ["hwEditTitle"] = "ИЗМЕНИТЬ ДОМАШКУ",
            ["renameTip"] = "Переименовать", ["mapTip"] = "Показать на карте",
            ["placeholderTitle"] = "Раздел в разработке", ["placeholderHint"] = "Появится на следующем этапе", ["loadingTitle"] = "Загружаю расписание…",
            ["themeToggleTip"] = "Переключить тему", ["sidebarToggleTip"] = "Свернуть панель",
            ["groupPickTitle"] = "Выбор группы", ["search"] = "Поиск", ["groupSearchHint"] = "Номер группы…", ["select"] = "Выбрать",
            ["confirm"] = "Подтвердить", ["delete"] = "Удалить", ["updatedChip"] = "обновлено {0}", ["errorTitle"] = "Ошибка",
            ["bootstrapError"] = "Не удалось загрузить расписание", ["bootstrapHint"] = "Проверьте сеть и повторите", ["retry"] = "Повторить",
            ["friendAbsent"] = "нет рядом", ["inter100"] = "в той же аудитории", ["inter75"] = "на том же этаже", ["inter50"] = "в том же корпусе", ["inter25"] = "в вузе",
            ["savedOk"] = "Сохранено", ["noGroup"] = "Группа не выбрана", ["noGroupHint"] = "Нажмите на карточку группы слева",
            ["winMinimize"] = "Свернуть", ["winMaximize"] = "Развернуть", ["winClose"] = "Закрыть",
            ["refreshOk"] = "Расписание обновлено", ["refreshNone"] = "Расписание актуально",
            ["refreshFail"] = "Не удалось обновить расписание: {0}", ["refreshTip"] = "Обновить расписание (F5)",
            ["weekCurrentSuffix"] = " · текущая", ["weekOpenDayTip"] = "Открыть этот день в расписании",
            ["summaryTotal"] = "Всего пар", ["summaryByDay"] = "По дням", ["summaryByType"] = "По типам",
            ["summarySubjects"] = "Предметы", ["summaryTeachers"] = "Преподаватели", ["summaryRooms"] = "Аудитории", ["summaryBothShort"] = "Обе",
            ["teachersSearchHint"] = "Фамилия, кафедра или предмет", ["teachersOnlyMine"] = "Только мои", ["teachersCount"] = "{0} из {1}",
            ["teachersPick"] = "Выберите преподавателя", ["teachersPickHint"] = "Список слева: поиск по фамилии или предмету",
            ["teachersLoading"] = "Загружаем справочник…", ["teachersLoadFail"] = "Справочник преподавателей недоступен: {0}",
            ["teachersNoSource"] = "нет ни кэша, ни встроенной копии, ни сети", ["teachersMine"] = "моя",
            ["teachersTeachesMine"] = "Ведёт у вашей группы", ["teachersNotMine"] = "Не ведёт у вашей группы",
            ["mapNextLesson"] = "Следующая пара", ["mapLessonPrefix"] = "Пара: {0}", ["mapPickPlan"] = "Выберите план", ["mapFloorN"] = "{0} этаж",
            ["mapInMinutes"] = "через {0} мин", ["mapInHours"] = "через {0} ч", ["mapInDays"] = "через {0} дн.", ["mapNow"] = "идёт сейчас",
            ["mapToNext"] = "К следующей паре", ["mapVc"] = "ВЦ — показан план ГК",
            ["mapDownloadAll"] = "Скачать свежие планы", ["mapOpenFolder"] = "Открыть папку карт", ["mapVerify"] = "Проверить офлайн-кэш",
            ["mapCacheStatus"] = "{0} из {1} планов офлайн", ["mapDownloaded"] = "Планы скачаны: {0} из {1}",
            ["mapFullscreen"] = "На весь экран", ["mapExitFullscreen"] = "Закрыть (Esc)", ["mapFit"] = "Вписать",
            ["mapZoomIn"] = "Крупнее", ["mapZoomOut"] = "Мельче", ["mapReset"] = "100%", ["mapMore"] = "Ещё",
            ["mapNoImage"] = "План не загружен: нет сети и встроенной копии",
            ["mapRemoteHint"] = "Нажмите ◉ у пары или выберите корпус и этаж",
        },
        ["en"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["appTitle"] = "Voenmeh - schedule & maps",
            ["headerHint"] = "Group {0} · {1} week",
            ["headerSub"] = "Voenmeh timetable · tomorrow by default",
            ["odd"] = "odd",
            ["even"] = "even",
            ["oddShort"] = "odd",
            ["evenShort"] = "even",
            ["oddBadge"] = "ODD",
            ["evenBadge"] = "EVEN",
            ["mon"] = "Monday", ["tue"] = "Tuesday", ["wed"] = "Wednesday", ["thu"] = "Thursday", ["fri"] = "Friday", ["sat"] = "Saturday", ["sun"] = "Sunday",
            ["monShort"] = "Mon", ["tueShort"] = "Tue", ["wedShort"] = "Wed", ["thuShort"] = "Thu", ["friShort"] = "Fri", ["satShort"] = "Sat", ["sunShort"] = "Sun",
            ["yesterday"] = "Yesterday", ["today"] = "Today", ["tomorrow"] = "Tomorrow", ["week"] = "Week",
            ["noLessons"] = "No lessons",
            ["noLessonsShort"] = "No lessons",
            ["colNo"] = "No.", ["colTime"] = "Time", ["colSubject"] = "Subject", ["colTeacher"] = "Teacher", ["colRoom"] = "Room/Building", ["colType"] = "Type",
            ["settings"] = "SETTINGS",
            ["myGroup"] = "My group",
            ["invertParity"] = "Invert week parity",
            ["invertHint"] = "If university shifted the week, enable inversion.",
            ["friends"] = "FRIENDS (up to 5)",
            ["friendsHint"] = "Color — one of 5, icon inside cell.",
            ["strictness"] = "Intersection strictness",
            ["strict0"] = "0 — any time", ["strict40"] = "40 — building", ["strict100"] = "100 — room",
            ["notifications"] = "NOTIFICATIONS",
            ["notifHint"] = "Text uses renamed titles and marks burning homework.",
            ["time1"] = "Time 1", ["time2"] = "Time 2", ["saveTimes"] = "Save times",
            ["sync"] = "SYNC", ["export"] = "Export", ["import"] = "Import", ["refresh"] = "Refresh schedule", ["updated"] = "Updated: {0}", ["lastAutoCheck"] = "Auto-check: {0}",
            ["language"] = "Language", ["langRu"] = "Русский", ["langEn"] = "English",
            ["auto"] = "auto", ["invert"] = "invert",
            ["parity"] = "Parity",
            ["group"] = "GROUP",
            ["onlyCurrentWeek"] = "Current week only",
            ["weekLabel"] = "Week:",
            ["weekOdd"] = "Odd", ["weekEven"] = "Even",
            ["emptyWeek"] = "No lessons",
            ["renameTitle"] = "RENAME", ["original"] = "Original: {0}", ["newName"] = "New name", ["footnote"] = "Footnote", ["scope"] = "Scope", ["global"] = "Global (all occurrences)", ["weekdayOnly"] = "Only this weekday", ["preview"] = "Preview: {0}", ["reset"] = "Reset", ["cancel"] = "Cancel", ["save"] = "Save",
            ["hwTitle"] = "HOMEWORK", ["hwSubject"] = "Subject: {0}", ["hwText"] = "Task text", ["hwN"] = "In how many occurrences (1..10)", ["hwDue"] = "Due: {0}", ["hwNoDate"] = "Due: — (no lessons)", ["hwStatusHint"] = "Status: far (hidden) → approaching (gray) → burning (bright)",
            ["notifNoLessons"] = "No lessons today",
            ["notifBurning"] = "[HW!]",
            ["room"] = "room",
            ["semesterOddNote"] = "Note: semester starts with odd week!",
            ["stale"] = " · stale data",
            ["ready"] = "Ready",
            ["loading"] = "Loading...",
            ["updatedOk"] = "Ready — schedule updated",
            ["exportOk"] = "Export saved {0} + QR {1}",
            ["importOk"] = "Import: {0} renames, {1} HW, {2} friends",
            ["mapTitle"] = "MAP",
            ["mapNext"] = "Where to go — next lesson",
            ["mapNoNext"] = "No upcoming lessons",
            ["mapBuilding"] = "Building",
            ["mapFloor"] = "Floor",
            ["mapRoom"] = "Room",
            ["mapOpen"] = "Open full",
            ["mapOpenSite"] = "Open on site",
            ["mapDownload"] = "Download maps",
            ["mapRemote"] = "Remote — no map needed",
            ["mapNoRoom"] = "Room not specified",
            ["mapAll"] = "All maps",
            ["mapHint"] = "Maps from voenmeh.ru/openmap — GK 1-4, ULK 1-5; cached locally",
            ["mapWhere"] = "Where: {0}",
            ["mapWhen"] = "When: {0}",
            ["mapCacheDir"] = "Cache: {0}",
            ["mapDownloading"] = "Downloading maps...",
            ["blockWidth"] = "BLOCK WIDTH",
            ["blockWidthHint"] = "Drag splitter between schedule and map or move slider. All blocks adjust.",
            ["blockWidthReset"] = "Reset 300",
            ["blockWidthWide"] = "Full width",
            ["summaryTitle"] = "SUMMARY",
            ["summaryBoth"] = "Both weeks (2 weeks)",
            ["summaryHint"] = "Summary for all lessons: types, subjects, teachers, rooms",
            ["teachers"] = "Teachers",
            ["teachersHint"] = "List of all teachers by subjects for the student — where and when",
            ["nextPair"] = "Next",
            ["nextPairHint"] = "Date of next occurrence for this subject",
            ["weekNum"] = "week {0}",
            ["autoUpdate"] = "Auto-update from GitHub",
            ["updTitle"] = "Update",
            ["updDownloading"] = "Downloading update {0}...",
            ["updReady"] = "Update {0} downloaded. Restart now to install?",
            ["updNone"] = "You have the latest version {0}",
            ["updFail"] = "Failed to check for updates",
            // ---- Desktop v2 (Avalonia) ----
            ["navSchedule"] = "Schedule", ["navWeek"] = "Week", ["navSummary"] = "Summary", ["navTools"] = "Tools",
            ["navTeachers"] = "Teachers", ["navMaps"] = "Maps", ["navFriends"] = "Friends", ["navHomework"] = "Homework", ["navSettings"] = "Settings",
            ["goToday"] = "Today", ["prevDay"] = "Previous day", ["nextDay"] = "Next day",
            ["lessons1"] = "{0} lesson", ["lessons2"] = "{0} lessons", ["lessons5"] = "{0} lessons",
            ["weekOf"] = "week {0}", ["parityWeek"] = "{0} week", ["nextShort"] = "next {0}",
            ["noLessonsDay"] = "No lessons", ["noLessonsSunday"] = "Sunday — no lessons", ["nextLessonHint"] = "next lesson — {0}, {1}",
            ["typeLek"] = "lecture", ["typePr"] = "practice", ["typeLab"] = "lab", ["typeKons"] = "consultation",
            ["typeZach"] = "credit", ["typeEkz"] = "exam", ["typeKurs"] = "course work", ["typePraktika"] = "internship",
            ["remote"] = "online", ["originalLabel"] = "original: {0}",
            ["hwLabel"] = "Homework", ["hwBurningTomorrow"] = "due tomorrow", ["hwBurningToday"] = "due today", ["hwOverdue"] = "overdue {0}",
            ["hwDone"] = "done", ["hwDueOn"] = "due {0}", ["hwInLessons1"] = "in {0} lesson", ["hwInLessons2"] = "in {0} lessons", ["hwInLessons5"] = "in {0} lessons",
            ["hwMarkDone"] = "Done", ["hwUndo"] = "Reopen", ["hwEdit"] = "Edit", ["hwDelete"] = "Delete", ["hwAdd"] = "Add homework",
            ["hwDeleteConfirm"] = "Delete homework “{0}”?", ["hwEditTitle"] = "EDIT HOMEWORK",
            ["renameTip"] = "Rename", ["mapTip"] = "Show on map",
            ["placeholderTitle"] = "Section under construction", ["placeholderHint"] = "Coming in the next stage", ["loadingTitle"] = "Loading the timetable…",
            ["themeToggleTip"] = "Toggle theme", ["sidebarToggleTip"] = "Collapse sidebar",
            ["groupPickTitle"] = "Choose group", ["search"] = "Search", ["groupSearchHint"] = "Group number…", ["select"] = "Select",
            ["confirm"] = "Confirm", ["delete"] = "Delete", ["updatedChip"] = "updated {0}", ["errorTitle"] = "Error",
            ["bootstrapError"] = "Could not load the timetable", ["bootstrapHint"] = "Check your connection and retry", ["retry"] = "Retry",
            ["friendAbsent"] = "not nearby", ["inter100"] = "same room", ["inter75"] = "same floor", ["inter50"] = "same building", ["inter25"] = "at the university",
            ["savedOk"] = "Saved", ["noGroup"] = "No group selected", ["noGroupHint"] = "Click the group card on the left",
            ["winMinimize"] = "Minimize", ["winMaximize"] = "Maximize", ["winClose"] = "Close",
            ["refreshOk"] = "Timetable updated", ["refreshNone"] = "Timetable is up to date",
            ["refreshFail"] = "Could not update the timetable: {0}", ["refreshTip"] = "Refresh timetable (F5)",
            ["weekCurrentSuffix"] = " · current", ["weekOpenDayTip"] = "Open this day in the schedule",
            ["summaryTotal"] = "Lessons total", ["summaryByDay"] = "By day", ["summaryByType"] = "By type",
            ["summarySubjects"] = "Subjects", ["summaryTeachers"] = "Teachers", ["summaryRooms"] = "Rooms", ["summaryBothShort"] = "Both",
            ["teachersSearchHint"] = "Surname, department or subject", ["teachersOnlyMine"] = "Only mine", ["teachersCount"] = "{0} of {1}",
            ["teachersPick"] = "Pick a teacher", ["teachersPickHint"] = "The list on the left: search by surname or subject",
            ["teachersLoading"] = "Loading the directory…", ["teachersLoadFail"] = "Teacher directory unavailable: {0}",
            ["teachersNoSource"] = "no cache, no bundled copy, no network", ["teachersMine"] = "mine",
            ["teachersTeachesMine"] = "Teaches your group", ["teachersNotMine"] = "Does not teach your group",
            ["mapNextLesson"] = "Next lesson", ["mapLessonPrefix"] = "Lesson: {0}", ["mapPickPlan"] = "Pick a plan", ["mapFloorN"] = "floor {0}",
            ["mapInMinutes"] = "in {0} min", ["mapInHours"] = "in {0} h", ["mapInDays"] = "in {0} d", ["mapNow"] = "right now",
            ["mapToNext"] = "To the next lesson", ["mapVc"] = "ВЦ — showing the ГК plan",
            ["mapDownloadAll"] = "Download fresh plans", ["mapOpenFolder"] = "Open maps folder", ["mapVerify"] = "Check offline cache",
            ["mapCacheStatus"] = "{0} of {1} plans offline", ["mapDownloaded"] = "Plans downloaded: {0} of {1}",
            ["mapFullscreen"] = "Full screen", ["mapExitFullscreen"] = "Close (Esc)", ["mapFit"] = "Fit",
            ["mapZoomIn"] = "Zoom in", ["mapZoomOut"] = "Zoom out", ["mapReset"] = "100%", ["mapMore"] = "More",
            ["mapNoImage"] = "Plan not loaded: no network and no bundled copy",
            ["mapRemoteHint"] = "Press ◉ on a lesson or pick a building and floor",
        }
    };

    public I18nService(string lang = "ru") => SetLanguage(lang);

    public void SetLanguage(string lang)
    {
        lang = (lang ?? "ru").ToLowerInvariant();
        if (lang != "ru" && lang != "en") lang = "ru";
        if (_lang == lang) return;
        _lang = lang;
        LanguageChanged?.Invoke();
    }

    public string T(string key, params object[] args)
    {
        if (!_dict.TryGetValue(_lang, out var d) || !d.TryGetValue(key, out var v))
        {
            // fallback to ru then key
            if (_dict["ru"].TryGetValue(key, out var v2)) v = v2; else v = key;
        }
        if (args.Length > 0) try { return string.Format(v, args); } catch { return v; }
        return v;
    }

    public string FormatDate(DateTime d) => _lang == "ru" ? d.ToString("dd.MM.yyyy") : d.ToString("yyyy-MM-dd");
    public string FormatDay(DateTime d)
    {
        // returns localized weekday short
        var dow = (int)d.DayOfWeek;
        string key = dow switch { 0 => "sunShort", 1 => "monShort", 2 => "tueShort", 3 => "wedShort", 4 => "thuShort", 5 => "friShort", 6 => "satShort", _ => "monShort" };
        return T(key);
    }
    public string FormatDayFull(DateTime d)
    {
        var dow = (int)d.DayOfWeek;
        string key = dow switch { 0 => "sun", 1 => "mon", 2 => "tue", 3 => "wed", 4 => "thu", 5 => "fri", 6 => "sat", _ => "mon" };
        return T(key);
    }
    public string FormatParity(bool isOdd) => isOdd ? T("odd") : T("even");
    public string FormatParityBadge(bool isOdd) => isOdd ? T("oddBadge") : T("evenBadge");
}
