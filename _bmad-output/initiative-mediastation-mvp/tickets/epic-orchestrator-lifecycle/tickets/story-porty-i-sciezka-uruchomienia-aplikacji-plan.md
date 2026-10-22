---
title: 'Porty i ścieżka uruchomienia aplikacji'
type: 'feature'
ticket: '3'
created: '2026-10-22'
status: done
baseline_revision: '14285f8a5f6582f6216ffbb90da614924b047ca3'
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

**Problem:** Orchestrator nie ma żadnego wyjścia do wykonawców: `OrchestratorLoop.Run` nie ma do kogo wysłać komend, nie istnieje Intent wyboru kafelka, porty hostów, WindowManagera i routingu wejścia, Event `AppWindowAttached`, ani przejście `NotRunning → Starting → Foreground`, więc kafelek nie może uruchomić aplikacji, a epic-windows-audio, epic-input i epiki hostów nie mają się do czego podpiąć.

**Approach:** Core dostaje Intent `LaunchApp{appId}`, Event `AppWindowAttached{appId, hwnd, rootPid}`, komendy `StartApp`/`ActivateWindow`/`MinimizeWindow`/`ShowShell` i trzy porty (`IAppHostPort` per `AppType`, `IWindowManagerPort`, `IInputRoutingPort`). Orchestrator prowadzi stan przez `NotRunning → Starting → Foreground` (korelacja po `cmdId`, `CommandCompleted` bez zmiany stanu, `CommandFailed` tylko logowany), a każdą zmianę trybu routingu kwituje `SetInputRouting` z nowym `epoch`. App kieruje komendy routerem do portów, w composition root stawia adapter „brak hosta” (na `StartApp` → `CommandFailed{Unexpected}`) i adaptery tylko-log dla WindowManagera i routingu wejścia.

## Boundaries & Constraints

**Always:** Core `net10.0` bez Win32/WPF/IO (AD-3); Orchestrator jedynym writerem stanu i jedynym nadawcą `cmdId`/`epoch` (AD-3, AD-14); komendy fire-and-forget, żadnego `await` w handlerze (AD-14); port to interfejs w Core przyjmujący komendę, wynik wraca do kolejki jako Event (decyzja epiki); `rev` rośnie przy każdej zmianie snapshotu, `seq` fokusu tylko przy nowym żądaniu; schematy/TS tylko przez `SchemaExport` + `web/scripts/contracts.mjs`.

