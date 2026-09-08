# Zapara — контракт визуальной реализации

Дата: 2026-09-08. Основание — существующий Desktop и утверждённые локальные планы.
Это документация, не новый стиль, не реализация экранов и не свидетельство пройденного UI QA.
«Источник» ниже означает прочитанный код; «цель» — требование последующей реализации.

## 1. Характер, границы и источники

Сохраняем принятый монохром Inter: спокойные поверхности, тонкие границы,
инвертированные основные кнопки, штриховые иконки, цвет только по смыслу.
Windows не перестраиваем; Android адаптирует этот рисунок к телефону; админка — к браузеру.
Zapara — неофициальное приложение только для БГТУ «Военмех».
Ни приложение, ни роли сообщества не подразумевают одобрения университетом.
Цель всех трёх UI — русский язык. Английский UI и переключатель Windows ещё существуют;
их удаление — отдельная работа без изменения пользовательских данных и постороннего restyle.
Сценарии: студент без сети читает расписание/карты; участник работает со своим профилем;
персонал сообщества и администратор выполняют только разрешённые им действия.

### Карта источников (пути от корня репозитория)

| Обозначение | Источник и область |
|---|---|
| T | `src/Vograph.Desktop/Theme/Tokens.axaml`: цвета, радиусы, тени |
| Y | `src/Vograph.Desktop/Theme/Typography.axaml`: текст, карточки, чипы, тосты |
| I | `src/Vograph.Desktop/Theme/Icons.axaml`: геометрия Lucide |
| B | `src/Vograph.Desktop/Theme/Controls/Buttons.axaml`: кнопки и состояния |
| N | `src/Vograph.Desktop/Theme/Controls/Inputs.axaml`: поля, списки, tooltip, switch |
| C | `src/Vograph.Desktop/Theme/Controls/Custom.axaml`: icon, segmented, nav, skeleton, empty |
| M | `src/Vograph.Desktop/Theme/Motion.axaml`, `src/Vograph.Desktop/Services/MotionSettings.cs`: движение |
| S | `src/Vograph.Desktop/Shell/MainWindow.axaml`, `src/Vograph.Desktop/Features/Preferences/SettingsView.axaml`: оболочка и настройки |
| A | `docs/superpowers/plans/2026-09-08-android-ui-rework.md`, §§Д1–Д10: утверждённый локальный план Android |
| P | `docs/superpowers/plans/2026-09-08-platform-rest.md`, binding rulings, §§9–11: утверждённый локальный план платформы |
| Q | `docs/superpowers/specs/2026-09-08-account-platform-design.md`, §§2–6, 9–12: спецификация и границы |

Планы в `docs/superpowers/` локальные, игнорируемые; `.gitignore` ради них не менять.
При расхождении точных Desktop-значений с пересказом читать T/Y/B/N/C/M;
мобильные адаптации брать из A, более поздние платформенные решения — из P.
Не переносить исторические команды установки, коммитов или очистки из планов в эту задачу.

## 2. Цвет

Точные ключи T имеют префикс `Brush.`; ниже он опущен. Восемь цифр — **ARGB**, не CSS RGBA.
Android сохраняет значения байт-в-байт; `cardPressed` соответствует Desktop `CardHover`.
Razor получает именованные локальные токены с теми же значениями и корректным преобразованием alpha.
Режимы всех платформ: «Системная / Светлая / Тёмная», настройки оформления локальны устройству.
Desktop `src/Vograph.Desktop/Services/ThemeService.cs` уже сопоставляет System с ThemeVariant.Default;
Android/Razor должны следовать системной теме, пока пользователь явно не выбрал другую.

