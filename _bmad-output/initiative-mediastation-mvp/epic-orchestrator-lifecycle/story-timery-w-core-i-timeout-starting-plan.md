---
title: 'Timery w Core i timeout Starting'
type: 'feature'
ticket: '5'
created: '2026-10-22'
status: done
baseline_revision: 'e2253fd58e92c0b1874dfa21312745f7bdf889c2'
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

**Problem:** Orchestrator nie ma czasu: nie umie zaplanować timera (dobowe sprawdzenie w epic-do-oceny, timeouty), a aplikacja zawieszona w `Starting` zostaje w tym stanie na zawsze. `BannedSymbols.txt` nie zakazuje `TimeProvider.System`, `Stopwatch` ani `Environment.TickCount`, więc pierwsze użycie czasu w Core może ominąć AD-14 (N8).

**Approach:** Handler zwraca `ScheduleTimer{timerId, owner: appId, due}` i `CancelTimer{timerId}`, a scheduler w Core z wstrzykniętym `TimeProvider` (App go tylko hostuje) wkłada `TimerElapsed{timerId}` do kolejki. Orchestrator planuje timeout `startingTimeout` przy wejściu w `Starting`, anuluje go po `AppWindowAttached` (i przy wejściu w `Failed`) i ignoruje `TimerElapsed` o nieznanym albo anulowanym `timerId`. Ambient time dostaje wpisy w `BannedSymbols.txt` i test granicy Core.

## Boundaries & Constraints

**Always:** czas tylko przez wstrzyknięty `TimeProvider` (AD-14); handler czysty i synchroniczny, bez `await`; `cmdId` i `timerId` nadaje wyłącznie Orchestrator; timer jest jednorazowy i znika tylko po jawnym `CancelTimer` (albo `Dispose`); scheduler thread-safe, po `Dispose` nie publikuje; `TimerElapsed` nieznanego/anulowanego `timerId` nie daje komend ani snapshotu; nowe rekordy przez `MessageTypes.All` + `SchemaExport` (AD-16); komendy timera idą na koniec batcha, więc znaczenie istniejących komend się nie zmienia.

