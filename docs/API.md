# Vograph — Timetable API Recon (Phase 0)

**Date:** 2026-09-01  
**Source:** https://voenmeh.ru/obrazovanie/timetables/  
**Reference anchor:** `#3313` → `IdGroup=3313` (current semester: group **А863С**, see §7 note)

---

## 1. Overview

The site does **not** expose a JSON/REST API. The timetable is delivered as **static XML + XSLT** transformed client-side.

- HTML page `https://voenmeh.ru/obrazovanie/timetables/` contains a placeholder `<div id="studsTimetableresult">` and a `<select id="studsCbxGroupNumber">`.
- On load, `studs.js` fetches two static resources via **synchronous XHR** `GET` and populates the `<select>`, then transforms via `XSLTProcessor` when user clicks "Показать".

No `wp-json`, no `admin-ajax.php` is used for timetable data. All parsing must be resilient to layout changes — cache `raw` XML.

Directory listing (Apache) is open:

```
https://voenmeh.ru/wp-content/themes/Avada-Child-Theme-Voenmeh/_voenmeh_grafics/
  TimetableGroup50.xml   — 5.4 MB, 2026-08-28 13:38, 420 groups
  TimetableGroup.xsl     — 5.0 KB,  2025-02-12 11:53
  TimetableLecturer50.xml — 4.2 MB (lecturer view, not needed for MVP)
  TimetableLecturer.xsl
  studs.js               — 7.3 KB
  lect.js                — 7.8 KB
  archive*.tar.gz        — historic semesters (UTF-16LE, BOM FF FE)
```

---

## 2. Primary Endpoints

| Purpose | URL | Method | Content-Type | Notes |
|---------|-----|--------|--------------|-------|
| Student timetable (current) | `https://voenmeh.ru/wp-content/themes/Avada-Child-Theme-Voenmeh/_voenmeh_grafics/TimetableGroup50.xml` | GET | `application/xml; charset=utf-8` | Cache `Last-Modified: 2026-08-28`. Poll every 1-3 days. |
| Student XSL | `https://voenmeh.ru/wp-content/themes/Avada-Child-Theme-Voenmeh/_voenmeh_grafics/TimetableGroup.xsl` | GET | `application/xslt+xml` | Used only to understand rendering; parser should read XML directly. |
| Student JS logic | `https://voenmeh.ru/wp-content/themes/Avada-Child-Theme-Voenmeh/_voenmeh_grafics/studs.js` | GET | `text/javascript` | Contains parity logic (`studs_GetWeekCode`). |
| Lecturer timetable | `.../TimetableLecturer50.xml` | GET | `application/xml` | Same shape but `Lecturer` root; ignore for Phase 1. |
| Page shell | `https://voenmeh.ru/obrazovanie/timetables/` | GET | `text/html` | 1.8 MB, WordPress + Avada theme. |

No auth, no CORS issues via local fetch. All GETs are synchronous in original JS (`open("GET", url, false)`).

---

## 3. XML Schema (`TimetableGroup50.xml`)

```xml
<Timetable>
  <Period Title="ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г." StartYear="2026" StartMonth="9" StartDay="1" />
  <Weeks WeekCount="2" />
  <Group Number="А863С" IdGroup="3313">
    <Days>
      <Day Title="Понедельник">
        <GroupLessons>
          <Lesson>
            <DayTitle>Понедельник</DayTitle>
            <WeekCode>1</WeekCode>              <!-- 1 = odd (нечетная), 2 = even (четная) -->
            <Time>9:00 Нечетная</Time>          <!-- "HH:mm Четная/Нечетная" or "HH:mm" -->
            <Discipline>лек ВЫСШ. МАТЕМАТ</Discipline>  <!-- "<type> <subject>" type: лек/пр/лаб -->
            <Lecturers>
              <Lecturer>
                <IdLecturer>1287</IdLecturer>
                <ShortName>Барт Е.Л.</ShortName>
              </Lecturer>
            </Lecturers>
            <Classroom>493; </Classroom>        <!-- room + building, "*" = main building? "ВЦ 282" = building ВЦ -->
          </Lesson>
          <!-- ... more Lessons ... -->
        </GroupLessons>
      </Day>
      <!-- Tuesday ... Saturday (6 days, Mon-Sat) -->
    </Days>
  </Group>
  <!-- 420 Groups total -->
</Timetable>
```

