---
title: 'Polityka i hub: Action Keyboard z raportu HID Consumer'
type: 'feature'
ticket: '16'
created: '2026-10-25'
status: done
baseline_revision: '5498b5fc7e0e5cf0319d0587b4675ad8d6598cc2'
route: 'full'
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

**Problem:** Przycisk asystenta G20S Pro (strona `0x0C`, usage `0x00CF`) nie ma VK, więc polityka go nie widzi: `InputPolicy.OnHidConsumer` tylko buforuje dowód dla Home/Back, a `IInputTarget.OnHidConsumer` nie zwraca Actions (AD-8, bug 3.14). Config `hid` (story 3.15) już istnieje.

**Approach:** `OnHidConsumer` zwraca `InputDecision`; dla urządzenia `source: remote` z wpisem `hid` emituje `Keyboard`/`remote` na przejściu „brak → usage”, a `InputHub` wkłada te Actions do kolejki.

## Boundaries & Constraints

**Always:** brak połykania (`Swallow=false`); bramka foreground jak dla klawiszy (nad obcym oknem brak Action); tylko urządzenie `remote` i tylko usage z sekcji `hid`; `Keyboard` bez `repeat` i `long`; bez korelacji czasowej z VK; dowód Home/Back (`_pendingRemoteEvidence`) działa jak dotąd; raport z usage 0 jest zwolnieniem; w normalnej pracy log ma tylko `action` (AD-13).

