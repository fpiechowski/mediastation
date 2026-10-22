---
title: 'Wybudzenie: pełna polityka'
type: 'feature'
ticket: '8'
created: '2026-10-22'
status: done
baseline_revision: '9b01c6d5975d318db469a7cb6bb0c466c70324e0'
route: 'full'
route_source: 'auto'
risk: 'medium'
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

**Problem:** Wybudzenie z Modern Standby kończy się dziś samym `focusRequest{default}` (story 2): aplikacja z pierwszego planu zostaje w `Foreground` nad shellem z trybem wejścia `app`, a przeglądarki nie dostają `RenewSession`, więc po uśpieniu mają martwą sesję CDP i kafelek kłamie o stanie.

**Approach:** `SystemResumed` dostaje pełną politykę: nowy `transitionId` i nowy `seq` fokusu (wybudzenie zastępuje każdą wcześniejszą decyzję o pierwszym planie), aplikacja z `Foreground` schodzi w `Background` batchem `MinimizeWindow` → `ShowShell` → `SetInputRouting{shell}`, a każda aplikacja webowa w `Foreground` albo `Background` dostaje `RenewSession`. Kompensację okna z `Starting` daje istniejąca maszyneria `transitionId` ze story 6; timeout `Starting` biegnie dalej.

## Boundaries & Constraints

**Always:** `cmdId`, `seq` i `transitionId` nadaje wyłącznie Orchestrator; handler czysty i synchroniczny (AD-14); kolejność okienkowa `MinimizeWindow` → `ShowShell` → routing (AD-5/AD-15); nowy `seq` fokusu i nowe `transitionId` na każdym wybudzeniu; `RenewSession` tylko dla `AppType.Web` w `Foreground` albo `Background`, w kolejności `apps.json`; `RenewSession` jest fire-and-forget (bez korelacji wyniku, cisza hosta poprawna, awarię raportuje `CdpLost`); timeout `Starting` i `Stopping` nietknięte.

