---
title: 'Failed: wejście, wyjście przez AppMenu i awaria na pierwszym planie'
type: 'feature'
ticket: '4'
created: '2026-10-22'
status: done
baseline_revision: '0eb92bda9eb06b72cf157af3159938028ce6b987'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: ['oversized']
deferred:
  - summary: >-
      A late `ProcessExited` from the process killed by `RestartApp` can hit the freshly restarted app and show `Failed` although the new process is alive.
    evidence: |-
      `ProcessExited` carries only `appId`. After `CommandCompleted{StopApp}` the stop correlation is consumed, the Orchestrator moves to `Starting` and sends `StartApp`, and a second exit fact from the host (exit watcher next to the command confirmation) is now treated as a failure of the new start. Settled by host story 2.9 guaranteeing that `ProcessExited` refers to the current process (pid in the Event, or dedupe by `rootPid`), plus a loop test with two facts for one stop.
    location: >-
      src/MediaStation.Core/Orchestrator/Orchestrator.cs:269
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Orchestrator nigdy nie wchodzi w `Failed`: `CommandFailed` na starcie i `ProcessExited` poza `Stopping` są ignorowane, `CdpLost` nie istnieje, a `OnStop` przyjmuje tylko `Background`. Kafelek po awarii zostaje w `Starting`/`Foreground`, a użytkownik nie ma jak uruchomić aplikacji ponownie (J4).

**Approach:** Nowy Event `CdpLost{appId}` i jedno wspólne wejście w `Failed` po `CdpLost`, `ProcessExited` (`Starting`/`Foreground`/`Background`) i `CommandFailed` na `StartApp`. Awaria aplikacji w `Foreground` pokazuje shell, routuje `shell` i prosi o fokus na kafelku `<appId>` z nowym `seq`. Wyjście z `Failed` tylko przez `RestartApp` i `CloseApp`, czyli `Failed → Stopping`.

## Boundaries & Constraints

**Always:** komendy fire-and-forget, żadnego `await` w handlerze (AD-14); `cmdId`, `epoch` i `seq` nadaje wyłącznie Orchestrator; `Failed` wchodzi tylko ze `Starting`, `Foreground` i `Background`; `focusRequest` dostaje nowy `seq` wyłącznie przy awarii z `Foreground`; wyjście z `Failed` prowadzi przez `Stopping` (`StopApp`), a dopiero potem `NotRunning` albo `Starting`; nowe rekordy przez `MessageTypes.All` + `SchemaExport` (AD-16); `AppLifecycleState` bez zmian.

