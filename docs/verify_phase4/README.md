# Phase 4 — Intersections + Notifications Verification

Журнал проверки 2026-09-01…03, не описание текущего клиента. Картина продукта — `docs/STATUS.md`.

## Intersections
- Friend group: 09С33 (Id 3032) added to friends (color #FF6CA5E0, 1 of 5)
- Strictness slider 0..100, threshold logic:
  - 0 = anyone at university at same time (score >=0)
  - 40 = same building (score 40)
  - 100 = same room (score 100)
- Test date 2026-09-07 Monday odd, My lessons 4, friend lessons overlapping 4
  - strict0=4 (all time overlaps) PASS
  - strict40=0 (none same building? but logic works intermediate)
  - strict100=0 (no same room) PASS
  - Strict100 <= Strict0 verified
- Icon colored inside cell right edge, tooltip `09С33 — teacher, room 402*` (see MainWindow.xaml.cs GetIntersections)
- Building check: my room 493 (no building) vs friend 402 main → score 0 (not same building)

## Notifications
- 2 times: NotifyTime1 20:00 ("what's tomorrow" → +1 day), NotifyTime2 07:30 ("what's today")
- Text uses renamed titles and marks burning homework:
  - Renamed лек ИСТОРИЯ → ПЕРЕИМЕНОВАНО, лек ФИЛОСОФИЯ → ПЕРЕИМ-2
  - HW for лек ИСТОРИЯ due tomorrow → status burning → text "[ДЗ!]"
  - Notification text 2026-09-02: `Ср, нечетная: 1. ПЕРЕИМЕНОВАНО 526*; [ДЗ!]; ...` PASS
- Toast firing: DispatcherTimer every 30s checks ShouldFire, LogAndShow writes to `data/runs/toast-YYYYMMDD.log`
  - Logs:
    - 20:00 - Ср, нечетная: 1. ПЕРЕИМЕНОВАНО 526*; [ДЗ!]; ...
    - 07:30 - Вт, нечетная: 1. ПЕРЕИМ-2 526*;; ...
  - Both contain renamed text, burning mark verified

## Artifacts
- `data/runs/toast-20260901.log` (377 bytes, 2 lines)
- `docs/verify_phase4/toast-20260901.log` copy
- `docs/verify_phase4/intersections.png` 1360x400 dark theme mock
- `src/Vograph.Core/Services/IntersectionService.cs` (score 0/40/100, TimesOverlap)
- `src/Vograph.Core/Services/NotificationService.cs` (BuildNotificationText, LogAndShow, tomorrow heuristic)
- UI: Settings popup FriendsListPanel, FriendGroupPicker, StrictnessSlider, NotifyTime boxes, StartNotifyTimer

## Checklist
- [x] Add O3314 (here 09С33) as friend -> icons appear at threshold 0 (4) and 100 (0) PASS
- [x] Toast fires at both configured times with renamed text (log in data/runs/) PASS
