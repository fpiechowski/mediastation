---
title: 'transitionId i Home w trakcie Starting'
type: 'feature'
ticket: '6'
created: '2026-10-22'
status: done
baseline_revision: '2084c3b497e428d7956af193a30c3a55e0e1b4fa'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 1
followup_review_recommended: false
context: []
warnings: ['oversized']
deferred:
  - summary: >-
      Spóźniony AppWindowAttached po timeoucie Starting jest ignorowany, a Hwnd zdjęte w Failed, więc okno, które pojawi się po terminie, może zostać na wierzchu nad shellem bez kompensującego MinimizeWindow.
    evidence: |-
      Guard `app.State != AppLifecycleState.Starting` w `OnAppWindowAttached` (Orchestrator.cs:215) i `app.Hwnd = null` w `EnterFailed` (Orchestrator.cs:400) pochodzą ze story 5 i nie zmieniły się w tej story; zmiana domyka kompensację tylko dla attachu w Starting. Skutek potwierdzony sondą: Home w Starting → TimerElapsed → AppWindowAttached daje brak komend i brak snapshotu, a kolejny Home nic nie robi. Rozwiązanie wymaga decyzji, czy Failed zachowuje Hwnd i minimalizuje spóźnione okno, czy zostawia to hostowi — to osobna story.
    location: >-
      src/MediaStation.Core/Orchestrator/Orchestrator.cs:215
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Decyzje Orchestratora nie mają kolejności: Home w trakcie `Starting` jest ignorowany, a spóźnione `AppWindowAttached` wysyła `ActivateWindow` i wchodzi w `Foreground`, więc start wygrywa z nowszą decyzją użytkownika i przykrywa ekran główny.

**Approach:** Każda decyzja o pierwszym planie dostaje wewnętrzny, monotoniczny `transitionId` (start aplikacji, powrót z tła, Home, `OrchestratorStarted` i `SystemResumed` z fokusem `default`, `Failed` z pierwszego planu). `AppState` pamięta id decyzji, która miała wprowadzić aplikację na pierwszy plan; `AppWindowAttached` porównuje je z bieżącym id i po przegranej daje `MinimizeWindow` + `Background` zamiast `ActivateWindow`, bez `SetInputRouting`. Home w trakcie `Starting` od razu wysyła `ShowShell`, nie rusza `focusRequest` i nie anuluje timeoutu `Starting`, który dalej prowadzi w `Failed`.

## Boundaries & Constraints

**Always:** czas tylko przez `TimeProvider` (AD-14); handler czysty i synchroniczny; `cmdId`, `timerId`, `transitionId` nadaje wyłącznie Orchestrator; `transitionId` jest wewnętrzny, nie trafia do kontraktów (wynik komendy wraca jako `cmdId`); „nowsza decyzja” = większe id; Home nie zmienia `focusRequest` (AD-16) i nie anuluje timeoutu `Starting`; Home w trakcie `Starting` bez `SetInputRouting`, bo tryb zostaje `shell`; po kompensacji aplikacja jest w `Background` z zapisanym `hwnd`; komendy timera na końcu batcha; `Starting` bez attachu zostaje `Starting`, żeby timeout mógł zadziałać (AD-8: OK na Starting jest ignorowane).