**Never:** timery, `stoppingTimeout` i timeout `Starting` (story 5/11), `transitionId` i Home w trakcie `Starting` (story 6), adopcja (story 7), `RenewSession`/wybudzenie (story 8), prawdziwe hosty, Win32/IO w Core, zmiany w `CommandRouter`, `MissingHostPort`, `CompositionRoot`, `web/**`, `config/**`, `ShellBridge`, pliki ticketów, nowe wpisy logu.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Start nieudany | `CommandFailed{cmdId StartApp}` po `LaunchApp` | `Starting → Failed`; brak komend; `rev`+1, `focusRequest` bez zmian | `Unexpected` z adaptera „brak hosta” działa tak samo |
| Awaria na pierwszym planie | `ProcessExited("netflix")` albo `CdpLost("netflix")` w `Foreground` | `Failed`; `ShowShell`, `SetInputRouting{shell, null, blocked, epoch+1}`; `focusRequest{"netflix", seq+1}`; `rev`+1 | — |
| Awaria w tle | `CdpLost`/`ProcessExited` w `Background` | `Failed`; brak komend; `rev`+1, `focusRequest` i `seq` bez zmian | — |
| Awaria w trakcie startu | `CdpLost` w `Starting` | `Failed`; brak komend; `rev`+1, `focusRequest` bez zmian | — |
| Zakończenie przy zamykaniu | `ProcessExited` w `Stopping` z korelacją stopu | `NotRunning` (zachowanie story 10); nigdy `Failed` | spóźniony `CommandCompleted` ignorowany |
| Fakty poza żywym cyklem | `ProcessExited`/`CdpLost` w `NotRunning`, `Stopping` bez korelacji albo `Failed` | brak komend i snapshotu | spóźniony fakt nie wskrzesza `Failed` |
| `Failed` ignoruje wejścia | `LaunchApp`, `Action{Home}`, `CdpLost`, `ProcessExited`, `AppWindowAttached` w `Failed` | brak komend i snapshotu | — |
| Restart z `Failed` | `RestartApp` w `Failed` | `Stopping` + `StopApp`; `CommandCompleted`/`ProcessExited` daje `Starting` + `StartApp`; `AppWindowAttached` daje `Foreground` z `ActivateWindow` i `SetInputRouting{app}` | nieudany `StopApp` zostawia `Stopping` (story 10) |
| Zamknięcie z `Failed` | `CloseApp` w `Failed` | `Stopping` + `StopApp`; `CommandCompleted`/`ProcessExited` daje `NotRunning` | — |
| Inna aplikacja aktywna | `CloseApp`/`RestartApp` w `Failed`, gdy inna aplikacja jest w `Foreground`/`Starting` | brak komend i snapshotu | ten sam guard co story 10 |
| Nieznana aplikacja | `CdpLost`/`ProcessExited`/`CommandFailed` z `appId` lub `cmdId` spoza configu | brak komend i snapshotu | — |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Contracts/Messages.cs:69-76` — po `ProcessExited` dopisz `CdpLost(string AppId) : Event` (AD-10: BrowserHost po nieudanym reconnecie w `cdpReconnectTimeout`); popraw dok `ProcessExited` (`:65-69`), bo od tej story `Starting`/`Foreground`/`Background` prowadzą w `Failed`, a nie „są ignorowane”.
- `src/MediaStation.Core/Contracts/MessageTypes.cs:31-32` — `typeof(CdpLost)` po `typeof(ProcessExited)`, przed `ForegroundChanged`; `SchemaExport` sam doda `cdp-lost.schema.json` i TS.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — stan i przejścia:
  - `:59-72` `switch` dostaje `CdpLost lost => OnCdpLost(lost)`.
  - `:193-206` `OnStop` przyjmuje `Background` albo `Failed` (reszta bez zmian, z `AnotherAppIsActive`); dok `:187-192` mówi o wyjściu z `Failed`.
  - `:237-241` `OnCommandFailed`: `_pendingStarts.Remove(cmdId, out var appId)` → wspólne wejście w `Failed` (stan `Starting`), w przeciwnym razie brak zmian (porażka `StopApp` zostawia `Stopping`).
  - `:248-263` `OnProcessExited`: gałąź `Stopping` z korelacją zostaje bez zmian; nowe `Starting`/`Foreground`/`Background` → wspólne wejście w `Failed`; `NotRunning`/`Failed`/`Stopping` bez korelacji ignorowane.
  - Nowe `OnCdpLost` i wspólne `EnterFailed(AppState app, bool wasForeground)`: `Hwnd = null`, `State = Failed`; tylko `wasForeground` daje `ShowShell`, `SetInputRouting(Shell, null, Blocked, NextEpoch())` i `_focus = new FocusRequest(app.AppId, ++_focusSeq)`; snapshot w obu przypadkach.
  - `:343` `Hwnd` czyścimy przy wejściu w `Failed` (okno po awarii nie jest już nasze); `OnStopped` (`:266-281`) bez zmian.
- `tests/MediaStation.Core.Tests/OrchestratorTests.cs` — fixture `Started()`/`StateOf`/`Foreground()`/`Background()` (`:17-40`) wystarczają. Zastąp `:178-190` (`Command_failed_for_start_keeps_starting...`) testem wejścia w `Failed`; przepisz `:611-636` (`Process_exited_outside_stopping_is_ignored`), bo `Starting`/`Foreground`/`Background` dają teraz `Failed`; rozszerz `:587-609` o `CdpLost`; nowe testy `Failed` po `:667`; zachowaj `Close_or_restart_outside_background_is_ignored` (`:551-585`) bez `failed` na liście ignorowanych.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs:144-187` — adapter „brak hosta” kończy start w `Failed`: czekaj na trzeci snapshot i porównaj stan; nazwa testu mówi o `Failed`.
- `tests/MediaStation.Core.Tests/CoreMessageContractTests.cs` — `:170-179` wzorzec koperty `ProcessExited` dla `CdpLost`; `:378` lista nazw rejestru z `cdpLost`; `:397-421` lista plików z `cdp-lost.schema.json` (po `apps-config.schema.json`); `:507-530` wiersz `cdp-lost.schema.json`/`CdpLost`/`appId`; `:532-550` `appId` jako `string`.
- Reuse: `AppLifecycleState.Failed` (już istnieje), `FocusRequest`, `ErrorCode.Unexpected`, `RecordingLog`; nie ruszać `ShellBridge`, `web/shell`, `config/**`, `tools/**`.