| Ключ | Dark | Light |
|---|---|---|
| Canvas | `#0D0D0D` | `#FFFFFF` |
| Surface | `#111111` | `#F7F7F7` |
| Card | `#171717` | `#F5F5F5` |
| CardHover | `#1C1C1C` | `#EFEFEF` |
| Chip | `#222222` | `#EBEBEB` |
| Line | `#262626` | `#E5E5E5` |
| LineStrong | `#333333` | `#D4D4D4` |
| Text1 / Accent | `#F2F2F2` | `#111111` |
| Text2 | `#8B8B8B` | `#767676` |
| Text3 | `#5C5C5C` | `#A3A3A3` |
| OnAccent | `#0D0D0D` | `#FFFFFF` |
| Selection | `#14FFFFFF` | `#0D000000` |
| FocusRing | `#59FFFFFF` | `#40000000` |
| Backdrop | `#8C000000` | `#59000000` |
| SegThumb | `#171717` | `#FFFFFF` |
| Ok | `#4CC38A` | `#2FA36B` |
| Warn / WarnSoft / OnWarn | `#F2A33C` / `#24F2A33C` / `#1A1A1A` | `#D9861B` / `#1FD9861B` / `#FFFFFF` |
| Bad / BadSoft / OnBad | `#EF5B6B` / `#24EF5B6B` / `#FFFFFF` | `#D7404F` / `#1FD7404F` / `#FFFFFF` |
| Info | `#5AA9FF` | `#2B7FD9` |
| Friend1 | `#F2A33C` | `#D9861B` |
| Friend2 | `#4CC38A` | `#2FA36B` |
| Friend3 | `#5AA9FF` | `#2B7FD9` |
| Friend4 | `#C77DFF` | `#9B51E0` |
| Friend5 | `#FF7A9C` | `#E0527A` |
| MapInk / MapInkSoft / OnMapInk | `#2B7FD9` / `#402B7FD9` / `#FFFFFF` | те же |
| QrPaper | `#FFFFFFFF` | `#FFFFFFFF` |
| CloseHover | `#E81123` | `#E81123` |

- Canvas — фон; Surface — оболочка; Card — контент; Chip — компактные подложки.
- Text1 — значимый текст, Text2 — вторичный, Text3 — слабый; контраст проверяется по §8.
- Акцент монохромный. Не вводить цветную бренд-палитру, Material dynamic color или синий Fluent accent.
- Цветовые исключения: статусы, пять групп друзей, чернила карты, системное закрытие окна.
- Друзья — идентичность группы, не новый статус; смысл дублируется подписью, не только цветом.
- Y: `far` → Text2, `approaching` → Text1, `burning` → WarnSoft/Warn,
  `urgent` → Warn/OnWarn, `overdue` → BadSoft/Bad, `done` → opacity 0.55.
- Подсветка карты остаётся синей на светлой бумаге в обеих темах; планы не инвертировать.
- QrPaper остаётся белым для сканирования. Это не разрешение добавлять QR-сценарии Android.
- Официальные знаки VK ID/Yandex ID — отдельное бренд-исключение, не самодельные Lucide-логотипы.

## 3. Типографика и иконки

Источник Y: `fonts:Inter#Inter`; `src/Vograph.Desktop/Vograph.Desktop.csproj`
подключает `Avalonia.Fonts.Inter` 12.1.1. Android/Razor используют локальные лицензированные assets.
При распространении Inter сохранять применимые права, copyright и SIL OFL;
наличие готовых Android/Razor файлов и лицензий этим документом не подтверждается.
A задаёт будущие `res/font/inter_regular.ttf`, `inter_medium.ttf`, `inter_semibold.ttf`
и `android/app/src/main/assets/fonts/OFL.txt`; не класть текст лицензии в `res/font`.
Новые шрифты сейчас не скачивать. У иконок сохранить уведомление ISC (источник I).

| Платформа / роль | Размер / вес | Дополнение |
|---|---|---|
| Desktop Window | 13.5 / наследуемый | основной текст |
| Desktop display | 26 / SemiBold | tracking −0.5 |
| Desktop title | 18 / SemiBold | заголовок |
| Desktop subject | 15 / SemiBold | название пары |
| Desktop caption | 12 / наследуемый | подпись |
| Desktop micro (существующее) | 10.5 / Medium | tracking 0.8, Text3; не стандарт нового текста |
| Android title | 22sp / 500 | шапка |
| Android section | 17sp / 500 | карточка / пара |
| Android body / bodyStrong | 15sp / 400 либо 500 | основной текст |
| Android caption | 12sp / 400 | Text2 |