**Never:** timeout `Stopping` i górne granice `startingTimeout` (story 11), `transitionId`/Home w `Starting` (story 6), adopcja (7), `RenewSession`/wybudzenie (8), prawdziwe hosty, Win32/IO/wątki w Core, zmiany w `web/**` poza generowanymi kontraktami, nowe wpisy logu, zmiana zachowania istniejących gałęzi `CommandRouter` i `Orchestrator`, `Settings.cs` (pole `StartingTimeout` już jest walidowane > 0).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Start z timeoutem | `LaunchApp` w `NotRunning` | `Starting`; `StartApp` + `ScheduleTimer{timerId, appId, GetUtcNow()+startingTimeout}`; `rev`+1 | — |
| Timeout wygasa | `TimerElapsed{timerId}` w `Starting` | `Failed`; brak komend; `rev`+1; `focusRequest` bez zmian | — |
| Okno przed deadlinem | `AppWindowAttached` w `Starting` | `Foreground`; `ActivateWindow`, routing, `CancelTimer{timerId}`; `rev`+1 | — |
| Spóźniony timer | `TimerElapsed` po `AppWindowAttached`/`CancelTimer` | brak komend i snapshotu | anulowany `timerId` ignorowany |
| Nieznany timer | `TimerElapsed` spoza korelacji | brak komend i snapshotu | ignorowany, także po `Dispose` schedulera |
| Awaria w trakcie startu | `CommandFailed` startu, `ProcessExited`, `CdpLost` w `Starting` | `Failed`; tylko `CancelTimer`; bez shella i fokusu | — |
| Restart po timeoucie | `RestartApp` → `Stopping` → `Starting` | nowy `ScheduleTimer` z nowym `timerId`; spóźniony `TimerElapsed` starego id ignorowany | — |
| Scheduler: termin | `FakeTimeProvider` przesunięty po `due` | dokładnie jeden `TimerElapsed{timerId}` do kolejki | — |
| Scheduler: anulowanie | `CancelTimer` przed `due` | brak publikacji; nieznany `timerId` to no-op | — |
| Ambient time | Core używa `Stopwatch`/`TickCount(64)`/`TimeProvider.System` | build Core pada na RS0030, test granicy trzyma wpisy zakazu | `TreatWarningsAsErrors` |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Contracts/AppCommands.cs` — po `ShowShell`: `abstract record TimerCommand(long CmdId) : Command(CmdId)`, `ScheduleTimer(long CmdId, long TimerId, string Owner, DateTimeOffset Due)`, `CancelTimer(long CmdId, long TimerId)` (wzór `WindowCommand`, `cmdId` pierwszy).
- `src/MediaStation.Core/Contracts/Messages.cs` — po `CdpLost`: `TimerElapsed(long TimerId) : Event`, dok mówi o ignorowaniu nieznanego/anulowanego `timerId`.
- `src/MediaStation.Core/Contracts/MessageTypes.cs` — `ScheduleTimer`/`CancelTimer` po `ShowShell`, `TimerElapsed` po `CdpLost`.
- `src/MediaStation.Core/Ports/Ports.cs` — `ITimerPort { void Execute(TimerCommand command); }` (jak `IWindowManagerPort`).
- `src/MediaStation.Core/Orchestrator/TimerScheduler.cs` **(nowy)** — `ITimerPort` + `IDisposable`: ctor `(TimeProvider time, Action<IOrchestratorMessage> post)`; `Dictionary<long, ITimer>` pod `lock`; `Schedule` liczy `due - GetUtcNow()` (ujemne → zero), nadpisuje duplikat `timerId`, tworzy timer przez `TimeProvider.CreateTimer`; callback usuwa wpis i dopiero wtedy publikuje `TimerElapsed`; `Cancel` usuwa/`Dispose`-uje podany `timerId`; `Dispose` zwalnia wszystko i blokuje publikację.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — ctor `(LoadedConfig config, TimeProvider time)` + `ArgumentNullException`, zapamiętany `Settings.StartingTimeout`; `ScheduleTimer ScheduleStartTimeout(appId)` i `CancelTimer? CancelStartTimeout(appId)` na mapie `timerId → appId`; `ScheduleTimer` po `StartApp` w `OnLaunchApp` (`NotRunning`) i `OnStopped` (restart); `CancelTimer` po `ActivateWindow`/routingu w `OnAppWindowAttached` i po komendach `EnterFailed`; nowy `OnTimerElapsed`: zdejmij korelację, w `Starting` wejdź w `Failed` bez shella/fokusu, inaczej `None`; `TimerElapsed` w `switch`.
- `src/MediaStation.App/Orchestrator/CommandRouter.cs` — nowy `ITimerPort` po `IInputRoutingPort`; `case TimerCommand timer: _timers.Execute(timer);`.
- `src/MediaStation.App/CompositionRoot.cs` — `new TimerScheduler(time, message => postsTo?.Post(message))` do routera, `new CoreOrchestrator(config, time)`; w `app.Exit` zwolnij scheduler przed pętlą (`notifications`, `reporter`, scheduler, `loop`).
- `src/MediaStation.Core/BannedSymbols.txt` — po wpisach `DateTimeOffset`: `P:System.TimeProvider.System`, `T:System.Diagnostics.Stopwatch`, `P:System.Environment.TickCount`, `P:System.Environment.TickCount64`, każdy `;AD-14: czas tylko przez wstrzyknięty TimeProvider`.
- `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` — `None` z `..\..\src\MediaStation.Core\BannedSymbols.txt`, `Link="BannedSymbols.txt"`, `CopyToOutputDirectory="PreserveNewest"`.
- `tests/MediaStation.Core.Tests/CoreBoundaryTests.cs` — test: plik z katalogu wyjściowego ma cztery wpisy; metadane Core bez typu `System.Diagnostics.Stopwatch` i bez `System.TimeProvider::get_System`, `System.Environment::get_TickCount`, `get_TickCount64` (wzór `TypeReferences`, plus odczyt `MemberReferences`); kontrola pozytywna jak w `Type_reference_check_detects_file_system_types`.
- `tests/MediaStation.Core.Tests/TimerSchedulerTests.cs` **(nowy)** — `FakeTimeProvider` + lista postów: publikacja dopiero po `due` i raz, `CancelTimer` przed terminem tłumi, nieznany `CancelTimer` no-op, `Dispose` tłumi i zwalnia, duplikat `timerId` zastępuje timer.
- `tests/MediaStation.Core.Tests/OrchestratorTests.cs` — helper `Create(TimeProvider? time = null)` (domyślnie `FakeTimeProvider` z `T0 = 2026-10-22T12:00Z`) i `Started()` na nim; nowe testy timeoutu; dotychczasowe asercje `cmdId`/liczby komend przesuwają się o komendy timera: `Started`(1) → `StartApp`(2), `ScheduleTimer`(3) → attach `ActivateWindow`(4), routing(5), `CancelTimer`(6).
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs` — `new CoreOrchestrator(TwoApps(), time)` (oba miejsca), `TimerScheduler` w `CommandRouter`; nowy test: host przyjmuje `StartApp` bez odpowiedzi, `Advance(startingTimeout)` → trzeci snapshot `Failed`.
- `tests/MediaStation.Core.Tests/CommandRouterTests.cs` — `FakeTimerPort` w `CreateRouter` i dwóch ręcznych konstrukcjach; test routingu `ScheduleTimer`/`CancelTimer`.
- `tests/MediaStation.Core.Tests/CoreMessageContractTests.cs` — koperty `scheduleTimer`/`cancelTimer`/`timerElapsed` + round-trip, nazwy rejestru `:392`, pliki `:411-436`, wiersze `:522-534`, typy `timerId`/`due` `:548-567`.
- Reuse: `RecordingLog`, `FakeTimeProvider`, `OrchestratorResult.None`, `AppLifecycleState.Failed`. Nie ruszać `ShellBridge`, `web/shell`, `config/**`, `tools/**`.