## Tasks & Acceptance

**Execution:**
- [x] `Core/Contracts/Messages.cs`, `Core/Contracts/MessageTypes.cs` -- `CdpLost` + rejestr -- fakt AD-10 z eksportem do JSON Schema/TS (AD-16).
- [x] `Core/Orchestrator/Orchestrator.cs` -- wejście w `Failed` z `CommandFailed`/`ProcessExited`/`CdpLost`, shell+fokus przy awarii z `Foreground`, `OnStop` dla `Failed` -- cykl życia AD-10/AD-14.
- [x] `tests/MediaStation.Core.Tests/*` -- każdy wiersz macierzy, ścieżka `verify` ticketu, rejestr/schematy i pętla -- dowód na fake'ach.

**Acceptance Criteria:**
- Given `netflix` w `Starting` po `LaunchApp`, when `CommandFailed{cmdId, Unexpected}`, then stan to `Failed`, brak komend, `rev`+1 i ten sam `FocusRequest`.
- Given `netflix` w `Foreground`, when `ProcessExited("netflix")` albo `CdpLost("netflix")`, then stan to `Failed`, komendy to dokładnie `ShowShell` i `SetInputRouting{shell, null, blocked, epoch+1}`, a snapshot ma `rev`+1 i `FocusRequest{"netflix", seq+1}`.
- Given `netflix` w `Background`, when `CdpLost("netflix")` albo `ProcessExited("netflix")`, then stan to `Failed`, brak komend, a `FocusRequest` i `seq` się nie zmieniają.
- Given `netflix` w `Failed`, when `LaunchApp("netflix")`, `Action{Home}`, `CdpLost` albo `ProcessExited`, then brak komend i snapshotu.
- Given `netflix` w `Failed`, when `RestartApp("netflix")`, then `Stopping` z `StopApp`; `CommandCompleted` na tym `cmdId` (albo `ProcessExited` w `Stopping`) daje `Starting` z `StartApp`, a `AppWindowAttached` daje `Foreground` z `ActivateWindow` i `SetInputRouting{app}`.
- Given `netflix` w `Failed`, when `CloseApp("netflix")` i `CommandCompleted`, then `NotRunning` bez komend.
- Given `ProcessExited("netflix")` po `CloseApp`, when `Handle`, then `NotRunning`, nigdy `Failed`; `Stopping` bez korelacji i `NotRunning` ignorują `ProcessExited`/`CdpLost` bez snapshotu.
- Given `CdpLost("netflix")`, when `ContractJson.SerializeEnvelope<Event>`, then dokładnie `{"v":1,"type":"cdpLost","payload":{"appId":"netflix"}}` i round-trip, a rejestr oraz eksport schematów zawierają `cdpLost`/`cdp-lost` z `title`, `required` i `additionalProperties: false`.
- Given pętla z `MissingHostPort`, when `LaunchApp("netflix")`, then po `CommandFailed` publikowany jest snapshot z `Failed` (a nie `Starting`).