**Never:** nie ruszać `--calibrate-input` poza dopasowaniem do nowej sygnatury interfejsu (story 3.17); nie rejestrować Raw Input klawiatury; nie mapować `0x0223`/`0x0224`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Naciśnięcie | `remote`, `0x0C:0x00CF` w `hid`, okno nasze | jedna `Keyboard`/`remote`, `Swallow=false` | — |
| Ponowny raport | ten sam usage bez zwolnienia | brak Action | — |
| Zwolnienie i ponowne | usage 0, potem `0x00CF` | druga Action | — |
| Trzymanie | wiele raportów tego samego usage, timer | brak `repeat`/`long`, `NextDeadline` null | — |
| Obce okno | `IsManaged()==false` | brak Action | — |
| Nie z `hid` / inne urządzenie | usage spoza sekcji, ścieżka innego urządzenia | brak Action | — |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Input/InputTarget.cs` -- `IInputTarget.OnHidConsumer` zmienia typ zwracany na `InputDecision`.
- `src/MediaStation.Core/Input/InputPolicy.cs` -- ctor czyta `device.Hid` urządzeń `Source==Remote` (klucz `0xPPPP:0xUUUU`, regex jak `ConfigValidation.HidKey`); `OnHidConsumer` (ok. l. 495) zachowuje logikę dowodu Home/Back i dodaje ścieżkę `Keyboard`; stan „trzymany usage” per sygnatura; `IsForeign()`, `MatchDevice(path, includeWildcard:false)`, `MaxRepeatHoldMs` do reużycia.
- `src/MediaStation.Core/Input/InputCalibration.cs` -- `OnHidConsumer` (l. 169) tylko zmiana typu zwracanego (pass decision); logika bez zmian.
- `src/MediaStation.App/Platform/Input/TracingInputTarget.cs` -- przekazuje decyzję wewnętrzną.
- `src/MediaStation.App/Platform/Input/InputHub.cs` -- `OnRawInput` (l. 434): `Enqueue(decision.Actions)` przed `ArmTimer()`; usage 0 już trafia do polityki (nic nie filtruje); nie dodawać logów.
- `tests/MediaStation.Core.Tests/InputPolicyTests.cs`, `ForegroundGateTests.cs` -- wzorce `Create(...)`, `DonglePath`, `FakeProbe`; istniejące wywołania `OnHidConsumer` jako instrukcje nadal się kompilują.

## Tasks & Acceptance

**Execution:**
- [x] `InputTarget.cs`, `InputCalibration.cs`, `TracingInputTarget.cs` -- `OnHidConsumer` zwraca `InputDecision` -- Actions z raportu HID
- [x] `InputPolicy.cs` -- mapowanie `hid` i emisja `Keyboard`/`remote` na przejściu „brak → usage”, zwolnienie na usage 0 (także inny usage kończy trzymanie), nieświeży wpis po `MaxRepeatHoldMs` -- AD-8
- [x] `InputHub.cs` -- `Enqueue` decyzji z `OnHidConsumer` -- Action trafia do kolejki
- [x] testy w `MediaStation.Core.Tests` (polityka na `FakeTimeProvider`; Home/Back po `0x0223`/`0x0224` nadal jedna Action po VK; hub: test czystej części, jeśli hub nie da się testować w Core.Tests, dowód przez kod i log-assert opisany w planie)

**Acceptance Criteria:**
- Given sekcja `hid` z `0x000C:0x00CF` dla `remote`, when raport `0x00CF`, then jedna `Keyboard`/`remote` bez połknięcia, a drugi raport bez zwolnienia nie daje Action, po usage 0 kolejne naciśnięcie daje Action.
- Given obce okno, usage spoza `hid` albo urządzenie inne niż `remote`, when raport, then brak Action.
- Given Home po `0x0223` i VK `0xAC`, when naciśnięcie, then nadal jedna Action po VK.

## Implementation Notes

- Pliki: `InputTarget.cs`, `InputCalibration.cs`, `TracingInputTarget.cs` (typ zwracany), `InputPolicy.cs` (mapowanie `hid` per sygnatura `remote`, stan â€žtrzymany usageâ€ť `_hidHeld`), `InputHub.cs` (`Enqueue` decyzji z `OnHidConsumer`), testy `HidKeyboardPolicyTests.cs` i `RawInputBodyTests.cs`.
- Decyzja: press nad obcym oknem jest zapamiÄ™tany jako trzymany (jak wpis `_held` dla VK), wiÄ™c nie odpala po powrocie okna naszego bez zwolnienia.
- Decyzja: inny usage albo usage 0 koĹ„czy trzymanie; Ĺ›wieĹĽoĹ›Ä‡ trzymania liczona od ostatniego raportu (`MaxRepeatHoldMs` to tylko siatka na zgubione zwolnienie).
- Hub nie ma testĂłw w Core.Tests (Win32). DowĂłd Ĺ›cieĹĽki huba: `RawInputBody` czyta raport zwolnienia jako usage 0 (test), hub niczego nie filtruje i wkĹ‚ada `decision.Actions` do kolejki (kod `InputHub.cs`, kompiluje siÄ™ w `dotnet test`); nieczytelny raport hub pomija, bo usage 0 zmienia stan polityki; hub nie loguje usage poza `MS_INPUT_TRACE` (`hidTrace`), wiÄ™c log normalnej pracy ma tylko `action`.

## Plan Change Log

## Review Triage Log

### 2026-10-25 â€” Review pass
- verdicts: 4 findings â€” high 0, medium 0, low 3, false 1, maybe-false 0
- findings:
  - `[low]` `patch` Brak bookkeepingu i dowodu huba w planie â€” zadania odhaczone, notatki i dowĂłd dopisane.
  - `[low]` `patch` Okno nieĹ›wieĹĽego trzymania liczone od pierwszego naciĹ›niÄ™cia â€” Ĺ›wieĹĽoĹ›Ä‡ odĹ›wieĹĽana kaĹĽdym raportem tego samego usage; test `Continuous_reports_keep_the_hold_fresh_past_max_repeat_hold`.
  - `[low]` `patch` Nieczytelne ciaĹ‚o HID szĹ‚o do polityki jako usage 0 (zwolnienie) â€” hub je pomija (`InputHub.cs`).
  - `[false]` `reject` Test obcego okna sprawdza wiÄ™cej niĹĽ macierz â€” zgodne z zapisem decyzji naciĹ›niÄ™cia jak dla VK; decyzja opisana w Implementation Notes.

## Design Notes

Raport zwolnienia (usage 0) jest w AD-8 `[ASSUMPTION]`; jeśli G20S go nie wysyła, `MaxRepeatHoldMs` jest tylko siatką bezpieczeństwa, a regułę debounce ustala odbiór UM760.

## Verification

**Commands:**
- `dotnet test` -- expected: wszystko przechodzi, w tym nowe testy `hid` polityki i huba

Maszyna: UM760 (`COMPUTERNAME` MEDIASTATION, board F7BSI, model EliteMini Series), HEAD 5498b5f.

## Auto Run Result

Status: built

- Summary: `IInputTarget.OnHidConsumer` zwraca `InputDecision`; `InputPolicy` emituje `Keyboard`/`remote` (bez poĹ‚ykania, `repeat` i `long`) na przejĹ›ciu â€žbrak â†’ usageâ€ť z sekcji `hid` urzÄ…dzenia `remote`, z bramkÄ… foreground; usage 0 to zwolnienie; `InputHub` wkĹ‚ada Actions do kolejki.
- Files: `src/MediaStation.Core/Input/{InputTarget,InputPolicy,InputCalibration}.cs`, `src/MediaStation.App/Platform/Input/{InputHub,TracingInputTarget}.cs`, `tests/MediaStation.Core.Tests/{HidKeyboardPolicyTests,RawInputBodyTests}.cs`.
- Review: 3 patches applied (low), 0 deferred, 1 rejected (zgodne z decyzjÄ… naciĹ›niÄ™cia jak dla VK).
- Follow-up review recommended: false.
- Verification (UM760: `COMPUTERNAME` MEDIASTATION, board F7BSI, model EliteMini Series; HEAD 5498b5f): `dotnet test` â†’ 933/933 passed, 0 failed, w tym 13 nowych testĂłw `HidKeyboardPolicyTests` (wszystkie wiersze macierzy, Home/Back po VK nadal jedna Action) i test `RawInputBody` dla raportu zwolnienia. Fizyczny G20S Pro: nie dotyczy (odbiĂłr caĹ‚ej epiki na UM760).
- Residual risks: raport zwolnienia (usage 0) po `0x00CF` niezmierzony [ASSUMPTION AD-8]; bez niego ponowne naciĹ›niÄ™cie przed `MaxRepeatHoldMs` (60 s) nie da Action â€” pomiar i ewentualny debounce w odbiorze UM760.