**Never:** przejście w `Failed`, polityka wybudzenia (story 8), `CloseApp`/`RestartApp`/`StopApp`/`ProcessExited` (story 10), `CdpLost` (story 4), timery (story 5), `ProbeExisting`/adopcja (story 7), pełne sekwencje `restore`/`unmute`/`pause` (epic-windows-audio), przyjmowanie Intents w `ShellBridge` i kafelki w shellu (epic-home-screen), prawdziwe hosty, projekt testów App, ręczna edycja `web/contracts/**`, pliki ticketów.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Start | `OrchestratorStarted` | snapshot jak w story 1 (`rev`+1, `seq`+1) + `SetInputRouting{shell, null, blocked, epoch 1}`, `cmdId` 1 | — |
| Launch | `LaunchApp("netflix")`, `NotRunning` | stan `Starting`, `StartApp{cmdId, "netflix"}`, snapshot `rev`+1 ze stanem, `focusRequest` bez zmian | — |
| Unknown app | `LaunchApp("nope")` | brak komend i snapshotu | ignorowane |
| Launch w innym stanie | `LaunchApp` znanej aplikacji w `Starting`/`Foreground`/`Background` | brak komend i snapshotu | ignorowane (ticket wymienia `Starting`) |
| Start completed | `CommandCompleted(cmdId StartApp)` | stan bez zmian, brak snapshotu | nieznany `cmdId` ignorowany |
| Start failed | `CommandFailed(cmdId StartApp, code)` | stan bez zmian (do story 4) | pętla loguje `commandFailed`; nieznany `cmdId` ignorowany |
| Window attached | `AppWindowAttached{netflix, hwnd, rootPid}`, `Starting` | `Foreground`, `ActivateWindow{hwnd}`, `SetInputRouting{app, web, blocked, epoch 2}`, snapshot `rev`+1 | poza `Starting` ignorowane |
| Missing host | `StartApp` do adaptera „brak hosta” | `CommandFailed{same cmdId, Unexpected}` w kolejce + log `startWithoutHost` | brak wyjątku przez granicę portu |
| Log-only porty | `ActivateWindow`/`MinimizeWindow`/`ShowShell`, `SetInputRouting` | wpis logu z `cmdId` i polami komendy | brak efektu ubocznego |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Contracts/Messages.cs:13` — baza `Intent` (komentarz „epiki 2–4”); dopisz `LaunchApp(string AppId) : Intent` po `OrchestratorStarted`, a `AppWindowAttached(string AppId, long Hwnd, int RootPid) : Event` po `SystemResumed`.
- `src/MediaStation.Core/Contracts/AppCommands.cs` (nowy) — `StartApp(long CmdId, string AppId) : Command`; baza `WindowCommand(long CmdId) : Command` + `ActivateWindow(long CmdId, long Hwnd)`, `MinimizeWindow(long CmdId, long Hwnd)`, `ShowShell(long CmdId)`; `Hwnd` jako `long` (HWND nie mieści się w `int`, a `IntPtr` nie jest kontraktem JSON).
- `src/MediaStation.Core/Contracts/SetInputRouting.cs:32` — istniejąca komenda routingu; `AppType` (`:12`) i `PointerPolicy` (`:20`) już są kontraktem. Bez zmian w rekordzie.
- `src/MediaStation.Core/Contracts/MessageTypes.cs:14-22` — rejestr w kolejności: `Action`, `LaunchApp`, `SetInputRouting`, `StartApp`, `ActivateWindow`, `MinimizeWindow`, `ShowShell`, `CommandCompleted`, `CommandFailed`, `OrchestratorStarted`, `SystemResumed`, `AppWindowAttached` (lista camelCase jest asercją w testach).
- `src/MediaStation.Core/Ports/Ports.cs` (nowy) — interfejsy portów: `IAppHostPort.Start(StartApp)`, `IWindowManagerPort.Execute(WindowCommand)`, `IInputRoutingPort.Execute(SetInputRouting)`; kontrakt „wykonawca nie przepuszcza wyjątku” w komentarzu (AD-4). Porty nie trafiają do `MessageTypes`, więc nie mają własnych schematów.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs:43-53` — `switch`; `:59-79` `OnDefaultFocus` zostaje dla `SystemResumed` (zero komend), a `OrchestratorStarted` dostaje własną gałąź ze snapshotem + routingiem `shell`. `:81-88` `AppState` zyskuje `AppType` i `PointerPolicy` z `WebApp`/`MoonlightApp`/`NativeApp` (`Config/AppsConfig.cs:29-80`).
- Stan Orchestratora: `_cmdId` i `_epoch` (monotoniczne, od 1), `_pendingStarts` (`Dictionary<long, string>`), ostatni `FocusRequest` (snapshoty bez nowego fokusu powtarzają poprzedni `seq`, UI dedupuje po `seq`).
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs:27` — konstruktor dostaje dispatcher komend; `:99-123` `Run` wysyła `result.Commands` przed publikacją snapshotu i loguje `commandFailed {cmdId, errorCode}` dla `CommandFailed`; komentarz w `:107-108` („porty dochodzą w story 3”) zastąpić prawdą.
- `src/MediaStation.App/Orchestrator/CommandRouter.cs` (nowy) — `Dispatch(IReadOnlyList<Command>)`: `StartApp` → `IAppHostPort` wybrany po `AppType` z mapy `appId → AppType` (zbudowanej w composition root z configu), `WindowCommand` → `IWindowManagerPort`, `SetInputRouting` → `IInputRoutingPort`; nieznany typ → `warn commandWithoutPort`, nieznany `appId` → `warn commandForUnknownApp`.
- `src/MediaStation.App/Hosts/MissingHostPort.cs` (nowy) — `IAppHostPort`: `Start` loguje `startWithoutHost {cmdId, appId}` i wkłada `CommandFailed(command.CmdId, ErrorCode.Unexpected)` przez wstrzyknięty `Action<IOrchestratorMessage>`.
- `src/MediaStation.App/Platform/Window/LogWindowManagerPort.cs` (nowy) — `IWindowManagerPort`: log `windowCommand {type, cmdId, hwnd}` (src `window`; `hwnd` nieobecne dla `ShowShell`).
- `src/MediaStation.App/Platform/Input/LogInputRoutingPort.cs` (nowy) — `IInputRoutingPort`: log `inputRouting {cmdId, mode, appType, pointer, epoch}` (src `input`; `appType` nieobecne, gdy `null` — `JsonLinesLog` pomija `null`).
- `src/MediaStation.App/CompositionRoot.cs:104-105` — budowa routera i trzech `MissingHostPort` (po jednym na `AppType`, żeby epiki hostów podmieniały je osobno) + `LogWindowManagerPort`/`LogInputRoutingPort`, przekazanie do pętli; `:23-27` stałe src (`app`, `config`, `system`) + nowe `window`, `input`.
- `tools/MediaStation.SchemaExport/Program.cs` — bez zmian: nowe rekordy z `MessageTypes.All` dają `launch-app`, `start-app`, `app-window-attached`, `activate-window`, `minimize-window`, `show-shell` (`.schema.json` + TS w buildzie).
- `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj:21-30` — link `Compile Include` dla `CommandRouter`, `MissingHostPort`, `LogWindowManagerPort`, `LogInputRoutingPort` (żaden nie dotyka Win32/WPF); precedens `OrchestratorLoop.cs`.
- `tests/MediaStation.Core.Tests/OrchestratorTests.cs:9-15` — `TwoApps()` (web `netflix` + native `plex`); `:17-44` asercja pustych komend startu do zmiany; `:91-101` `Ignored_events_do_not_advance_rev` zostaje.
- `tests/MediaStation.Core.Tests/CoreMessageContractTests.cs:222-231` — lista nazw rejestru; `:240-260` lista plików schematów; `:263-343` wzorce asercji kształtu schematu.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs:88-93` — `CreateLoop` dostaje dispatcher.
- Reuse: `ILog`/`LogExtensions`, `ContractJson.SerializeEnvelope`, `ErrorCode.Unexpected`, `AppLifecycleState` (story 1). Nie ruszać `ShellBridge` (`:48` `Publish`), `web/shell/**`, `config/**`.