Android: line-height 1.3, без капса кроме аббревиатур данных; цифры не моноширинные.
Desktop `tnum` — табличные цифры Inter, не смена семейства. Y не задаёт общий line-height.
Razor переносит иерархию Desktop в масштабируемые единицы, не копирует micro для форм/ошибок.
Не сжимать текст до 10px ради плотности; использовать перенос, адаптацию строк и прокрутку.
I/C: штриховые Lucide в поле 24×24, без заливки, круглые концы/соединения, цвет текста.
A: Android drawable из тех же путей, штрих 1.75; не подключать `material-icons-extended`.
Никаких эмодзи/Unicode-пиктограмм вместо иконок; icon-only действие имеет русское доступное имя.
Кнопки «Войти с VK ID» / «Войти с Яндекс ID» сохраняют официальные правила брендинга,
если знак встроен; не перекрашивать его произвольно и не заменять generic-иконкой.

## 4. Отступы, геометрия и платформы

T: `Radius.Card=12`, `Control=8`, `Chip=6`, `Pill=999`, `Dialog=14`, `Icon=9`, `Toast=10`.
Это Avalonia logical units; A переносит радиусы в dp. Размеры текста Android — sp.
A: `ZaparaSpace`: `xs=4`, `s=8`, `m=12`, `l=16`, `xl=24` dp; цель касания минимум 44dp.
Desktop не имеет этих spacing-ключей в T: не выдавать мобильную шкалу за существующие ресурсы XAML.
Точные существующие рецепты: card padding `16,14`; button `14,7`; input `10,7`;
chip `8,3`, room chip `10,5`; toast `12,9`; segmented container `3` (Y/B/N/C).
Сохранять эти рецепты Windows, не округлять всё к сетке 4. Новые экраны компоновать из них.

### Windows — существующая оболочка

S: окно 1280×800, минимум 960×600, строка заголовка 40; боковая навигация сохраняется.
Восемь разделов: Расписание, Неделя, Сводка, Преподаватели, Карты, Друзья, Домашка, Настройки.
Сохранять Ctrl+1…8, Ctrl+Comma, Ctrl+B, F5; не делать девятый пункт аккаунта в sidebar.
Настройки: ScrollViewer, stack margin `32,26`, spacing `14`, max-width `720`;
карточки со stack spacing `10`, строки label/control. Аккаунт добавляется такой же карточкой (P §9).

### Android — утверждённая цель A, не уже готовая оболочка

Низ: Расписание / Карты / Домашка / Разделы; 64dp + системный inset, равные ширины.
Иконка 22dp, caption; активный индикатор 18×2dp Text1, не Material-пилюля.
«Разделы» открывает нижнюю шторку: сетка 2 колонки Неделя/Сводка/Преподаватели/Друзья,
затем Настройки на всю ширину; активна при открытом разделе из шторки.
Шапка 56dp, title, чип группы; список: края `l`, промежутки `s`, карточка `m`/`l`.
Расписание — pager дней, полоска дат, «Сегодня»; действия пары — long-press шторка,
не перенос hover. Редактор: нижняя шторка, «Отмена» и «Сохранить», учёт IME/insets.
В A ghost редактора имеет обводку LineStrong; Desktop ghost в B остаётся без неё.
Одновременно одна шторка; back закрывает верхний модальный слой/карту, затем возвращает к расписанию.
Экран/список владеет прокруткой; длинные названия и масштаб шрифта не перекрывают нижнюю панель.

### Razor admin — будущая W6

ASP.NET Core/.NET 8 Razor Pages в текущем host, без нового React/Node-стека.
Переносить точный серый рисунок Desktop: поверхности, границы, Inter, первичные/вторичные кнопки.
Не копировать оконный chrome и абсолютные desktop-ширины; формы перестраиваются при узком viewport.
Шрифты/иконки локальны, никаких удалённых шрифтов, аналитики и выгрузки персональных данных.
Разметка страниц, новые экраны и новые CSS-токены в этой работе не создаются.

## 5. Компоненты и честные состояния

