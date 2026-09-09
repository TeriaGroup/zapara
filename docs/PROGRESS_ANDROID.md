# PROGRESS_ANDROID — ЗАПАРА (Kotlin + Compose, minSdk 26, branch `android`)

## Phase A0 — Recon (2026-09-03)

**Status:** DONE

### Verification Checklist
- [x] Live endpoints re-measured (group/lecturer XML, openmap, studs/lect.js, XSL) — all HTTP 200
- [x] Counts match Windows recon exactly (420 groups / 11 357 lessons / 3313 = 31 = 18+13 / 718 lecturers / 8 265 lessons / 9 maps)
- [x] Parity probe 5 dates match `ParityService` (09-01→1 odd, 09-03→1 odd, 09-08→2 even)
- [x] `docs/API_ANDROID.md` written (7 sections)

### Raw numbers
- `TimetableGroup50.xml` 5 649 721b, `Last-Modified Fri 28 Aug 2026 10:38:55 GMT`, UTF-8 no BOM
- `TimetableLecturer50.xml` 4 413 327b, `Last-Modified Fri 28 Aug 2026 10:40:22 GMT`
- `openmap.html` 1 916 908b; 9 full JPGs byte-identical to `src/Vograph/maps/` (132 896 … 76 738, 12 Feb 2025)
- `studs.js` 7 448b, `lect.js` 8 024b, `TimetableGroup.xsl` 5 169b — unchanged
- Parity: 09-01 Tue w1 odd / 09-03 Thu w1 odd / 09-04 Fri w1 odd / 09-08 Tue w2 even / 09-15 Tue w3 odd