## Tasks & Acceptance

**Execution:**
- [x] `Core/Contracts/{AppCommands,Messages,MessageTypes}.cs`, `Core/Ports/Ports.cs` — rekordy timera w kopercie i port wykonawcy — AD-4/AD-14/AD-16.
- [x] `Core/Orchestrator/TimerScheduler.cs` — jednorazowe timery na `TimeProvider`, jawny `CancelTimer`, publikacja `TimerElapsed` — czas w Core tylko przez `TimeProvider`.
- [x] `Core/Orchestrator/Orchestrator.cs` — timeout `Starting` i obsługa `TimerElapsed` — `Failed` zamiast wiecznego `Starting` (AD-10/AD-14).
- [x] `App/Orchestrator/CommandRouter.cs`, `App/CompositionRoot.cs` — App tylko hostuje scheduler — composition root.
- [x] `Core/BannedSymbols.txt`, `tests/.../CoreBoundaryTests.cs`, `tests/.../MediaStation.Core.Tests.csproj` — zakaz ambient time z testem granicy — N8/AD-14.
- [x] `tests/MediaStation.Core.Tests/{TimerSchedulerTests,OrchestratorTests,OrchestratorLoopTests,CommandRouterTests,CoreMessageContractTests}.cs` — każdy wiersz macierzy, `verify` ticketu i kontrakty — dowód na fake'ach.

**Acceptance Criteria:**
- Given `startingTimeout` = 30 s i `FakeTimeProvider`, when `LaunchApp("netflix")`, then snapshot ma `Starting`, a komendy to `StartApp` i `ScheduleTimer{timerId, "netflix", T0+30s}`.
- Given pętla ze schedulerem i host bez odpowiedzi, when zegar przekroczy `startingTimeout`, then publikowany jest snapshot z `netflix` w `Failed`, bez nowego `focusRequest` i bez komend startu.
- Given `AppWindowAttached` przed deadlinem, when zegar przekroczy deadline, then stan zostaje `Foreground`, w batchu attach jest `CancelTimer{timerId}`, a żaden kolejny snapshot nie powstaje.
- Given `TimerElapsed` anulowanego albo nieznanego `timerId`, when `Handle`, then brak komend i brak snapshotu.
- Given aplikacja w `Starting`, when `CommandFailed` startu, `ProcessExited` albo `CdpLost`, then `Failed`, jedyna komenda to `CancelTimer`, a `FocusRequest` się nie zmienia.
- Given `Failed` po timeoucie, when `RestartApp` i potwierdzony stop, then `Starting` dostaje nowy `ScheduleTimer` z nowym `timerId`, a spóźniony `TimerElapsed` starego id nie zmienia stanu.
- Given Core z `Stopwatch`, `Environment.TickCount(64)` albo `TimeProvider.System`, when `dotnet build src/MediaStation.Core`, then build pada na RS0030, a `CoreBoundaryTests` trzyma wpisy w `BannedSymbols.txt`.
- Given `ScheduleTimer`, `CancelTimer`, `TimerElapsed`, when `SerializeEnvelope` + round-trip, then `type` to `scheduleTimer`/`cancelTimer`/`timerElapsed`, a eksport schematów ma trzy nowe pliki z polami `cmdId`,`timerId`,`owner`,`due` / `cmdId`,`timerId` / `timerId`.

