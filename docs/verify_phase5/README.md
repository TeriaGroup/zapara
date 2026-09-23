# Phase 5 — Sync Verification

Журнал проверки 2026-09-01, не описание текущего клиента. Телефон этот обмен по локальной сети больше не принимает. Картина продукта — `docs/STATUS.md`.

## Checks
- [x] Export: `vograph-sync-YYYYMMDD.json` contains `overrides + homework + friends + settings` Version:1 — file 2118 bytes (see `vograph-sync-20260901.json`)
- [x] Import: merge by key, lastWriteWins, no server used — Machine A (2 ov, 2 hw, 1 friend, strictness 73) → export → Machine B import → B has same 2/2/1 PASS
- [x] Conflict: lastWriteWins — B newer override "МатАн B NEWER" imported to A wins PASS
- [x] QR: small JSON (<1500) encodes directly, large (>1500) encodes `http://<ip>:8765/sync#token` (local IP 26.184.88.94) — `qr.png` 632 bytes
- [x] HTTP optional: `SyncHost` on 8765 (GET /sync returns JSON, POST merges), Join via `http://ip:8765/sync` — code in SyncService.cs, not required for file test

## Artifacts
- `vograph-sync-20260901.json` 2118 bytes version 1
- `qr.png` 632 bytes (QR for http URL)
- `sync_log.txt` 63 bytes
- `SyncService.cs`  ExportToJson/ExportToFile/ImportFromJson/ImportFromFile/SaveQrImage/GetLocalIp/SyncHost/JoinViaHttp

## Logs
- A overrides 2 hw 2 fr 1 → B after import 2/2/1 PASS
- ExportedAt 2026-08-31, Version 1
- No cloud/server, LAN file only