**Never:** przełączanie między dwiema żywymi aplikacjami i zdejmowanie guardu `AnotherAppIsActive` (poza intentem); zmiany w `web/**`, `CompositionRoot`, `ShellBridge`, `CommandRouter`, hostach, `Messages.cs`, `AppCommands.cs` i configu; nowe wpisy logu; adopcja (7) i `RenewSession`/polityka wybudzenia (8) — ta story daje tylko mechanizm; parkowanie ticketu na UM760.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Start bez Home | `LaunchApp` → attach | `ActivateWindow`, `SetInputRouting{app}`, `CancelTimer` startu; `Foreground` (bez zmian) | — |
| Home w trakcie Starting | Home przy aplikacji w `Starting` | tylko `ShowShell`; stan `Starting`; `rev`+1; ten sam `focusRequest`/`seq`; brak `CancelTimer` | — |
| Spóźniony attach | attach po Home | `MinimizeWindow{hwnd}` + `CancelTimer` startu; `Background`; brak `ActivateWindow` i routingu | — |
| Timeout po Home | Home, `Advance(30 s)`, `TimerElapsed` | brak komend; `Failed`; bez nowego `focusRequest` | korelacja `_startTimers` żyje do terminu |
| Powrót po kompensacji | `LaunchApp` przy `Background` z `hwnd` | `ActivateWindow` + `SetInputRouting{app}`; `Foreground` | — |
| Home bez Starting/Foreground | Home przy brakach | brak komend i snapshotu (bez zmian) | — |
| Home z Foreground | Home przy `Foreground` | `MinimizeWindow`, `ShowShell`, `SetInputRouting{shell}` (bez zmian) i nowe id, więc równoległy `Starting` też dostanie kompensację | — |
| Attach po timeoucie | attach w `Failed` | ignorowany | — |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — cała zmiana stanu. Pole `_transitionId` obok `_cmdId` (`:37`), `NextTransitionId()` obok `NextCmdId()` (`:533`); `AppState` (`:546-560`) dostaje `ForegroundTransition` (0 = brak decyzji).
  - `OnDefaultFocus` (`:102`), `OnResume` (`:142`) i `EnterFailed` (`:355`, tylko `wasForeground: true`) podbijają licznik; `OnResume` zapisuje id w aplikacji. `RestartApp` to decyzja podjęta w `OnStop` (`:215`), więc przy `restart: true` nowe id powstaje tam i jest zapisane w `PendingStop`, a `OnStopped` (gałąź restartu `:487`) używa tego id zamiast nadawać nowe — inaczej restart podjęty przed Home albo wybudzeniem wygrywa z nimi po potwierdzeniu stopu.
  - `OnLaunchApp` (`:114`, gałąź `NotRunning` `:131`) zapisuje id decyzji startu w `app.ForegroundTransition` przed `Starting`.
  - `OnHome` (`:161`): przy `Foreground` dotychczasowy batch `MinimizeWindow`/`ShowShell`/routing + podbicie id; przy braku `Foreground`, ale aplikacji w `Starting` — podbicie id i sam `ShowShell` ze snapshotem; inaczej `None`.
  - `OnAppWindowAttached` (`:186`): po guardzie `Starting` rozgałęzienie — `app.ForegroundTransition == _transitionId` daje dotychczasowy batch (`ActivateWindow`, routing, `CancelTimer`), inaczej `Background` + `MinimizeWindow` + `CancelTimer` i zero routingu. `Hwnd` w obu gałęziach.
  - Doc `OnHome` (`:155-159`) i `OnAppWindowAttached` (`:181-185`) mówią dziś, że Home w `Starting` jest ignorowany; komentarz `AnotherAppIsActive` (`:236`) zostaje.
- `tests/MediaStation.Core.Tests/OrchestratorTests.cs` — `Home_while_app_is_starting_is_ignored` (`:336-348`) łamie nowy kontrakt i wymaga zamiany; helpery `TwoApps`/`Create`/`Started`/`Foreground`/`Background` (`:14-47`) i `Home_from_foreground_...` (`:300-321`) bez zmian; `Attached_window_in_starting_...` (`:220-239`) pilnuje zwykłego attachu (`ActivateWindow(4)`, routing `5`, `CancelTimer(6, 1)`), a `Timer_elapsed_in_starting_...` (`:992-1008`) timeoutu. Nowe asercje używają konkretnych `cmdId`/`timerId`, bo licznik się przesuwa.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs` — wzór `SilentHost` + `TimerScheduler` + `postsTo` (`:203-291`); nowy test po `:291`, helpery `:347-408` bez zmian.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — wewnętrzny `transitionId`, zapis decyzji w `AppState`, podbicie w Home/default/resume/restart/Failed — nowsza decyzja zastępuje poprzednią, a spóźniony attach nie wchodzi na pierwszy plan. Restart bierze id z chwili decyzji (`OnStop`), nie z potwierdzenia stopu.
- [x] `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — Home w trakcie `Starting` (`ShowShell`, bez fokusu i bez anulowania timeoutu) — kafelek nie utyka, ekran główny zostaje na wierzchu.
- [x] `tests/MediaStation.Core.Tests/OrchestratorTests.cs` — zamiana `Home_while_app_is_starting_is_ignored` i testy każdego wiersza macierzy plus `verify` i przegranej restartu — dowód na `FakeTimeProvider`.
- [x] `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs` — pętla: Home w trakcie `Starting`, attach, dispatch `MinimizeWindow`, snapshot `Background`, potem `Advance(30 s)` bez `Failed` — dowód, że kompensacja dochodzi do wykonawców.