## Implementation Notes

- `EnterFailed(AppState, bool wasForeground)` jest jedynym wejściem w `Failed`. Wołający czyta stan przed mutacją (`app.State == Foreground` w argumencie), więc `wasForeground` decyduje o `ShowShell` + `SetInputRouting{shell}` + nowym `seq`; awaria z `Starting`/`Background` daje tylko snapshot.
- `OnCommandFailed` wchodzi w `Failed`, gdy `cmdId` jest w `_pendingStarts` **i** aplikacja jest w `Starting`, `Foreground` albo `Background`; dla `Foreground` `wasForeground` dokłada shell, routing i fokus. Dzięki temu porażka startu dociera też po `AppWindowAttached`, a guard chroni nowy start przed spóźnioną porażką martwej korelacji. Porażka `StopApp` dalej zostawia `Stopping` (story 10/11 bez zmian).
- `EnterFailed` dokłada `DropPendingStarts(appId)`: po awarii korelacje startu tej aplikacji przepadają, więc spóźniony `CommandFailed`/`CommandCompleted` z poprzedniego startu nie może trafić w aplikację już restartowaną z `Failed`. To rozszerzenie opisu z Code Map, wymuszone wierszem „`Failed` ignoruje wejścia” i „spóźniony fakt nie wskrzesza `Failed`”; nie dokłada komend ani wpisów logu.
- Kolejność komend przy awarii z `Foreground` to `ShowShell`, potem `SetInputRouting(Shell, null, Blocked, NextEpoch())`; fokus dostaje `FocusRequest(appId, ++_focusSeq)`. Bez `MinimizeWindow`, bo okna/procesu może już nie być.
- `ProcessExited` w `Stopping` z korelacją zostaje bez zmian (story 10); `CdpLost` w `Stopping` jest ignorowany, bo zamykana aplikacja kończy się `NotRunning`, a timeout `Stopping` należy do story 11. `NotRunning` i `Failed` ignorują oba fakty.
- `Stopping` bez korelacji stopu jest nieosiągalny z publicznych przejść (`OnStop` zawsze dodaje wpis, a zdejmuje go tylko `OnStopped`/`ProcessExited`, zmieniając stan), więc gałąź `FindPendingStop == null` jest obronna. Test `Facts_in_stopping_without_stop_correlation_are_ignored` czyści korelację przez refleksję (świadome odstępstwo, żeby pokryć ten wiersz macierzy), a `CdpLost` w `Stopping`, `NotRunning` i `Failed` mają osobne testy.
- `CdpLost` wszedł do `MessageTypes.All` między `ProcessExited` a `ForegroundChanged`; `SchemaExport` wygenerował `cdp-lost.schema.json`, a `web/scripts/contracts.mjs` — `cdp-lost.ts` (bez ręcznej edycji `web/**`).
- Poprawione martwe odwołania do story 4 w dokach: `ProcessExited` (`Messages.cs`), `OnStop`/`OnCommandFailed`/`OnProcessExited`/`AppState.Hwnd` (`Orchestrator.cs`) oraz komentarz przy logu `commandFailed` (`OrchestratorLoop.cs:114`); żadnego nowego wpisu logu.
- Testy: 432/432 (407 przed story; +25 przypadków w `OrchestratorTests`, `OrchestratorLoopTests`, `CoreMessageContractTests`).

## Plan Change Log

