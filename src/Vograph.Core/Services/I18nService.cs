namespace Vograph.Core.Services;

public class I18nService
{
    public string Language => "ru";
    public event Action? LanguageChanged;

    private readonly Dictionary<string, string> _dict = new(StringComparer.OrdinalIgnoreCase)
    {
            ["accountTitle"] = "Аккаунт",
            ["accountUnconfigured"] = "Сервер аккаунтов не настроен",
            ["accountGuest"] = "Гостевой профиль: данные доступны без аккаунта и сети.",
            ["accountLocal"] = "Локальные данные аккаунта. Синхронизация личных данных пока недоступна.",
            ["accountIsolation"] = "Данные гостя и каждого аккаунта хранятся отдельно. Автоматического переноса и отправки личных данных нет.",
            ["accountRecovery"] = "Переключение профиля требует восстановления. Повторите попытку.",
            ["accountRecover"] = "Восстановить профиль",
            ["accountTransitionFailed"] = "Профиль не переключён. Завершите текущие операции и повторите попытку.",
            ["accountReauth"] = "Требуется повторный вход. Локальные данные сохранены.",
            ["accountOffline"] = "Сервер недоступен. Локальные данные сохранены; повторите попытку позже.",
            ["accountCreated"] = "Аккаунт создан. Теперь введите пароль и войдите.",
            ["accountRegistrationUnavailable"] = "Регистрация на этом сервере недоступна. Войдите в существующий аккаунт или продолжайте как гость.",
            ["accountLogoutLocal"] = "Вы вышли на этом устройстве. Отзыв сессии на сервере не подтверждён этой операцией интерфейса.",
            ["accountValidation"] = "Логин: 3–32 латинские буквы, цифры, точка, дефис или подчёркивание. Пароль: 12–128 символов. Имя: до 80 символов.",
            ["accountCancelled"] = "Операция отменена: профиль изменился или приложение закрывается.",
            ["accountFailed"] = "Операция аккаунта не выполнена. Повторите попытку позже.",
            ["accountBadLogin"] = "Неверный логин или пароль.",
            ["accountUsernameTaken"] = "Этот логин уже занят.",
            ["accountRateLimited"] = "Слишком много попыток. Подождите и повторите позже.",
            ["accountProfileLoaded"] = "Профиль получен с сервера.",
            ["accountProfileSaved"] = "Имя профиля сохранено на сервере.",
            ["accountDevicesLoaded"] = "Список устройств получен с сервера.",
            ["accountDeviceRevoked"] = "Сессия выбранного устройства отозвана.",
            ["accountUsername"] = "Логин",
            ["accountPassword"] = "Пароль",
            ["accountDisplayName"] = "Отображаемое имя (необязательно)",
            ["accountLogin"] = "Войти",
            ["accountRegister"] = "Создать аккаунт",
            ["accountMode"] = "Вход / регистрация",
            ["accountRegistration"] = "Регистрация",
            ["accountLogout"] = "Выйти из аккаунта",
            ["accountConfirmLogout"] = "Выйти и открыть гостевой профиль? Данные аккаунта сохранятся отдельно.",
            ["accountCancel"] = "Отмена",
            ["accountRefresh"] = "Обновить профиль",
            ["accountSave"] = "Сохранить имя",
            ["accountDevices"] = "Устройства",
            ["accountMore"] = "Показать ещё",
            ["accountRevoke"] = "Отозвать сессию",
            ["accountCurrentDevice"] = "Это устройство",
            ["accountRevokeAll"] = "Отозвать все сессии и выйти",
            ["accountCurrentPassword"] = "Текущий пароль",
            ["accountNewPassword"] = "Новый пароль",
            ["accountChangePassword"] = "Изменить пароль и выйти",
            ["accountExport"] = "Экспорт данных",
            ["accountExportDownload"] = "Скачать экспорт",
            ["accountDelete"] = "Удалить аккаунт",
            ["accountDeleteConfirm"] = "Удалить аккаунт на этом сервере? Локальные данные устройства сохранятся отдельно; удалённый wipe не обещается.",
            ["accountProof"] = "Подтвердите личность паролем",
            ["accountRecoveryEmail"] = "Почта для восстановления",
            ["accountResetPassword"] = "Сбросить пароль",
            ["accountVk"] = "Войти с VK ID",
            ["accountYandex"] = "Войти с Яндекс ID",
            ["accountLink"] = "Привязать",
            ["accountUnlink"] = "Отвязать",
            ["accountIdentities"] = "Способы входа",
            // Communities
            ["communityTitle"] = "Сообщества",
            ["communityJoin"] = "Подать заявку",
            ["communityPending"] = "Заявка на рассмотрении",
            ["communityMembers"] = "Участники",
            ["communityStaff"] = "Персонал",
            ["communityHomework"] = "Общая домашка",
            ["communityAnnouncements"] = "Объявления",
            ["communityPolls"] = "Опросы",
            ["communityVote"] = "Голосовать",
            ["communityResults"] = "Результаты",
            ["communityAccept"] = "Принять",
            ["communityReject"] = "Отклонить",
            ["communityEmpty"] = "Сообществ пока нет",
            ["communityNeedAccount"] = "Чтобы вступить в сообщество, войдите в аккаунт",
            ["communityForbidden"] = "Нет доступа к этому сообществу",
            // Private sync conflict / offline
            ["syncConflictTitle"] = "Конфликт синхронизации",
            ["syncKeepLocal"] = "Оставить локальные",
            ["syncKeepServer"] = "Оставить серверные",
            ["syncConflictBody"] = "Локальные и серверные данные разошлись. Выберите, какую версию сохранить.",
            ["syncExpired"] = "Операция синхронизации устарела — повторите действие",
            ["syncOffline"] = "Сервер недоступен. Синхронизация отложена; локальные данные сохранены.",
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
            ["navTeachers"] = "Преподаватели", ["navMaps"] = "Карты", ["navFriends"] = "Друзья", ["navHomework"] = "Домашка", ["navCommunity"] = "Сообщества", ["navSettings"] = "Настройки",
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
            ["themeToggleTip"] = "Переключить тему", ["sidebarToggleTip"] = "Свернуть панель (Ctrl+B)", ["sidebarExpandTip"] = "Развернуть панель (Ctrl+B)",
            ["groupPickTitle"] = "Выбор группы", ["search"] = "Поиск", ["groupSearchHint"] = "Номер группы…", ["select"] = "Выбрать",
            ["confirm"] = "Подтвердить", ["delete"] = "Удалить", ["updatedChip"] = "обновлено {0}", ["errorTitle"] = "Ошибка",
            ["bootstrapError"] = "Не удалось загрузить расписание", ["bootstrapHint"] = "Проверьте сеть и повторите", ["retry"] = "Повторить",
            ["friendAbsent"] = "нет рядом", ["inter100"] = "в той же аудитории", ["inter75"] = "на том же этаже", ["inter50"] = "в том же корпусе", ["inter25"] = "в вузе",
            ["savedOk"] = "Сохранено", ["noGroup"] = "Группа не выбрана", ["noGroupHint"] = "Нажмите на карточку группы слева",
            ["winMinimize"] = "Свернуть", ["winMaximize"] = "Развернуть", ["winClose"] = "Закрыть", ["winRestore"] = "Свернуть в окно",
            ["refreshOk"] = "Расписание обновлено", ["refreshNone"] = "Расписание актуально",
            ["refreshFail"] = "Не удалось обновить расписание: {0}", ["refreshTip"] = "Обновить расписание (F5)",
            // The reason substituted into refreshFail/updFailWith when the run was started with VOGRAPH_OFFLINE=1.
            ["offlineMode"] = "сеть отключена для этого запуска (VOGRAPH_OFFLINE)",
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
            ["mapCacheStatus"] = "{0} из {1} планов офлайн", ["mapDownloaded"] = "Планы скачаны: {0} из {1}", ["mapDownloadPartial"] = "Скачано {0} из {1} — часть планов недоступна",
            ["mapFullscreen"] = "На весь экран", ["mapExitFullscreen"] = "Закрыть (Esc)", ["mapFit"] = "Вписать",
            ["mapZoomIn"] = "Крупнее", ["mapZoomOut"] = "Мельче", ["mapReset"] = "100%", ["mapMore"] = "Ещё",
            ["mapNoImage"] = "План не загружен: нет сети и встроенной копии",
            ["mapRemoteHint"] = "Нажмите ◉ у пары или выберите корпус и этаж",
            ["friendsSubtitle"] = "До пяти групп: их пары появляются точками на ваших карточках", ["friendsCount"] = "{0} из {1}", ["friendsAdd"] = "Добавить группу", ["friendsMax"] = "Максимум пять групп", ["friendsNames"] = "Имена товарищей", ["friendsEnabled"] = "Показывать", ["friendsRemove"] = "Удалить", ["friendsRemoveConfirm"] = "Убрать группу {0} из друзей?", ["friendsEmpty"] = "Друзей пока нет", ["friendsEmptyHint"] = "Добавьте группу — её пары появятся точками на ваших карточках", ["friendsColor"] = "Цвет", ["friendAdded"] = "Группа {0} добавлена", ["intersections"] = "Пересечения", ["strictnessHint"] = "Точка загорается, когда друг в это же время не дальше выбранного уровня", ["alwaysShowAll"] = "Всегда все светофоры", ["alwaysShowAllHint"] = "Друзья без пересечения — серой точкой", ["previewTitle"] = "Превью", ["previewNone"] = "В ближайшие две недели пересечений нет",
            ["strictTick25"] = "в вузе", ["strictTick50"] = "корпус", ["strictTick75"] = "этаж", ["strictTick100"] = "аудитория",
            ["hwGroupUrgent"] = "Горит сегодня", ["hwGroupBurning"] = "Горит", ["hwGroupApproaching"] = "Скоро", ["hwGroupFar"] = "Далеко", ["hwGroupOverdue"] = "Просрочено", ["hwGroupDone"] = "Сдано",
            ["hwOpen1"] = "{0} открытая", ["hwOpen2"] = "{0} открытые", ["hwOpen5"] = "{0} открытых", ["hwDoneCount"] = "сдано {0}",
            ["hwAddShort"] = "Добавить", ["hwEmpty"] = "Домашки нет", ["hwEmptyHint"] = "Добавьте задание кнопкой выше или через ＋ на карточке пары",
            ["hwPickSubject"] = "ПРЕДМЕТ", ["hwPickSubjectHint"] = "Название предмета…", ["hwNoSubjects"] = "У группы нет пар — добавить домашку не к чему",
            ["setAppearance"] = "Внешний вид", ["setTheme"] = "Тема", ["themeSystem"] = "Как в системе", ["themeLight"] = "Светлая", ["themeDark"] = "Тёмная",
            ["setCompactSidebar"] = "Компактный сайдбар", ["setAnimations"] = "Анимации", ["setSchedule"] = "Расписание", ["setChange"] = "изменить",
            ["setAutoCheckAt"] = "автопроверка {0}", ["setNever"] = "ещё не было", ["setAbout"] = "О программе", ["setVersion"] = "Версия {0}",
            ["setReleases"] = "Страница релизов", ["setSources"] = "Источники данных", ["setSourceTimetable"] = "Расписание студентов — voenmeh.ru",
            ["setSourceMaps"] = "Планы корпусов — voenmeh.ru/openmap", ["setDataFolder"] = "Открыть папку данных",
            ["setNotifications"] = "Уведомления", ["notifEnabled"] = "Показывать уведомления о парах",
            ["notifTime1Label"] = "Вечером — про завтра", ["notifTime2Label"] = "Утром — про сегодня",
            ["notifSave"] = "Сохранить времена", ["notifTest"] = "Тест уведомления",
            ["notifBadTime"] = "Время указывается как ЧЧ:ММ", ["notifSaved"] = "Времена сохранены: {0} и {1}",
            ["setSync"] = "Синхронизация", ["syncExport"] = "Экспорт JSON", ["syncImport"] = "Импорт JSON",
            ["syncShowQr"] = "Показать QR", ["syncHideQr"] = "Скрыть QR",
            ["syncQrHint"] = "Отсканируйте в Android: Настройки → Синхронизация",
            ["syncQrServerHint"] = "Данных много: QR ведёт на сервер в локальной сети — включите его ниже",
            ["syncLan"] = "Сервер в локальной сети :8765", ["syncLanAddress"] = "Адрес: {0}",
            ["syncLanFail"] = "Не удалось запустить сервер: {0}",
            ["syncLanBusy"] = "Порт {0} занят другой программой",
            ["syncExported"] = "Экспорт сохранён: {0}",
            // Updates card / sidebar item («updTitle», «autoUpdate» and «updDownloading» above are reused as they are)
            ["setUpdates"] = "Обновления", ["updIdle"] = "Проверка ещё не выполнялась", ["updChecking"] = "Проверка…",
            ["updUpToDate"] = "Актуальная версия {0} · проверено {1}", ["updAvailable"] = "Доступна {0}",
            ["updDownloaded"] = "Скачано {0} — готово к установке", ["updInstall"] = "Установить и перезапустить", ["updLater"] = "Позже",
            ["updCheck"] = "Проверить обновление", ["updInBrowser"] = "В браузере",
            ["updRateLimited"] = "GitHub ограничил запросы с вашей сети (лимит или VPN). Попробуйте позже или откройте страницу релизов",
            ["updFailWith"] = "Не удалось проверить обновление: {0}", ["updNoReleases"] = "Релизов для Windows не найдено",
            ["updUpdatingTo"] = "Обновляюсь до {0}…",
            ["updDialogHint"] = "Приложение закроется, распакует обновление поверх себя и запустится снова. Данные не затрагиваются.",
            ["updDownloadFail"] = "Не удалось скачать обновление: {0}", ["updApplyFail"] = "Не удалось запустить установку: {0}",
            ["updBadZip"] = "Скачанный архив повреждён — попробуйте ещё раз",
    };

    public I18nService(string lang = "ru") { _ = lang; }

    public void SetLanguage(string lang) { _ = lang; _ = LanguageChanged; }

    public string T(string key, params object[] args)
    {
        if (!_dict.TryGetValue(key, out var v)) v = key;
        if (args.Length == 0) return v;
        try { return string.Format(v, args); }
        catch (FormatException) { return v; }
    }

    public string FormatDate(DateTime d) => d.ToString("dd.MM.yyyy");
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
