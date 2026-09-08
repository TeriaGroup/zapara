# Vograph: Timetable — Development Prompt

> **Уточнение 2026-09-08:** ниже сохранён исторический план serverless MVP. Пользователь разрешил собственный сервер и реализацию первого локального среза API расписания: модульный монолит ASP.NET Core/.NET 8 + PostgreSQL. Прежний запрет сервера более не действует только в этой части; остальные требования и история сохраняются. Офлайн-гость и локальные данные остаются. Актуальные правила продукта, размещения API и персональных данных в РФ, разрешений на установки/деплой и commit — в `AGENTS.md`; исторический auto-commit не заменяет явного запроса пользователя. Это не утверждение о поставке платформы.

> **Temporary code name:** `Vograph Завтра` / `VOG-ZAVTRA` (replace before release)
> **Локальные тесты, решение 2026-09-08:** пользователь отдельно разрешил Docker для серверных проверок. Исторический запрет Docker ниже не применяется к этому тестовому окружению; доступ к чужим контейнерам и прод-деплой не разрешены.

> **Location:** `C:\Users\NiLle\Desktop\projects\vograph`
> **Style reference:** `C:\Users\NiLle\Desktop\projects\char\Charon2\src\Charon.App\Themes\Charon.xaml:7-24` + `MainWindow.xaml:8-10`
> **Timetable source:** https://voenmeh.ru/obrazovanie/timetables/ (test group `O3313` / anchor `#3313`)
> **Goal:** First build a Windows desktop MVP to debug UI and logic, then port to Android sharing the same logic. No dedicated server, sync only over local network.

---

## 0. Agent Execution Rules — AUTO-ADVANCE BETWEEN PHASES

**This is mandatory.** The agent MUST work autonomously phase-by-phase:

1.  Execute phases **strictly in order**: `0 -> 1 -> 2 -> 3 -> 4 -> 5 -> 6`. Do not skip, do not merge.
2.  **After finishing each phase, immediately start the next phase** — do not wait for user confirmation, do not ask "should I continue?".
3.  Each phase ends with its **Verification Checklist** (defined in §10). The agent must:
    - run the checks,
    - write results to `docs/PROGRESS.md` (raw numbers/screenshots/logs, not opinions),
    - if ALL checks pass -> commit with message `phase N done` and **auto-start phase N+1** in the same session,
    - if ANY check fails -> write `docs/BLOCKER.md` with failure + one root-cause hypothesis and stop.
4.  Keep reports in English, UI text in Russian, code comments in English.
5.  Verification is cheap — always re-read files/execute code instead of guessing.
6.  All commands must be native Windows PowerShell 5.1 compatible, no WSL/Docker.

---

## 1. Understood Requirements (confirmed 2026-09-01)

1.  Show schedule for **tomorrow as default screen**, plus `today / week` views with odd/even week (numerator/denominator) handling.
2.  **Week parity:** the week containing **September 1** is week 1 = odd. Alternate thereafter. Auto-calc, plus manual `invert` toggle in settings if university shifts.
3.  Lesson times come from the site if available, but user overrides (renames/notes/homework) **must never be lost** on site refresh. Store `raw` + `custom` separately.
4.  Site updates once per semester, but ad-hoc edits happen — background refresh every 1-3 days is enough (no polling every hour).
5.  Table columns: `No. | Time | Subject | Teacher | Room/Building | Type`. Must be readable on narrow screens.
6.  Per-cell rename + footnote with scope: `global (all occurrences of this subject)` vs `only on specific weekday (Mon..Sat)`. Keep original value for rollback/diff.
7.  Homework under each subject: text + binding `to next occurrence of this same subject` or `in N occurrences of this same subject` (deferred). Statuses: `far/hidden` -> `gray (approaching, 1 lesson before due)` -> `bold/bright (burning, due tomorrow/today)` -> `overdue (optional)`. Can mark as `done`.
8.  Notifications: **2 user-chosen times** (e.g. 20:00 "what's tomorrow" + 07:30 "what's today"), text uses renamed titles and marks burning homework.
9.  Intersections: user picks up to 5 friend groups (each = its own color icon). Strictness slider `0 = anyone at university at same time` to `100 = same room`. Intermediate: same building / floor. Icon colored inside cell.
10. One primary group per student (switching groups is rare, via settings).
11. No cloud server. Sync only via LAN: file JSON, QR, local HTTP/mDNS.
12. Priority is **simplicity of implementation** for the Windows stage. Choose the fastest stack, not the trendiest.

---

## 2. Hard Constraints