## Tasks & Acceptance

**Execution:**
- [x] `Core/Contracts/Messages.cs` -- `LaunchApp`, `AppWindowAttached` -- Intent z UI i fakt hosta; oba eksportowane do JSON Schema/TS.
- [x] `Core/Contracts/AppCommands.cs` -- `StartApp`, `WindowCommand` + `ActivateWindow`/`MinimizeWindow`/`ShowShell` -- komendy z `cmdId` pierwszym polem (AD-4, AD-15).
- [x] `Core/Contracts/MessageTypes.cs` -- rejestracja nowych rekordów -- koperta i schematy (AD-16).
- [x] `Core/Ports/Ports.cs` -- trzy interfejsy portów -- seam dla hostów, epic-windows-audio i epic-input.
- [x] `Core/Orchestrator/Orchestrator.cs` -- routing `shell` przy starcie, `LaunchApp` → `Starting`, korelacja `cmdId`, `AppWindowAttached` → `Foreground` + `ActivateWindow` + routing `app`, snapshoty ze stanem -- stan cyklu życia (AD-10, AD-14).
- [x] `App/Orchestrator/OrchestratorLoop.cs` -- dispatcher komend i log `commandFailed` -- jedyne wyjście do wykonawców i ślad po nieudanej komendzie.
- [x] `App/Orchestrator/CommandRouter.cs` -- routing komenda → port po typie i `AppType` -- decyzja epiki „pętla kieruje w App”.
- [x] `App/Hosts/MissingHostPort.cs` -- `StartApp` → `CommandFailed{Unexpected}` -- kontrakt hosta przed epikami hostów.
- [x] `App/Platform/Window/LogWindowManagerPort.cs`, `App/Platform/Input/LogInputRoutingPort.cs` -- adaptery tylko-log -- podmiana w epic-windows-audio/epic-input, dowód działania w logu.
- [x] `App/CompositionRoot.cs` -- mapa `appId → AppType`, router, porty, nowe src -- jeden composition root dla wszystkich zależności pętli.
- [x] `tests/MediaStation.Core.Tests/*` -- pełna ścieżka na fake portach, korelacja, kontrakty/schematy/rejestr, router, adapter „brak hosta”, log-only porty, dispatcher pętli -- każdy wiersz macierzy ma uruchomiony test.