### Logs
- Fetch via `curl.exe -sS` with `-D` headers, `HEAD` via `curl -sI` for maps
- Parse via regex counts on UTF-8 bytes (`parse.py`), parity via `probe.py` (ceil-days formula)
- Artifacts in Temp only (`%LocalAppData%\Temp\opencode\a0\`), not committed

### Next
- Auto-advance to Phase A1: scaffold `android/` + data layer + unit tests

## Phase A1 — Data layer (2026-09-03)

**Status:** DONE

### Verification Checklist
- [x] Gradle scaffold builds: `:app:testDebugUnitTest` green + `:app:assembleDebug` APK
- [x] 25 unit tests pass (parity 5 dates, fixture parse, intersection, maps, next-pair)
- [x] Counts match Windows recon (fixture mirrors real schema; live-file counts verified in A0)

### Raw numbers
- Toolchain: JDK Temurin 17.0.20.1 (`C:\Android\jdk17`), Gradle 8.7 (`C:\Android\gradle-8.7` + wrapper), SDK `C:\Android\Sdk` (platform-tools 37.0.1, android-34, build-tools 34.0.0), AGP 8.5.2, Kotlin 2.0.21, Compose BOM 2024.06.00, Room 2.6.1 + KSP 2.0.21-1.0.25
- Config: `ru.bgtu_voenmeh.zapara`, minSdk 26, target/compile 34, versionCode 3 / 1.2
- Tests: ParityTest 4 / GroupParserTest 4 / LecturerParserTest 2 / IntersectionTest 4 / MapResolveTest 6 / ScheduleTest 5 = 25, failures 0, errors 0
- Fixture asserts: 2 groups, 3313 = 7 lessons (odd 5 / even 2), `09:00`→`10:35`, `493;`→ГК / `563*;`→УЛК / `ВЦ 280;`→ВЦ / `дистанционно`, lecturer 1287 = 2 lessons + groups `3313/А863С`
- Next-pair on fixture: subject from Wed 09-02 → Mon 09-07; teacher Волченкова → Wed 09-09
- `app-debug.apk` 8 838 225b (debug, Compose unshrunk)

### Logs
- `app/build/test-results/testDebugUnitTest/TEST-*.xml` (6 files, all green)
- DOM parsing via `javax.xml` (works in JVM tests AND on device — `XmlPullParser` avoided: android.jar stubs throw in unit tests)
- `local.properties` (sdk.dir) gitignored; toolchain paths in this file

### Next
- Auto-advance to Phase A2: schedule UI (tabs, dark theme, responsive table, summary)

## Phase A2 — Schedule UI (2026-09-03)

**Status:** DONE

### Verification Checklist
- [x] Tomorrow tab (smart start → Fri 09-04, Thu lessons over) shows 3 rows matching raw XML
- [x] Week odd view matches raw XML (Mon 4 / Tue 4 / Wed 4 / Thu 2 shown)
- [x] Summary odd = 18, byDay exact, byType exact, counts left (per user rule)
- [x] Dark Charon theme + mono font, `ЗАПАРА` header, group dropdown `А863С`
- [x] Screenshots `docs/a2_tomorrow.png`, `docs/a2_week.png`, `docs/a2_summary.png`

### Raw numbers
- Emulator: Pixel 7 AVD, android-34 google_apis x86_64, headless swiftshader; app `ru.bgtu_voenmeh.zapara` installed + launched
- Smart start: Thu 09-03 17:39 local > last lesson end 16:30+15m → Tomorrow Fri 09-04 odd
- Fri odd rows: `09:00 [лек] ВВЕД. В СПЕЦ / Саваровский / 313; / next 18.09` · `10:50 [пр] ОСН. ГОС. УПР. / Хомелева / 526*; / 18.09` · `12:40 [лаб] ФИЗИКА / Попова / 563*; / next 11.09` — all match `TimetableGroup50.xml` dow=5 wc=1 (ASCII dump: 5 lessons total, odd 3)
- Week odd: Mon `09:00 493; / 10:50 — / 12:40 563*; / 14:55 502*;` · Tue `10:50 526*; / 12:40 401*; / 14:55 312*; / 16:45 526*;` · Wed `09:00 526*; / 10:50 — / 12:40 323*; / 14:55 564*;` · Thu `09:00 564*; / 10:50 ВЦ 282;` — match
- Summary: odd 18 (`Пн 4 · Вт 4 · Ср 4 · Чт 2 · Пт 3 · Сб 1`), `пр 9 · лек 8 · лаб 1`
- `app-debug.apk` 10 664 358b
- Note: initial Friday-row confusion resolved — garbled console misattributed Wed `лаб ФИЗИКА 323*;` to Fri; ASCII dump confirmed app is correct

### Logs
- Verification via `adb shell uiautomator dump` text (screenshots unreadable in this console, PNGs kept in `docs/`)
- Room on IO dispatcher only (main-thread access refactored: `nextMap`/`allLessons` precomputed in `render()`)
- Fixes during phase: ExposedDropdownMenu API, Typography monospace mapping, ExposedDropdownMenuBox params

### Next
- Auto-advance to Phase A3: personalization (overrides/homework) + friend traffic lights

## Phase A3 — Personalization + traffic lights (2026-09-03)

**Status:** DONE

### Verification Checklist
- [x] JVM: OverrideService global>weekday, Homework N=2 due + 5 statuses, CRUD — green
- [x] Device: override/homework/traffic/friends-dialog render (instrumented, text-tree asserts)
- [x] All 5 traffic gradations in code (100/75/50/25/off-dimmed) + member names + always-show mode

### Raw numbers
- JVM tests: 33 total (25 A1 + Override 1 + Homework 3 + ... = 29 unit + ... ) — 0 failures
  - Precisely: Parity 4, GroupParser 4, LecturerParser 2, Intersection 4, MapResolve 6, Schedule 5, Override 1, Homework 3 = 29 unit tests green
  - Homework N=2 for `лек ВЫСШ. МАТЕМАТ` from 2026-09-01 → due 2026-09-14 (norm includes type prefix; Wed 09-09 even is `пр`, not counted)
  - Statuses verified: burning_urgent/burning/approaching/overdue/done/far
- Instrumented `ScheduleFlowTest` 2/2 green on Pixel 7 AVD (seed 2 groups/12 lessons/1 override/1 hw/1 friend):
  - `[лек] МАТАН` + original line, `прочитать §5` block, `- 09С31 (Иван)` traffic, `Друзья (до 5)` dialog + `Всегда все светофоры` toggle
- Room v2 `MIGRATION_1_2` (overrides/homework/settings columns); `networkEnabled` test hook in repository
- Test-infra lessons (emulator): `XmlPullParser` unusable in JVM tests → DOM via `javax.xml`; main-thread Room crashes → IO-only access + precomputed maps; compose finders see stale snapshots → tree-dump polling asserts; rule launches before `@Before` → `@BeforeClass` flag + per-test reseed + `reload()`

### Logs
- `app/build/test-results/testDebugUnitTest/TEST-*.xml` (8 files green)
- `adb logcat ZaparaTest`: seed counts + `tree n=39 :: ЗАПАРА ## Группа А863С ... ## [лек] МАТАН ## ... ## - 09С31 (Иван) ## ... ## прочитать §5 ...`
- `app-debug.apk` ~10.7 MB

### Next
- Auto-advance to Phase A4: offline maps + teacher finder

## Phase A4 — Offline maps + teacher finder (2026-09-03)

**Status:** DONE

### Verification Checklist
- [x] Maps offline (airplane-mode style): 9 bundled JPGs → `filesDir/maps` cache, no network required
- [x] Teacher finder lists bundled `TimetableLecturer50.xml` (only-mine finds `Барт` via 3313 lessons)

### Raw numbers
- Assets: `app/src/main/assets/maps/` 9 JPGs (132 896 … 76 738, 2025-02-12) + `maps/coords.json` 1 197b + `TimetableLecturer50.xml` 4 413 327b (bundled; also cached to `filesDir`)
- Stores: `MapStore` (asset→cache copy, coords JSON) + `LecturerStore` (DOM parse, 718 lecturers / 8 265 lessons, `myTeacherIds` via surname match, `search`/`lessonsFor`)
- Map viewer: pinch/zoom (`transformable` 0.4–4×), `MapCard` picker + fullscreen `Dialog`
- Teacher dialog: `TeacherDialog` (search + `Только мои` + list + details grouped by discipline)
- Tests: `MapTeacherTest` 2/2 green on AVD (offline `maps/` cache check + `КОРПУС` map title + `Барт` found)

### Logs
- `MapTeacherTest` + `ScheduleFlowTest` 4/4 green (was 1 failure before fix: row button text `◉` vs `?` encoding + `UiDevice` injection flakiness → `ViewModel.showMapFor` + `waitUntil` + `treeTexts`)
- `app-debug.apk` ~10.6 MB
- Previous A4 failure analysis kept in logs: `mapbtn tree n=21` (no rows) → `forEachIndexed` vs `itemsIndexed` + `fillMaxSize` height 0, `шапка` vs `предмет` encoding, `LazyColumn` vs `Column`

### Next
- Auto-advance to Phase A5: notifications + LAN sync with Windows

## Phase A3 — Personalization + traffic lights (2026-09-03)

**Status:** DONE

### Verification Checklist
- [x] JVM: OverrideService global>weekday, Homework N=2 due + 5 statuses, CRUD — green
- [x] Device: override/homework/traffic/friends-dialog render (instrumented, text-tree asserts)
- [x] All 5 traffic gradations in code (100/75/50/25/off-dimmed) + member names + always-show mode

### Raw numbers
- JVM tests: 33 total (25 A1 + Override 1 + Homework 3 + ... = 29 unit + ... ) — 0 failures
  - Precisely: Parity 4, GroupParser 4, LecturerParser 2, Intersection 4, MapResolve 6, Schedule 5, Override 1, Homework 3 = 29 unit tests green
  - Homework N=2 for `лек ВЫСШ. МАТЕМАТ` from 2026-09-01 → due 2026-09-14 (norm includes type prefix; Wed 09-09 even is `пр`, not counted)
  - Statuses verified: burning_urgent/burning/approaching/overdue/done/far
- Instrumented `ScheduleFlowTest` 2/2 green on Pixel 7 AVD (seed 2 groups/12 lessons/1 override/1 hw/1 friend):
  - `[лек] МАТАН` + original line, `прочитать §5` block, `- 09С31 (Иван)` traffic, `Друзья (до 5)` dialog + `Всегда все светофоры` toggle
- Room v2 `MIGRATION_1_2` (overrides/homework/settings columns); `networkEnabled` test hook in repository
- Test-infra lessons (emulator): `XmlPullParser` unusable in JVM tests → DOM via `javax.xml`; main-thread Room crashes → IO-only access + precomputed maps; compose finders see stale snapshots → tree-dump polling asserts; rule launches before `@Before` → `@BeforeClass` flag + per-test reseed + `reload()`

### Logs
- `app/build/test-results/testDebugUnitTest/TEST-*.xml` (8 files green)
- `adb logcat ZaparaTest`: seed counts + `tree n=39 :: ЗАПАРА ## Группа А863С ... ## [лек] МАТАН ## ... ## - 09С31 (Иван) ## ... ## прочитать §5 ...`
- `app-debug.apk` ~10.7 MB

### Next
- Auto-advance to Phase A4: offline maps + teacher finder

## Phase A6 — UI 2.0, дизайн-система «Kimi-минимализм» (2026-09-09)

**Status:** DONE — Compose-клиент на монохромных токенах Dark/Light, Inter, нижней навигации (Расписание · Карты · Домашка · Разделы), экранах-разделах и reduce motion; package `ru.zapara.app`, versionName 2.0.0 / versionCode 24; виджеты «Расписание» и «Домашка»

### Verification Checklist
- [x] Токены / только русский / LocalMotion (`TokensParityTest`, `GuardsTest`, `MotionGuardTest`) — JVM green
- [x] Разделы Compose 2.0 + bottom nav; Room-миграции без wipe `MIGRATION_1_2`…`MIGRATION_4_5` (схема v5)
- [x] Виджеты расписания и домашки (JVM composers/job + instrumented clear A→B)
- [x] `:app:testGithubDebugUnitTest` green в сессии 2026-09-09

### Raw numbers
- JVM `testGithubDebugUnitTest`: 180 tests, 0 failures, 0 errors (51 suite XML в `android/app/build/test-results/testGithubDebugUnitTest/`, штамп 2026-09-09)
- Виджеты: 17 JVM widget + Guards в отчёте widgets; instrumented `WidgetRemoteViewsTest` 1/1 на emulator-5554
- Room schemas: `3.json` / `4.json` / `5.json`; AutoUpdate `CURRENT_TAG = android-v2.0.0`
- Размеры APK в этой записи не фиксировались
