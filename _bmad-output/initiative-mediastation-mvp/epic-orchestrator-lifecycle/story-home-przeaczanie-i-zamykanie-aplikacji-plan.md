---
title: 'Home, przełączanie i zamykanie aplikacji'
type: 'feature'
ticket: '10'
created: '2026-10-22'
status: done
baseline_revision: '2244b079fbec392e440e9c75c88152f7eb22305c'
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
deferred: []
---

<intent-contract>

## Intent

**Problem:** Orchestrator nie umie wrócić na Home, przywrócić aplikacji z tła, zamknąć jej ani zrestartować: `Action{Home}` wpada w `_ => None`, `LaunchApp` obsługuje wyłącznie `NotRunning`, a `CloseApp`/`RestartApp`/`StopApp`/`ProcessExited`/`ForegroundChanged` nie istnieją. Stany `Background` i `Stopping` są nieosiągalne, więc story 4, 6, 8 i 11 nie mają się do czego podpiąć.

**Approach:** Core dostaje `Action{Home}`, Intents `CloseApp{appId}`/`RestartApp{appId}`, komendę `StopApp{cmdId, appId}`, Eventy `ProcessExited{appId}` i `ForegroundChanged{hwnd}` oraz zapamiętany `hwnd` z `AppWindowAttached`. Orchestrator prowadzi `Foreground ⇄ Background` (Home → `MinimizeWindow`, `ShowShell`, `SetInputRouting{shell}`; `LaunchApp` z tła → `ActivateWindow`, `SetInputRouting{app}`) i `Background → Stopping → NotRunning | Starting` (Close/Restart, zamknięcie na `CommandCompleted{StopApp}` albo oczekiwanym `ProcessExited`). App kieruje `StopApp` do hosta i loguje `foregroundChanged`.

## Boundaries & Constraints

**Always:** komendy fire-and-forget, żadnego `await` w handlerze (AD-14); `cmdId`/`epoch` nadaje wyłącznie Orchestrator; Home wysyła komendy w kolejności `MinimizeWindow`, `ShowShell`, `SetInputRouting{shell}` i nie zmienia `focusRequest`; `CloseApp`/`RestartApp` działają tylko na `Background`; `ProcessExited` kończy przejście wyłącznie ze `Stopping`; nowe rekordy przez `MessageTypes.All` + `SchemaExport` (AD-16); `AppLifecycleState` bez zmian.