**Acceptance Criteria:**
- Given skonfigurowane `netflix` (web) i `plex` (native) i pierwszy `OrchestratorStarted`, when `Handle`, then snapshot `rev: 1`/`seq: 1` jak w story 1, a komendy to dokładnie `SetInputRouting{cmdId: 1, mode: "shell", appType: null, pointer: "blocked", epoch: 1}`.
- Given `LaunchApp("netflix")` w `NotRunning`, when `Handle`, then stan `netflix` to `Starting` w snapshocie z `rev`+1, `focusRequest` pozostaje `{default, 1}`, a jedyną komendą jest `StartApp{cmdId: 2, appId: "netflix"}`.
- Given `AppWindowAttached{"netflix", hwnd, rootPid}` po `LaunchApp`, when `Handle`, then stan to `Foreground`, komendy to `ActivateWindow{cmdId: 3, hwnd}` i `SetInputRouting{cmdId: 4, mode: "app", appType: "web", pointer: "blocked", epoch: 2}`, a snapshot ma `rev`+1 bez nowego `seq`.
- Given `LaunchApp` znanej aplikacji w stanie innym niż `NotRunning` (`Starting`, `Foreground`, `Background`) albo `LaunchApp` z nieznanym `appId`, when `Handle`, then wynik to brak komend i brak snapshotu, a stan i `rev` się nie zmieniają.
- Given `CommandCompleted` z `cmdId` komendy `StartApp`, when `Handle`, then brak komend i snapshotu, stan pozostaje `Starting`; a dla `cmdId` spoza korelacji (np. 999) `CommandCompleted` i `CommandFailed` też nie zmieniają stanu.
- Given fake porty i `AppWindowAttached` dla aplikacji w `NotRunning` (bez wcześniejszego `StartApp`) oraz `CommandFailed` z `ErrorCode.CdpUnavailable` po `StartApp`, when testy `OrchestratorTests`, then żaden przypadek nie daje `ActivateWindow` ani nie zmienia stanu aplikacji (`CommandFailed` nie przechodzi w `Failed` — to story 4).
- Given `StartApp` skierowany do `MissingHostPort`, when `Start`, then kolejka dostaje `CommandFailed` z tym samym `cmdId` i `ErrorCode.Unexpected`, a log zawiera `startWithoutHost {cmdId, appId}`.
- Given `CommandRouter` z hostami per `AppType`, when `StartApp("plex")`, `ActivateWindow`, `MinimizeWindow`, `ShowShell` i `SetInputRouting`, then każda komenda trafia do właściwego portu; `StartApp` z nieznanym `appId` i komenda bez portu kończą się wpisem `warn` bez wyjątku.
- Given `LaunchApp("netflix")`, when `ContractJson.SerializeEnvelope<Intent>`, then dokładnie `{"v":1,"type":"launchApp","payload":{"appId":"netflix"}}` i round-trip; `MessageTypes.All` ma zarejestrowane `launchApp`, `startApp`, `appWindowAttached`, `activateWindow`, `minimizeWindow`, `showShell`, a eksport schematów zawiera `launch-app.schema.json`, `start-app.schema.json`, `app-window-attached.schema.json`, `activate-window.schema.json`, `minimize-window.schema.json`, `show-shell.schema.json` z `title`, `required` i `additionalProperties: false`.
- Given pętla z dispatcherem, when `OrchestratorStarted`, then dispatcher dostaje `SetInputRouting{shell}` przed publikacją snapshotu; a `CommandFailed` z nieznanym `cmdId` daje wpis `warn commandFailed {cmdId, errorCode}` i nie zmienia opublikowanego `rev`.