## Implementation Notes

- `TimerScheduler` trzyma `Dictionary<long, Entry>` pod jedną blokadą; `Entry` istnieje przed `CreateTimer`, więc callback odporny jest na provider, który strzeli w trakcie tworzenia timera (callback sprawdza tożsamość wpisu, więc nie publikuje dla zastąpionego/anulowanego terminu). Publikacja `TimerElapsed` idzie pod blokadą, żeby `Dispose` nie mógł się wciąć między usunięcie wpisu a `Post`; `_post` w App to nieblokujące `BlockingCollection.Add`, więc blokada nie przenosi się na wątek pętli.
- Termin w przeszłości (`due - GetUtcNow() < 0`) liczy się jako zero; `due` z `FakeTimeProvider` przesuwa publikację dopiero po `Advance`, więc testy są deterministyczne bez czekania na wątek puli.
- `Orchestrator` trzyma `_startTimers: timerId → appId` i nadaje `timerId` własnym licznikiem; `ScheduleStartTimeout` woła `NextCmdId()` dopiero po `StartApp`, więc komenda timera jest ostatnia w batchu i nie zmienia znaczenia istniejących komend.
- `EnterFailed` woła `CancelStartTimeout` na końcu (po `ShowShell`/routingu), a `OnTimerElapsed` usuwa korelację **przed** `EnterFailed`, więc czasowy timeout nie produkuje `CancelTimer` (macierz: „Timeout wygasa → brak komend”). `AppWindowAttached` emituje `CancelTimer` jako trzecią komendę batcha, a `Failed`/`Background` po attachu nie mają już czego anulować.
- `TimerElapsed` poza `Starting` (np. gdyby korelacja przetrwała) jest ignorowany bez komend i snapshotu; nieznany `timerId` nie dotyka stanu.
- `CoreBoundaryTests` czyta `BannedSymbols.txt` z katalogu wyjściowego (`None` + `CopyToOutputDirectory`) i sprawdza metadane Core: brak typu `System.Diagnostics.Stopwatch` oraz brak member reference `System.TimeProvider::get_System`, `System.Environment::get_TickCount`, `System.Environment::get_TickCount64`; kontrola pozytywna (`System.IO.File::OpenRead` w assembly testów) dowodzi, że ekstrakcja `typ::nazwa` działa.
- `CompositionRoot` tworzy scheduler po deklaracji `postsTo` i zwalnia go w `app.Exit` między adapterem power a pętlą; brak nowych wpisów logu.

## Plan Change Log

