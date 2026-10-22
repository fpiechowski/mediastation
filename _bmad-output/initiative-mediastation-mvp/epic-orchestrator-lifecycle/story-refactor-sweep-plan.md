---
title: 'Refactor sweep'
type: 'refactor'
ticket: '9'
created: '2026-10-22'
status: done
baseline_revision: 'ad03f93f3cee9dc2f9b91c59afe7b9f0f229ed34'
route: 'full'
route_source: 'auto'
risk: 'low'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Z czterech findings z dispozycją `defer` w planach stories 1–8, 10 i 11 dwa dotyczą kodu tej epiki i nie mają innego właściciela: okno echa rejestracji (`PowerNotifications._registering`) i mapowanie trzech źródeł siedzą w shimie wymagającym HWND/WPF, więc zostają bez testu, a konstrukcja, która padnie po `AddHook`, zostawia hook do końca procesu.

**Approach:** Decyzja adaptera (okno rejestracji + mapowanie źródeł na reporter) przenosi się do `PowerSignalRouter` bez Win32/WPF, linkowanego do testów jak `PowerSignalReporter`; shim zostaje cienką plumbą i przy nieudanej konstrukcji zwalnia to, co już podłączył. Komendy, snapshoty, wpisy logu i zachowanie Orchestratora bez zmian; DW-3 (należy do hosta, story 2.9 w epic-browserhost) i DW-4 (osobna story z decyzją o `Failed`) zostają otwarte.

## Boundaries & Constraints

**Always:** decyzja i księgowość w `App/Platform/System`, nigdy w Core (AD-3, `CoreBoundaryTests.Core_has_no_wake_signal_rule`); logi bez zmian: `powerSignal {source}`, `systemResumed {source}`, `powerSignalsRegistered {displayState, windowHook, powerModeChanged}`, wszystkie z `src: system`; okno echa odrzuca wyłącznie `GUID_CONSOLE_DISPLAY_STATE`, a `PBT_APMRESUMEAUTOMATIC` raportuje także w trakcie rejestracji; hook zawsze zwraca `false`; `PowerNotifications.Start(hwnd, reporter)` zostaje jedynym wejściem adaptera, więc `CompositionRoot` się nie zmienia; ledger opisuje stan po sweepie.

**Never:** reguła wybudzenia w Core; zmiany w `Orchestrator`, kontraktach, schematach, `web/**`, `CompositionRoot`, `WakeSignalGate`, `DisplayStateNotification`; naprawa DW-2 szersza niż zwolnienie już podłączonych zasobów (raport rejestracji zostaje jak dziś, z `powerModeChanged: true`); rozwiązywanie DW-3/DW-4; nowy projekt testów App; nowe wpisy logu; zmiany w `tools/inject-power.ps1`; parkowanie ticketu na UM760.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Echo rejestracji | payload `=on` w oknie rejestracji | brak `powerSignal`, brak Eventu | — |
| Display-state po starcie | ten sam payload po `CompleteRegistration` | `powerSignal` i `systemResumed {GUID_CONSOLE_DISPLAY_STATE}` oraz jeden `SystemResumed` | — |
| Resume w oknie rejestracji | `OnResumeAutomatic` przed zamknięciem okna | `powerSignal` i `systemResumed {PBT_APMRESUMEAUTOMATIC}` oraz Event (okno go nie tłumi) | — |
| PowerModeChanged | `OnPowerModeResumed` po rejestracji | tylko `powerSignal {PowerModeChanged}`, bez `systemResumed` i Eventu | gate odrzuca źródło spoza reguły |
| Payload spoza reguły | off, dimmed, inny GUID, payload krótszy niż deklarowany | brak wpisu i Eventu | — |
| Niepełna rejestracja | `CompleteRegistration(false, true, true)` | `powerSignalsRegistered` jako `warn` | — |
| Pad konstrukcji shimu | wyjątek po `AddHook` | hook zdjęty, wyjątek leci do `CompositionRoot` → `powerNotificationsStartFailed` | brak wycieku hooka |
| Start zdrowy | publish + start z ważnym configiem | `powerSignalsRegistered` `info` z trzema `true`, `snapshotPublished {rev: 1, focusRequest {default, 1}}` | — |

</intent-contract>

## Code Map