## Implementation Notes

- `_pendingStarts` (`cmdId → appId`) jest rekordem korelacji wymaganym przez story: w tej story wpis powstaje w `LaunchApp` i znika po `CommandCompleted`/`CommandFailed`; story 4 odczyta go, żeby zdecydować o `Failed`. `AppWindowAttached` go nie czyści, bo host odpowiada na `StartApp` już po pokazaniu okna.
- `Orchestrator` trzyma ostatni `FocusRequest` w `_focus` i powtarza go w każdym snapshocie bez nowego fokusu (`LaunchApp`, `AppWindowAttached`); `seq` rośnie wyłącznie w `OrchestratorStarted`/`SystemResumed`.
- `LaunchApp` ma jeden warunek `state != NotRunning`; `Background` powstaje dopiero w story 10 (test nie może go osiągnąć), więc wiersz macierzy dla `Background` pokrywa ta sama gałąź — `OrchestratorTests` testuje `Starting` i `Foreground`, a komentarz w teście wskazuje story 10.
- `LogInputRoutingPort` pomija `appType` **jawnie**, budując słownik warunkowo. Założenie z planu, że `JsonLinesLog` pominie `null`, jest nieprawdziwe dla wartości słownika (`DefaultIgnoreCondition.WhenWritingNull` dotyczy właściwości, nie wpisów `data`; `JsonLinesLogTests` serializuje `["none"] = null` jako `"none": null`). Test `LogInputRoutingPortTests` przypina faktyczny JSON z `JsonLinesLog.Format`.
- Stałe src `window` i `input` żyją w adapterach (`LogWindowManagerPort.Src`, `LogInputRoutingPort.Src`), jak `PowerSignalReporter.Src`, a nie w `CompositionRoot`; `CompositionRoot` trzyma tylko src, których sam używa w logu, więc nie powstają martwe stałe.
- `MissingHostPort` loguje `startWithoutHost` na poziomie `warn` ze src `host` (plan nie nazywał src), bo brak hosta oznacza, że komendy nie da się wykonać.
- `CommandRouter` dokłada trzeci przypadek `warn commandWithoutPort {cmdId, appId, appType}`: znany `appId`, ale brak hosta dla jego `AppType`. Composition root rejestruje wszystkie trzy typy, więc to zabezpieczenie seamu dla epik hostów.
- Adaptery tylko-log nie wysyłają `CommandCompleted` (nie udają wykonawców); `CompositionRoot` podaje `MissingHostPort` delegat do `OrchestratorLoop.Post` przez leniwy `OrchestratorLoop?`, bo pętla powstaje po routerze, a przed `Start()` nikt nie publikuje do kolejki.

## Plan Change Log

### 2026-10-22 — Implementation
- Code Map `CompositionRoot.cs:23-27`: stałe src `window`/`input` przeniesione do adapterów zamiast do `CompositionRoot` (bez martwych stałych); zachowanie logu bez zmian.
- Code Map `LogInputRoutingPort`: `appType` jest pomijany jawnie w adapterze, bo `JsonLinesLog` serializuje `null` w `data` jako `"appType": null` (sprzeczne z założeniem planu); dowód w `LogInputRoutingPortTests`.
- Code Map `CommandRouter`: dodatkowy `warn commandWithoutPort {cmdId, appId, appType}` dla znanego `appId` bez hosta jego `AppType` (obok planowanych `commandForUnknownApp` i `commandWithoutPort`).

## Review Triage Log

