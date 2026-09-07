# ZAPARA Android — API Recon (Phase A0)

**Date:** 2026-09-03
**Branch:** `android` (from `master@c36dda9`)
**Scope:** re-measure every network source the Windows MVP uses, so the Android port has zero guesswork.
**Result:** ALL sources byte-identical to Windows recon (`docs/API.md`, 2026-09-01). No schema drift.

---

## 1. Endpoints (live probe 2026-09-03 14:39 UTC)

| Purpose | URL | Status | Size | Last-Modified | Notes |
|---|---|---|---|---|---|
| Student timetable | `.../_voenmeh_grafics/TimetableGroup50.xml` | 200 `application/xml` | 5 649 721 | Fri, 28 Aug 2026 10:38:55 GMT | UTF-8, no BOM |
| Lecturer timetable | `.../_voenmeh_grafics/TimetableLecturer50.xml` | 200 `application/xml` | 4 413 327 | Fri, 28 Aug 2026 10:40:22 GMT | UTF-8, no BOM |
| Student XSL | `.../TimetableGroup.xsl` | 200 | 5 169 | — | reference only |
| Student JS (parity) | `.../studs.js` | 200 | 7 448 | — | `studs_GetWeekCode` unchanged |
| Lecturer JS | `.../lect.js` | 200 | 8 024 | — | reference only |
| Map page | `https://voenmeh.ru/openmap/` | 200 | 1 916 908 HTML | — | 9 full-size JPGs + WP `srcset` thumbnails |
| Map JPGs (×9) | `https://voenmeh.ru/wp-content/uploads/2024/09/karta-*.jpg` | 200 `image/jpeg` ×9 | see §4 | 12 Feb 2025 | byte-identical to `src/Vograph/maps/` bundle (до удаления WPF-клиента; см. локальный тег `wpf-final`) |

Base: `https://voenmeh.ru/wp-content/themes/Avada-Child-Theme-Voenmeh/_voenmeh_grafics/`.
No JSON API, no auth. Poll via `HEAD If-Modified-Since` (24h), full GET only when `Last-Modified` is newer.

## 2. `TimetableGroup50.xml` (student)

- `Period Title="ОСЕННИЙ СЕМЕСТР 2026/2027 уч. г." Start 2026-09-01`, `Weeks WeekCount=2`.
- **420** `<Group Number=...>` nodes, **11 357** `<Lesson>` nodes total.
- Lesson fields: `DayTitle`, `WeekCode` (1 odd / 2 even), `Time` (`H:mm` + `Нечетная/Четная`), `Discipline` (`лек/пр/лаб ...`), `Lecturers/Lecturer/ShortName`, `Classroom` (`*` = УЛК per 2026-09-01 correction, `ВЦ ...`, `дистанционно`).
- Test group `IdGroup=3313` → `А863С`: **31 lessons (odd 18 / even 13)** — matches Windows DB exactly.
- Time end not in source: derive `+95 min`.

## 3. `TimetableLecturer50.xml` (lecturer)

- **718** `<Lecturer IdLecturer=... LecturerName=... Kafedra=...>` nodes, **8 265** `<Lesson>` nodes.
- Lesson adds `Groups/Group(IdGroup, Number)` — needed for "only mine" filter + own-group highlight.
- Bundled in APK assets + disk cache (4.4 MB). Same file the Windows app ships.

## 4. Maps (`openmap/`)

Full-size URLs only (ignore WP `srcset` `-200x95 ... -1536x730` thumbnails):

| Map | Bytes | Last-Modified |
|---|---|---|
| ГК 1 `karta-glavnyj-korpus-1-etazh-2022.jpg` | 132 896 | 12 Feb 2025 |
| ГК 2 | 121 886 | 12 Feb 2025 |
| ГК 3 | 131 217 | 12 Feb 2025 |
| ГК 4 | 116 501 | 12 Feb 2025 |
| УЛК 1 `karta-ulk.-1-etazh-2022.jpg` | 70 269 | 12 Feb 2025 |
| УЛК 2 | 80 570 | 12 Feb 2025 |
| УЛК 3 | 78 130 | 12 Feb 2025 |
| УЛК 4 | 80 726 | 12 Feb 2025 |
| УЛК 5 | 76 738 | 12 Feb 2025 |

All 9 byte-identical to `src/Vograph/maps/*.jpg` (до удаления WPF-клиента; см. локальный тег `wpf-final`). Total ~0.9 MB — safe to bundle in APK.
`coords.json` (room rects 0..1) ships alongside; read-only on phone.

## 5. Parity probe (formula ported 1:1 from `ParityService`)

Monday-align `periodStart`, `days=ceil(date-monday)` min 1, `weekNumber=ceil(days/7)`, `code=weekNumber%2` (0→2):

| Date | Weekday | weekNumber | Code | Parity |
|---|---|---|---|---|
| 2026-09-01 | Tue | 1 | 1 | odd |
| 2026-09-03 | Thu | 1 | 1 | odd |
| 2026-09-04 | Fri | 1 | 1 | odd |
| 2026-09-08 | Tue | 2 | 2 | even |
| 2026-09-15 | Tue | 3 | 1 | odd |

## 6. Diffs vs Windows recon

None. Same bytes, same counts, same schema. Android reuses: parity formula, `*`=УЛК rule, score gradations 100/75/50/25/off, next-pair scan (date+1..+60, skip Sunday), sync v1 JSON shape (accept legacy `vograph-sync-*.json` on import).

## 7. Artifacts (NOT committed, Temp only)

`%LocalAppData%\Temp\opencode\a0\`: `TimetableGroup50.xml`, `TimetableLecturer50.xml`, `openmap.html`, `studs.js`, `lect.js`, `TimetableGroup.xsl`, `parse.py`, `probe.py`.