### 2026-10-22 — Implementation
- Bez odstępstw od Code Map. `OrchestratorLoopTests` dostał trzy miejsca konstrukcji `CoreOrchestrator` (nie dwa): `CreateLoop`, test startu i test braku hosta; wszystkie dostały `FakeTimeProvider(T0)`, a dwa ręczne routery — realny `TimerScheduler`.
- `CoreMessageContractTests` dostał osobny test typów schematów timera (`Timer_schemas_type_ids_as_integer_and_due_as_date_time`), bo wiersze `:548-567` obejmowały tylko `appId`/`hwnd`; `due` wychodzi jako `string` z `format: date-time`.
- `BannedSymbols.txt` ma wpisy w miejscu wskazanym przez Code Map (po `DateTimeOffset.UtcNow`, przed `System.Diagnostics.Process`), żeby zakazy czasu zostały razem.

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 3 findings — high 0, medium 1, low 2, false 0, maybe-false 0
- findings:
  - `[low]` `[patch]` `CoreBoundaryTests.Core_references_no_ambient_time_sources` używał `Assert.DoesNotContain(ForbiddenAmbientTimeMembers, members)`, co w xunit.v3 wiąże sub-span overload: przechodzi, gdy tylko jedna z trzech nazw jest w metadanych (sonda: `members` zawierające `System.TimeProvider::get_System` przechodzi, a cała trójka musi wystąpić kolejno) — poprawione na `Assert.DoesNotContain(members, member => ForbiddenAmbientTimeMembers.Contains(member))`; kontrola pozytywna `System.IO.File::OpenRead` bez zmian.
  - `[medium]` `[patch]` `TimerScheduler.Schedule` wpisywał `Entry` do mapy przed `TimeProvider.CreateTimer`, więc odrzucony termin zostawiał wieczny wpis, a `ArgumentOutOfRangeException` przechodził przez `ITimerPort.Execute` (AD-4: wykonawca nie przepuszcza wyjątku przez granicę portu). `startingTimeout` ≥ ~4 294 968 s przechodzi walidację (`CheckPositive`), a `TimeProvider.System` rzuca dla `due` dalszego niż ~49,7 dnia (potwierdzone sondą); `OrchestratorLoop` dispatchuje przed publikacją, więc snapshot `Starting` przepadał, a kafelek zostawał bez timera — poprawione: `CreateTimer` w `try/catch` z rollbackiem tego samego wpisu i bez wyjątku przez port; nowy test `Create_timer_failure_does_not_escape_execute_and_never_publishes`.
  - `[low]` `[reject]` `Dispose_suppresses_publication_and_ignores_later_schedules` nie dowodzi zwolnienia `ITimer`, a clamp terminu z przeszłości nie ma testu — wyciek dotyczy jednego `Dispose` przy zamknięciu aplikacji i nie jest osiągalny w codziennym użyciu, a zaobserwowanie zwolnienia wymaga własnego fake'a `ITimer` (więcej niż bezpośrednia poprawka lub usunięcie); tłumienie publikacji po `Dispose` jest pokryte, a ścieżkę błędu `CreateTimer` pokrywa nowy test.

## Design Notes

`ScheduleTimer` i `CancelTimer` to `Command`-y, choć ticket wypisuje tylko ich pola swoiste: AD-4 wymaga `cmdId` na każdej komendzie, a router i koperta działają na `Command`. `cmdId` tych komend nie ma korelacji — scheduler go ignoruje i nie odpowiada `CommandCompleted`, bo timer nie jest zadanym efektem, tylko terminem.

Timeout `Starting` żyje w mapie `timerId → appId` w Orchestratorze: `AppWindowAttached` i `EnterFailed` zdejmują korelację i dopisują `CancelTimer`, więc spóźniony `TimerElapsed` jest ignorowany bez zaglądania w stan schedulera. Kolejność w batchu nie ma znaczenia dla poprawności (korelacja znika w tym samym `Handle`), dlatego komendy timera idą na koniec i zmieniają tylko numerację `cmdId`, którą testy liczą od nowa.

Awaria w `Starting` daje `Failed` z samym `CancelTimer` (bez `ShowShell` i fokusu), bo nie ma jeszcze okna na pierwszym planie — zachowanie story 4 rozszerzone o anulowanie zegara. `ProcessExited`/`CdpLost` w `Stopping` i `TimerElapsed` po `Failed` pozostają ignorowane.

## Verification

**Commands:**
- `dotnet test MediaStation.slnx -c Release` -- zielone, w tym `TimerSchedulerTests`, timeout w `OrchestratorTests`/`OrchestratorLoopTests`, kontrakty i granica Core.
- `dotnet build MediaStation.slnx -c Release` -- 0 ostrzeżeń; `SchemaExport` zapisuje `schedule-timer`/`cancel-timer`/`timer-elapsed`, `npm run contracts`, `npm run typecheck`, `npm run build` przechodzą.
- `dotnet build src/MediaStation.App -c Debug` -- 0 ostrzeżeń.
- Sonda zakazu: tymczasowe `System.Diagnostics.Stopwatch` w pliku Core → `dotnet build src/MediaStation.Core -c Release` pada na RS0030 → cofnięcie sondy i build zielony.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + uruchomienie z ważnym `%LOCALAPPDATA%\MediaStation\config` -- proces żyje, log ma `snapshotPublished` i `stop {exitCode: 0}`, zero `warn`/`error`.

