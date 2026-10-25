---
title: 'Config: sekcja hid w input.json dla przycisku asystenta'
type: 'feature'
ticket: '15'
created: '2026-10-25'
status: done
baseline_revision: '95fde51f3370554c66798acbc481188155231f65'
route: 'oneshot'
route_source: 'auto'
risk: 'low'
review: 'quick'
review_source: 'pinned'
lenses_ran: [quick]
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** Przycisk asystenta G20S Pro (strona `0x0C`, usage `0x00CF`) nie ma VK, więc `input.json` nie potrafi go opisać (AD-8, bug 3.14). Stories 3.16 (polityka) i 3.17 (kalibracja) potrzebują sekcji `hid`.

**Approach:** `InputDevice` dostaje opcjonalne `Hid` (klucz `<page>:<usage>` hex z `0x` → `keyboard`), `CheckInput` waliduje je regułami AD-8, a sample dostaje wpis `"hid": { "0x000C:0x00CF": "keyboard" }` dla `VID_1915&PID_1025`. Brak sekcji = brak wyzwalacza HID. Polityki i kalibracji nie ruszamy.

</intent-contract>

## Implementation Notes

Oneshot (~80 linii, 4 pliki): `src/MediaStation.Core/Config/InputConfig.cs` (`Hid` jako `init`, jak `Gamepad`, więc istniejące konstruktory `InputDevice` działają), `ConfigValidation.cs` (`CheckHid`), `tools/samples/config/input.json`, `ConfigValidationTests.cs`. Odrzucane: `hid` na urządzeniu innym niż `remote` (w tym `*`), wartość inna niż `keyboard`, usage `0x0223`/`0x0224`, strona inna niż `0x000C`, zdublowany klucz (porównanie numeryczne), drugi klucz z tą samą Action w urządzeniu; do tego zły format klucza i usage 0 (znacznik zwolnienia). Maszyna: UM760 (MEDIASTATION, F7BSI, EliteMini Series).

## Plan Change Log


## Verification

**Commands:**
- `dotnet test` -- expected: wszystkie testy przechodzą, w tym nowe walidacji `hid` i sample z `0x000C:0x00CF`

**Evidence (UM760; `COMPUTERNAME` MEDIASTATION, board F7BSI, model EliteMini Series):** `dotnet test` → 918/918 passed, 0 failed (28 tests matching `Hid`), including sample `0x000C:0x00CF` loading and rejections of: `hid` on `*` and on non-`remote`, value `ok`, usage `0x0223`/`0x0224`/`0x0000`, page `0x0001`, malformed key, duplicate key (textual, case-different and `0xC:0xCF` width-different), second key with the same Action.

## Review Triage Log

### 2026-10-25 — Review pass
- verdicts: 3 findings — high 0, medium 0, low 3, false 0, maybe-false 0
- findings:
  - `[low]` `patch` Plan has no verification evidence or machine check — evidence and machine identification added to the plan (Verification section).
  - `[low]` `patch` Missing tests: textual duplicate key and `0xC:0xCF` width-different duplicate — both added to `ConfigValidationTests.cs`, pass.
  - `[low]` `reject` No test for `*` with `source: remote` — that combination already errors on the signature rule; `hid` check is covered by `*`/`keyboard`; no extra logic would be tested.

## Auto Run Result

Status: built

- Summary: `InputDevice.Hid` (optional, `init`) plus `CheckHid` validation per AD-8 and sample entry `"hid": { "0x000C:0x00CF": "keyboard" }` for `VID_1915&PID_1025`. Missing section = no HID trigger.
- Files: `src/MediaStation.Core/Config/InputConfig.cs` (Hid property), `src/MediaStation.Core/Config/ConfigValidation.cs` (CheckHid), `tools/samples/config/input.json` (sample), `tests/MediaStation.Core.Tests/ConfigValidationTests.cs` (tests).
- Review: 2 patches applied, 0 deferred, 1 rejected (reason above).
- Follow-up review recommended: false.
- Verification: `dotnet test` 918/918 on UM760. Physical G20S Pro: not applicable per ticket (whole-epic acceptance by the user).
- Residual risks: none known; usage 0 is also rejected (release marker) beyond the ticket's list.
