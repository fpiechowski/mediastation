---
title: 'Kalibracja: przycisk Klawiatura także bez VK, zapis do hid'
type: 'feature'
ticket: '17'
created: '2026-10-25'
status: done
baseline_revision: 'f4b931b97f968e78ddf87d98b3d056c5f9283029'
route: 'oneshot'
route_source: 'auto'
risk: 'medium'
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

**Problem:** Kalibracja loguje `rawInput` z `hasVk`, ale nie potrafi zapisać wybranego przycisku Klawiatura: nie ma logiki zapisu do `input.json`, a przycisk bez VK (asystent `0x00CF`) nie ma gdzie trafić poza sekcją `hid` (AD-8, bug 3.14; stories 3.15–3.16 dały format i politykę).

**Approach:** Czysta funkcja Core zamienia wybrany wpis `RawInputEntry` i tekst `input.json` w nowy tekst: `hasVk` → `keys["0xNN"] = "keyboard"`, bez VK → `hid["0xPPPP:0xUUUU"] = "keyboard"` w urządzeniu `remote` o sygnaturze wpisu; wynik przechodzi walidację configu. Wpis `rawInput` i jego pola bez zmian (usage logowany tylko w tym trybie, AD-13).

</intent-contract>

## Implementation Notes

Oneshot (~90 linii kodu, 1 nowy plik Core + testy): `src/MediaStation.Core/Input/KeyboardButtonMapping.cs` (`Apply(inputJson, entry)` → `KeyboardMappingResult(Text, Error)`, JsonNode, format kluczy `0xNN` / `0x000C:0x00CF`, walidacja przez `ConfigValidation.Validate`, błędy pliku `input.json`), `tests/MediaStation.Core.Tests/KeyboardButtonMappingTests.cs`. Wybór przycisku w trybie bez UI nie jest częścią ticketu (verify to testy Core), więc hub, `LogRawInputSink` i `CompositionRoot` nie są ruszane. Maszyna: UM760 (MEDIASTATION, F7BSI, EliteMini Series).

## Plan Change Log

## Review Triage Log

### 2026-10-25 — Review pass
- verdicts: 5 findings — high 0, medium 0, low 3, false 2, maybe-false 0
- findings:
  - `[low]` `patch` `Apply` rzucało `InvalidOperationException` na źle otypowanym JSON (korzeń tablicą, nietekstowa sygnatura/Action) — `Apply` łapie je i zwraca `Error`; test `Wrongly_typed_json_is_refused_not_thrown` (3 przypadki).
  - `[low]` `reject` Zastępowanie poprzedniego Keyboard tylko w obrębie sekcji — AD-8 dopuszcza wyzwalacz HID obok VK, a ticket każe pisać według typu przycisku; czyszczenie drugiej sekcji byłoby poza zakresem.
  - `[low]` `reject` Klucze o innym zapisie (`0x0d`, `0xC:0xCF`) omijają idempotencję — walidacja odmawia głośno („powtarza”), nic nie jest psute; poprawka to normalizacja kluczy (więcej złożoności).
  - `[false]` `reject` Dwa urządzenia `remote` o tej samej sygnaturze — walidacja configu wymaga unikalnej sygnatury, więc taki plik nie jest poprawnym wejściem.
  - `[false]` `reject` `WriteIndented` zmienia układ pliku — plik nie ma komentarzy, a parsowany wynik zachowuje wszystkie wartości (test `Written_file_passes_validation_and_keeps_other_mappings`); ticket nie wymaga zachowania formatowania.

## Verification

**Commands:**
- `dotnet test` -- expected: wszystko przechodzi, w tym `KeyboardButtonMappingTests` (bez VK → `hid`, z VK → `keys`, plik przechodzi walidację 3.15, polityka 3.16 daje `Keyboard`/`remote`)

## Auto Run Result

Status: built

- Summary: `KeyboardButtonMapping.Apply(inputJson, RawInputEntry)` (Core, czysta funkcja): przycisk z `hasVk` zapisuje `keys["0xNN"]="keyboard"`, bez VK `hid["0x000C:0x00CF"]="keyboard"` w urządzeniu `remote` o sygnaturze wpisu; wynik przechodzi `ConfigValidation` (reguły 3.15), poprzedni Keyboard w tej sekcji jest zastępowany, błędy wracają jako `Error`. `rawInput`, `LogRawInputSink` i tryb główny bez zmian.
- Files: `src/MediaStation.Core/Input/KeyboardButtonMapping.cs` (nowy), `tests/MediaStation.Core.Tests/KeyboardButtonMappingTests.cs` (nowy, 15 testów).
- Review: 1 patch applied (low), 0 deferred, 4 rejected (reasons in the triage log).
- Follow-up review recommended: false.
- Verification (UM760: `COMPUTERNAME` MEDIASTATION, board F7BSI, model EliteMini Series; HEAD f4b931b): `dotnet test` → 948/948 passed, 0 failed (baseline 933); bez VK → wpis w `hid`, z VK → wpis w `keys`, zapisany plik przechodzi walidację, polityka 3.16 z wpisem `hid` daje `Keyboard`/`remote` bez połknięcia. Fizyczny G20S Pro: nie dotyczy (odbiór całej epiki na UM760).
- Residual risks: ticket nie określa, jak autor wybiera przycisk w trybie bez UI, więc `--calibrate-input` nadal tylko loguje `rawInput`, a funkcja zapisu nie jest podpięta do CLI/App; podpięcie wymaga osobnej decyzji. Klucze o niekanonicznym zapisie (`0x0d`) dają odmowę walidacji zamiast idempotencji.