### Field Details

| Node | Type | Example | Notes |
|------|------|---------|-------|
| `Timetable/Period@Title` | string | `ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г.` | Human title, used in header. |
| `Period@StartYear/Month/Day` | int | `2026/9/1` | Semester start. Week 1 = week containing Sep 1. |
| `Weeks@WeekCount` | int | `2` | Always 2 (odd/even). Could change? |
| `Group@Number` | string | `А863С` | Cyrillic letter + digits + Cyrillic letter. Example `09С31` = numeric faculty. |
| `Group@IdGroup` | string/int | `3313` | Stable internal ID. Used as URL hash `#3313`. Group picker `<option value="IdGroup">Number</option>`. |
| `Day@Title` | string | `Понедельник` | Cyrillic weekday, Mon-Sat (6). |
| `Lesson/WeekCode` | enum `1\|2` | `1` | `1` = нечетная (odd), `2` = четная (even). `Time` suffix duplicates this. If `WorkCount=2`, no `0=both`; each lesson duplicated per week. |
| `Lesson/Time` | string | `9:00 Нечетная` | Format `H:mm` or `HH:mm` + space + `Нечетная`/`Четная`. Some rooms have no suffix for common lessons. Parse via regex `^(\d{1,2}:\d{2})` → `timeStart`. No `timeEnd` in source; must map to standard slot table or infer +95 min. |
| `Lesson/Discipline` | string | `лек ВЫСШ. МАТЕМАТ` | Prefix `лек` (lecture), `пр` (practical), `лаб` (lab), `конс` etc. Subject raw = trim, remainder. TypeRaw = prefix. |
| `Lecturer/ShortName` | string | `Барт Е.Л.` | May be multiple per lesson. Empty `<Lecturers/>` → no teacher. |
| `Lesson/Classroom` | string | `493;` `563*;` `ВЦ 282;` `дистанционно` | Contains room and building. `*` marks main building. `ВЦ` = computing center, `СЭК` etc. Empty string → no room (e.g., физ-ра). Semi-colon separated if multiple. No separate `Building` field; parser must split: `roomRaw = before ";"`, `buildingRaw = extract prefix letters`. |

**No dedicated fields for:** `Building`, `Type` separate, `TimeEnd`. Must derive.

**Total groups:** 420 in current file (2026-08-28). Historic archives contain similar counts but UTF-16LE encoded.

---

## 4. Parity Logic (Week Odd/Even)

From `studs.js:4-15`:

```js
var studs_startDate;
var studs_weekCount;

function studs_GetWeekCode(start, today) {
    var difference = today.getTime() - start.getTime();
    if (difference < 1) difference = 1;
    difference = Math.ceil(difference / (1000 * 60 * 60 * 24));
    var weekCode = (Math.ceil(difference / 7)) % studs_weekCount;
    if (0 == weekCode) weekCode = studs_weekCount;
    return weekCode; // 1..WeekCount
}

// init:
studs_startDate = new Date(StartYear, StartMonth-1, StartDay, 0,0,0,0);
var dow = studs_startDate.getDay(); if (0==dow) dow=7; dow-=1;
studs_startDate.setTime(studs_startDate.getTime() - dow*86400000);
studs_startDate.setHours(0,0,0,0);
studs_weekCount = Weeks@WeekCount; // 2
studs_style = fetch(XSL);
...
xsltProcessor.setParameter(null, "paramCurrentWeekCode", studs_GetWeekCode(studs_startDate, new Date()));
```

**Interpretation:**

