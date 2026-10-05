# Phase 3 — Personalization Verification

Журнал проверки 2026-09-01…03, не описание текущего клиента. Картина продукта — `docs/STATUS.md`.

## Checks
- [x] Rename survives "Refresh schedule" (global and weekday:3) — DB count 2 → after refresh 2, display "МатАн (переименовано)" retained
- [x] Homework N=2 due = 2nd next occurrence — created 2026-09-02 лек ВЫСШ. МАТЕМАТ N=2 → due 2026-09-14 Monday (2 Mondays later) PASS
- [x] gray->bold transition works
  - far (>1 lesson before due) hidden/dot — not shown in card (filtered)
  - approaching (1 lesson before due) → Foreground MarbleDim #FF6B7280 regular
  - burning (due == tomorrow) → Marble #FFC5CAD3 Bold PanelAlt
  - burning_urgent (due == today) → Bronze #FF6CA5E0 bold + BorderDim highlight
  - Verified via HomeworkService.ComputeStatus: today 2026-09-01 due 2026-09-14 → approaching (gray), due tomorrow → burning (bold)
- [x] mark done works — status done strikethrough Opacity 0.5, DoneAt set, unmark returns to approaching
- [x] Original kept — SubjectRaw never overwritten, display via OverrideService.GetDisplayName, scope respected (global overrides weekday)

## Artifacts
- DB vograph_phase3.db 31 lessons, 2 overrides, 1 homework
- Code: Services/OverrideService.cs (global > weekday), Services/HomeworkService.cs (ComputeDueDate, ComputeStatus, RecomputeAllStatuses)
- UI: Dialogs/RenameDialog.xaml (scope radio, preview, reset), Dialogs/HomeworkDialog.xaml (Text + N 1..10 + due preview)
- MainWindow CreateLessonCard now shows renamed title + original dim + note + homework blocks below row with status colors/borders
- Verification console: Vograph.Verify3 output (see log)
- Screenshots:
  - verify_phase3_rename_global.png (sim)
  - verify_phase3_homework_status.png (sim)