**Acceptance Criteria:**
- Given `Started` i `LaunchApp("netflix")` w `NotRunning`, when Home, then jedyna komenda to `ShowShell`, snapshot ma `Starting` i ten sam `focusRequest`/`seq`, brak `CancelTimer` i `SetInputRouting`.
- Given Home w trakcie `Starting`, when `AppWindowAttached("netflix", hwnd, pid)`, then komendy to `MinimizeWindow{hwnd}` i `CancelTimer` startu, snapshot ma `Background`, brak `ActivateWindow` i `SetInputRouting`.
- Given Home w trakcie `Starting` bez attachu, when `TimerElapsed` po `startingTimeout`, then brak komend, `Failed`, a `focusRequest` i `seq` bez zmian.
- Given `Background` po kompensacji, when `LaunchApp`, then `ActivateWindow{hwnd}` i `SetInputRouting{app}` z większym `epoch`, snapshot `Foreground`.
- Given `Started` bez `Starting` i `Foreground`, when Home, then brak komend i snapshotu.
- Given `Foreground`, when Home, then `MinimizeWindow`, `ShowShell`, `SetInputRouting{shell}` w tej kolejności, a `focusRequest` bez zmian.
- Given `Starting` po Home, when `LaunchApp` tej aplikacji, then ignorowane; stan `Starting` do attachu albo timeoutu.
- Given `RestartApp` w `Background` i Home (albo `SystemResumed`) w trakcie `Stopping`, when `CommandCompleted` stopu i `AppWindowAttached`, then komendy to `MinimizeWindow{hwnd}` i `CancelTimer` startu, snapshot ma `Background`, brak `ActivateWindow` i `SetInputRouting`.

## Design Notes

`transitionId` nie trafia do kontraktów: żadne zdarzenie go nie niesie, wykonawca odpowiada `cmdId`, a id rozstrzyga tylko, która decyzja jest bieżąca (AD-14; architektura `:386` wymienia `transitionId` wśród identyfikatorów nadawanych przez Orchestrator). Porównanie z globalnym licznikiem wystarcza, bo pierwszy plan ma jednego właściciela — bez mapy korelacji do sprzątania.

Id powstaje w chwili decyzji, nie w chwili jej skutku: `RestartApp` decyduje w `OnStop`, więc `PendingStop` niesie id, a `OnStopped` go używa. Inaczej stop potwierdzony po Home albo wybudzeniu nadawałby restartowi świeższe id niż decyzja użytkownika i aplikacja wracałaby na wierzch nad shellem.

Home nie anuluje timeoutu `Starting`, bo anulowanie zostawiłoby kafelek w `Starting` na zawsze (AD-8; review-adversary-update H3). Kompensacja daje `Background`, a nie `Starting`, bo attach jest dowodem istnienia okna; zapisane `hwnd` pozwala wrócić przez `LaunchApp`.

## Implementation Notes