- `src/MediaStation.App/Platform/System/PowerSignalRouter.cs` (nowy, bez Win32/WPF) — `BeginRegistration()`, `CompleteRegistration(displayState, windowHook, powerModeChanged)` (zamyka okno i woła `ReportRegistration`, żeby okno nie zostało otwarte po starcie), `OnResumeAutomatic()`, `OnDisplayStateChanged(ReadOnlySpan<byte> payload, Guid setting)`, `OnPowerModeResumed()`; `PowerModeChangedSource = "PowerModeChanged"` przenosi się tu z shimu.
- `src/MediaStation.App/Platform/System/PowerNotifications.cs:18-159` — shim traci `_registering`, pole `_reporter` i całe mapowanie; zostaje `HwndSource.FromHwnd(...).AddHook`, `RegisterPowerSettingNotification`, `SystemEvents.PowerModeChanged`, `Marshal` payloadu (`TryCopyDisplayStateSetting`, `lParam == 0` odrzucony) i delegacja do routera; podłączenia idą przez `PowerSourcesAttachment` (DW-2), a raport rejestracji zostaje w `finally` z wartościami z chwili startu.
- `src/MediaStation.App/Platform/System/PowerSourcesAttachment.cs` (nowy, bez Win32/WPF) — `Attach(attach, release)` zdejmuje już podłączone źródła i przepuszcza wyjątek, `Dispose` zwalnia w odwrotnej kolejności i jest idempotentny.
- `tests/MediaStation.Core.Tests/PowerSourcesAttachmentTests.cs` (nowy) — wiersz „pad konstrukcji shimu”: rzucone podłączenie zwalnia wcześniejsze, kolejność odwrotna, idempotencja.
- `tests/MediaStation.Core.Tests/PowerSignalRouterTests.cs` (nowy) — wiersze macierzy na `RecordingLog` + `FakeTimeProvider` + `WakeSignalGate`; fixture'y payloadu jak w `DisplayStateNotificationTests` (bajty GUID `6FE69556-704A-47A0-8F24-C28D936FDA47`).
- `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj:21-34` — `Compile Link` dla `PowerSignalRouter.cs` obok `PowerSignalReporter.cs`; komentarz o nietestowanym shimie zwęża się do plumbingu Win32/WPF.
- `tests/MediaStation.Core.Tests/PowerSignalReporterTests.cs:36` — literał `"PowerModeChanged"` → `PowerSignalRouter.PowerModeChangedSource`.
- `tests/MediaStation.Core.Tests/CoreBoundaryTests.cs:143-156` — `Core_has_no_wake_signal_rule` dopisuje `Assert.DoesNotContain("PowerSignalRouter", typeNames)`.
- `_bmad-output/initiative-mediastation-mvp/deferred-work.md:1-29` — DW-2 → `resolved` z dowodem; DW-1 zwężone do plumbingu Win32/WPF; DW-3/DW-4 zostają `open`, wszystkie `location:` po symbolu (`OnProcessExited`, `OnAppWindowAttached`/`EnterFailed`, router/shim), bo numery linii już się przesunęły.
- Reuse: `PowerSignalReporter`, `WakeSignalGate`, `DisplayStateNotification.IsWake`, `RecordingLog`, `FakeTimeProvider`. Nie ruszać `Orchestrator`, kontraktów, `web/**`, `CompositionRoot`, `tools/inject-power.ps1`.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.App/Platform/System/PowerSignalRouter.cs` — wydzielona decyzja adaptera (okno echa i mapowanie trzech źródeł) — DW-1: decyzja wychodzi z typu wymagającego HWND.
- [x] `src/MediaStation.App/Platform/System/PowerNotifications.cs` — shim deleguje do routera i zwalnia podłączone zasoby, gdy konstrukcja padnie — DW-1 i DW-2 bez zmiany zachowania.
- [x] `src/MediaStation.App/Platform/System/PowerSourcesAttachment.cs` i `tests/MediaStation.Core.Tests/PowerSourcesAttachmentTests.cs` — księgowość podłączeń (rzucone podłączenie zwalnia wcześniejsze) — wiersz „pad konstrukcji shimu” ma test na seamie, którego używa shim.
- [x] `tests/MediaStation.Core.Tests/PowerSignalRouterTests.cs` — wiersze macierzy (echo, resume, PowerModeChanged, payload spoza reguły, rejestracja `info`/`warn`).
- [x] `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj`, `PowerSignalReporterTests.cs`, `CoreBoundaryTests.cs` — linkowanie plików, wspólna stała źródła, granica Core.
- [x] `_bmad-output/initiative-mediastation-mvp/deferred-work.md` — DW-2 `resolved`, DW-1 zwężone, DW-3/DW-4 po symbolu i nadal `open`.

**Acceptance Criteria:**
- Given router w otwartym oknie rejestracji i payload `GUID_CONSOLE_DISPLAY_STATE=on`, when `OnDisplayStateChanged`, then log i kolejka są puste.
- Given `CompleteRegistration`, when ten sam payload, then log ma `powerSignal` i `systemResumed` z `source: GUID_CONSOLE_DISPLAY_STATE`, a kolejka jeden `SystemResumed`.
- Given otwarte okno rejestracji, when `OnResumeAutomatic`, then log ma `powerSignal` i `systemResumed` z `source: PBT_APMRESUMEAUTOMATIC`, a kolejka jeden `SystemResumed`.
- Given zamknięta rejestracja, when `OnPowerModeResumed`, then jest tylko `powerSignal {source: PowerModeChanged}`, bez `systemResumed` i bez Eventu.
- Given payload off, dimmed, inny GUID albo krótszy niż deklarowany, when `OnDisplayStateChanged` po rejestracji, then log i kolejka są puste.
- Given `CompleteRegistration(true, true, true)` i `CompleteRegistration(false, true, true)`, when reporter zapisuje linię, then pierwsza jest `info`, druga `warn`, obie `powerSignalsRegistered` z trzema flagami.
- Given `dotnet test MediaStation.slnx -c Release`, when przebieg kończy się, then wszystkie dotychczasowe 506 testów plus nowe przechodzą bez edycji istniejących asercji zachowania — zachowanie epiki bez zmian.
- Given opublikowany build uruchomiony z ważnym configiem, when proces żyje 10 s i zamyka się, then log ma `powerSignalsRegistered` `info` z trzema `true`, `snapshotPublished {rev: 1, focusRequest {default, 1}}`, `stop {exitCode: 0}` i zero `warn`/`error`.
- Given `tools/inject-power.ps1` na tym buildzie, when skrypt kończy, then wypisuje `OK: 3 powerSignal, 2 systemResumed (src system, PBT_APMRESUMEAUTOMATIC), 3 snapshotPublished (seq 1..3)` — hook i gate działają jak przed refaktorem.
- Given `deferred-work.md`, when otworzę ledger, then DW-2 ma `status: resolved` z dowodem, DW-1 jest zwężone do plumbingu Win32/WPF i wskazuje symbol, a DW-3/DW-4 są `open` ze wskazaniem symbolu.

## Implementation Notes

- 2026-10-22 — zakres sweepu wyszedł z ledgera `deferred-work.md`, nie z nowego przeglądu: cztery findings `defer` (DW-1…DW-4). DW-3 należy do hosta (tożsamość procesu w Evencie, story 2.9 w epic-browserhost), DW-4 wymaga decyzji o `Hwnd` w `Failed` (osobna story), a finding z planu story 10 (Home przy drugiej aplikacji w `Starting`) jest zadaniem story 6 — wszystkie trzy poza zakresem, `status: open` bez zmian.
- `PowerSignalRouter` przejął z shimu okno echa i mapowanie trzech źródeł; shim przekazuje tylko `PBT_APMRESUMEAUTOMATIC`, skopiowany payload `PBT_POWERSETTINGCHANGE` i `PowerModes.Resume`, więc decyzja jest testowalna bez HWND. `PBT_APMRESUMEAUTOMATIC` zostaje poza oknem echa (tłumienie zmieniłoby zachowanie).
- Wiersz macierzy „pad konstrukcji shimu” nie da się sprawdzić na prawdziwym HWND, a plan zabrania projektu testów App, więc podłączenia shimu idą przez `PowerSourcesAttachment` bez Win32/WPF: `Attach` przy rzuconym podłączeniu zwalnia wcześniejsze w odwrotnej kolejności i przepuszcza wyjątek. `PowerSourcesAttachmentTests` pokrywa ten seam; połowa „wyjątek leci do `CompositionRoot` → `powerNotificationsStartFailed`” to niezmieniony kod `CompositionRoot` (poza diffem, potwierdzony buildem). Audyt macierzy: echo → `Display_state_echo_inside_the_registration_window_is_ignored`, display-state po starcie → `Display_state_after_registration_logs_and_posts_one_resume`, resume w oknie → `Resume_automatic_inside_the_registration_window_still_wins`, `PowerModeChanged` → `Power_mode_resume_after_registration_only_logs_a_candidate`, payload spoza reguły → `Display_off_or_dimmed…`, `Other_power_setting_guid…`, `Zero_data_length…`, `Data_length_other_than_a_dword…`, `Payload_shorter_than_claimed_data…`, niepełna rejestracja → `Incomplete_registration_is_logged_as_warn_with_all_three_flags` (+ `…_as_info`), start zdrowy → publish/start z logiem (pid 45476), pad konstrukcji → `Attach_that_throws_releases_the_sources_attached_before_it`; wszystkie 17 nowych testów przebiegły w 523/523.
- Wartości linii `powerSignalsRegistered` liczone są w chwili podłączenia (lokalne `displayState`/`windowHook`), więc nieudana konstrukcja raportuje dokładnie to, co dawniej, mimo że źródła są już zdjęte; `powerModeChanged` zostaje `true` jak przed sweepem. `System.Action` w `PowerSourcesAttachment` jest kwalifikowany przez `global::`, bo projekt testów aliasuje `Action` na rekord kontraktu, a `System` przesłania segment przestrzeni nazw pliku.
- Weryfikacja (2026-10-22, baseline `ad03f93f`, ALDER / B760 Pro RS/D4 / Windows 11 Home): `dotnet test MediaStation.slnx -c Release` 523/523 (506 + 12 `PowerSignalRouterTests` + 5 `PowerSourcesAttachmentTests`), filtr nowych klas 17/17; `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` 0 ostrzeżeń, 0 błędów; publish self-contained + start (pid 45476) — `powerSignalsRegistered {displayState: true, windowHook: true, powerModeChanged: true}`, `snapshotPublished {rev: 1, focusRequest {default, 1}}`, `stop {exitCode: 0}`, zero `warn`/`error`; `tools/inject-power.ps1` na tym buildzie — `OK: 3 powerSignal, 2 systemResumed (src system, PBT_APMRESUMEAUTOMATIC), 3 snapshotPublished (seq 1..3)`, `stop: exitCode 0`.
- Kontrolny przebieg po poprawkach przeglądu (2026-10-22, ten sam HEAD `ad03f93f` i finalne drzewo): `dotnet test MediaStation.slnx -c Release` 524/524 (doszedł `Power_mode_resume_logs_the_wire_source_literal`), 0 skipped; `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` 0 ostrzeżeń, 0 błędów; publish self-contained + start z configiem (pid 30052) — `powerSignalsRegistered {displayState: true, windowHook: true, powerModeChanged: true}`, `inputRouting {cmdId: 1, shell, blocked, epoch: 1}`, `snapshotPublished {rev: 1, focusRequest {default, 1}}`, `stop {exitCode: 0}`, 5 wpisów bez `warn`/`error`; `tools/inject-power.ps1` na tym buildzie (pid 39168) — `OK: 3 powerSignal, 2 systemResumed (src system, PBT_APMRESUMEAUTOMATIC), 3 snapshotPublished (seq 1..3), 3 powerSignalInjected.`, `stop: exitCode 0`.

## Plan Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 5 findings — high 0, medium 0, low 4, false 1, maybe-false 0
- findings:
  - `[false]` `[reject]` Dowód smoke (AC8/AC9) nie mógł objąć kodu z przeglądu, bo artefakty buildów są starsze niż ostatnia edycja `PowerSourcesAttachment.cs` — sprawdzone: jedyną zmianą po zarejestrowanym przebiegu była normalizacja końców linii w dwóch nowych plikach (treść bez zmian, pełny `dotnet test` przebudował i przeszedł), a po poprawce przeglądu przebieg powtórzono na finalnym drzewie: publish + start (pid 30052) z `powerSignalsRegistered`/`snapshotPublished`/`stop`, `inject-power.ps1` na pid 39168.
  - `[low]` `[reject]` `PowerSourcesAttachment.Dispose` zwalnia w jednej pętli bez osłony, więc rzucone zwolnienie przerwie resztę i zostawi źródła, a w `Attach` przykryje wyjątek z attachu — żadne zwolnienie shimu nie rzuca (`HwndSource.RemoveHook`, `SafeHandle.Dispose`, `-=` na `SystemEvents`), więc fix to dodatkowe `try/catch` wokół stanu, którego nie da się pokazać; stan po awarii i tak widać w `powerNotificationsStartFailed`.
  - `[low]` `[reject]` Po nieudanej konstrukcji shim loguje `powerSignalsRegistered` jako `info` z trzema `true`, choć źródła są już zdjęte — prawdziwe, ale intencja ticketu („bez zmiany zachowania”) wyklucza zmianę treści tej linii, a realny stan niesie następujący po niej `powerNotificationsStartFailed`; dotyczy wyłącznie ścieżki wyjątku przy starcie adaptera.
  - `[low]` `[reject]` AC9 cytuje linię `OK: ...` bez końcówki `, 3 powerSignalInjected.`, której skrypt nie pomija — poprawka wymaga edycji kryterium akceptacji w planie, a to jest odrzucane z definicji; cytowany tekst jest prefiksem realnego wyjścia, a pełna linia jest w Implementation Notes.
  - `[low]` `[patch]` Wartość `PowerModeChanged` nie jest już przypięta żadnym testem po zamianie literału w `PowerSignalReporterTests` na stałą, więc zmiana stałej przeszłaby na zielono zmieniając `powerSignal.source` — dodany test `Power_mode_resume_logs_the_wire_source_literal` przypina emitowaną wartość do literału.

## Design Notes

Brak projektu testów App jest granicą epiki, więc testowalność bierze się z wydzielenia, nie z nowego projektu: pliki bez Win32/WPF idą do testów przez `Compile Link` (precedens `PowerSignalReporter`, `WakeSignalGate`). Wydzielamy tylko decyzję — okno echa i mapowanie źródeł; hook, rejestracja i `SystemEvents` zostają w shimie, bo bez HWND nie da się ich sprawdzić inaczej niż obserwacją na maszynie.

`PBT_APMRESUMEAUTOMATIC` zostaje poza oknem echa: to nie echo rejestracji, a tłumienie go w trakcie startu zmieniłoby zachowanie (dziś wybudzenie w oknie rejestracji wygrywa). `CompleteRegistration` zamyka okno i raportuje linię w jednym kroku, bo rozdzielenie tych dwóch rzeczy pozwoliłoby zostawić okno otwarte po starcie. DW-3 i DW-4 nie wchodzą: jedna należy do hosta (tożsamość procesu w Evencie), druga wymaga decyzji o zachowaniu `Hwnd` w `Failed`.

## Verification

**Commands:**
- `dotnet test MediaStation.slnx -c Release` -- zielone: 506 dotychczasowych + nowe `PowerSignalRouterTests`.
- `dotnet build MediaStation.slnx -c Release` -- 0 ostrzeżeń, 0 błędów.
- `dotnet build src/MediaStation.App -c Debug` -- 0 ostrzeżeń, 0 błędów.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + uruchomienie z configiem `%LOCALAPPDATA%\MediaStation\config` -- proces żyje, `powerSignalsRegistered` z trzema `true`, `snapshotPublished {rev: 1, focusRequest {default, 1}}`, `stop {exitCode: 0}`, zero `warn`/`error`.
- `powershell -File tools/inject-power.ps1` na tym buildzie -- `OK: 3 powerSignal, 2 systemResumed (src system, PBT_APMRESUMEAUTOMATIC), 3 snapshotPublished (seq 1..3)`.

## Auto Run Result

**Status:** built.

**Summary:** Sweep domknął dwa findings z dispozycją `defer`, które dotyczą kodu tej epiki: okno echa rejestracji i mapowanie trzech źródeł wyszły z shimu wymagającego HWND do `PowerSignalRouter` (bez Win32/WPF, linkowany do testów), a podłączenia shimu idą przez `PowerSourcesAttachment`, który przy rzuconym podłączeniu zwalnia wcześniejsze źródła w odwrotnej kolejności (DW-2). Komendy, snapshoty, stany i wpisy logu zostały bez zmian; DW-3 (host, story 2.9 w epic-browserhost) i DW-4 (decyzja o `Hwnd` w `Failed`) zostają `open`, a ledger wskazuje teraz symbole zamiast numerów linii.

**Files changed:**
- `src/MediaStation.App/Platform/System/PowerSignalRouter.cs` (nowy) — decyzja adaptera bez Win32/WPF: okno echa, `PBT_APMRESUMEAUTOMATIC`, display-state i `PowerModeChanged`.
- `src/MediaStation.App/Platform/System/PowerSourcesAttachment.cs` (nowy) — księgowość podłączeń: rzucone podłączenie zwalnia wcześniejsze, `Dispose` w odwrotnej kolejności i idempotentny.
- `src/MediaStation.App/Platform/System/PowerNotifications.cs` — shim deleguje do routera i podłącza źródła przez `PowerSourcesAttachment`; raport rejestracji bez zmian.
- `tests/MediaStation.Core.Tests/PowerSignalRouterTests.cs` (nowy) — 13 testów: echo, resume w oknie, display-state, `PowerModeChanged` (także literał `source`), payloady spoza reguły, rejestracja `info`/`warn`.
- `tests/MediaStation.Core.Tests/PowerSourcesAttachmentTests.cs` (nowy) — 5 testów: zwolnienie po rzuconym podłączeniu, kolejność odwrotna, idempotencja.
- `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` — linkowanie obu plików App; komentarz zwężony do plumbingu Win32/WPF.
- `tests/MediaStation.Core.Tests/PowerSignalReporterTests.cs` — literał `PowerModeChanged` → `PowerSignalRouter.PowerModeChangedSource`.
- `tests/MediaStation.Core.Tests/CoreBoundaryTests.cs` — `Core_has_no_wake_signal_rule` pilnuje też braku `PowerSignalRouter` w Core.
- `_bmad-output/initiative-mediastation-mvp/deferred-work.md` — DW-2 `resolved` z dowodem, DW-1 zwężone do plumbingu, DW-3/DW-4 po symbolu i nadal `open`.

**Review findings (quick):** 5 findings — high 0, medium 0, low 4, false 1, maybe-false 0.
- Patches applied: 1 (low) — test przypinający emitowaną wartość `source: PowerModeChanged` do literału.
- Deferred: 0.
- Rejected: 4 — świeżość dowodu smoke (false: po przebiegu zmieniły się tylko końce linii, a smoke powtórzono na finalnym drzewie); nieosłonięta pętla zwolnień w `PowerSourcesAttachment` (low: żadne zwolnienie shimu nie rzuca, fix to osłony dla nieosiągalnego stanu); `info` `powerSignalsRegistered` po nieudanej konstrukcji (low: intencja wyklucza zmianę logu, stan niesie `powerNotificationsStartFailed`); cytat linii `OK: ...` bez `, 3 powerSignalInjected.` (low: poprawka wymaga edycji planu).

**Patched counts by verdict:** high 0, medium 0, low 1.

**Follow-up review recommendation:** `false`.

**Verification:** `dotnet test MediaStation.slnx -c Release` 524/524 (506 + 13 `PowerSignalRouterTests` + 5 `PowerSourcesAttachmentTests`), 0 failed, 0 skipped; `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` — 0 ostrzeżeń, 0 błędów; publish self-contained + start z ważnym configiem (pid 30052): proces żył 10 s, log `powerSignalsRegistered {displayState: true, windowHook: true, powerModeChanged: true}`, `snapshotPublished {rev: 1, focusRequest {default, 1}}`, `stop {exitCode: 0}`, zero `warn`/`error`; `powershell -File tools/inject-power.ps1` na tym buildzie (pid 39168) — `OK: 3 powerSignal, 2 systemResumed (src system, PBT_APMRESUMEAUTOMATIC), 3 snapshotPublished (seq 1..3), 3 powerSignalInjected.`, `stop: exitCode 0`.

**Residual risks:** plumbing Win32/WPF shimu (hook, `RegisterPowerSettingNotification`, `SystemEvents`, `Marshal` payloadu) zostaje bez testu — DW-1; realne echo rejestracji i wybudzenie z Modern Standby na UM760 są do zaobserwowania wyłącznie na sprzęcie użytkownika, więc całościowy odbiór epiku na UM760 zostaje po jego stronie; DW-3 i DW-4 nadal czekają na swoich właścicieli.