### 2026-10-22 — Implementation
- Code Map `Orchestrator.cs` → `EnterFailed`: oprócz `Hwnd = null` i `State = Failed` usuwa też korelacje startu tej aplikacji (`DropPendingStarts`), żeby spóźniony wynik startu po `CdpLost`/`ProcessExited` nie działał na aplikacji restartywanej z `Failed` (wiersz macierzy „`Failed` ignoruje wejścia”).
- Code Map `Orchestrator.cs` → `OnCommandFailed`: oprócz obecności `cmdId` w `_pendingStarts` wymagany jest żywy stan (`Starting`, `Foreground` albo `Background`, z `wasForeground` dla `Foreground`), bo porażka startu może dotrzeć po `AppWindowAttached`; spóźniona porażka martwego startu dalej nie zmienia stanu.
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs`: odświeżony komentarz przy `commandFailed` (plik spoza Code Map; zmiana wyłącznie w komentarzu, bez nowego wpisu logu).
- Wiersz macierzy „`Stopping` bez korelacji” nie ma publicznego przejścia, więc test `Facts_in_stopping_without_stop_correlation_are_ignored` usuwa korelację stopu przez refleksję (jedyne odstępstwo od stylu testów, żeby pokryć ten wiersz) i sprawdza brak komend/snapshotu oraz pozostanie w `Stopping`; `CdpLost` w `Stopping` zachowuje się jak dotąd (zamknięcie kończy `NotRunning`).

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 5 findings — high 0, medium 2, low 3, false 0, maybe-false 0
- findings:
  - `[medium]` `[patch]` `OnCommandFailed` wymagał stanu `Starting`, więc `CommandFailed{CdpUnavailable}` po `AppWindowAttached` (stan `Foreground`, albo `Background` po Home) przepadał i kafelek nie mógł dojść do `Failed` — AD-13 wprost mówi, że przy żywym procesie i nieprzyjętym porcie CDP kafelek pokazuje `Failed`; poprawione: akceptowane `Starting`/`Foreground`/`Background`, a `Foreground` dostaje shell, routing i fokus; nowy test `Command_failed_for_start_after_attached_window_enters_failed_from_foreground`.
  - `[medium]` `[defer]` Spóźniony `ProcessExited` procesu zabitego przez `RestartApp` może trafić w świeżo zrestartowaną aplikację (`Starting`) i pokazać `Failed`, mimo że nowy proces żyje — fakt niesie tylko `appId`, a korelacja stopu jest już zużyta. Źródło w kontrakcie story 10 (brak tożsamości procesu), nie w tej zmianie; rozstrzygnie story hosta 2.9 (`pid`/dedupe) albo test pętli z dwoma faktami na jeden stop. Zapisane w `deferred`.
  - `[low]` `[patch]` Doki XML `CloseApp`/`RestartApp` nadal mówiły „wyłącznie w `Background`”, sprzecznie z `OnStop` przyjmującym też `Failed` — poprawione w `Messages.cs`.
  - `[low]` `[patch]` Obie ochrony z Plan Change Log (`DropPendingStarts` i żywy stan w `OnCommandFailed`) nie miały testów, więc dało się je usunąć bez czerwonego suite — dodane `Stale_start_result_after_restart_in_failed_does_not_change_restarted_app` (spóźniony `CommandCompleted`/`CommandFailed` starego `cmdId` po restarcie) obok testu porażki startu po `AppWindowAttached`.
  - `[low]` `[patch]` Komentarz przy `commandFailed` w pętli sugerował, że każda porażka wykonawcy daje `Failed`, a nieudany `StopApp` zostawia `Stopping` — doprecyzowany.

## Design Notes

`ProcessExited` i `CdpLost` w `NotRunning` są ignorowane, mimo że ticket mówi „poza `Stopping`”: spóźniony fakt po zamknięciu aplikacji nie może wskrzesić kafelka `Failed`, a `NotRunning` nie ma procesu, którego awarię mógłby raportować. `Stopping` należy do korelacji stopu (story 10) i przyszłego timeoutu (story 11), więc `CdpLost` w `Stopping` też jest ignorowany — zamykana aplikacja kończy się `NotRunning`, a nie `Failed`.

`Failed` czyści `Hwnd`, bo okno po awarii nie jest już pod kontrolą Orchestratora; restart i tak przechodzi przez `StopApp` po `appId`, a nowy `AppWindowAttached` ustawia świeży uchwyt. Guard „inna aplikacja w `Foreground`/`Starting`” obowiązuje tak samo dla wyjścia z `Failed`: restart ze `Stopping` skończyłby się drugim `Foreground`, a `CloseApp` w story 10 ma ten sam guard.

Kolejność komend przy awarii z `Foreground` to `ShowShell`, potem routing (bez `MinimizeWindow`, bo procesu/okna może już nie być); AD-5/AD-15 wymagają shella na wierzchu, zanim InputHub przełączy tryb. Fokus dostaje `<appId>`, bo UI ma podświetlić kafelek, który padł (AD-16, EXPERIENCE J4).

## Verification

**Commands:**
- `dotnet test MediaStation.slnx -c Release` -- wszystkie testy zielone; nowe przypadki w `OrchestratorTests`, `OrchestratorLoopTests`, `CoreMessageContractTests`.
- `dotnet build MediaStation.slnx -c Release` -- 0 ostrzeżeń; `SchemaExport` zapisuje `cdp-lost.schema.json`, `npm run contracts` generuje `cdp-lost.ts`, `npm run typecheck` i Vite przechodzą.
- `dotnet build src/MediaStation.App -c Debug` -- 0 ostrzeżeń.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + uruchomienie z ważnym `%LOCALAPPDATA%\MediaStation\config` -- proces żyje, log ma `snapshotPublished {rev: 1}` i `stop {exitCode: 0}`; `Failed` nie jest osiągalny z UI (brak InputHub/AppMenu), więc jego dowodem są testy Orchestratora, a UM760 zostaje w odbiorze epiku.

**Current-machine evidence (2026-10-22, HEAD 0eb92bda9eb06b72cf157af3159938028ce6b987, ALDER / B760 Pro RS/D4 / Windows 11 Home):**
- `dotnet test MediaStation.slnx -c Release` -- 432/432 passed (407 przed story; +25 przypadków). Nowe/rozszerzone: `OrchestratorTests` (`Command_failed` → `Failed` z `Unexpected` i `CdpUnavailable`, awaria w `Starting`/`Background`/`Foreground` po `ProcessExited` i `CdpLost`, `CdpLost` w `Stopping`, `Stopping` bez korelacji stopu, spóźnione fakty w `NotRunning`/`Failed`, `Failed` ignoruje `LaunchApp`/Home/`ProcessExited`/`CdpLost`/`AppWindowAttached`, restart i zamknięcie z `Failed`, guard innej aktywnej aplikacji, nieznana aplikacja także dla `cdpLost`), `OrchestratorLoopTests` (`CommandFailed` z `MissingHostPort` publikuje trzeci snapshot w `Failed`), `CoreMessageContractTests` (koperta `cdpLost`, rejestr, plik i kształt `cdp-lost.schema.json`, `appId` jako `string`).
- `dotnet build MediaStation.slnx -c Release` -- success, 0 warnings, 0 errors; `SchemaExport` zapisał `web/contracts/schema/cdp-lost.schema.json` (`title: CdpLost`, `required: [appId]`, `additionalProperties: false`), `npm run contracts` wygenerował `web/contracts/generated/cdp-lost.ts` (`export interface CdpLost { appId: string }`), `npm run typecheck` przeszedł, Vite zbudował shell.
- `dotnet build src/MediaStation.App -c Debug` -- success, 0 warnings; `npm run contracts` i `npm run typecheck` przeszły.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` -- success. Uruchomiony `MediaStation.exe` (pid 42868) z ważnym `%LOCALAPPDATA%\MediaStation\config`: okno żyło, a log `%LOCALAPPDATA%\MediaStation\logs\mediastation-2026-10-21.jsonl` (nazwa pliku z daty UTC; lokalnie był już 2026-10-22) zawiera dla tego przebiegu `start`, `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}`, `powerSignalsRegistered`, `snapshotPublished {rev: 1, focusRequest: {target: "default", seq: 1}}`; zamknięcie okna dało `stop {exitCode: 0}` i zero wpisów `warn`/`error`.
- `Failed` nie jest osiągalny z UI (brak InputHub/AppMenu), więc jego dowodem są testy Orchestratora i pętli; sprawdzenie J4 na UM760 zostaje w odbiorze epiku użytkownika.
- Po poprawkach przeglądu (2026-10-22, ten sam HEAD, ta sama maszyna) przebieg powtórzony w całości: `dotnet test MediaStation.slnx -c Release` 435/435 (dwa nowe testy: porażka startu po `AppWindowAttached` i spóźniony wynik startu po restarcie), `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` z 0 ostrzeżeń, publish self-contained i uruchomienie (pid 45876): `start`, `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}`, `powerSignalsRegistered`, `snapshotPublished {rev: 1, focusRequest {default, 1}}`, `stop {exitCode: 0}`, zero wpisów `warn`/`error`.