- Loopback po przeglądzie (2026-10-22): kod pierwszego przebiegu wycofany, więc jego notatki i dowody usunięte — dotyczyły wersji, w której restart dostawał `transitionId` po potwierdzeniu stopu.
- 2026-10-22 — implementacja: `_transitionId` + `NextTransitionId()` obok `_cmdId`, `AppState.ForegroundTransition` (0 = brak decyzji), `PendingStop.TransitionId`. Podbicie: `OnDefaultFocus` (start i wybudzenie), `OnLaunchApp` (gałąź `NotRunning`, zapis w aplikacji), `OnResume` (zapis w aplikacji), `OnStop` przy `restart` (zapis w `PendingStop`, użyty w `OnStopped`), `EnterFailed` tylko przy `wasForeground`, `OnHome`. `AppWindowAttached` po guardzie `Starting` porównuje zapisane id z bieżącym: zgodność daje dotychczasowy batch, przegrana daje `MinimizeWindow` + `CancelTimer` i `Background` bez routingu, z `hwnd` w stanie.
- Odczyt Code Map wymagany przez AC „RestartApp … i Home … w trakcie `Stopping`”: `OnHome` podbija `transitionId` zawsze, także gdy nie ma czego chować (aplikacja w `Stopping`, shell na wierzchu) i wynik to `None`. Bez tego Home w trakcie `Stopping` nie odbierałoby restartowi prawa do pierwszego planu. Kontrakt zewnętrzny wiersza „Home bez Starting/Foreground” bez zmian: brak komend i snapshotu.
- Testy: `OrchestratorTests.Home_while_app_is_starting_shows_shell_without_focus_routing_or_timer_cancel` (zamiast `..._is_ignored`), `Late_attached_window_after_home_is_minimized_and_moves_to_background`, `Start_timeout_after_home_enters_failed_without_commands_or_new_focus`, `Launch_after_home_compensation_activates_window_and_routes_app_input`, `Home_from_foreground_makes_parallel_starting_app_lose_its_window`, `Restart_decided_before_home_or_wake_in_stopping_loses_its_window` (`[Theory]`: `home`, `resumed`); `OrchestratorLoopTests.Home_while_starting_minimizes_late_window_and_cancels_start_timeout`.

## Plan Change Log

### 2026-10-22 — Loopback po przeglądzie (quick, iteracja 1)
- Trigger: `[medium]` `[bad_plan]` — restart nadawał `transitionId` w `OnStopped` (po potwierdzeniu stopu), więc Home albo `SystemResumed` w trakcie `Stopping` przegrywał z decyzją podjętą wcześniej, a spóźniony attach przykrywał shell.
- Amended: Code Map (`OnStop`/`PendingStop`/`OnStopped`), Design Notes (id powstaje w chwili decyzji), Tasks i nowe AC dla przegranej restartu. Wnętrze `<intent-contract>` bez zmian.
- Known-bad state avoided: restart → `Starting` z najświeższym id po Home/wake → `ActivateWindow` i `Foreground` mimo shella na wierzchu.
- KEEP: `transitionId` wewnętrzny (poza kontraktami), jeden globalny licznik + `AppState.ForegroundTransition`; kompensacja `MinimizeWindow` + `CancelTimer` + `Background` bez routingu; Home w `Starting` = sam `ShowShell`, bez fokusu i bez anulowania timeoutu; batch Home z `Foreground` bez zmian; brak zmian w kontraktach, configu i logach; test pętli z kompensacją.

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 3 findings — high 0, medium 2, low 0, false 1, maybe-false 0
- findings:
  - `[medium]` `[bad_plan]` Restart nadawał `transitionId` dopiero w `OnStopped`, więc decyzja podjęta wcześniej wygrywała z Home/`SystemResumed` w trakcie `Stopping` i spóźniony attach przykrywał shell — potwierdzone w kodzie (`OnStop` bez id, `OnStopped` `NextTransitionId()`) i w sekwencji restart → `LaunchApp` innej aplikacji → Home → stop → attach; poprawka: id powstaje w `OnStop` i `PendingStop` je niesie, kod re-derived (Plan Change Log).
  - `[false]` `[reject]` `OnHome` po zmianie wpada w gałąź `Starting`, gdy aplikacja w `Foreground` nie ma `Hwnd`, co miało dawać niespójny snapshot — sprawdzone: żadna ścieżka nie ustawia `Foreground` bez `Hwnd` (`OnResume` wymaga `Hwnd`, a `OnAppWindowAttached` ustawia je przed zmianą stanu), więc stan jest nieosiągalny, a guard byłby kodem na stan, którego program nie może osiągnąć.
  - `[medium]` `[defer]` Spóźniony attach po timeoucie `Starting` jest ignorowany, a `Hwnd` już zdjęte, więc okno, które pojawi się po `Failed`, nie dostaje `MinimizeWindow` — zachowanie sprzed tej zmiany (guard `State != Starting` ze story 5), nie caused by this change; pozycja do listy `deferred` po re-derivacji, bo ta iteracja kończy się loopbackiem `bad_plan`.