### 2026-10-22 — Review pass (quick)
- verdicts: 7 findings — high 0, medium 0, low 7, false 0, maybe-false 0
- findings:
  - `[low]` `[reject]` `_pendingStarts` is only written and removed, never read, so `CommandCompleted`/`CommandFailed` with any `cmdId` take the same path and the correlation is satisfied only trivially — true as a fact, and deliberate: this story has no consumer of the result (story 4 turns a correlated `CommandFailed` into `Failed`), the observable behaviour is exactly the AC (no state change for any result), and deleting the map would break the seam story 4 reads; the smallest fix is either that deletion or a public accessor that exists only for a test, so it is not worth adding surface a user or developer meets.
  - `[low]` `[reject]` the `Background` case of the "LaunchApp in another state" AC has no executed test because `Background` is unreachable before story 10 — true; the row is covered by the single `state != NotRunning` branch (`Starting` and `Foreground` are tested), the plan's Implementation Notes and Design Notes name the story-10 seam, and the only fix is editing this build's plan's AC text.
  - `[low]` `[reject]` `AppEntry → AppType` is mapped twice (`CompositionRoot.AppTypeOf` and `Orchestrator.Describe`), and the composition-root throw sits outside the adapter `try` — the duplication is real, but the claimed harm needs a fourth `AppEntry` subtype, which does not exist (the base is sealed-derived, the three records are closed) so the throw is unreachable; a shared mapper would add public Core surface for a case no epic has scheduled, and a user never meets it.
  - `[low]` `[reject]` dispatching commands inside the same `try` before the snapshot means a throwing port would drop the snapshot, and a slow port would block the loop — no port in this story can throw (`MissingHostPort` posts to a queue that swallows shutdown, both log-only ports only write to `JsonLinesLog`, which never throws) and AD-4 puts "never throw across the port" on executors, so the guard would cover state not demonstrated; the dispatch-before-publish order is deliberate so a real host cannot outrun the view.
  - `[low]` `[patch]` `RecordingLog` is a plain `List` written from the Orchestrator thread while `OrchestratorLoopTests` enumerates it, so a concurrent `Add` can make `List<T>.Enumerator.MoveNext` throw — fixed: writes append under a lock and `Entries` returns a locked snapshot copy.
  - `[low]` `[patch]` nothing composes `OrchestratorLoop` + `CommandRouter` + `MissingHostPort`, so the AC "`CommandFailed` trafia do kolejki" was verified only at the delegate — fixed: `Missing_host_answer_returns_to_the_queue_and_keeps_app_starting` wires the real loop, router and missing-host ports through `loop.Post` and asserts `startWithoutHost`, `commandFailed {cmdId: 2, errorCode: Unexpected}` and the `Starting` state in the published snapshot.
  - `[low]` `[reject]` the recorded evidence says `npm run contracts` proves "Intent ma typ TS", while `web/contracts/generated` holds per-record interfaces and no `Intent` union — true; the generated `LaunchApp` interface is the AD-16 artifact and the base `Intent` deliberately has no schema, so the only fix is the wording of this build's plan's Verification block.

## Design Notes

Kierowanie komend mieszka w App, bo decyzja epiki mówi „pętla w App kieruje Command do adaptera po `AppType`”, a Core zostaje czystym handlerem; mapa `appId → AppType` w routerze bierze się z tych samych rekordów configu, z których Orchestrator bierze `pointer`. `StartApp` niesie wyłącznie `appId`, bo AD-11 czyni `appId` jedynym identyfikatorem, a host i tak zna swoje aplikacje — kolejne komendy (`StopApp`, `RenewSession`, `ProbeExisting`) użyją tej samej mapy. `Hwnd` jest `long`, bo `nint`/`IntPtr` nie serializuje się do kontraktu JSON, a 64-bitowy Windows nie zmieści uchwytu w `int`. `CommandFailed` jest w tej story logowany w pętli (nie w Core), bo Orchestrator nie ma `ILog` i nie może go dostać bez złamania czystego handlera; story 4 zamieni ten log na przejście w `Failed`. Drugi `OrchestratorStarted` też wysyła routing `shell` z nowym `epoch` — to jedyny przypadek powtórzenia trybu, a nowy `epoch` jest bezpieczniejszy niż warunek „tylko przy zmianie”. Snapshot bez nowego fokusu powtarza ostatni `FocusRequest`, bo UI stosuje `seq` dokładnie raz (AD-16). Adapter „brak hosta” jest jeden na `AppType`, żeby epic-browserhost/epic-moonlight-native podmieniały tylko swój wpis. Adaptery tylko-log nie udają wykonawców: nie wysyłają `CommandCompleted`, bo dopóki nie ma prawdziwego hosta/WindowManagera, wynik byłby fikcją; `MissingHostPort` odpowiada `CommandFailed`, bo to jawny fakt braku hosta. Seam story 7: `AppWindowAttached` poza `Starting` jest ignorowane i story 7 dokłada osobną gałąź adopcji, nie zmienia tej reguły. Seam story 10: ta story zna wyłącznie przejścia pojedynczej aplikacji (`LaunchApp` uruchamia tylko `NotRunning`), więc nie przenosi poprzedniej aplikacji z `Foreground` w `Background`; przełączanie między aplikacjami i powrót z `Background` przez `LaunchApp` należą do story 10, a shell schowany za aplikacją nie pozwala wybrać kafelka, gdy jakaś aplikacja jest na pierwszym planie.