**Never:** `RenewSession` dla `Starting`, `Stopping`, `Failed` i `NotRunning`; zmiany w adapterze power (`App/Platform/System`), `web/**`, `CompositionRoot`, `TimerScheduler`, configu i adapterach tylko-log; nowe wpisy logu; parkowanie ticketu na UM760.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Wybudzenie z pierwszego planu | web `netflix` `Foreground` (hwnd), web `plex-web` `Starting`, Moonlight `Background` | `MinimizeWindow{hwnd}`, `ShowShell`, `SetInputRouting{shell, nowy epoch}`, `RenewSession{netflix}`; `netflix` → `Background`, snapshot `focusRequest{default, nowy seq}` | — |
| Wybudzenie z Home | web `netflix` `Background`, natywna `plex` `Background` | tylko `RenewSession{netflix}`; brak komend okienkowych i routingu; snapshot z nowym `seq` | — |
| Nic nie działa | wszystkie `NotRunning` | brak komend, snapshot z nowym `seq` | — |
| Wybudzenie w trakcie `Starting` | web `plex-web` `Starting` z timerem | bez `RenewSession`; attach po wybudzeniu → `MinimizeWindow` + `CancelTimer`, `Background` | `TimerElapsed` nadal prowadzi w `Failed` |
| Web w `Failed`/`Stopping` | `Failed` po `CdpLost`; `Stopping` po `CloseApp` | bez `RenewSession`; `Stopping` bez komend | — |
| Brak hosta | `RenewSession` do `MissingHostPort` | brak Eventu i wpisu logu | brak wyjątku przez granicę portu |
| Nieznana aplikacja | `RenewSession` w `CommandRouter` | `warn commandForUnknownApp` | brak wyjątku |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Contracts/AppCommands.cs` — `RenewSession(long CmdId, string AppId) : Command(CmdId)` po `ProbeExisting`; doc: host web odświeża sesję CDP (połączenie + `addBinding`, AD-10), cisza jest poprawna, a awarię raportuje `CdpLost` (wzór doc-a `ProbeExisting`:21-23).
- `src/MediaStation.Core/Contracts/MessageTypes.cs:23` — `typeof(RenewSession)` po `typeof(ProbeExisting)`; rejestr napędza kopertę, JSON Schema i typy TS.
- `src/MediaStation.Core/Contracts/Messages.cs:15-21` — doc bazy `Command` wymienia obie komendy fire-and-forget (`ProbeExisting`, `RenewSession`), a doc `SystemResumed` (:52-56) opisuje pełną politykę (Background, routing `shell`, `RenewSession`).
- `src/MediaStation.Core/Ports/Ports.cs:7-27` — `void Renew(RenewSession command);` w `IAppHostPort` po `Probe`, z doc-em o ciszy; summary portu wymienia oba wyjątki od `CommandCompleted`/`CommandFailed`.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs:74` — `SystemResumed => OnSystemResumed()` zamiast `OnDefaultFocus()`; nowa metoda obok `OnDefaultFocus` (:114-120), która zostaje dla `OrchestratorStarted` (jej doc i doc `OnOrchestratorStarted` :89-95 przestają mówić o wybudzeniu); bump `transitionId` **przed** komendami, żeby `AppState.ForegroundTransition` (:665) aplikacji w `Starting` przegrało z wybudzeniem.
- `src/MediaStation.App/Hosts/MissingHostPort.cs:61-68` — `Renew` jako cichy no-op po `Probe` (brak CDP = brak sesji do odświeżenia); summary klasy (:9-15) nazywa czwarty no-op.
- `src/MediaStation.App/Orchestrator/CommandRouter.cs:73-79` — `case RenewSession renew:` → `TryGetHost(...).Renew(renew)`; doc klasy (:9-15) wymienia komendę.
- `tests/MediaStation.Core.Tests/OrchestratorTests.cs` — helper `TwoWebAndMoonlight()` obok `TwoApps()`/`WebAndMoonlight()` (:14-31), `Foreground`/`Background` (:51-63) do ustawienia scen; `System_resumed_yields_default_focus_with_new_rev_and_seq` (:130-145) zmienia się w wiersz „nic nie działa”; nowe testy macierzy obok niego i przy kompensacji `Starting` (:189-269, wzór `Home_while_app_is_starting_*`); test `Restart_decided_before_home_or_wake_in_stopping_loses_its_window` (:901-944) zostaje zielony bez przenumerowania `cmdId` (apka w `Stopping` nie jest `Foreground`).
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs:307-367` — wzór pętli z `SilentHost`/`ProbingHost` (:553-589); nowy test wybudzenia z aplikacją w `Foreground` asertuje `dispatched` (`MinimizeWindow`, `ShowShell`, `SetInputRouting{shell}`, `RenewSession`) i kolejny snapshot.
- `tests/MediaStation.Core.Tests/CommandRouterTests.cs:39-69,231-244` — `FakeHost.Renewed` + test routingu `RenewSession` po `AppType` i `warn commandForUnknownApp`.
- `tests/MediaStation.Core.Tests/MissingHostPortTests.cs:54-66` — wzór testu ciszy dla `Probe`; nowy test dla `Renew`.
- `tests/MediaStation.Core.Tests/CoreMessageContractTests.cs:173-181,443-452,462-495,582-603` — koperta `renewSession` z round-tripem, lista rejestru, lista schematów (`renew-session.schema.json`) i `InlineData("renew-session.schema.json", "RenewSession", "cmdId,appId")`.
- Reuse: `AnotherAppIsActive`, `NextTransitionId`, `NextEpoch`, `NextCmdId`, `Snapshot`, `TryGetHost`, `RecordingLog`, `FakeTimeProvider`, `TimerScheduler`. Nie ruszać `web/**` (`web/contracts/**` generuje `BuildWeb` z `MessageTypes`), `App/Platform/System/**`, `CompositionRoot`, `TimerScheduler`.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Contracts/AppCommands.cs` — rekord `RenewSession` — komenda odświeżenia sesji przeglądarki (AD-10).
- [x] `src/MediaStation.Core/Contracts/MessageTypes.cs` — rejestracja `RenewSession` — koperta, JSON Schema i TS dla hostów.
- [x] `src/MediaStation.Core/Contracts/Messages.cs` — doc-e `Command` i `SystemResumed` — kontrakt opisuje pełną politykę wybudzenia.
- [x] `src/MediaStation.Core/Ports/Ports.cs` — `IAppHostPort.Renew` — seam hosta na `RenewSession`.
- [x] `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — `OnSystemResumed` — akcja z pierwszego planu, routing `shell` i odświeżenie webów.
- [x] `src/MediaStation.App/Hosts/MissingHostPort.cs` i `src/MediaStation.App/Orchestrator/CommandRouter.cs` — cichy `Renew` i routing po `AppType` — adapter bez hosta nic nie raportuje.
- [x] `tests/MediaStation.Core.Tests/*` — każdy wiersz macierzy, `RenewSession` w pętli, kontrakt i router — dowód na `FakeTimeProvider`.

**Acceptance Criteria:**
- Given web `netflix` w `Foreground` (hwnd), web `plex-web` w `Starting` i Moonlight w `Background`, when `SystemResumed`, then komendy to `MinimizeWindow{hwnd}`, `ShowShell`, `SetInputRouting{shell}` z nowym `epoch` i dokładnie jedno `RenewSession{netflix}` (bez `RenewSession` dla `plex-web` i Moonlighta), `netflix` ma `Background`, a snapshot nowe `focusRequest{default, seq}` i `rev`+1.
- Given wybudzenie z Home (web `Background`, natywna `Background`), when `SystemResumed`, then jedyną komendą jest `RenewSession` dla aplikacji webowej, bez `MinimizeWindow`, `ShowShell` i `SetInputRouting`.
- Given wszystkie aplikacje `NotRunning`, when `SystemResumed`, then brak komend, a snapshot ma nowy `seq` i ten sam `apps`.
- Given aplikacja webowa w `Starting`, when po `SystemResumed` przychodzi `AppWindowAttached`, then dostaje `MinimizeWindow` + `CancelTimer` i `Background` bez `ActivateWindow`; a gdy zamiast tego przyjdzie `TimerElapsed` terminu startu, then `Failed` bez nowego `focusRequest`.
- Given aplikacja webowa w `Failed` albo `Stopping`, when `SystemResumed`, then nie dostaje `RenewSession`.
- Given `RenewSession{3, "netflix"}`, when `ContractJson.SerializeEnvelope<Command>`, then dokładnie `{"v":1,"type":"renewSession","payload":{"cmdId":3,"appId":"netflix"}}` i round-trip; rejestr zawiera `renewSession`, a eksport schematów `renew-session.schema.json`.
- Given `CommandRouter` z hostami per `AppType`, when `RenewSession("moonlight-pc")`, then trafia do hosta `Moonlight`; nieznany `appId` daje `warn commandForUnknownApp` bez wyjątku.
- Given `ProbeExisting`/`RenewSession` w `MissingHostPort`, when wywołanie, then kolejka i log są puste.

## Design Notes

`OnSystemResumed` powtarza rdzeń `OnDefaultFocus` (nowe `transitionId`, `_focusSeq++`, `FocusRequest{DefaultTarget}`), a potem dokłada komendy:

1. aplikacja w `Foreground` z `hwnd` → `Background`, `MinimizeWindow`, `ShowShell`, `SetInputRouting{shell}` z nowym `epoch` — ta sama trójka i kolejność co Home z pierwszego planu;
2. `RenewSession` dla każdej aplikacji `AppType.Web` w `Foreground` albo `Background` w kolejności `apps.json` — po schowaniu, bo shell ma wrócić na wierzch, a minimalizowana przeglądarka nadal jest w `Background` i nadal potrzebuje świeżej sesji.

Gdy nic nie jest w `Foreground`, komendy okienkowe i routing nie lecą: `Background`, `Starting` i `Stopping` utrzymują tryb `shell` (tak samo jak Home poza pierwszym planem, story 6/10), więc nie ma czego chować ani przestawiać, a Z-order shella pilnuje adopcja/kompensacja. Wybudzenie zostawia więc wtedy tylko nowy fokus i `RenewSession`.

Typ webowy jest jedynym, który ma sesję CDP do odświeżenia: Moonlight i aplikacje natywne nie dostają `RenewSession`. Wybudzenie w trakcie przełączania zastępuje decyzję samym `transitionId`: `Starting` nie dostaje `RenewSession`, nie traci terminu `startingTimeout`, a jego spóźnione okno wpada w istniejącą kompensację `MinimizeWindow` + `CancelTimer`.

## Verification

**Commands:**
- `dotnet test MediaStation.slnx -c Release` -- zielone, w tym nowe testy `OrchestratorTests`, `OrchestratorLoopTests`, `CommandRouterTests`, `MissingHostPortTests` i `CoreMessageContractTests`.
- `dotnet build MediaStation.slnx -c Release` -- 0 ostrzeżeń; `BuildWeb` eksportuje `renew-session.schema.json` i `renew-session.ts` (gitignored).
- `dotnet build src/MediaStation.App -c Debug` -- 0 ostrzeżeń.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + uruchomienie z configiem `%LOCALAPPDATA%\MediaStation\config` i `powershell -File tools/inject-power.ps1` -- `OK: ... 3 snapshotPublished (seq 1..3)`, `stop: exitCode 0`, brak `warn`/`error` (syntetyczny sygnał, bo Modern Standby jest na tej maszynie niedostępny — `powercfg /a`).

**Implementation-time evidence (2026-10-22, ALDER, HEAD 9b01c6d):**
- `dotnet test MediaStation.slnx -c Release` — 506/506 passed, 0 failed, 0 skipped. Nowe/rozszerzone: wybudzenie z pierwszego planu (`MinimizeWindow` → `ShowShell` → `SetInputRouting{shell, epoch 5}` → jedno `RenewSession{netflix}`, `Background`, `focusRequest{default, 2}`), wybudzenie z Home (tylko `RenewSession`), brak komend przy samych `NotRunning`, kolejność `RenewSession` po `apps.json` z pominięciem natywnych i Moonlighta, `Starting` bez `RenewSession` z kompensacją `MinimizeWindow` + `CancelTimer`, timeout `Starting` po wybudzeniu nadal w `Failed` bez nowego fokusu, `Failed`/`Stopping` bez `RenewSession`; pętla dispatchuje batch wybudzenia i `RenewSession`; router po `AppType` i `warn commandForUnknownApp`; `RenewSession` w `MissingHostPort` bez Eventu i wpisu logu; koperta `{"v":1,"type":"renewSession","payload":{"cmdId":3,"appId":"netflix"}}` z round-tripem, rejestr i schemat `renew-session.schema.json`.
- `dotnet build MediaStation.slnx -c Release` — Build succeeded, 0 warningów, 0 błędów; `BuildWeb` wyeksportował `web/contracts/schema/renew-session.schema.json` i `web/contracts/generated/renew-session.ts` (gitignored) i przeszedł `tsc`.
- `dotnet build src/MediaStation.App -c Debug` — Build succeeded, 0 warningów, 0 błędów.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + uruchomienie publish z configiem `%LOCALAPPDATA%\MediaStation\config` + `powershell -File tools/inject-power.ps1` — `OK: 3 powerSignal, 2 systemResumed (src system, PBT_APMRESUMEAUTOMATIC), 3 snapshotPublished (seq 1..3), 3 powerSignalInjected.`, `stop: exitCode 0`; bieżący przebieg w logu: 14 wpisów, 0 `warn`, 0 `error`.
- `powercfg /a` — S0 Low Power Idle (Modern Standby) niedostępne na tej maszynie (firmware), więc dowód jest syntetyczny. Realne uśpienie na UM760 pozostaje odbiorem całego epiku u użytkownika.
- Kontrolny przebieg po implementacji (2026-10-22, ten sam HEAD `9b01c6d` i working tree): `dotnet test MediaStation.slnx -c Release` — 506/506 passed, 0 failed, 0 skipped; `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` — 0 ostrzeżeń, 0 błędów; publish self-contained + uruchomienie (pid 13152, config `netflix`/`moonlight-pc`/`plex`) + `powershell -File tools/inject-power.ps1` — `OK: 3 powerSignal, 2 systemResumed, 3 snapshotPublished (seq 1..3)`, `stop {exitCode: 0}`, 15 wpisów w logu bez `warn`/`error`; wszystkie nowe testy obecne w `--list-tests`.

## Implementation Notes

- 2026-10-22 — implementacja: `SystemResumed => OnSystemResumed()`; nowa metoda robi nowe `transitionId` i `seq` fokusu przed komendami, chowa aplikację z `Foreground` batchem Home i wysyła `RenewSession` webom w `Foreground`/`Background` w kolejności `_apps` (czyli `apps.json`). `RenewSession` to komenda fire-and-forget (bez korelacji), `IAppHostPort.Renew`, cichy `MissingHostPort.Renew` i routing po `AppType` w `CommandRouter`.
- Wybudzenie bez aplikacji w `Foreground` zostawia tylko nowy fokus i `RenewSession`, bo `Background`, `Starting` i `Stopping` już utrzymują tryb `shell` — tak samo jak Home poza pierwszym planem.
- Poprawki z przeglądu (2026-10-22): doc `OnSystemResumed` nie twierdzi już, że Z-order pilnuje kompensacja (`ShowShell` wysyłają Home i adopcja, a spóźnione okno `Starting` dostaje `MinimizeWindow`); test kolejności `RenewSession` ustawia tła odwrotnie do `apps.json` i nazywa się `..._skips_moonlight`, bo config nie ma aplikacji natywnej.

## Plan Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 4 findings — high 0, medium 0, low 3, false 1, maybe-false 0
- findings:
  - `[low]` `[patch]` Wybudzenie w trakcie `Starting` nie wysyła `ShowShell`, a doc twierdził, że Z-order pilnuje kompensacja — komentarz `OnSystemResumed` mówi teraz wprost, że `ShowShell` wysyłają Home i adopcja, a spóźnione okno `Starting` dostaje tylko `MinimizeWindow`; zachowanie bez zmian, bo ticket przypisuje `Starting` kompensację ze story 6, a nie batch Home.
  - `[low]` `[patch]` Test kolejności `RenewSession` ustawiał tła w kolejności `apps.json`, więc kolejność zmian stanu też by przeszła, a nazwa i doc obiecywały pokrycie aplikacji natywnej, której w configu nie ma — tła ustawiane są teraz odwrotnie (`plex-web`, potem `netflix`) przy oczekiwaniu `netflix` → `plex-web`, a test nazywa się `..._skips_moonlight`.
  - `[false]` `[reject]` Gałąź `Foreground` w filtrze `RenewSession` jest rzekomo nieosiągalna i publikuje sprzeczny snapshot — sprawdzone: `Foreground` powstaje wyłącznie razem z `hwnd` (`OnResume`, `OnAppWindowAttached`), więc guard `Hwnd is { } hwnd` nie da się ominąć i sprzeczny snapshot nie jest osiągalny; lista stanów w filtrze odwzorowuje brzmienie ticketu.
  - `[low]` `[reject]` Odwołania `:linia` w Code Map nie wskazują opisanych miejsc po wstawieniu nowych linii — poprawka wymaga edycji planu, więc odpada z definicji; sam kod i testy są zgodne z mapą po symbolach.

## Auto Run Result

**Status:** built.

**Summary:** `SystemResumed` realizuje pełną politykę wybudzenia: nowy `transitionId` i nowy `seq` fokusu `default`, aplikacja z `Foreground` schodzi do `Background` batchem `MinimizeWindow` → `ShowShell` → `SetInputRouting{shell}` z nowym `epoch`, a każda aplikacja webowa w `Foreground` albo `Background` dostaje nową komendę `RenewSession` (host web odtwarza sesję CDP; komenda fire-and-forget, cisza poprawna, awarię raportuje `CdpLost`). Gdy nic nie jest w `Foreground`, wybudzenie nie wysyła komend okienkowych ani routingu, bo tryb już jest `shell`; `Starting`, `Stopping`, `Failed` i `NotRunning` nie dostają `RenewSession`. Wybudzenie w trakcie przełączania zastępuje poprzednią decyzję przez `transitionId`, więc okno startującej aplikacji dostaje kompensujące `MinimizeWindow` + `CancelTimer`, a jej timeout `Starting` biegnie dalej.

**Files changed:**
- `src/MediaStation.Core/Contracts/AppCommands.cs` — rekord `RenewSession` po `ProbeExisting` z doc-em fire-and-forget.
- `src/MediaStation.Core/Contracts/MessageTypes.cs` — rejestracja `RenewSession` (koperta `renewSession`, schemat, typ TS).
- `src/MediaStation.Core/Contracts/Messages.cs` — doc bazy `Command` (oba wyjątki od wyniku komendy) i doc `SystemResumed` (pełna polityka).
- `src/MediaStation.Core/Ports/Ports.cs` — `IAppHostPort.Renew` i summary portu z oboma komendami bez korelacji.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — `OnSystemResumed` (fokus, batch Home, `RenewSession` webów) i doc-e startu.
- `src/MediaStation.App/Hosts/MissingHostPort.cs` — cichy `Renew`.
- `src/MediaStation.App/Orchestrator/CommandRouter.cs` — routing `RenewSession` po `AppType`.
- `tests/MediaStation.Core.Tests/OrchestratorTests.cs` — wszystkie wiersze macierzy (foreground, Home, `NotRunning`, kolejność `apps.json`, `Starting` + kompensacja i timeout, `Failed`/`Stopping`).
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs` — batch wybudzenia i `RenewSession` dochodzą do portów; `SilentHost`/`ProbingHost` dostają `Renew`.
- `tests/MediaStation.Core.Tests/CommandRouterTests.cs` — routing `RenewSession` i `warn commandForUnknownApp`.
- `tests/MediaStation.Core.Tests/MissingHostPortTests.cs` — `Renew` bez Eventu i logu.
- `tests/MediaStation.Core.Tests/CoreMessageContractTests.cs` — koperta `renewSession`, rejestr, lista schematów, `InlineData`.

**Review findings (quick):** 4 findings — high 0, medium 0, low 3, false 1, maybe-false 0.
- Patches applied: 2 (low) — komentarz `OnSystemResumed` mówi prawdę o `ShowShell` przy `Starting`; test kolejności `RenewSession` ustawia tła odwrotnie do `apps.json` i nie obiecuje pokrycia natywnego, którego nie ma.
- Deferred: 0.
- Rejected: 2 — gałąź `Foreground` w filtrze rzekomo publikuje sprzeczny snapshot (false: `Foreground` zawsze z `hwnd`, guard nie do ominięcia); odwołania `:linia` w Code Map wymagają edycji planu (low).

**Patched counts by verdict:** high 0, medium 0, low 2.

**Follow-up review recommendation:** `false`.

**Verification:** `dotnet test MediaStation.slnx -c Release` 506/506 (0 failed, 0 skipped); `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` z 0 ostrzeżeń i 0 błędów, `renew-session.schema.json`/`renew-session.ts` wyeksportowane przez `BuildWeb`; publish self-contained + uruchomienie z configiem (pid 16024) + `powershell -File tools/inject-power.ps1` — `OK: 3 powerSignal, 2 systemResumed, 3 snapshotPublished (seq 1..3)`, `stop {exitCode: 0}`, 15 wpisów bez `warn`/`error`. Wszystkie nowe testy widoczne w `--list-tests`.

**Residual risks:** realny `RenewSession` (reconnect + `addBinding`) nie ma wykonawcy w repozytorium, bo `CompositionRoot` nadal stawia `MissingHostPort`; dowodem są testy Core, pętli i routera. Modern Standby jest niedostępny na ALDER (`powercfg /a`), więc dowód wybudzenia jest syntetyczny — realne uśpienie i całościowy test całego epiku na UM760 wykonuje użytkownik przy `epic-boundary`. Teoretyczny `Foreground` bez `hwnd` nie jest chowany (stan nieosiągalny), a web dostałby wtedy `RenewSession`; brak testu na ten stan.