- `StartDate` = `YYYY-MM-DD` from `Period`.
- Align to **Monday** of that week: subtract `(dow-1)` days where Monday=1, Sunday=7.
- For date `D`, `days = ceil((D - startMonday)/86400000)`, `weekIndex = ceil(days/7) % WeekCount`, with 0 → WeekCount.
- Result `1` = odd (нечетная), `2` = even (четная). Week containing Sep 1 = week 1 = odd. Matches spec §2 `isOddWeek`.

**Manual `invertParity` bool** in settings toggles `isOdd = !isOdd` if university shifts.

**Spec parity formula for MVP:**

```csharp
bool IsOddWeek(DateTime date, bool invert) {
    var start = new DateTime(periodYear, periodMonth, periodDay);
    int dow = (int)start.DayOfWeek; if (dow==0) dow=7;
    var monday = start.AddDays(-(dow-1)).Date;
    int days = (int)Math.Ceiling((date.Date - monday).TotalDays);
    if (days < 1) days = 1;
    int weekCode = (int)Math.Ceiling(days / 7.0) % weekCount;
    if (weekCode==0) weekCode = weekCount;
    bool isOdd = weekCode==1;
    return invert ? !isOdd : isOdd;
}
```

`WeekCount` from XML, fallback `2`.

Edge: before semester start (< monday), `difference` forced to 1 → weekCode 1 (show odd warning). JS shows banner: "Обратите внимание! Семестр начинается с нечетной недели!" when `paramCurrentWeekCode==0` (not yet started).

---

## 5. XSL Rendering (for reference)

`TimetableGroup.xsl` transforms:

- Header: `Расписание занятий группы {Number} на {Period Title}`
- Note table: highlights current week row (`class="timetable_table_current_row"`)
- For each `Group` where `@IdGroup == $paramGroupId`, for each `Day`:
  - Table per day with title `Day@Title`
  - Header: `Время | Дисциплина | Преподаватель | Аудитория` (4 columns, original HTML lacks `No.` and `Type` separation)
  - For each `Lesson` where `not($paramCurrentWeekOnly) or WeekCode==paramCurrentWeekCode`:
    - Row class `timetable_table_current_row` if `WeekCode==paramCurrentWeekCode` else `timetable_table_row`
    - Cells: `Time`, `Discipline`, `Lecturers/ShortName;`, `Classroom`

No JS post-processing. `paramCurrentWeekOnly` bound to checkbox `studsCbCurrentWeekOnly` ("Только текущая неделя").

---

## 6. Caching & Resilience + Auto-refresh (v3)

- **Cache raw XML** to `vograph.db` or file `schedule_cache`. Store fetch timestamp.
- On parse failure (XML malformed, network 404), show last cache + "stale" badge.
- Parser must be XSL-independent: read `TimetableGroup50.xml` via `XmlReader`/`XDocument`, not via rendered HTML.
- Encoding: current file UTF-8 without BOM; historic archives UTF-16LE with BOM `FF FE`. Detect BOM, fallback to UTF-8.
- **Auto-refresh (v3, §2):** On app start + background timer every 24h check `TimetableGroup50.xml` via `HEAD` + `If-Modified-Since` / `Last-Modified`. Respect 1-3 day interval, do not poll hourly. If `304 Not Modified` → skip; if `200` and `Last-Modified` newer → fetch full, re-parse, keep `overrides/homework` intact, update `settings.lastFetchedAt` and `settings.lastAutoCheckAt`, log to `data/runs/autorefresh-YYYYMMDD.log`. Show `last updated` badge + `last auto-check` + manual `Refresh now`.
- Keep `raw_html`/`raw_xml` column for diff.

**Settings extension v3:** `settings` now includes `language TEXT DEFAULT 'ru'` (ru|en, per §2 i18n) and `lastAutoCheckAt TEXT`. Migration via `ALTER TABLE ADD COLUMN` TryAddColumn. `I18nService` (`src/Vograph.Core/Services/I18nService.cs`) holds ru/en dictionaries, `T(key)` and `FormatDate/Day/Parity`, `LanguageChanged` event, persists to `settings.language`, switch instantly without restart.