| Parameter | Decision |
|---|---|
| OS (phase 1) | Windows 10/11 native. No WSL/Docker |
| UI language | Russian |
| Stack | **Simplicity > cross-platform**. Recommended for MVP: `C# WinForms or WPF` or `Python + PyQt/Tk` (single file/exe). If shared code with Android is needed, `Flutter` or `.NET MAUI` only if it does NOT complicate the MVP |
| Storage | Local SQLite (`vograph.db`). Tables: `groups`, `schedule_cache`, `overrides`, `homework`, `friends`, `settings` |
| Parser | Resilient to layout changes. Cache `raw_html`, parse into `schedule_cache`. On parse failure show last cache + "stale" badge |
| Parity formula | `isOddWeek(date)` = weeks since Monday of week containing Sep 1. Manual `invertParity` bool in settings |
| Palette | Strictly from Charon2 (see §5) |

---

## 3. Data Model (conceptual)

```
Group { id, name ("O3313"), url, lastFetchedAt }
Lesson { id, groupId, dayOfWeek(1-6), parity(0=both,1=odd,2=even), index(1..7), timeStart, timeEnd, subjectRaw, teacherRaw, roomRaw, buildingRaw, typeRaw }
Override { id, subjectRawNormalized, scope("global"|"weekday:3"), displayName, note, createdAt }
Homework { id, subjectRawNormalized, text, createdAt, targetNthOccurrence (1..10), dueDateComputed, status("pending"|"approaching"|"burning"|"done"|"overdue"), doneAt }
FriendGroup { id, groupName, colorHex (one of 5), enabled }
Settings { myGroupId, parityInvert(bool), notifyTime1, notifyTime2, intersectionStrictness(0..100), lastSyncAt }
```

Rules:
- `subjectRawNormalized = trim + lower + ё->е` — key for Override/Homework.
- `global` scope overrides `weekday` scope.
- Homework `dueDate` = date of N-th next `Lesson` where `subjectRawNormalized` matches. Count occurrences of the same subject, not calendar days. Recompute on every cache update.
- On site `Lesson` update — never delete Override/Homework, rebind by normalized key.

---

## 4. Homework Status Logic

- `far` (>1 lesson before due): hidden or dot indicator
- `approaching` (1 lesson before due): `Foreground = MarbleDim #FF6B7280`, regular weight (gray)
- `burning` (due == tomorrow): `Foreground = Marble #FFC5CAD3`, Bold, background `PanelAlt`, icon
- `burning_urgent` (due == today, morning): `Foreground = Bronze #FF6CA5E0` or `Patina #FF98C379` bright + `BorderDim` highlight — "burns very brightly in the morning"
- `done`: strikethrough, `Opacity 0.5`, moved to bottom, filter "show completed"
- Actions: tap homework -> `Mark done` / `Edit` / `Delete`

---

## 5. Palette & Style — Charon2 Reference

Copy 1:1 from `Charon.xaml:7-24`:

```
Obsidian #0E1013  — window background
Panel    #15181D  — card background
PanelAlt #1B1F26  — fields / alt cards
Marble   #C5CAD3  — primary text
MarbleDim #6B7280 — secondary / gray (approaching HW)
Bronze   #6CA5E0  — accent, interactive, "burns in the morning"
Patina   #98C379  — success / healthy
Cinnabar #E06C75  — alarm / overdue
BorderDim #262B33 — thin 1px border
ScrollThumb #2E343F
```

Font: `Cascadia Code / Cascadia Mono / JetBrains Mono / Consolas`, monospaced everywhere, base `FontSize 11`.
Cards: `Border CornerRadius=3, BorderThickness=1, BorderBrush=BorderDim, Background=Panel, Padding=7` — `Card` style `Charon.xaml:119-125`.
Buttons: `GhostButton` (ghost, hover highlights Bronze) and `FerryButton` for primary action.
Scrollbar thin 9px dark.
Section headers: `SectionLabel` — Bronze, 10pt, SemiBold, uppercase.
Window `MainWindow.xaml:8-10` — 1360x820, CenterScreen, header with thin Bronze bottom border.

Do NOT use bright Material colors, radius >4px, or emojis unnecessarily.

---

## 6. Screens (Windows MVP)

1.  **Header** (like Charon): left `≡` + `VOG-ZAVTRA` + hint "Group O3313 · odd week", right `⚙` (settings).
2.  **Main — Tomorrow Table** (default):
    - Date + weekday + parity badge.
    - Columns: `No | Time | Subject (custom/original) | Teacher | Room/Building | ·` (intersection icons).
    - Homework block under each row with status.
    - Tabs: `Today | Tomorrow | Week`.
    - Empty: "No lessons".
3.  **Week view** — 6 columns (Mon-Sat) or vertical day list, parity highlighted.
4.  **Cell dialog** (long-press/right-click): `Rename | Footnote | Reset to original` + scope radio + preview.
5.  **Homework dialog**: `Text` + `In how many occurrences of this subject (1..10)` + computed due + `Save`.
6.  **Settings (Popup like Charon)**:
    - `My group` (search + pick from site list, cached)
    - `Parity: auto / invert`
    - `Notifications: time 1 [__:__] on/off, time 2 [__:__]`
    - `Friend groups (up to 5)` — add/color/remove
    - `Intersection strictness` — slider 0..100 with labels
    - `Sync` — `Export to file / Import / Show QR / Discover on LAN`
    - `Refresh schedule now` + `last updated`