**Current-machine evidence (2026-10-22, HEAD `e2253fd` + working tree, ALDER / B760 Pro RS/D4 / Windows 11 Home):**
- `dotnet test MediaStation.slnx -c Release` -- 456/456 passed, 0 failed. Nowe/rozszerzone: `TimerSchedulerTests` (publikacja po `due` dokładnie raz, anulowanie przed terminem, nieznany `CancelTimer` jako no-op, `Dispose` + późniejsze `ScheduleTimer`, duplikat `timerId` zastępuje termin), `OrchestratorTests` (batch `StartApp` + `ScheduleTimer{timerId: 1, due: T0+30s}`, `TimerElapsed` w `Starting` → `Failed` bez komend i bez nowego `seq`, spóźniony `TimerElapsed` po attachu ignorowany, nieznany `timerId` ignorowany, restart po timeoucie z nowym `timerId` i ignorowanym starym `TimerElapsed`, `CancelTimer` jedyną komendą awarii w `Starting`, `CancelTimer` trzecią komendą attachu), `OrchestratorLoopTests` (host przyjmuje `StartApp` bez odpowiedzi, `Advance(30 s)` → trzeci snapshot `Failed` bez nowego `focusRequest`), `CommandRouterTests` (routing `ScheduleTimer`/`CancelTimer`), `CoreMessageContractTests` (koperty `scheduleTimer`/`cancelTimer`/`timerElapsed` + round-trip, rejestr, trzy pliki schematów, `timerId` jako `integer`, `due` jako `string`/`date-time`), `CoreBoundaryTests` (cztery wpisy ambient time w pliku z katalogu wyjściowego, brak `Stopwatch` w metadanych Core, brak member reference `TimeProvider::get_System`/`Environment::get_TickCount(64)`, kontrola pozytywna `System.IO.File::OpenRead`).
- `dotnet build MediaStation.slnx -c Release` -- success, 0 warnings, 0 errors; `web/contracts/schema/{schedule-timer,cancel-timer,timer-elapsed}.schema.json` (`required` odpowiednio `cmdId,timerId,owner,due` / `cmdId,timerId` / `timerId`, `additionalProperties: false`), `web/contracts/generated/{schedule-timer,cancel-timer,timer-elapsed}.ts`, `npm run typecheck` i Vite build zielone.
- `dotnet build src/MediaStation.App -c Debug` -- success, 0 warnings; `npm run contracts` + `npm run typecheck` zielone.
- Sonda zakazu (AD-14/N8): tymczasowy plik Core z `Stopwatch.StartNew()`, `TimeProvider.System`, `Environment.TickCount`, `Environment.TickCount64` dał 5× `error RS0030` (zakaz obowiązuje dla wszystkich czterech wpisów) i `Build FAILED`; po usunięciu pliku `dotnet build src/MediaStation.Core -c Release` → success, 0 warnings.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` -- success. Uruchomiony `MediaStation.exe` (pid 43108) z ważnym `%LOCALAPPDATA%\MediaStation\config`: proces żył po 8 s, a log `%LOCALAPPDATA%\MediaStation\logs\mediastation-2026-10-21.jsonl` (nazwa z daty UTC) zawiera dla przebiegu `start`, `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}`, `powerSignalsRegistered`, `snapshotPublished {rev: 1, focusRequest: {target: "default", seq: 1}}` oraz `stop {exitCode: 0}` po zamknięciu okna; zero wpisów `warn`/`error`.
- Timeout `Starting` nie jest osiągalny w opublikowanej aplikacji bez prawdziwego hosta (dziś `MissingHostPort` odpowiada `CommandFailed` natychmiast), więc jego dowodem są testy Orchestratora i pętli z `FakeTimeProvider`. Sprawdzenie całości na UM760 zostaje w odbiorze epiku użytkownika.
- Po poprawkach przeglądu (2026-10-22, ten sam baseline, ALDER) przebieg powtórzony w całości: `dotnet test MediaStation.slnx -c Release` 457/457 (nowy `Create_timer_failure_does_not_escape_execute_and_never_publishes`), `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` z 0 ostrzeżeń, publish self-contained i uruchomienie (pid 37368): `start`, `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}`, `powerSignalsRegistered`, `snapshotPublished {rev: 1, focusRequest {default, 1}}`, `stop {exitCode: 0}`, zero wpisów `warn`/`error`. Sondy triage'u (`Assert.DoesNotContain` wiąże sub-span; `TimeProvider.System` rzuca dla `due` > ~49,7 dnia) wykonane tymczasowymi plikami testowymi, potem usunięte.

## Auto Run Result

**Status:** built.

**Summary:** Core zyskał komendy `ScheduleTimer{timerId, owner, due}`/`CancelTimer{timerId}` i Event `TimerElapsed{timerId}`. `TimerScheduler` w Core liczy terminy wyłącznie na wstrzykniętym `TimeProvider`, publikuje jeden `TimerElapsed` na termin, a anulowanie jest wyłącznie jawne. Orchestrator planuje timeout `Starting` z `settings.startingTimeout` przy wejściu w `Starting`, anuluje go po `AppWindowAttached` (i przy wejściu w `Failed`) i ignoruje `TimerElapsed` nieznanego albo anulowanego `timerId`. App tylko hostuje scheduler w composition root. `BannedSymbols.txt` zakazuje `TimeProvider.System`, `Stopwatch` i `Environment.TickCount(64)`, a `CoreBoundaryTests` pinuje wpisy i brak ambient time w metadanych Core.

**Files changed:**
- `src/MediaStation.Core/Contracts/AppCommands.cs` — `TimerCommand`, `ScheduleTimer`, `CancelTimer`.
- `src/MediaStation.Core/Contracts/Messages.cs` — `TimerElapsed`.
- `src/MediaStation.Core/Contracts/MessageTypes.cs` — rejestr trzech nowych rekordów.
- `src/MediaStation.Core/Ports/Ports.cs` — `ITimerPort`.
- `src/MediaStation.Core/Orchestrator/TimerScheduler.cs` — scheduler na `TimeProvider`, jawny `CancelTimer`, publikacja `TimerElapsed`, odporny na odrzucony termin.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — zegar w konstruktorze, mapa `timerId → appId`, timeout `Starting`, obsługa `TimerElapsed`.
- `src/MediaStation.App/Orchestrator/CommandRouter.cs` — routing komend timera do portu.
- `src/MediaStation.App/CompositionRoot.cs` — scheduler w composition root, zwalniany przed pętlą.
- `src/MediaStation.Core/BannedSymbols.txt` — cztery wpisy ambient time (AD-14).
- `tests/MediaStation.Core.Tests/{TimerSchedulerTests,OrchestratorTests,OrchestratorLoopTests,CommandRouterTests,CoreMessageContractTests,CoreBoundaryTests}.cs`, `MediaStation.Core.Tests.csproj` — dowód na fake'ach, kontrakty i granica Core.

**Review findings (quick):** 3 findings — high 0, medium 1, low 2. Patches applied: 2 (granica Core: sprawdzanie per element zamiast sub-span; scheduler: `CreateTimer` w `try/catch` z rollbackiem wpisu i testem). Deferred: 0. Rejected: 1 (low — brak asercji zwolnienia `ITimer` w teście `Dispose`; wyciek tylko przy zamknięciu aplikacji, a jego obserwacja wymaga własnego fake'a `ITimer`).

**Patched counts by verdict:** high 0, medium 1, low 1.

**Follow-up review recommendation:** `false`.

**Verification:** `dotnet test MediaStation.slnx -c Release` 457/457; `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` z 0 ostrzeżeń (`schedule-timer`/`cancel-timer`/`timer-elapsed` w schematach i TS, typecheck i Vite zielone); sonda zakazu dała 5× RS0030 dla wszystkich czterech wpisów; publish self-contained i uruchomienie z ważnym configiem (pid 37368): `snapshotPublished {rev: 1, focusRequest {default, 1}}`, `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}`, `stop {exitCode: 0}`, brak `warn`/`error`.

**Residual risks:** bardzo duży `startingTimeout` (≥ ~4 294 968 s, przyjmowany przez walidację) nie tworzy timera — scheduler nie przepuszcza wyjątku, ale aplikacja zostaje w `Starting` bez timeoutu; górną granicę dokłada story 11. Scheduler publikuje `TimerElapsed` pod blokadą (bezpieczne, bo `OrchestratorLoop.Post` nie blokuje). Timeout `Starting` nie jest osiągalny w opublikowanej aplikacji bez prawdziwego hosta, więc jego dowodem są testy Core i pętli; całościowy test na UM760 zostaje w odbiorze epiku użytkownika.