**Bilingual (v3, §6):** All UI strings via `I18nService.T()` / `Resources/ru.xaml`+`en.xaml` (no hardcoded Russian in XAML code-behind). Date/weekday/parity/notifications respect `settings.language`. Reports stay English.

---

## 7. Test Group `O3313` / Anchor `#3313`

**Prompt states:** test group `O3313` / anchor `#3313`.

**Current reality (2026-08-28):**

- No group with `Number="O3313"` (Latin O or Cyrillic О) exists in current `TimetableGroup50.xml`.
- Search for `Number` containing `3313` → 0 hits. Digits extraction `3313` → 0 hits.
- `IdGroup="3313"` **does exist** and maps to **`Number="А863С"`** (Cyrillic А). Hex `d090383633d0a1`.
- HTML anchor `#3313` in `studs.js` (`studs_getHash()` → `location.hash.substring(1)` → `studs_setCbx` matches `option.value == IdGroup`) will select this group.

**Interpretation:** group numbering changed between prompt (2026-09-01) and current semester; `O3313` likely renamed to `А863С` or `O3313` was example. Anchor `#3313` remains valid selector for verification.

**Sample raw extracts for `IdGroup=3313` are committed as:**

- `docs/raw/O3313_raw_Group_3313.xml` — full `<Group>` node (all days, both weeks)
- `docs/raw/O3313_Week_1_odd.xml` — filtered WeekCode=1 (нечетная)
- `docs/raw/O3313_Week_2_even.xml` — filtered WeekCode=2 (четная)

Both weeks contain lessons; e.g., Monday 09:00 `лек ВЫСШ. МАТЕМАТ` in both weeks, but room/building and some subjects differ by week (see samples). These serve as pixel-perfect reference for Phase 1 parser.

**Group details for `А863С` (Id 3313):**

| Day | Week | Time | Discipline | Room | Teacher |
|-----|------|------|------------|------|---------|
| Понедельник | 1 | 9:00 Нечетная | лек ВЫСШ. МАТЕМАТ | 493; | Барт Е.Л. |
| Понедельник | 2 | 9:00 Четная | лек ВЫСШ. МАТЕМАТ | 493; | Барт Е.Л. |
| Понедельник | 1 | 10:50 Нечетная | пр ЭК ПО ФК И СПОРТУ | (empty) | — |
| Понедельник | 1 | 12:40 Нечетная | пр ОСН РОС ГОС | 563*; | Лысенко Е.М. |
| Понедельник | 2 | 12:40 Четная | пр ОСН РОС ГОС | 564*; | Лысенко Е.М. |
| ... | ... | ... | ... | ... | ... |
| Суббота | 1 | 12:40 Нечетная | лек ФК И СПОРТ | дистанционно | Петров А.Б. |

Full dump in `docs/raw/`.

**If tester expects `O3313` literal:** fallback to searching `Number` case-insensitive `O` vs Cyrillic `О` and `ё->е`, or simply select any group via picker; anchor `#3313` will auto-select `А863С` for automated check.

---

## 8. Time Slots

`Time` field only gives start `H:mm`. No end in source. For MVP, assume fixed slots per university standard (95 min + break). Infer `timeEnd = timeStart + 95m` if needed for intersection overlap calc. Classroom `*` likely indicates main building; no explicit floor.

---

## 9. Related Files (for completeness)

- `lect.js` — similar logic for lecturer view (`TimetableLecturer50.xml`): root `<Timetable><Lecturer IdLecturer>...<LecturerLessons><Lesson>...<Groups><Group IdGroup Number>`.
- `template_studs.php` / `template_lect.php` — WordPress templates that embed `studsBlock`.
- Archives `archive*.tar.gz` — contain historic `TimetableGroup50.xml` (UTF-16LE) for diff testing.

---

## 10. Verification Steps Done