## Verification

**Commands:**
- `dotnet test` -- wszystkie testy zielone (istniejące + nowe pliki testów Orchestratora, kontraktów, pętli, routera i portów).
- `dotnet build MediaStation.slnx -c Release` -- 0 ostrzeżeń; `SchemaExport` generuje sześć nowych `.schema.json`, `web/contracts/generated` dostaje odpowiadające `.ts`, Vite buduje shell.
- `dotnet build src/MediaStation.App -c Debug` -- 0 ostrzeżeń; `npm run contracts` + `npm run typecheck` przechodzą (Intent ma typ TS, shell się nie zmienia).
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + uruchomienie z ważnym `%LOCALAPPDATA%\MediaStation\config` (kopie z `tools/samples/config`) -- proces żyje, log zawiera `snapshotPublished {rev: 1}`, `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}` z adaptera tylko-log i `stop {exitCode: 0}`; ścieżka `LaunchApp` nie jest osiągalna z UI (epic-home-screen), więc jej dowodem są testy, a UM760 zostaje w odbiorze epiku.

**Current-machine evidence (2026-10-22, HEAD 14285f8a5f6582f6216ffbb90da614924b047ca3, ALDER / B760 Pro RS/D4 / Windows 11 Home):**
- `dotnet test MediaStation.slnx -c Release` -- 352/352 passed (281 before this story; 352 after the two review patches, which added `Missing_host_answer_returns_to_the_queue_and_keeps_app_starting`); macierz I/O pokryta testami `OrchestratorTests`, `CommandRouterTests`, `MissingHostPortTests`, `LogWindowManagerPortTests`, `LogInputRoutingPortTests`, `OrchestratorLoopTests` i `CoreMessageContractTests`.
- `dotnet build MediaStation.slnx -c Release` -- success, 0 warnings, 0 errors; `SchemaExport` zapisał `launch-app`, `start-app`, `app-window-attached`, `activate-window`, `minimize-window`, `show-shell` (`.schema.json`), `web/scripts/contracts.mjs` wygenerował odpowiadające `.ts`, a Vite zbudował shell.
- `dotnet build src/MediaStation.App -c Debug` -- success, 0 warnings; `npm run contracts` i `npm run typecheck` przeszły (Intent ma typ TS, shell bez zmian).
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` -- success. Uruchomiony `MediaStation.exe` z ważnym `%LOCALAPPDATA%\MediaStation\config` (kopie z `tools/samples/config`): proces żył, a `%LOCALAPPDATA%\MediaStation\logs\mediastation-2026-10-21.jsonl` (data UTC) zawiera `snapshotPublished {rev: 1, focusRequest: {target: "default", seq: 1}}` i `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}` ze src `input` i bez klucza `appType`. Zamknięcie okna (`taskkill` bez `/F`) dało `stop {exitCode: 0}`.
- Ścieżka `LaunchApp` nie jest osiągalna z UI (epic-home-screen), więc jej dowodem są testy Orchestratora, routera i pętli; UM760 zostaje w odbiorze epiku użytkownika.
- Przebieg po dwóch poprawkach z przeglądu (2026-10-22, pid 38268, ten sam HEAD): log nadal ma `snapshotPublished {rev: 1}` i `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}`, `stop {exitCode: 0}`, zero `error`.

## Auto Run Result

**Status:** built.

**Summary:** Core zyskał Intent `LaunchApp`, Event `AppWindowAttached`, komendy `StartApp`/`ActivateWindow`/`MinimizeWindow`/`ShowShell` oraz trzy porty (`IAppHostPort` per `AppType`, `IWindowManagerPort`, `IInputRoutingPort`). Orchestrator prowadzi pojedynczą aplikację przez `NotRunning → Starting → Foreground`, nadaje `cmdId` i `epoch`, koreluje wyniki `StartApp` i publikuje stan w snapshocie; `OrchestratorStarted` wysyła routing `shell`. App kieruje komendy routerem do portów, composition root stawia adapter „brak hosta” (`StartApp` → `CommandFailed{Unexpected}`) i adaptery tylko-log dla WindowManagera i routingu wejścia, a pętla loguje `commandFailed`.

**Files changed:**
- `src/MediaStation.Core/Contracts/Messages.cs` — `LaunchApp`, `AppWindowAttached`.
- `src/MediaStation.Core/Contracts/AppCommands.cs` (nowy) — `StartApp`, `WindowCommand`, `ActivateWindow`, `MinimizeWindow`, `ShowShell`.
- `src/MediaStation.Core/Contracts/MessageTypes.cs` — rejestr sześciu nowych rekordów.
- `src/MediaStation.Core/Ports/Ports.cs` (nowy) — interfejsy portów wykonawców.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — stan aplikacji, `cmdId`/`epoch`, korelacja startu, snapshoty ze stanem.
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` — dispatcher komend, log `commandFailed`.
- `src/MediaStation.App/Orchestrator/CommandRouter.cs` (nowy) — komenda → port po typie i `AppType`.
- `src/MediaStation.App/Hosts/MissingHostPort.cs` (nowy) — `startWithoutHost`, `CommandFailed{Unexpected}`.
- `src/MediaStation.App/Platform/Window/LogWindowManagerPort.cs`, `src/MediaStation.App/Platform/Input/LogInputRoutingPort.cs` (nowe) — adaptery tylko-log.
- `src/MediaStation.App/CompositionRoot.cs` — mapa `appId → AppType`, router, trzy adaptery „brak hosta”, dwa adaptery tylko-log.
- `tests/MediaStation.Core.Tests/` — `CommandRouterTests`, `MissingHostPortTests`, `LogWindowManagerPortTests`, `LogInputRoutingPortTests`, `RecordingLog` (nowe), rozszerzone `OrchestratorTests`, `CoreMessageContractTests`, `OrchestratorLoopTests`, link `Compile Include` w csproj.