### 2026-10-22 — Review pass (iteracja 2)
- verdicts: 4 findings — high 0, medium 1, low 2, false 1, maybe-false 0
- findings:
  - `[medium]` `[defer]` Spóźniony attach po timeoucie `Starting` nadal ignorowany, a `Hwnd` zdjęte — carried: ten sam location i claim co wiersz wyżej, kod po re-derivacji nadal tak działa; pozycja dopisana do frontmatter `deferred` z evidence i `severity: medium`.
  - `[false]` `[reject]` Dwa równoległe `Starting` i start drugiej aplikacji rzekomo chowa okno pierwszej bez decyzji użytkownika — sprawdzone: wybór kafelka drugiej aplikacji jest nowszą decyzją o pierwszym planie (AD-14), więc kompensacja pierwszej jest zamierzona, zachowuje niezmiennik „jedna aplikacja na pierwszym planie” (stary kod aktywowałby obie po kolei), a kafel `Background` wraca przez `LaunchApp`; `CommandFailed` drugiej zostawia shell na wierzchu, więc opisana szkoda się nie materializuje.
  - `[low]` `[reject]` Odwołania `:linia` w Code Map nie zgadzają się z drzewem po zmianie — poprawka wymaga edycji planu, więc odpada z definicji; mapa nazywa symbole i testy jednoznacznie, a numery dryfowały przez wstawki po jej spisaniu.
  - `[low]` `[patch]` Doc `ForegroundTransition` obiecywał porównanie „mniejsze id” i zerowanie przy braku decyzji, a kod porównuje `!=` i nigdy nie zeruje pola — doc przepisany na „każda różnica znaczy nowszą decyzję”, a `0` opisane jako stan tylko przed pierwszą decyzją.

## Verification

**Commands:**
- `dotnet test MediaStation.slnx -c Release` -- zielone, w tym nowe testy Home w `Starting`, kompensacji, timeoutu i przegranej restartu.
- `dotnet build MediaStation.slnx -c Release` -- 0 ostrzeżeń; kontrakty i schematy bez zmian, więc `npm run contracts` nie jest potrzebne.
- `dotnet build src/MediaStation.App -c Debug` -- 0 ostrzeżeń.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + uruchomienie z ważnym configiem -- proces żyje, log bez `warn`/`error`, snapshot `rev: 1`.