- Fetched `TimetableGroup50.xml` (5 649 721 bytes, 2026-08-28) and `TimetableGroup.xsl` (5 169 bytes) via `GET`.
- Parsed with `XDocument`/`ET`, confirmed 420 groups, Period 2026-09-01, WeekCount 2.
- Verified parity logic matches `studs.js` and prompt §2.
- Verified group hash selection via `IdGroup`.
- Saved raw samples for `IdGroup 3313` (both weeks) under `docs/raw/`.

Next: Phase 1 Parser + DB must read these XML files, cache raw, split `Discipline`/`Classroom`, handle encoding BOM, and expose `getSchedule(date)` filtering by `WeekCode` and weekday.

---

## 11. OpenMap — Building Maps (2026-09-01)

**Source:** `https://voenmeh.ru/openmap/` — static page with 9 JPGs (no API, direct HTML `<img>` + “Скачать изображение”).

| Building | Floor | URL | Size |
|----------|-------|-----|------|
| Главный корпус (ГК) | 1 | `https://voenmeh.ru/wp-content/uploads/2024/09/karta-glavnyj-korpus-1-etazh-2022.jpg` | 132896 |
| ГК | 2 | `karta-glavnyj-korpus-2-etazh-2022.jpg` | 121886 |
| ГК | 3 | `karta-glavnyj-korpus-3-etazh-2022.jpg` | 131217 |
| ГК | 4 | `karta-glavnyj-korpus-4-etazh-2022.jpg` | 116501 |
| УЛК | 1 | `karta-ulk.-1-etazh-2022.jpg` | 70269 |
| УЛК | 2 | `karta-ulk.-2-etazh-2022.jpg` | 80570 |
| УЛК | 3 | `karta-ulk.-3-etazh-2022.jpg` | 78130 |
| УЛК | 4 | `karta-ulk.-4-etazh-2022.jpg` | 80726 |
| УЛК | 5 | `karta-ulk.-5-etazh-2022.jpg` | 76738 |

No JSON, no SVG overlay, no room coordinates — floor plans are raster JPGs. Mapping must be by **Classroom → building/floor heuristic** (see §3 `Classroom` parsing, corrected 2026-09-01 per user: “кабинеты со звездочкой — УЛК”):

- `*` → УЛК (e.g. `331*;` `526*;` `507*а;` `270*(фест);`)
- `ВЦ 372*;` → ВЦ (computing center) → show ГК plan with note “ВЦ — показать план ГК” (ВЦ priority before `*` check)
- no `*` and not `ВЦ` → ГК (e.g. `324;` `450;` `313а;` `101;`)
- `дистанционно` → remote, no map
- floor = first digit of room number (e.g. `493` → 4, `270` → 2, `101` → 1), clamp ГК 4 / УЛК 5 (УЛК 5 retains 5th floor, e.g. `526*` → УЛК 5)
- building raw codes like `СК`, etc. fallback to ГК if numeric

Cache: `%LocalAppData%\Vograph\maps\karta-*.jpg` + bundled `publish/maps/` + `src/Vograph.Desktop/Assets/maps/` (`CopyToOutputDirectory`) for offline first launch. `MapService.EnsureCachedAsync` prefers local → bundled → download via `HttpClient` (User-Agent Vograph/1.0).

Next lesson resolution: `GetNextLesson(groupId, now)` scans today remaining (timeStart/timeEnd > now) → tomorrow → next 7 days, respects `ParityService` + `settings.parityInvert`, used for “Куда идти — следующая пара” panel (`MapWhereText`, `MapWhenText`, `MapImage`). Per-lesson `◉` button and context menu “Показать на карте” also calls `MapService.Resolve(classroomRaw)`.

Verified via `src/Vograph.VerifyMap` (2026-09-01, corrected star=УЛК): 14 classroom cases PASS (`331*;`→УЛК3, `324;`→ГК3, `ВЦ 372*;`→ВЦ, `507*а;`→УЛК5, `526*;`→УЛК5), next lesson 3313 `526*;` → УЛК 5 PASS, `EnsureAllMapsCachedAsync` 9/9 cached PASS.