**Review findings (quick):** 7 findings — medium 0, low 7. Patches applied: 2 low (`RecordingLog` thread-safe; pętla+router+`MissingHostPort` round trip przez kolejkę). Deferred: 0. Rejected: 5 (correlation map has no consumer until story 4; `Background` AC unreachable before story 10 and its fix is a plan edit; duplicated `AppEntry → AppType` mapping whose harm needs a fourth subtype that does not exist; dispatch-before-publish guard for a port that cannot throw; evidence wording about a generated `Intent` union that AD-16 does not produce).

**Patched counts by verdict:** high 0, medium 0, low 2.

**Follow-up review recommendation:** `false`.

**Verification:** `dotnet test MediaStation.slnx -c Release` 352/352; `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` z 0 ostrzeżeń (schematy `launch-app`/`start-app`/`app-window-attached`/`activate-window`/`minimize-window`/`show-shell` + TS, Vite buduje shell); publish self-contained i uruchomienie z ważnym configiem: proces żył, log `snapshotPublished {rev: 1, focusRequest{default,1}}`, `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}`, `stop {exitCode: 0}` (dwa przebiegi, przed i po poprawkach z przeglądu).

**Residual risks:** `LaunchApp` nie jest osiągalny z UI do epic-home-screen, więc ścieżkę startu potwierdzają wyłącznie testy; `AppWindowAttached` poza `Starting` jest ignorowane do story 7 (adopcja); przełączanie między aplikacjami i `Background` należą do story 10; `CommandFailed` nie przechodzi w `Failed` do story 4; composition root nie ma testów (brak projektu testów App), a `MissingHostPort` nie odpowiada na przyszłe `ProbeExisting`; prawdziwe trzy źródła wybudzenia i całościowy test UM760 zostają w odbiorze epiku u użytkownika.
