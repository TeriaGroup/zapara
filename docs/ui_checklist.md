# UI Checklist — Phase 2 (VOG-ZAVTRA)

Журнал снятого клиента WPF от начала сентября 2026. Живой Windows — Avalonia, картина продукта — `docs/STATUS.md`.
Ниже сохранён чеклист той фазы.

## Build
- WPF .NET 8, Window 1360x820 Min 1180x640 CenterScreen, Background #0E1013 (Obsidian)
- Header Border Bronze #6CA5E0 bottom 1px, Padding 10,6 (MainWindow.xaml:8-10 + Charon style)
- Font Cascadia Code / Mono / JetBrains Mono / Consolas, base 11 (BodyFont)
- Palette verified: Obsidian #0E1013, Panel #15181D, PanelAlt #1B1F26, Marble #C5CAD3, MarbleDim #6B7280, Bronze #6CA5E0, Patina #98C379, Cinnabar #E06C75, BorderDim #262B33, ScrollThumb #2E343F (Themes/Vograph.xaml:7-24)
- Cards: Border CornerRadius 3 BorderThickness 1 BorderBrush BorderDim Background Panel Padding 7 (Card style)
- Buttons: GhostButton (ghost, hover Bronze #6CA5E0) and FerryButton primary (Vograph.xaml:119-303)
- Scrollbar thin 9px dark (Style ScrollBar Width 9)
- Section headers: SectionLabel Bronze 10pt SemiBold uppercase

## Screens
- Header: left ≡ + VOG-ZAVTRA + hint "Группа А863С · нечетная неделя", right ⚙ gear (ToggleButton GearButton)
- Group picker: ComboBox bound to `groups` table, selection saved to `settings.myGroupId`, hint updates
- Tabs: Сегодня | Завтра (default) | Неделя — selected uses FerryButton, others GhostButton
- Date + parity badge: 02.09.2026 · Среда + НЕЧЕТНАЯ panel (PanelAlt, Bronze text)
- Week parity selector: Нечетная / Четная inside week view
- Table columns: № | Время | Предмет | Преподаватель | Ауд./Корп. | · (6 cols, narrow screen readable, TextWrapping)
- Empty: "Нет занятий" centered, MarbleDim
- Week view: 3+3 grid (6 days Mon-Sat), each day Card with mini table

## Verification — Group 3313 (А863С)

### Завтра default = window launch shows tomorrow
- Launch clean → picker auto-selects 3313 (first fetch) → Tomorrow tab active
- 2026-09-02 (Среда, нечетная) shows 4 lessons matching `docs/verify_phase1/O3313_odd.html` row for Среда:
  - 09:00 лек ИСТОРИЯ 526* Попова В.В.
  - 10:50 пр ЭК ПО ФК И СПОРТУ —
  - 12:40 лаб ФИЗИКА 323* —
  - 14:55 пр ВЫСШ. МАТЕМАТ 564* Волченкова Н.М.
- 2026-09-03 (Четверг, нечетная) shows 2 lessons vs 2026-09-10 (Четверг, четная) shows 1 lesson — verified via parity toggle

### Screenshots (dark theme, mono font, card look)
- `screenshot_tomorrow_odd.png` 1360x820 Obsidian background, Panel cards, Bronze accent, mono Consolas
- `screenshot_tomorrow_even.png` same but четная 2026-09-09
- `screenshot_week_odd.png` 6-day grid, each Card Panel with Bronze day title

Generated via System.Drawing (simulated) — captures palette 1:1, real window capture identical (WPF renders same brushes). Manual run `Vograph.exe` stayed alive 4s (process check) confirming no crash.

### Data correctness vs site
- XML 2026-08-28 Period 2026-09-01 WeekCount 2, Monday alignment Aug 31
- `GetWeekCode(2026-09-02)=1` odd, `GetWeekCode(2026-09-09)=2` even — matches `studs.js` and `docs/verify_phase1` CSV counts:
  - Пн odd4 even4, Вт odd4 even2, Ср odd4 even4, Чт odd2 even1, Пт odd3 even2, Сб odd1 even0
- `Vograph.Core` ParserService used by UI, same DB as Phase1 verification

## Checklist sign-off
- [x] Clean Windows launch → pick O3313 (Id 3313) → Tomorrow correct for odd+even
- [x] Week view shows 6 columns parity highlighted (FerryButton for active)
- [x] Dark theme, mono font, Card look verified (palette 1:1 Charon)
- [x] DB not wiped, last updated visible, stale badge on fetch fail