---

## 7. Notifications

- Windows: `Windows Toast Notification` (WinRT) or `Task Scheduler` at chosen times. Text: `Today (Mon, odd): 1. Math (renamed) room 321 [HW burning] ...`. Click opens app on today.
- Android later: `WorkManager` daily + `AlarmManager` exact.
- 2 independent `HH:mm` times, recreate task on change.
- If no lessons — "No lessons today" or suppress (setting).

---

## 8. Intersections — Algorithm

For each `myLesson` on selected day:
```
for friend in friends (up to 5):
  friendLessons = scheduleCache[friend.group][day][parityFilter]
  for fl in friendLessons where fl.time overlaps myLesson.time:
    score = 0..100
    if sameBuilding -> +40
    if sameRoom -> +100
    else if sameTimeOnly -> +0
    // slider threshold: if score >= threshold -> show icon in friend.color
```
`threshold = intersectionStrictness`. 0 = any time overlap, 100 = only 100 (same room). Icon = small `●` in cell's right edge, tooltip `O3315 — Ivanov, room 322`.

---

## 9. LAN Sync (no server)

- Export: `vograph-sync-YYYYMMDD.json` contains `overrides + homework + settings`. `version:1`.
- Import: merge by key, conflict = `lastWriteWins` + dialog.
- Optional: local HTTP `http://<ip>:8765/sync` (same Wi-Fi) — one device "Host", other "Join via IP/QR". No internet needed.
- QR encodes JSON (if small) or `http://ip:port/sync#token`.

---

## 10. Work Plan — Phases with Auto-Advance and Verification

### Phase 0 — Recon (1 day)
Inspect `voenmeh.ru/obrazovanie/timetables/` DevTools: find XHR for group list and schedule. Document in `docs/API.md` (URLs, JSON/HTML shape, parity/time fields). Verify group O3313.
**Verify:** `docs/API.md` exists + sample raw fetch for O3313 both parities committed.
**Then -> auto-start Phase 1.**

### Phase 1 — Parser + DB (2-3 days)
`ParserService` + `SQLite` per §3. Cache + `getSchedule(date)`. Keep `raw` immutable.
**Verify:** Table for O3313 both weeks matches site pixel-perfect (screenshot/HTML dump in `docs/verify_phase1/`). `overrides` not wiped on re-parse.
**Then -> auto-start Phase 2.**

### Phase 2 — Windows MVP Table (1.5 weeks)
WinForms/WPF window per palette §5, Tomorrow/Today/Week views, group picker. Simplest stack.
**Verify:** Clean Windows launch -> pick O3313 -> Tomorrow table correct for odd+even weeks. `docs/ui_checklist.md` with screenshots (dark theme, mono font, card look).
**Then -> auto-start Phase 3.**

### Phase 3 — Personalization (1 week)
Overrides + Homework + statuses §4. Original kept, scope respected.
**Verify:** Rename survives "Refresh schedule", homework `N=2` due = 2nd next occurrence, gray->bold transition works, `mark done` works.
**Then -> auto-start Phase 4.**

### Phase 4 — Intersections + Notifications (1 week)
5 colors, strictness slider, 2 toast times.
**Verify:** Add O3314 as friend -> icons appear correctly at threshold 0 and 100. Toast fires at both configured times with renamed text (log in `data/runs/`).
**Then -> auto-start Phase 5.**

### Phase 5 — Sync (3 days)
Export/import JSON + QR.
**Verify:** Two machines on same Wi-Fi import same `overrides+homework` correctly, no server used.
**Then -> auto-start Phase 6.**

### Phase 6 — Android Port (when MVP approved)
Port logic (Kotlin/Flutter), adapt notifications, build APK.
**Verify:** APK installs, Tomorrow table + homework + friend icons + 2 notifications work offline. Parity same as Windows.

**MVP Done criterion:** Clean Windows launch, O3313 Tomorrow correct for both parities, rename+homework persist, 2 notifications fire, friend icons work at both strictness extremes.

---

## 11. What NOT To Do

- Do not require cloud/server.
- Do not code Android before Windows MVP is verified.
- Do not lose overrides on site refresh.
- Do not use bright palette outside Charon2.
- Do not over-engineer cross-platform at Phase 1 — simplicity first.
- Do not wait for user between phases — auto-advance.

---

## 12. Open Questions (ask only if blocked)

- Exact lesson time format on site (if parser finds nothing).
- Max N for deferred homework (currently 10).
- Whether semester calendar (holidays) is needed or always Mon-Sat.
- Whether room/teacher should also be editable like subject (currently subject only).

---

*Prompt v2.0, 2026-09-01. English version with auto-advance. Based on user clarifications. Next step — hand to agent starting at Phase 0.*