## Auto Run Result

**Status:** built.

**Summary:** Core zyskał Event `CdpLost` i jedno wejście w `Failed` po `CdpLost`, `ProcessExited` (`Starting`/`Foreground`/`Background`) oraz `CommandFailed` na `StartApp` (także po `AppWindowAttached`, np. `CdpUnavailable`). Awaria aplikacji w `Foreground` pokazuje shell, routuje `shell` i ustawia `focusRequest` na jej kafelku z nowym `seq`; awaria w tle albo w trakcie startu daje sam snapshot. Wyjście z `Failed` prowadzi wyłącznie przez `StopApp` (`RestartApp` → `Stopping` → `Starting` → `Foreground`, `CloseApp` → `NotRunning`), a `LaunchApp`, Home, spóźnione fakty i ponowne awarie są ignorowane.

**Files changed:**
- `src/MediaStation.Core/Contracts/Messages.cs` — `CdpLost`; poprawione doki `ProcessExited`, `CloseApp` i `RestartApp` (`Background`/`Failed`).
- `src/MediaStation.Core/Contracts/MessageTypes.cs` — `CdpLost` w rejestrze koperty.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — `EnterFailed`, `OnCdpLost`, `DropPendingStarts`, wejście w `Failed` z `CommandFailed`/`ProcessExited`, `OnStop` dla `Failed`.
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` — doprecyzowany komentarz przy `commandFailed` (bez nowego wpisu logu).
- `tests/MediaStation.Core.Tests/` — rozszerzone `OrchestratorTests`, `OrchestratorLoopTests`, `CoreMessageContractTests` (27 nowych przypadków).

**Review findings (quick):** 5 findings — high 0, medium 2, low 3. Patches applied: 4 (poraźka startu po `AppWindowAttached` + test; doki `CloseApp`/`RestartApp`; testy ochron `DropPendingStarts` i żywego stanu; komentarz pętli). Deferred: 1 (spóźniony `ProcessExited` po restarcie — tożsamość procesu należy do hosta/story 2.9; wpis w `deferred`). Rejected: 0.

**Patched counts by verdict:** high 0, medium 1, low 3.

**Follow-up review recommendation:** `false`.

**Verification:** `dotnet test MediaStation.slnx -c Release` 435/435; `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` z 0 ostrzeżeń (`cdp-lost.schema.json`, `cdp-lost.ts`, typecheck i Vite zielone); publish self-contained i uruchomienie z ważnym configiem (pid 45876): `snapshotPublished {rev: 1, focusRequest {default, 1}}`, `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}`, `stop {exitCode: 0}`, brak `warn`/`error`.

**Residual risks:** `Failed` nie jest jeszcze osiągalny z UI (brak InputHub/AppMenu), więc ścieżkę potwierdzają testy Core i pętli, a całościowy test UM760 (J4) zostaje w odbiorze epiku użytkownika. Spóźniony `ProcessExited` po restarcie czeka na tożsamość procesu w Evencie (story hosta 2.9). Timeout `Stopping` i `Starting` należy do stories 11 i 5, a przełączanie między dwiema żywymi aplikacjami i `transitionId` do story 6. Test wiersza „`Stopping` bez korelacji” sięga po pole przez refleksję, bo ten stan nie powstaje z publicznych przejść.