**Implementation-time evidence (2026-10-22, baseline `2084c3b` + working tree, ALDER / B760 Pro RS/D4 / Windows 11 Home):**
- `dotnet test MediaStation.slnx -c Release` -- 477/477 passed, 0 failed, 0 skipped. Nowe: Home w `Starting` (`ShowShell(4)`, ten sam `focusRequest`/`seq`, brak `CancelTimer`/`SetInputRouting`), spóźniony attach (`MinimizeWindow(5)` + `CancelTimer(6, 1)`, `Background`, bez aktywacji i routingu), timeout po Home (`Failed` bez komend i bez nowego fokusu), powrót po kompensacji (`ActivateWindow(7)` + `SetInputRouting(8, …, epoch 2)`), Home z `Foreground` odbierający okno równolegle startującej aplikacji, przegrana restartu po Home i po `SystemResumed` w `Stopping`, pętla z kompensacją dochodzącą do `CommandRouter`/`TimerScheduler` i brakiem `Failed` po `Advance(30 s)`. Po dodaniu `Attached_window_after_start_timeout_is_ignored` (domyka wiersz „Attach po timeoucie”: attach po `TimerElapsed` daje brak komend i brak snapshotu) przebieg kontrolny: 478/478 passed, 0 skipped.
- `dotnet test … --filter OrchestratorLoopTests` powtórzony 3× -- 11/11 passed za każdym razem (test pętli stabilny).
- `dotnet build MediaStation.slnx -c Release` -- 0 ostrzeżeń, 0 błędów (kontrakty bez zmian, `npm run contracts` nie jest potrzebne).
- `dotnet build src/MediaStation.App -c Debug` -- 0 ostrzeżeń.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` -- success. `MediaStation.exe` (pid 45348): proces żył po 9 s, `CloseMainWindow` → `stop {exitCode: 0}`; log `%LOCALAPPDATA%\MediaStation\logs\mediastation-2026-10-22.jsonl` ma `start`, `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}`, `powerSignalsRegistered`, `snapshotPublished {rev: 1, focusRequest: {default, 1}}`, zero `warn`/`error`. Powtórka po dodaniu testu (pid 36560, żył po 9 s, `stop {exitCode: 0}`) dała ten sam zestaw wpisów, też bez `warn`/`error`.
- Home nie jest osiągalny z UI (brak InputHub), więc kompensację potwierdzają testy Core i pętli; całościowy odbiór epiku na UM760 zostaje u użytkownika.
- Po poprawce przeglądu (doc `ForegroundTransition`) przebieg kontrolny: `dotnet test MediaStation.slnx -c Release` 478/478 (0 skipped), `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` z 0 ostrzeżeń i 0 błędów.

## Auto Run Result

**Status:** built.

**Summary:** Orchestrator nadaje każdej decyzji o pierwszym planie wewnętrzny `transitionId`: start aplikacji, powrót z tła, Home, `OrchestratorStarted`/`SystemResumed` i `Failed` z pierwszego planu, a restart bierze id z chwili decyzji (`OnStop`) i niesie je w `PendingStop`. `AppState.ForegroundTransition` pamięta decyzję, która miała wprowadzić aplikację na pierwszy plan; `AppWindowAttached` porównuje ją z bieżącym id — zgodność daje `ActivateWindow` + routing + `CancelTimer`, przegrana daje kompensujące `MinimizeWindow` + `CancelTimer` i `Background` bez `SetInputRouting`, z zapisanym `hwnd`. Home w trakcie `Starting` pokazuje od razu `ShowShell`, nie rusza `focusRequest`, nie anuluje timeoutu `Starting` i sam podbija id, więc restart albo wybudzenie w trakcie `Stopping` wygrywa z decyzją podjętą wcześniej.

**Files changed:**
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — `_transitionId`/`NextTransitionId()`, `AppState.ForegroundTransition`, `PendingStop.TransitionId`, podbicia w Home/default/resume/restart/Failed, kompensacja w `OnAppWindowAttached`, Home w `Starting` jako sam `ShowShell`; doc-i zaktualizowane, kontrakty bez zmian.
- `tests/MediaStation.Core.Tests/OrchestratorTests.cs` — zamiana `Home_while_app_is_starting_is_ignored` i nowe testy macierzy, timeoutu po Home, powrotu po kompensacji, równoległego startu po Home, przegranej restartu po Home/`SystemResumed` i attachu po timeoucie.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs` — test pętli: `ShowShell`/`MinimizeWindow`/`CancelTimer` dochodzą do routera i schedulera, snapshot `Background`, po `Advance(30 s)` brak `Failed`.

**Review findings (quick):** 7 findings across 2 passes — high 0, medium 3, low 2, false 2, maybe-false 0.
- Patches applied: 1 (low) — doc `ForegroundTransition` opisuje porównanie `!=` i brak zerowania zamiast „mniejszego id”.
- Deferred: 1 (medium) — spóźniony attach po timeoucie `Starting` ignorowany bez kompensacji; zapisane w `deferred` frontmatter.
- Rejected: 3 — `OnHome` fall-through na nieosiągalnym stanie `Foreground` bez `Hwnd` (false); dwa równoległe `Starting` i zamierzona kompensacja nowszej decyzji (false); odwołania `:linia` w Code Map (low, poprawka wymaga edycji planu).
- Loopback `bad_plan`: 1 (medium) — restart nadawał id po potwierdzeniu stopu; poprawione w planie i re-derived (Plan Change Log, iteracja 1).

**Patched counts by verdict:** high 0, medium 0, low 1.

**Follow-up review recommendation:** `false`.

**Verification:** `dotnet test MediaStation.slnx -c Release` 478/478 (0 failed, 0 skipped); `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` z 0 ostrzeżeń i 0 błędów; `dotnet publish … -r win-x64 --self-contained true` + uruchomienie z configiem: proces żył, `snapshotPublished {rev: 1, focusRequest {default, 1}}`, `stop {exitCode: 0}`, zero `warn`/`error`.

**Residual risks:** Home nie jest osiągalny z UI (brak InputHub), więc kompensację potwierdzają wyłącznie testy Core i pętli. Spóźniony attach po timeoucie `Starting` zostaje bez kompensacji (deferred, medium): okno, które pojawi się po `Failed`, może zostać na wierzchu, dopóki story nie zdecyduje o zachowaniu `Hwnd` w `Failed`. Całościowy test UM760 całego epiku wykonuje użytkownik przy `epic-boundary`.