| Паттерн / источник | Реализация и обязательные состояния |
|---|---|
| Card (Y) | Card + Line 1 + Radius.Card; hoverable → CardHover/Shadow.Card/translateY(−1); next → LineStrong; past → opacity 0.6 |
| Button (B) | primary Accent/OnAccent; ghost transparent; danger Bad, hover BadSoft; focus-visible FocusRing 1; disabled 0.45 |
| TextBox (N) | Chip/Text1, Radius.Control; placeholder Text3; focus LineStrong; disabled 0.5; постоянная label и отдельная ошибка — цель новых форм |
| ListBoxItem (N) | hover Selection; selected Chip + Medium; selection не подменяет keyboard focus |
| Switch (N) | track 34×18, knob 14; checked Accent/OnAccent; disabled 0.5; touch-area расширяется на телефоне |
| SegmentedControl (B/C) | Chip, Pill, SegThumb/Shadow.Thumb; выбранный сегмент Text1/Medium, остальные Text2 |
| NavItem (C) | icon + label + badge; hover/active Selection/Text1; compact скрывает label/badge, нужно доступное имя |
| Chip/status (Y) | тип/аудитория/состояние; точная семантика §2; done strike-through только текста выполненного ДЗ |
| Tooltip (N) | Card/Line, Radius.Chip, padding 8,5, max-width 320; не единственный способ получить важный текст |
| Empty/Skeleton (C/A) | Desktop empty: icon/title/hint; Android: icon 28, section/caption/action; loading три skeleton-карточки 72dp |
| Toast (Y/A) | Desktop Card/Line, Radius.Toast, max-width 380; Android над bottom bar, края 12dp, до трёх, 4с |
| Dialog/sheet (A) | surface/card, Radius.Dialog, Backdrop; отмена/подтверждение, возврат фокуса; не подтверждать скрытое действие |

Android ZButton, уточнение 2026-09-08: keyboard focus — статический внутренний контур
OnAccent на primary, Text1 на ghost, толщина `2 × hairline`, зазор от края `xs`,
радиус по внутреннему краю Radius.Control. Рисуется поверх indication без изменения
размеров/отступов текста и цели 44dp; действует при MotionOff, не зависит от ripple.
Ghost сохраняет внешнюю LineStrong; disabled не получает контур.

Цель аккаунта (P/Q): карточка в Настройках, не отдельный визуальный язык.
Поля входа: логин, маскированный пароль; профиль: логин и необязательное отображаемое имя;
устройства и связанные провайдеры показывают разрешённые метаданные, никогда credentials.
Валидация берётся из согласованных account-моделей/контрактов, не определяется этим документом.
Состояния: гость, ожидание, ошибка, сессия истекла, офлайн, сервис/провайдер не настроен.
Не показывать успешный вход/sync или рабочий VK/Yandex при отсутствии конфигурации.
Logout — явное подтверждение; импорт локального — отдельное согласие с предупреждением о дублях.
Не смешивать гость/аккаунт A/аккаунт B; экспорт и удаление не обещают удалённый wipe.
При ошибке обновления сохранять last-good; пустое расписание, нет кэша и не загружено — разные состояния.
W5: будущие member/staff views, заявка ожидает/отклонена, личная сдача общего ДЗ,
объявления/опросы, отклонённые черновики; не выдумывать новые формы или маршруты навигации.
Роли: участник, староста, куратор конкретного сообщества, администратор платформы (P §11).
Выбор группы не даёт членства/прав; личные заметки не общее содержимое; голосование не называть анонимным.
W6: создание/привязка сообществ, назначение персонала, модерация заявок/контента,
отключение аккаунта/отзыв сессии, аудит — только объём P; права проверяет сервер, не вид кнопки.

## 6. Движение и взаимодействие