**Never:** przejście w `Failed` (story 4), timery i `stoppingTimeout` (story 11), `transitionId` i Home w trakcie `Starting` (story 6), adopcja (story 7), `RenewSession`/wybudzenie (story 8), pozostałe Actions i `Action{AppMenu}` (epic-home-screen), `restore`/`unmute`/`pause` (epic-windows-audio), prawdziwe hosty, Win32/IO w Core, ręczna edycja `web/contracts/**` i `web/shell/**`, `ShellBridge`, pliki ticketów.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Home z pierwszego planu | `Action{home}`, `netflix` `Foreground` z `hwnd` | `netflix` `Background`; komendy `MinimizeWindow{hwnd}`, `ShowShell`, `SetInputRouting{shell, null, blocked, epoch+1}`; snapshot `rev`+1, `focusRequest` bez zmian | — |
| Home bez pierwszego planu | `Action{home}` przy shellu albo gdy jedyna aplikacja jest `Starting`/`Stopping`/`Background` | brak komend i snapshotu | ignorowane (Home w `Starting` → story 6) |
| Inny Action | `Action{up/ok/...}` | brak komend i snapshotu | ignorowane |
| Powrót z tła | `LaunchApp("netflix")`, `netflix` `Background` z `hwnd`, nic innego w `Foreground`/`Starting` | `Foreground`; `ActivateWindow{hwnd}`, `SetInputRouting{app, web, blocked, epoch+1}`; snapshot `rev`+1, focus bez zmian | — |
| Powrót z tła w trakcie innej aplikacji | `LaunchApp("plex")` gdy `netflix` jest `Foreground` albo `Starting` | brak komend i snapshotu | ignorowane (seam story 6) |
| Zamknięcie | `CloseApp("netflix")`, `Background` | `Stopping`; `StopApp{cmdId, "netflix"}`; snapshot `rev`+1 | — |
| Zamknięcie poza tłem | `CloseApp`/`RestartApp` w `NotRunning`/`Starting`/`Foreground`/`Stopping` | brak komend i snapshotu | ignorowane (story 4 dołoży `Failed`) |
| Stop potwierdzony | `CommandCompleted{cmdId StopApp}` po `CloseApp` | `NotRunning`, snapshot `rev`+1, bez komend | nieznany `cmdId` ignorowany |
| Stop przez proces | `ProcessExited("netflix")` w `Stopping` po `CloseApp` | `NotRunning`, snapshot `rev`+1 | bez korelacji ignorowane |
| Restart potwierdzony | `CommandCompleted{cmdId StopApp}` po `RestartApp` | `Starting`, `StartApp{nowy cmdId}`; dalej `AppWindowAttached` → `Foreground` jak po `LaunchApp` | — |
| Restart przez proces | `ProcessExited` w `Stopping` po `RestartApp` | `Starting`, `StartApp{nowy cmdId}` | — |
| Stop nieudany | `CommandFailed{cmdId StopApp}` | stan zostaje `Stopping`, korelacja zostaje (domknie ją `ProcessExited` albo story 11) | pętla loguje `commandFailed` |
| Proces poza zamykaniem | `ProcessExited` w `NotRunning`/`Starting`/`Foreground`/`Background` | brak komend i snapshotu | ignorowane (story 4) |
| Zmiana foreground | `ForegroundChanged{1234}` | brak komend i snapshotu; wpis logu `foregroundChanged {hwnd}` | — |
| Nieznana aplikacja | `LaunchApp`/`CloseApp`/`RestartApp`/`ProcessExited` z `appId` spoza configu | brak komend i snapshotu | ignorowane |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Contracts/Messages.cs:36` — po `LaunchApp` dopisz `CloseApp(string AppId) : Intent` i `RestartApp(string AppId) : Intent`; po `AppWindowAttached` (`:50`) dopisz `ProcessExited(string AppId) : Event` i `ForegroundChanged(long Hwnd) : Event`; popraw dok XML `LaunchApp` (`:33-35`), bo od tej story uruchamia też `Background`.
- `src/MediaStation.Core/Contracts/AppCommands.cs:7` — po `StartApp` dopisz `StopApp(long CmdId, string AppId) : Command(CmdId)` (appId tylko, jak `StartApp`).
- `src/MediaStation.Core/Contracts/MessageTypes.cs:14-28` — kolejność: `Action`, `LaunchApp`, `CloseApp`, `RestartApp`, `SetInputRouting`, `StartApp`, `StopApp`, `ActivateWindow`, `MinimizeWindow`, `ShowShell`, `CommandCompleted`, `CommandFailed`, `OrchestratorStarted`, `SystemResumed`, `AppWindowAttached`, `ProcessExited`, `ForegroundChanged`.
- `src/MediaStation.Core/Ports/Ports.cs:10-14` — `IAppHostPort` zyskuje `void Stop(StopApp command)` obok `Start`; komentarz o granicy portu bez zmian (AD-4).
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — stan i przejścia:
  - `:26` obok `_pendingStarts` dołóż `Dictionary<long, PendingStop> _pendingStops` z `private sealed record PendingStop(string AppId, bool Restart)`.
  - `:176-187` `AppState` zyskuje `public long? Hwnd { get; set; }` (z `AppWindowAttached`; czyszczony przy `NotRunning`/restarcie).
  - `:57-66` `switch`: `Action` → `OnHome` (alias `using ActionMessage = MediaStation.Core.Contracts.Action;` — `Action` jest niejednoznaczne z `System.Action`), `CloseApp`/`RestartApp` → wspólny `OnStop(appId, restart)`, `ProcessExited` → `OnProcessExited`; `ForegroundChanged` zostaje w `_`.
  - `:94-105` `OnLaunchApp`: `NotRunning` bez zmian (start), nowa gałąź `Background` → `Foreground` z `ActivateWindow(hwnd)` + `SetInputRouting(App, app.Type, app.Pointer, NextEpoch())`, pomijana gdy brak `hwnd` albo inna aplikacja jest `Foreground`/`Starting`; każdy inny stan ignorowany.
  - `:111-122` `OnAppWindowAttached`: dodatkowo `app.Hwnd = attached.Hwnd`.
  - Nowe: `OnHome` (znajdź aplikację w `Foreground`, ustaw `Background`, komendy `MinimizeWindow(hwnd)`, `ShowShell()`, `SetInputRouting(Shell, null, Blocked, NextEpoch())`, snapshot) i `OnStop` (guard `state != Background` → `None`; `Stopping`; `StopApp`; `_pendingStops`).
  - `:128-141` `OnCommandCompleted`: najpierw `_pendingStops.Remove(cmdId)` → `OnStopped` (`Restart` → `Starting` + `StartApp` w `_pendingStarts`; inaczej `NotRunning`), potem dotychczasowe `_pendingStarts.Remove`. `OnCommandFailed` zostawia wpisy w `_pendingStops` (fakt procesu albo story 11 domyka `Stopping`), usuwa tylko `_pendingStarts`.
  - Nowe `OnProcessExited`: tylko dla `Stopping` z korelacją w `_pendingStops` (szukaj po `AppId`) → `OnStopped`; poza `Stopping` ignorowane (story 4). `OnStopped` zeruje `Hwnd`.
- `src/MediaStation.App/Orchestrator/CommandRouter.cs:52-72` — `case StopApp stop:` → host po `AppType`; wydziel wspólne `TryGetHost(cmdId, appId, out host)` z `DispatchStart` (`:74-98`), żeby `commandForUnknownApp`/`commandWithoutPort` zostały jednym kodem.
- `src/MediaStation.App/Hosts/MissingHostPort.cs:32-43` — `Stop(StopApp)` loguje `warn stopWithoutHost {cmdId, appId}` i wkłada `CommandFailed{cmdId, Unexpected}` (symetria z `Start`).
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs:110-131` — obok `commandFailed` (`:115-122`) dołóż log `Info foregroundChanged {hwnd}` dla `message is ForegroundChanged`; bez zmiany kolejności dispatch → publish.
- `src/MediaStation.App/CompositionRoot.cs` — bez zmian: `MissingHostPort` per `AppType` już jest w mapie hostów (`:117-123`), więc `StopApp` trafia tam bez nowego src.
- `tests/MediaStation.Core.Tests/OrchestratorTests.cs` — fixture `Started()` (`:17-22`) i `StateOf` (`:24-25`) wystarczają; pomócnik `Foreground()` (Launch + Attach) i `Background()` (+ Home) skraca nowe testy; `Launch_app_outside_not_running_is_ignored` (`:116-134`) zostaje dla `starting`/`foreground` (tło ma osobną gałąź).
- `tests/MediaStation.Core.Tests/CommandRouterTests.cs:132-137` — `FakeHost` dostaje `Stopped`; `CreateRouter` bez zmian.
- `tests/MediaStation.Core.Tests/MissingHostPortTests.cs` — wzorzec `startWithoutHost` do powtórzenia dla `stopWithoutHost`.
- `tests/MediaStation.Core.Tests/CoreMessageContractTests.cs:309` — lista nazw rejestru; `:328-347` — lista plików schematów (`close-app`, `restart-app`, `stop-app`, `process-exited`, `foreground-changed` dochodzą); wzorce kopert jak dla `LaunchApp`/`StartApp`.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs:207-227` — `CreateLoop` + `RecordingLog` wystarczają do testu `foregroundChanged`.
- Reuse: `ErrorCode.Unexpected`, `AppLifecycleState`, `FocusRequest`, `RecordingLog`, `NullLog`, `JsonLinesLog` (bez zmian). Nie ruszać `ShellBridge`, `web/**`, `config/**`, `tools/**`.

## Tasks & Acceptance

**Execution:**
- [x] `Core/Contracts/Messages.cs`, `Core/Contracts/AppCommands.cs`, `Core/Contracts/MessageTypes.cs` -- `CloseApp`, `RestartApp`, `StopApp`, `ProcessExited`, `ForegroundChanged` i rejestr -- kontrakty AD-16 z eksportem do JSON Schema/TS.
- [x] `Core/Ports/Ports.cs` -- `IAppHostPort.Stop` -- seam zamykania dla hostów.
- [x] `Core/Orchestrator/Orchestrator.cs` -- Home, powrót z tła, `Stopping`, korelacja stopu, `ProcessExited`, `hwnd` -- stan cyklu życia AD-10/AD-14.
- [x] `App/Orchestrator/CommandRouter.cs`, `App/Hosts/MissingHostPort.cs` -- `StopApp` do hosta i adapter „brak hosta” -- jedno wyjście do wykonawców.
- [x] `App/Orchestrator/OrchestratorLoop.cs` -- log `foregroundChanged` -- fakt AD-15 zapisany w logu bez reakcji.
- [x] `tests/MediaStation.Core.Tests/*` -- każdy wiersz macierzy, ścieżki `verify` ticketu, rejestr/schematy, router i pętla -- dowód działania na fake'ach.

**Acceptance Criteria:**
- Given `netflix` w `Foreground` (po `LaunchApp` i `AppWindowAttached`), when `Action{home, press}`, then stan to `Background`, komendy to dokładnie `MinimizeWindow{5, hwnd}`, `ShowShell{6}`, `SetInputRouting{7, shell, null, blocked, epoch 3}`, snapshot ma `rev`+1 i ten sam `FocusRequest{default, 1}`.
- Given shell na wierzchu (`OrchestratorStarted` bez aplikacji w `Foreground`) albo aplikacja w `Starting`, when `Action{home}`, then brak komend i brak snapshotu, a `rev` nie rośnie.
- Given `netflix` w `Background` z `hwnd`, when `LaunchApp("netflix")`, then stan to `Foreground`, komendy to `ActivateWindow{hwnd}` i `SetInputRouting{app, web, blocked, epoch+1}`, snapshot ma `rev`+1 bez nowego `seq`; gdy `plex` jest w `Foreground` albo `Starting`, ten sam `LaunchApp` daje brak komend i snapshotu.
- Given `netflix` w `Background`, when `CloseApp("netflix")`, then stan to `Stopping`, jedyną komendą jest `StopApp{cmdId, "netflix"}`, a snapshot ma `rev`+1; `CommandCompleted` na tym `cmdId` oraz `ProcessExited("netflix")` w `Stopping` prowadzą do `NotRunning` z `rev`+1 i bez komend.
- Given `netflix` w `Background`, when `RestartApp("netflix")` i `CommandCompleted` na `StopApp` (albo `ProcessExited("netflix")` w `Stopping`), then stan to `Starting` z `StartApp{nowy cmdId}`, a następne `AppWindowAttached` daje `Foreground` z `ActivateWindow` i `SetInputRouting{app}`.
- Given `CloseApp`/`RestartApp` dla `NotRunning`, `Starting`, `Foreground` i `Stopping` oraz `LaunchApp`/`CloseApp`/`RestartApp`/`ProcessExited` z nieznanym `appId`, when `Handle`, then wynik to brak komend i brak snapshotu, a stan i `rev` się nie zmieniają.
- Given `ProcessExited` dla aplikacji w `NotRunning`, `Starting`, `Foreground` albo `Background`, when `Handle`, then brak komend i snapshotu (przejście w `Failed` należy do story 4); `CommandFailed` na `StopApp` zostawia stan `Stopping` i pozwala domknąć go późniejszym `ProcessExited`.
- Given `ForegroundChanged{1234}`, when pętla z `RecordingLog`, then log ma `foregroundChanged {hwnd: 1234}` ze src `orchestrator`, nie ma nowego snapshotu, a po nim `OrchestratorStarted` daje `rev` 1.
- Given `CommandRouter` z hostami per `AppType`, when `StopApp`, then trafia do hosta swojego `AppType`; `StopApp` z nieznanym `appId` daje `warn commandForUnknownApp`, a `StopApp` bez hosta `warn commandWithoutPort`, bez wyjątku.
- Given `StopApp` do `MissingHostPort`, when `Stop`, then log zawiera `stopWithoutHost {cmdId, appId}`, a kolejka `CommandFailed` z tym samym `cmdId` i `ErrorCode.Unexpected`.
- Given `CloseApp("netflix")`, when `ContractJson.SerializeEnvelope<Intent>`, then dokładnie `{"v":1,"type":"closeApp","payload":{"appId":"netflix"}}` i round-trip; analogicznie `restartApp`, a `MessageTypes.All` i eksport schematów zawierają `closeApp`, `restartApp`, `stopApp`, `processExited`, `foregroundChanged` z `title`, `required` i `additionalProperties: false`.
- Given cała ścieżka ticketu, when testy `OrchestratorTests`, then przechodzą `Foreground → Background` (Home, komendy w podanej kolejności, bez nowego `seq`) `→ Foreground` (`LaunchApp`) `→ Background → Stopping → NotRunning` (`CloseApp`) oraz `Background → Stopping → Starting → Foreground` (`RestartApp`), a `ForegroundChanged` daje wpis w logu bez zmiany stanu.

## Implementation Notes

- `OnLaunchApp` rozdziela trzy przypadki: nieznana aplikacja → `None`, `Background` → `OnResume` (tylko gdy `Hwnd` znany i żadna inna aplikacja nie jest w `Foreground`/`Starting`), `NotRunning` → `StartApp`, każdy inny stan → `None`. `OnResume` jest osobną metodą, bo dzieli z Home wyłącznie intencję, a nie komendy.
- `OnHome` czyta `Action.Kind`, szuka aplikacji w `Foreground` i wymaga znanego `Hwnd` (`app?.Hwnd is not { } hwnd`). Dzięki temu Home bez pierwszego planu nie wysyła części komend: albo wszystkie trzy (`MinimizeWindow`, `ShowShell`, `SetInputRouting{shell}`), albo żadnej. `Foreground` bez `Hwnd` jest nieosiągalny (jedyna droga to `AppWindowAttached`), więc guard jest tylko zabezpieczeniem niezmiennika.
- `_pendingStops` (`cmdId → (appId, restart)`) jest jedyną korelacją stopu. `OnStop` dodaje wpis razem z `Stopping`; `OnCommandCompleted` zdejmuje go **przed** `_pendingStarts` i woła wspólny `OnStopped`, który zeruje `Hwnd` i wybiera `NotRunning` (zamknięcie) albo `Starting` + `StartApp` (restart). `OnCommandFailed` celowo nie rusza `_pendingStops`: brak potwierdzenia nie jest dowodem zniknięcia procesu, a `Stopping` domyka `ProcessExited` (timeout — story 11).
- `ProcessExited` nie niesie `cmdId`, więc `OnProcessExited` szuka korelacji po `AppId` (`FindPendingStop`), usuwa wpis i dopiero potem woła `OnStopped`; późniejszy `CommandCompleted` z tym samym `cmdId` jest już nieznany i nie zmienia stanu. Poza `Stopping` fakt jest ignorowany (story 4).
- `OnStop` (Close/Restart) i `OnResume` dzielą jeden guard `AnotherAppIsActive`: gdy inna aplikacja jest w `Foreground`/`Starting`, intencja jest ignorowana, bo restart skończyłby się w `Starting` → `Foreground` obok aplikacji już na pierwszym planie. `OnResume` zachowuje się bez zmian.
- `AppState.Hwnd` jest zapisywany w `OnAppWindowAttached` i czyszczony w `OnStopped`; `Foreground`/`Background` nie zmieniają `hwnd`, bo okno żyje przez cały cykl.
- `CommandRouter` ma jedno `TryGetHost(cmdId, appId, out host)` używane przez `StartApp` i `StopApp`; `commandForUnknownApp` i `commandWithoutPort` pozostały jednym kodem i jednym kształtem `data` (`cmdId`, `appId`, opcjonalnie `appType`). `MissingHostPort.Stop` jest symetryczny do `Start`: `warn stopWithoutHost {cmdId, appId}` + `CommandFailed{cmdId, Unexpected}`.
- `OrchestratorLoop` loguje `Info foregroundChanged {hwnd}` dokładnie tam, gdzie `commandFailed`, czyli przed `_dispatch` i przed publikacją snapshotu; kolejność dispatch → publish bez zmian, a `ForegroundChanged` nie produkuje snapshotu (Core zwraca `None`).
- Nowe rekordy weszły do `MessageTypes.All` w kolejności z planu, więc `SchemaExport` wygenerował `close-app`, `restart-app`, `stop-app`, `process-exited` i `foreground-changed` (`title`, `required`, `additionalProperties: false`), a `web/contracts/generated` dostał odpowiednie `.ts` bez ręcznej edycji.

## Plan Change Log

### 2026-10-22 — Implementation
- AC „Home z pierwszego planu” podaje `SetInputRouting{7, shell, null, blocked, epoch 2}`, ale macierz (`epoch+1`) i Code Map (`NextEpoch()`) dają `epoch 3`: `epoch 1` bierze `OrchestratorStarted`, `epoch 2` — `AppWindowAttached`, więc Home dostaje kolejny. Zaimplementowano zgodnie z macierzą i Code Map (nowy `epoch` na każdą decyzję routingu), a test `Home_from_foreground_minimizes_shows_shell_routes_shell_input_and_keeps_focus` przypina `epoch 3`.
- Code Map `CommandRouter.cs`: zamiast `DispatchStart` + `DispatchStop` powstał jeden `TryGetHost` i dwa krótkie `if` w `switch`; log `commandWithoutPort {cmdId, appId, type}` dla nieznanych typów komend bez zmian.
- Dodatkowe testy ponad macierz: `ShowShell`/`MinimizeWindow`/`SetInputRouting` Home sprawdzane też w teście całej ścieżki; `Action_other_than_home` obejmuje `Ok`/`Back`/`AppMenu` jako seam epic-home-screen.
- `OnStop` dostał ten sam guard „inna aplikacja w `Foreground`/`Starting`” co `OnResume` (wspólne `AnotherAppIsActive`), bo restart aplikacji z tła przy drugiej aplikacji na wierzchu prowadził do dwóch `Foreground`; test `Close_or_restart_in_background_is_ignored_while_another_app_is_active`.
- `Foreground_changed_is_logged_without_changing_state` publikuje po fakcie `SystemResumed` i porównuje całą sekwencję snapshotów, bo pętla loguje przed `_dispatch`/`_publish` i pojedyncze `Assert.Single` mogło przepuścić regresję.

## Review Triage Log

### 2026-10-22 — Review pass (quick)
- verdicts: 4 findings — high 0, medium 0, low 4, false 0, maybe-false 0
- findings:
  - `[low]` `[patch]` `OnStop` przyjmował `CloseApp`/`RestartApp` także wtedy, gdy inna aplikacja była w `Foreground`/`Starting`, więc restart kończył się w `Starting` → `Foreground` obok aplikacji już na pierwszym planie (dwie aplikacje w `Foreground`, a `OnResume` ten sam stan odrzucał) — naprawione: wspólny `AnotherAppIsActive` używany przez `OnResume` i `OnStop` oraz test `Close_or_restart_in_background_is_ignored_while_another_app_is_active` (inna aplikacja w `Foreground` i w `Starting`, close i restart).
  - `[low]` `[defer]` Home przy drugiej aplikacji w `Starting` działa na aplikację w `Foreground`, a aplikacja kończąca start zasłania shell bez kompensującego `MinimizeWindow` — prawdziwe, ale to zadanie story 6 (`tickets.toml` entry 6: „Home w trakcie `Starting`: `ShowShell` od razu, aplikacja, która dokończy start, dostaje kompensujące `MinimizeWindow`”), a stan mieszany jest dziś nieosiągalny, dopóki shell nie pozwoli wybrać kafelka przy aplikacji na pierwszym planie (epic-home-screen); dowód odłożenia: epic `epic-orchestrator-lifecycle.md:64` i `tickets.toml:94`.
  - `[low]` `[patch]` `Foreground_changed_is_logged_without_changing_state` mógł przejść także po regresji publikującej snapshot, bo pętla loguje przed `_dispatch`/`_publish` — naprawione: test publikuje po fakcie `SystemResumed` i porównuje całą sekwencję snapshotów (`Assert.Collection` z `rev`/`seq` 1 i 2).
  - `[low]` `[patch]` dok XML `ProcessExited` wskazywał AD-15 zamiast AD-4 (fakt raportowany przez hosta) i AD-10 (`Stopping`) — naprawione w `Core/Contracts/Messages.cs`.

## Design Notes

Home nie wysyła `focusRequest`, bo UI sam wraca na ostatni kafelek (`EXPERIENCE → Interaction Primitives`), a `seq` rosłoby bez potrzeby. Kolejność `MinimizeWindow` → `ShowShell` → routing wynika z AD-5/AD-15: shell musi być na wierzchu, zanim InputHub przełączy tryb, inaczej D-pad trafia do zminimalizowanej aplikacji. `LaunchApp` z `Background` nie startuje procesu — aplikacja żyje, więc wystarczą `ActivateWindow` i routing; `hwnd` jest znany, bo `Background` osiągalne jest tylko po `AppWindowAttached`.

Guard „inna aplikacja w `Foreground`/`Starting` blokuje powrót z tła, zamknięcie i restart” nie jest polityką przełączania (to story 6 z `transitionId`), a jedynie ochroną niezmiennika „jedna aplikacja na pierwszym planie”: shell jest schowany za aplikacją, więc dziś ten przypadek jest nieosiągalny, ale bez guardu snapshot mógłby pokazać dwie aplikacje w `Foreground`.

`StopApp` bez odpowiedzi zostawia `Stopping` i korelację, bo faktem zamykającym jest `CommandCompleted` albo `ProcessExited`; `CommandFailed` nie jest dowodem, że proces zniknął, a timeout `Stopping` należy do story 11. `ProcessExited` poza `Stopping` jest ignorowane do story 4 (tam prowadzi w `Failed`), a nieznany `cmdId` nie zmienia stanu — ta sama reguła co w story 3.

`ForegroundChanged` nie dotyka stanu: reakcję (dopasowanie trybu, wyciszenie) ustala epic-windows-audio, a AD-10 mówi, że Home zawsze ratuje. „Zapisywany” realizuje log JSONL (`src orchestrator`, `msg foregroundChanged`, `data.hwnd`), bo Orchestrator nie ma `ILog` i nie może go dostać bez złamania czystego handlera — ten sam wzorzec co `commandFailed` w pętli.

Restart nie wysyła `MinimizeWindow`/`ShowShell`: aplikacja jest już w tle i zminimalizowana, a shell na wierzchu; ponowne `ShowShell` byłoby komendą bez zmiany.

## Verification

**Commands:**
- `dotnet test MediaStation.slnx -c Release` -- wszystkie testy zielone (352 przed story; nowe pliki/rozszerzenia w `OrchestratorTests`, `CommandRouterTests`, `MissingHostPortTests`, `OrchestratorLoopTests`, `CoreMessageContractTests`).
- `dotnet build MediaStation.slnx -c Release` -- 0 ostrzeżeń; `SchemaExport` zapisuje `close-app`, `restart-app`, `stop-app`, `process-exited`, `foreground-changed` (`.schema.json`), `web/contracts/generated` dostaje odpowiadające `.ts`, Vite buduje shell.
- `dotnet build src/MediaStation.App -c Debug` -- 0 ostrzeżeń; `npm run contracts` i `npm run typecheck` przechodzą.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + uruchomienie z ważnym `%LOCALAPPDATA%\MediaStation\config` -- proces żyje, log zawiera `snapshotPublished {rev: 1}`, `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}` i `stop {exitCode: 0}`; Home/Close/Restart nie są osiągalne z UI (epic-home-screen, brak InputHub), więc ich dowodem są testy, a UM760 zostaje w odbiorze epiku.

**Current-machine evidence (2026-10-22, HEAD 2244b079fbec392e440e9c75c88152f7eb22305c, ALDER / B760 Pro RS/D4 / Windows 11 Home):**
- `dotnet test MediaStation.slnx -c Release` -- 407/407 passed (352 before; 55 new cases, w tym 4 z poprawek przeglądu). Macierz I/O pokryta `OrchestratorTests` (Home, powrót z tła z guardem na `Foreground`/`Starting`, `Stopping`, korelacja stopu, `ProcessExited` we wszystkich stanach, `CommandFailed` stopu, close/restart przy zajętym pierwszym planie, dwie ścieżki całego cyklu życia), `CommandRouterTests` (`StopApp` po `AppType`, `commandForUnknownApp`, `commandWithoutPort`), `MissingHostPortTests` (`stopWithoutHost`), `OrchestratorLoopTests` (`foregroundChanged` bez snapshotu) i `CoreMessageContractTests` (koperty, rejestr, schematy).
- `dotnet build MediaStation.slnx -c Release` -- success, 0 warnings, 0 errors; `SchemaExport` zapisał `close-app`, `restart-app`, `stop-app`, `process-exited`, `foreground-changed` (`.schema.json`), `web/scripts/contracts.mjs` wygenerował odpowiadające `.ts` (`close-app.ts`, `restart-app.ts`, `stop-app.ts`, `process-exited.ts`, `foreground-changed.ts`), `npm run typecheck` przeszedł, Vite zbudował shell (`shell/dist/index.html`).
- `dotnet build src/MediaStation.App -c Debug` -- success, 0 warnings; `npm run contracts` i `npm run typecheck` przeszły.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` -- success. Uruchomiony `MediaStation.exe` z ważnym `%LOCALAPPDATA%\MediaStation\config`: proces żył, a `%LOCALAPPDATA%\MediaStation\logs\mediastation-2026-10-21.jsonl` zawiera `start`, `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}`, `powerSignalsRegistered` i `snapshotPublished {rev: 1, focusRequest: {target: "default", seq: 1}}`; zamknięcie okna dało `stop {exitCode: 0}` i zero wpisów `error`/`warn`.
- Home/Close/Restart nie są osiągalne z UI (epic-home-screen nie ma jeszcze InputHub), więc ich dowodem są testy Orchestratora, routera, `MissingHostPort` i pętli; UM760 zostaje w odbiorze epiku użytkownika.
- Po poprawkach z przeglądu (2026-10-22, ten sam HEAD, ten sam komputer) przebieg powtórzony w całości: `dotnet test MediaStation.slnx -c Release` 407/407, `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` z 0 ostrzeżeń, publish self-contained i uruchomienie (pid 39676): `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}`, `snapshotPublished {rev: 1, focusRequest {default, 1}}`, `stop {exitCode: 0}`, brak `warn`/`error`.

## Auto Run Result

**Status:** built.

**Summary:** Core zyskał `CloseApp`/`RestartApp`, `StopApp`, `ProcessExited` i `ForegroundChanged` oraz zapamiętany `hwnd`. Orchestrator prowadzi `Foreground ⇄ Background` (Home → `MinimizeWindow`, `ShowShell`, routing `shell`; `LaunchApp` z tła → `ActivateWindow`, routing `app`) i `Background → Stopping → NotRunning | Starting` (zamknięcie na `CommandCompleted{StopApp}` albo oczekiwanym `ProcessExited`, restart startuje aplikację od nowa). `IAppHostPort.Stop` zamyka seam hostów, router kieruje `StopApp` po `AppType`, `MissingHostPort` odpowiada `CommandFailed{Unexpected}`, a pętla loguje `foregroundChanged` bez zmiany stanu. Close/Restart/Resume dzielą guard „inna aplikacja w `Foreground`/`Starting`”, który chroni niezmiennik jednej aplikacji na pierwszym planie.

**Files changed:**
- `src/MediaStation.Core/Contracts/Messages.cs` — `CloseApp`, `RestartApp`, `ProcessExited`, `ForegroundChanged`, poprawione doki XML `LaunchApp` i `ProcessExited`.
- `src/MediaStation.Core/Contracts/AppCommands.cs` — `StopApp`.
- `src/MediaStation.Core/Contracts/MessageTypes.cs` — nowe rekordy w rejestrze koperty.
- `src/MediaStation.Core/Ports/Ports.cs` — `IAppHostPort.Stop`.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — `Hwnd`, `_pendingStops`, `OnHome`, `OnResume`, `OnStop`, `OnStopped`, `OnProcessExited`, `AnotherAppIsActive`, alias `ActionMessage`.
- `src/MediaStation.App/Orchestrator/CommandRouter.cs` — `TryGetHost` współdzielony przez `StartApp` i `StopApp`.
- `src/MediaStation.App/Hosts/MissingHostPort.cs` — `stopWithoutHost` + `CommandFailed{Unexpected}`.
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` — log `foregroundChanged`.
- `tests/MediaStation.Core.Tests/` — rozszerzone `OrchestratorTests`, `CommandRouterTests`, `MissingHostPortTests`, `OrchestratorLoopTests`, `CoreMessageContractTests`.

**Review findings (quick):** 4 findings — high 0, medium 0, low 4. Patches applied: 3 low (wspólny guard dla Close/Restart przy zajętym pierwszym planie + test; deterministyczny test `foregroundChanged`; dok XML `ProcessExited` na AD-4/AD-10). Deferred: 1 (Home przy drugiej aplikacji `Starting` — kompensacja `MinimizeWindow` należy do story 6, stan dziś nieosiągalny). Rejected: 0.

**Patched counts by verdict:** high 0, medium 0, low 3.

**Follow-up review recommendation:** `false`.

**Verification:** `dotnet test MediaStation.slnx -c Release` 407/407; `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` z 0 ostrzeżeń (pięć nowych schematów + TS, Vite buduje shell); publish self-contained i uruchomienie z ważnym configiem: log `snapshotPublished {rev: 1}`, `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}`, `stop {exitCode: 0}`, brak `warn`/`error`.

**Residual risks:** Home/Close/Restart nie są jeszcze osiągalne z UI (epic-home-screen, brak InputHub), więc ścieżkę potwierdzają wyłącznie testy; `ProcessExited` poza `Stopping` i `CommandFailed` stopu zostają bez przejścia w `Failed` (story 4), a timeout `Stopping` należy do story 11; przełączanie między dwiema żywymi aplikacjami i `transitionId` to story 6 (guard blokuje close/restart/resume, gdy inna aplikacja jest `Foreground`/`Starting`); `hwnd` jest dowodem z `AppWindowAttached`, więc prawdziwe okna i całościowy test UM760 zostają w odbiorze epiku u użytkownika.