M: `MotionSettings.Enabled = prefs.Animations && systemAllows`; `Duration(ms)` иначе 0.
Windows учитывает SPI_GETCLIENTAREAANIMATION; вне VM-контекста `Resolve` возвращает Off.
`App.SetMotion` снимает Motion.axaml; шаблонные части управляются самими контролами.
M: background/opacity/buttons 150ms, press transform 100ms, card 150ms,
NavIndicator 220ms, sidebar width 200ms; основная кривая `(0.2,0.8,0.2,1)`.
Исключение текущего XAML: hwdot 1→0.35 и mapglow 1→0.3, 1600ms alternate, SineEaseInOut.
Не утверждать, что весь Desktop уже transform-only: M анимирует также Height/Width и brushes.
A: переход раздела 180ms/8dp; индикатор 200ms; press scale 0.98 за 120ms; тема 220ms;
каскад первых 8 карточек 200ms/8dp, шаг 30ms, окно 400ms; skeleton цикл 1400ms;
ДЗ alpha 1→0.55→1 за 1600ms; карта 0.4→0.8 за 1200ms; zoom кнопками 200ms.
Pager/шторки — штатное движение с обязательным путём без анимации; кривая A `(0.2,0.8,0.2,1)`.
Android: настройка && ANIMATOR_DURATION_SCALE > 0; иначе нулевые длительности, остановка циклов/каскада.
Razor: учитывать prefers-reduced-motion; новая анимация не требуется, при добавлении — transform/opacity.
Уменьшение движения не удаляет статус, содержимое или обратную связь и не блокирует действие.

## 7. Поверхности и глубина

Desktop: смешанная стратегия — тональная ступень + граница, карточка в покое без тени (Y).
Точные T shadow-рецепты (порядок Avalonia: x y blur spread color):
`Shadow.Card`: `0 2 10 0 #66000000` Dark / `0 2 10 0 #14000000` Light;
`Shadow.Thumb`: `0 1 3 0 #40000000` / `0 1 3 0 #22000000`;
`Shadow.Dialog`: `0 16 48 0 #99000000` / `0 16 48 0 #33000000`;
`Shadow.None`: `0 0 0 0 #00000000` в обеих. Не придумывать дополнительные elevation.
Android A запрещает декоративные градиенты/blur/glow; исключения самого A — skeleton-блик и map-пульс.
Это не разрешение glassmorphism. Существующий C skeleton shine — служебный градиент, не бренд-фон.

## 8. Доступность, долг и будущая проверка

Цель WCAG 2.2 AA: обычный текст ≥4.5:1, крупный ≥3:1; значимые границы/индикаторы ≥3:1.
Проверять фактическую пару фон/текст с alpha; наличие токена не доказывает контраст.
Text3, Text2 на светлой Card, FocusRing/LineStrong и цветные status-пары требуют проверки;
для значимого текста выбирать пригодный существующий токен, не изобретать HEX и не прятать смысл.
Клавиатура: видимый focus, логичный Tab, Enter/Space, Escape/back, отсутствие ловушек и возврат из modal.
Touch минимум 44dp; старые Desktop icon 34/26 и round 32 — рисунок, не мобильный hit target.
Учитывать font scaling/zoom, длинные русские строки, screen reader labels/status; не полагаться на hover.
Y скрывает `.acts` до pointerover: это текущий риск доступности, не доказанный keyboard-pass.
Отложена реализация, **не принято исключение из доступности**: micro/контраст/hover-only и маленькие цели
проверяет владелец каждого UI при его изменении; блокирующие дефекты устраняются до приёмки.
Windows: реальный OS UI через `src/Vograph.Desktop.UiVerify/`, scratch-профиль, dark/light/system,
минимальный размер и DPI/font scaling, фокус/диалоги, аккаунт/consent/logout, снимки без личных данных.
Android: существующий разрешённый эмулятор, ADB/Compose-кадры обоих тем, system/reduce-motion,
все разделы/шторки, focus/touch/font scaling, офлайн; виджеты расписания и ДЗ после logout/смены аккаунта.
Admin: локальный Playwright с тестовыми данными, responsive/zoom/keyboard, login/logout,
CSRF, запрет прямого доступа, отзыв роли, модерация и аудит; не маркетинговый Lighthouse/SEO-гейт.
React-tooling неприменим; Node/React-пакеты не нужны. Числовой score без запуска не указывать.
До UI-приёмки снять и просмотреть новые кадры и состояния примитивов, затем экраны на той же сборке.
Сейчас выполнена только сверка источников; браузер/ADB, сборки и UI-тесты не запускались.
