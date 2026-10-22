---
title: 'Adopcja żywych aplikacji po restarcie'
type: 'feature'
ticket: '7'
created: '2026-10-22'
status: done
baseline_revision: 'a3afae8736dcf2779700f29284ac860a081e0cbe'
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

**Problem:** Po restarcie MediaStation żywa przeglądarka i Moonlight nie mają właściciela: Orchestrator nigdy nie pyta hostów o istniejące okna, a `AppWindowAttached` poza `Starting` jest ignorowany, więc porzucone okno zostaje na wierzchu nad shellem, a kafelek kłamie `NotRunning` (AD-10 zabrania zabijania żywej przeglądarki po restarcie).

**Approach:** `OrchestratorStarted` dokłada do batcha startu `ProbeExisting{cmdId, appId}` dla każdej aplikacji z `apps.json` (po routingu `shell`). `AppWindowAttached` dla aplikacji w `NotRunning` — czyli bez wcześniejszego `StartApp` — jest adopcją: `hwnd` trafia do stanu, aplikacja ląduje w `Background` z `MinimizeWindow`, a `ShowShell` trzyma ekran główny na wierzchu. Tryb wejścia, `focusRequest` i timery zostają nietknięte.

## Boundaries & Constraints

**Always:** `cmdId` nadaje wyłącznie Orchestrator z jednego licznika, komendy fire-and-forget (AD-14); adopcja nie podbija `transitionId` ani `_focusSeq`, nie wysyła `SetInputRouting` i nie rusza timerów; kolejność adopcji to `MinimizeWindow`, potem `ShowShell` (AD-5/AD-15); `ShowShell` tylko gdy `AnotherAppIsActive(app)` jest fałszywe, bo sondy są fire-and-forget; handler czysty i synchroniczny; `MissingHostPort.Probe` milczy — bez Eventu i bez wpisu logu; sondy wyłącznie przy `OrchestratorStarted`.

**Never:** sondowanie na `SystemResumed`, Home i `LaunchApp`; adopcja w `Starting`, `Foreground`, `Background`, `Stopping` albo `Failed` (spóźniony attach po `Failed` zostaje w DW-4); nowe Eventy/komendy poza `ProbeExisting`; nowe wpisy logu; zmiany w `web/**`, `CompositionRoot`, `ShellBridge`, `TimerScheduler`, configu i adapterach tylko-log; parkowanie ticketu na UM760.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Start z sondami | `OrchestratorStarted`, `netflix` + `plex` | `[SetInputRouting{shell, null, blocked, epoch 1}, ProbeExisting{netflix}, ProbeExisting{plex}]` w kolejności `apps.json`; snapshot `rev: 1`, `focusRequest{default, 1}` | — |
| Adopcja przeglądarki | attach `netflix{hwnd, 4321}` w `NotRunning` | `MinimizeWindow{hwnd}`, `ShowShell`; `Background`, `hwnd` zapisany; brak `ActivateWindow` i `SetInputRouting`; ten sam `focusRequest`/`seq`; `rev`+1 | — |
| Adopcja Moonlighta | attach `moonlight` w `NotRunning` | jak wyżej; routing aplikacji nigdy nie leci | — |
| Druga adopcja | attach drugiej aplikacji po pierwszej | `MinimizeWindow` + `ShowShell` dla każdej; obie w `Background`; `focusRequest` zostaje `{default, 1}` | — |
| Adopcja przy zajętym planie | inna aplikacja w `Foreground`/`Starting`, attach w `NotRunning` | tylko `MinimizeWindow{hwnd}`; `Background`; bez `ShowShell` i routingu; druga aplikacja bez zmian | — |
| Powrót po adopcji | `LaunchApp` przy `Background` z `hwnd` | `ActivateWindow{hwnd}` + `SetInputRouting{app}` z nowym `epoch`; snapshot `Foreground` | — |
| Nieznana aplikacja | attach z `appId` spoza `apps.json` | brak komend i snapshotu (bez zmian) | ignorowane |
| Brak hosta | `ProbeExisting` do `MissingHostPort` | brak Eventu i wpisu logu; kafelek zostaje `NotRunning` | brak wyjątku przez granicę portu |
| Attach w `Failed` | attach po timeoucie `Starting` | ignorowany (zachowanie story 6; DW-4 zostaje otwarte) | — |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Contracts/AppCommands.cs` — `ProbeExisting(long CmdId, string AppId) : Command` po `StopApp`; doc: host podpina żywe okno i raportuje `AppWindowAttached`, albo milczy (cisza jest poprawną odpowiedzią).
- `src/MediaStation.Core/Contracts/MessageTypes.cs:22` — `typeof(ProbeExisting)` zaraz po `typeof(StopApp)`; rejestracja napędza kopertę, JSON Schema i typy TS.
- `src/MediaStation.Core/Contracts/Messages.cs:16-20, 57-63` — doc bazy `Command` nazywa sondę wyjątkiem od `CommandCompleted`/`CommandFailed`; doc `AppWindowAttached` rozdziela start (`Foreground` + `ActivateWindow`) od adopcji (`Background` + `MinimizeWindow`).
- `src/MediaStation.Core/Ports/Ports.cs:7-18` — `void Probe(ProbeExisting command);` w `IAppHostPort` z doc-em o ciszy jako poprawnej odpowiedzi (wzór: `TimerCommand` w `AppCommands.cs:31-36`).
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs:93-97` — `OnOrchestratorStarted` buduje listę: routing, potem `ProbeExisting` dla każdej pozycji `_apps` (kolejność `apps.json`) i przekazuje ją do `OnDefaultFocus`; `:213-245` — `OnAppWindowAttached`: po `TryGetValue` gałąź adopcji dla `NotRunning` (zapis `Hwnd`, `Background`, `MinimizeWindow`, `ShowShell` gdy `!AnotherAppIsActive(app)`), dopiero potem dotychczasowy guard `Starting` i kompensacja. Doc-i `:99-103`, `:112-118`, `:205-212` opisują adopcję zamiast „story 7 dokłada osobną gałąź”.
- `src/MediaStation.App/Hosts/MissingHostPort.cs:9-13` — summary nazywa `Probe` trzecim no-opem; `Probe` na końcu klasy nic nie publikuje i nie loguje.
- `src/MediaStation.App/Orchestrator/CommandRouter.cs:59-72` — `case ProbeExisting probe:` → `TryGetHost(...).Probe(probe)`, ta sama mapa `appId → AppType`.
- `tests/MediaStation.Core.Tests/OrchestratorTests.cs` — `TwoApps`/`Create`/`Started` bez zmian; `First_started_...` (`:50`) i `Next_started_...` (`:81`) asercje batcha startu; `Attached_window_without_start_is_ignored_and_keeps_not_running` (`:261`) wymienia kontrakt i wymaga zamiany na adopcję. Wszystkie literały `cmdId` po `Started()` rosną o 2 (liczba aplikacji) i o kolejne 2 za każdy następny `OrchestratorStarted`; przenumerowanie jest mechaniczne, łącznie z `CommandCompleted`/`CommandFailed`, żeby korelacje nadal wskazywały realny `cmdId` startu.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs:44-75` — kolejność `dispatch:SetInputRouting`, `dispatch:ProbeExisting` ×2, `publish`; `:78-93` — `StartApp(4)`/`ScheduleTimer(5)` i próg `dispatched.Count >= 5`; `:187-191` — `cmdId` 4; `:467` `SilentHost` dostaje `Probe`; nowy test z fake hostami odpowiadającymi na sondę (config web + moonlight) obok `:359`.
- `tests/MediaStation.Core.Tests/CommandRouterTests.cs:198-207` — `FakeHost` dostaje `Probed` + `Probe`; nowe testy: routing sondy per `AppType` i `warn commandForUnknownApp` dla nieznanego `appId`.
- `tests/MediaStation.Core.Tests/MissingHostPortTests.cs` — nowy test: `Probe` nie publikuje i nie loguje.
- `tests/MediaStation.Core.Tests/CoreMessageContractTests.cs:435` — rejestr (`probeExisting` po `stopApp`); `:454` — lista schematów (`probe-existing.schema.json` między `orchestrator-started` a `process-exited`); `:569` — `InlineData("probe-existing.schema.json", "ProbeExisting", "cmdId,appId")`; nowa koperta `{"v":1,"type":"probeExisting","payload":{"cmdId":3,"appId":"netflix"}}`.
- `_bmad-output/initiative-mediastation-mvp/deferred-work.md:23-29` — DW-4 zostaje `open` (adoptujemy tylko `NotRunning`, `Failed` nadal ignoruje spóźniony attach); `location:` wskazuje linię, która się przesunie — sweep weryfikuje po symbolu, nie po numerze.
- Reuse: `AnotherAppIsActive`, `NextCmdId`, `Snapshot`, `TryGetHost`, `RecordingLog`, `FakeTimeProvider`, `TimerScheduler`, `LogWindowManagerPort`. Nie ruszać `ShellBridge`, `web/**`, `config/**`, `TimerScheduler`, `LatestSnapshotSlot`; `web/contracts/{schema,generated}` są gitignored i generują się w `BuildWeb` z `MessageTypes`.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Contracts/AppCommands.cs` — rekord `ProbeExisting` po `StopApp` — komenda sondowania hosta (AD-4, AD-10).
- [x] `src/MediaStation.Core/Contracts/MessageTypes.cs` — `typeof(ProbeExisting)` po `typeof(StopApp)` — koperta, JSON Schema i typ TS dla sondy.
- [x] `src/MediaStation.Core/Contracts/Messages.cs` — doc bazy `Command` (sonda jako wyjątek fire-and-forget) i doc `AppWindowAttached` (start vs adopcja) — kontrakt czytelny dla autorów hostów.
- [x] `src/MediaStation.Core/Ports/Ports.cs` — `IAppHostPort.Probe` — seam hosta na adopcję (AD-10, AD-15).
- [x] `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — sondy w `OnOrchestratorStarted` i gałąź adopcji w `OnAppWindowAttached` — żywe okno ląduje w `Background`, shell zostaje na wierzchu, tryb i fokus bez zmian.
- [x] `src/MediaStation.App/Hosts/MissingHostPort.cs` i `src/MediaStation.App/Orchestrator/CommandRouter.cs` — cichy `Probe` i routing po `AppType` — adapter „brak hosta” nic nie raportuje, epiki hostów podmieniają jedno ogniwo.
- [x] `tests/MediaStation.Core.Tests/*` — batch startu, każdy wiersz macierzy, pętla z fake hostami i kontrakty — dowód na `FakeTimeProvider`; mechaniczne przenumerowanie `cmdId` w istniejących testach.

**Acceptance Criteria:**
- Given `netflix` (web) i `plex` (native), when `OrchestratorStarted`, then komendy to `SetInputRouting{shell, null, blocked, epoch 1}`, `ProbeExisting{"netflix"}`, `ProbeExisting{"plex"}` w tej kolejności, a snapshot ma `rev: 1` i `focusRequest{default, 1}`.
- Given `Started` i `AppWindowAttached{"netflix", hwnd, 4321}` bez `StartApp`, when `Handle`, then komendy to `MinimizeWindow{hwnd}` i `ShowShell`, snapshot ma `rev`+1, `Background`, ten sam `focusRequest`/`seq`, brak `ActivateWindow` i `SetInputRouting`.
- Given `Started` i attach Moonlighta bez `StartApp`, when `Handle`, then dostaje `MinimizeWindow` + `ShowShell` i `Background`, a po attachu drugiej aplikacji oba batche mają `MinimizeWindow` + `ShowShell`, obie aplikacje są w `Background`, a `focusRequest` zostaje `{default, 1}`.
- Given aplikacja w `Background` po adopcji, when `LaunchApp`, then `ActivateWindow{hwnd}` + `SetInputRouting{app}` z większym `epoch` i snapshot `Foreground`.
- Given attach w `NotRunning`, gdy inna aplikacja jest w `Foreground` albo `Starting`, when `Handle`, then jedyną komendą jest `MinimizeWindow{hwnd}`, stan to `Background`, bez `ShowShell` i routingu, a druga aplikacja zachowuje stan.
- Given `ProbeExisting` w `MissingHostPort`, when `Probe`, then kolejka i log są puste, a kafelek zostaje `NotRunning`.
- Given `CommandRouter` z hostami per `AppType`, when `ProbeExisting("plex")`, then trafia do hosta `Native`; nieznany `appId` daje `warn commandForUnknownApp` bez wyjątku.
- Given `ProbeExisting{3, "netflix"}`, when `ContractJson.SerializeEnvelope<Command>`, then dokładnie `{"v":1,"type":"probeExisting","payload":{"cmdId":3,"appId":"netflix"}}` i round-trip; rejestr ma `probeExisting`, a eksport schematów `probe-existing.schema.json`.
- Given pętla z fake hostami publikującymi `AppWindowAttached` na `ProbeExisting`, when `loop.Start()`, then oba kafelki są w `Background`, dispatcher dostaje `MinimizeWindow` per `hwnd` i `ShowShell`, żaden `SetInputRouting` nie leci po starcie, `focusRequest.Seq` zostaje 1, a `LaunchApp` daje `ActivateWindow`.

## Design Notes

Adopcję rozpoznaje sam stan `NotRunning`, bez mapy korelacji sond: `StartApp` ustawia `Starting` synchronicznie w tym samym handlerze, więc w `NotRunning` nie ma startu w locie ani terminu `Starting`, a ticket mówi wprost „`AppWindowAttached` bez wcześniejszego `StartApp`”. Sondy są fire-and-forget i nie mają `cmdId` do skorelowania (wzór: `ScheduleTimer`), bo brak żywej aplikacji nie jest porażką komendy.

`ShowShell` jest warunkowany `!AnotherAppIsActive(app)` liczonym już po ustawieniu adopcji na `Background`: odpowiedź hosta może dotrzeć po wyborze kafelka, a shell nad żywą aplikacją łamałby niezmiennik jednej aplikacji na pierwszym planie (AD-15). Gdy nic nie jest aktywne, kolejność `MinimizeWindow` → `ShowShell` jest ta sama co w Home.

`Failed` zostaje poza adopcją: to stan po timeoucie/awarii, w którym `Hwnd` już zniknęło, a decyzja o ewentualnej kompensacji spóźnionego okna należy do DW-4. `MissingHostPort.Probe` milczy zamiast odpowiadać `CommandFailed`, bo brak hosta to brak czegokolwiek do adopcji, a `warn` per aplikacja przy każdym starcie byłby stałym szumem.

## Verification

**Commands:**
- `dotnet test MediaStation.slnx -c Release` -- zielone, w tym batch startu z sondami, adopcja, pętla z fake hostami i `ProbeExisting`.
- `dotnet build MediaStation.slnx -c Release` -- 0 ostrzeżeń; `BuildWeb` eksportuje `probe-existing.schema.json` i `probe-existing.ts` (gitignored).
- `dotnet build src/MediaStation.App -c Debug` -- 0 ostrzeżeń.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + uruchomienie z ważnym configiem -- proces żyje, `snapshotPublished {rev: 1, focusRequest {default, 1}}`, brak `warn`/`error` (sondy idą do `MissingHostPort`), `stop {exitCode: 0}`.

**Implementation-time evidence (2026-10-22, baseline `a3afae87` + working tree, ALDER / B760 Pro RS/D4 / Windows 11 Home):**
- `dotnet test MediaStation.slnx -c Release` -- 492/492 passed, 0 failed, 0 skipped. Nowe/rozszerzone: batch startu `SetInputRouting{shell}`, `ProbeExisting{"netflix"}`, `ProbeExisting{"plex"}` w kolejności `apps.json` oraz drugi start z `cmdId` 4/5/6 i `epoch` 2; adopcja `MinimizeWindow` + `ShowShell`, `Background`, zapisany `hwnd`, ten sam `focusRequest{default, 1}`; powrót `ActivateWindow` + `SetInputRouting{app, epoch 2}`; druga adopcja z dwoma batchami `MinimizeWindow` + `ShowShell`; adopcja Moonlighta bez routingu aplikacji; adopcja przy `Foreground` i `Starting` (tylko `MinimizeWindow`, druga aplikacja bez zmian); attach w `Foreground`/`Background`/`Stopping` i w `Failed` ignorowany; nieznana aplikacja (także `AppWindowAttached`) bez komend i snapshotu; `ProbeExisting` w `MissingHostPort` bez Eventu i wpisu logu, a pętla z `MissingHostPort` zostawia oba kafelki `NotRunning` i nie ma żadnego `warn` przed startem; router po `AppType` i `warn commandForUnknownApp`; koperta `{"v":1,"type":"probeExisting","payload":{"cmdId":3,"appId":"netflix"}}` z round-tripem, rejestr i schemat `probe-existing.schema.json`; pętla z `ProbingHost` (web + moonlight): oba kafelki `Background`, `MinimizeWindow` per `hwnd`, dwa `ShowShell`, jeden `SetInputRouting{shell}`, brak `ActivateWindow`/`ScheduleTimer`, `focusRequest.Seq` 1, `LaunchApp` daje `ActivateWindow`, zero `warn`/`error` w logu.
- `dotnet test --filter OrchestratorLoopTests` powtórzony 3x -- 12/12 passed za każdym razem (nowy test pętli stabilny).
- `dotnet build MediaStation.slnx -c Release` -- 0 ostrzeżeń, 0 błędów; `web/contracts/schema/probe-existing.schema.json` i `web/contracts/generated/probe-existing.ts` (`cmdId: number; appId: string`) wyeksportowane przez `BuildWeb`.
- `dotnet build src/MediaStation.App -c Debug` -- 0 ostrzeżeń, 0 błędów (z `npm run contracts`, `npm run typecheck` i Vite build).
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + uruchomienie z configiem `%LOCALAPPDATA%\MediaStation\config` (netflix, moonlight-pc, plex): `MediaStation.exe` (pid 43212) żył po 10 s, `CloseMainWindow` dał `stop {exitCode: 0}`; log `mediastation-2026-10-22.jsonl` ma `start`, `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}`, `powerSignalsRegistered`, `snapshotPublished {rev: 1, focusRequest: {default, 1}}` i zero `warn`/`error` -- sondy `ProbeExisting` (cmdId 2-4) przeszły przez `MissingHostPort` bez śladu w logu, zgodnie z AD-14.
- Hostów nie ma w repozytorium, więc adopcję żywego okna potwierdzają wyłącznie testy Core i pętli na fake hostach; całościowy odbiór epiku na UM760 zostaje u użytkownika.
- Kontrolny przebieg po poprawkach przeglądu (2026-10-22, ten sam HEAD i working tree): `dotnet test MediaStation.slnx -c Release` -- 493/493 passed, 0 skipped; `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` -- 0 ostrzeżeń, 0 błędów; publish self-contained + uruchomienie (pid 38640, config `netflix`/`moonlight-pc`/`plex`) -- proces żył po 10 s, `CloseMainWindow` dał `stop {exitCode: 0}`, log: `start`, `inputRouting {cmdId: 1, shell, blocked, epoch: 1}`, `powerSignalsRegistered`, `snapshotPublished {rev: 1, focusRequest {default, 1}}`, zero `warn`/`error`.

## Implementation Notes

- 2026-10-22 — implementacja: `ProbeExisting` jako `Command` po `StopApp` (bez korelacji), `IAppHostPort.Probe`, rejestr typów. `OnOrchestratorStarted` buduje listę `[SetInputRouting{shell}, ProbeExisting × apps.json]` i przekazuje ją do `OnDefaultFocus`; `OnAppWindowAttached` po `TryGetValue` rozgałęzia `NotRunning` na nowy `OnAdopt`, a dopiero potem sprawdza `Starting`.
- `OnAdopt` ustawia `Hwnd`, przechodzi w `Background` i zwraca `MinimizeWindow`, a `ShowShell` tylko gdy `!AnotherAppIsActive(app)` — liczone już po ustawieniu `Background`, więc adopcja nie liczy się jako aktywna aplikacja.
- Adopcja nie dotyka `transitionId`, `_focusSeq`, `_epoch` ani timerów; nie tworzy też korelacji startu, bo sonda nie ma `cmdId` do skorelowania (wzór: `ScheduleTimer`).
- `MissingHostPort.Probe` milczy (bez `CommandFailed`, bez logu) — brak hosta to brak aplikacji do adopcji, a `warn` per aplikacja przy każdym starcie byłby stałym szumem. Wiersz „brak hosta” macierzy domyka też pętla z `MissingHostPort`: po starcie oba kafelki są `NotRunning`, a log nie ma `warn`.
- `OrchestratorTests`: wszystkie literały `cmdId` po `Started()` przesunięte o 2 (dwie aplikacje w `TwoApps`), mechanicznie też `CommandCompleted`/`CommandFailed`, żeby korelacje wskazywały realny `cmdId` startu; `TwoApps`/`Create`/`Started` bez zmian, nowe helpery `WebAndMoonlight`/`StartedWith` tylko dla adopcji Moonlighta.
- Poprawki z przeglądu (2026-10-22): summary `IAppHostPort` i doc `AppWindowAttached` mówią teraz, że `ProbeExisting` jest wyjątkiem od wyniku `CommandCompleted`/`CommandFailed`, a `ShowShell` przy adopcji leci tylko wtedy, gdy żadna inna aplikacja nie jest aktywna. Nowy test `Adoption_while_another_app_is_starting_leaves_its_start_decision_current` przypina niezmiennik „adopcja nie podbija `transitionId`”.
- Korekta uzasadnienia z Design Notes: `AnotherAppIsActive` pomija adoptowaną aplikację przez `ReferenceEquals`, więc kolejność ustawienia `Background` przed liczeniem warunku nie ma znaczenia — `ShowShell` zależy wyłącznie od stanu innych aplikacji.

## Plan Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 5 findings — high 0, medium 0, low 4, false 1, maybe-false 0
- findings:
  - `[false]` `[reject]` Sonda odpowiadająca `AppWindowAttached` w trakcie `Starting` jest traktowana jak okno startu i dostaje `ActivateWindow` i routing — sprawdzone: wybór kafelka jest nowszą decyzją użytkownika, więc aktywacja znalezionego okna jest zamierzonym skutkiem, a drugie „prawdziwe” okno startu nie może powstać obok żywego, bo host przy żywej aplikacji kończy `StartApp` jako `PortInUse`/`ProfileInUse` (AD-13); `CommandFailed` na `StartApp` prowadzący w `Failed` i shell to zachowanie story 4 (`OnCommandFailed`) i ten diff go nie zmienia.
  - `[low]` `[patch]` Brak testu na „adopcja nie podbija `transitionId`”, więc dodanie `NextTransitionId()` w `OnAdopt` przeszłoby na zielono — dodany test `Adoption_while_another_app_is_starting_leaves_its_start_decision_current`: adopcja w trakcie `Starting` innej aplikacji, a jej późniejsze okno dostaje `ActivateWindow(7)`, routing i `Foreground`.
  - `[low]` `[patch]` Summary `IAppHostPort` obiecywał `CommandCompleted`/`CommandFailed` dla każdej komendy, sprzecznie z milczącą sondą — summary nazywa teraz `ProbeExisting` wyjątkiem (cisza jest poprawną odpowiedzią, brak korelacji wyniku).
  - `[low]` `[patch]` Doc `AppWindowAttached` obiecywał `ShowShell` bezwarunkowo — doc mówi teraz, że `ShowShell` leci tylko wtedy, gdy żadna inna aplikacja nie jest aktywna.
  - `[low]` `[reject]` Uzasadnienie w Design Notes i arytmetyka `cmdId`/`location:` w Code Map nie pasują do drzewa — poprawka wymaga edycji planu, więc odpada z definicji; sam mechanizm jest bez wpływu na zachowanie (`AnotherAppIsActive` pomija aplikację przez `ReferenceEquals`), a korektę zapisano w Implementation Notes.

## Auto Run Result

**Status:** built.

**Summary:** `OrchestratorStarted` dokłada do batcha startu `ProbeExisting{cmdId, appId}` dla każdej pozycji `apps.json` po routingu `shell`, a `AppWindowAttached` w `NotRunning` jest adopcją: `hwnd` trafia do stanu, aplikacja ląduje w `Background` z `MinimizeWindow`, a `ShowShell` (tylko gdy nic innego nie jest aktywne) trzyma shell na wierzchu. Adopcja nie jest decyzją o pierwszym planie, więc nie podbija `transitionId`/`seq`/`epoch` i nie rusza timerów; `LaunchApp` wraca przez `ActivateWindow` i routing aplikacji. `MissingHostPort.Probe` milczy, a `CommandRouter` kieruje sondy po `AppType`.

**Files changed:**
- `src/MediaStation.Core/Contracts/AppCommands.cs` — `ProbeExisting` po `StopApp` z doc-em o ciszy jako poprawnej odpowiedzi.
- `src/MediaStation.Core/Contracts/MessageTypes.cs` — `typeof(ProbeExisting)` po `typeof(StopApp)`.
- `src/MediaStation.Core/Contracts/Messages.cs` — doc bazy `Command` (wyjątek fire-and-forget) i doc `AppWindowAttached` (start → `Foreground`, adopcja → `Background`, `ShowShell` tylko przy wolnym pierwszym planie).
- `src/MediaStation.Core/Ports/Ports.cs` — `IAppHostPort.Probe`; summary portu nazywa sondę wyjątkiem od `CommandCompleted`/`CommandFailed`.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — sondy w `OnOrchestratorStarted`, `OnAdopt`, rozgałęzienie w `OnAppWindowAttached`, doc-i.
- `src/MediaStation.App/Hosts/MissingHostPort.cs` — cichy `Probe`.
- `src/MediaStation.App/Orchestrator/CommandRouter.cs` — `case ProbeExisting` → host po `AppType`.
- `tests/MediaStation.Core.Tests/OrchestratorTests.cs` — batch startu z sondami, adopcja, powrót, druga adopcja, Moonlight, adopcja przy zajętym planie, attach poza `NotRunning`/`Starting`, niezmiennik `transitionId` (nowy test z przeglądu), przenumerowanie `cmdId`.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs` — kolejność dispatch/publish z sondami, przenumerowanie `cmdId`, `Probe` w `SilentHost`, test pętli z `ProbingHost` (web + moonlight).
- `tests/MediaStation.Core.Tests/CommandRouterTests.cs` — routing sondy po `AppType`, `warn commandForUnknownApp`, `Probed` w `FakeHost`.
- `tests/MediaStation.Core.Tests/MissingHostPortTests.cs` — sonda bez Eventu i bez logu.
- `tests/MediaStation.Core.Tests/CoreMessageContractTests.cs` — koperta `probeExisting`, rejestr, lista schematów, `InlineData` dla `probe-existing.schema.json`.

**Review findings (quick):** 5 findings — high 0, medium 0, low 4, false 1, maybe-false 0.
- Patches applied: 3 (low) — test przypinający brak podbicia `transitionId` w adopcji; summary `IAppHostPort` i doc `AppWindowAttached` zgodne z milczącą sondą i warunkowym `ShowShell`.
- Deferred: 0.
- Rejected: 2 — sonda w trakcie `Starting` traktowana jak okno startu (false: aktywacja to zamierzony skutek nowszej decyzji użytkownika, a drugie okno startu wyklucza `PortInUse`/`ProfileInUse` z AD-13); uzasadnienie w Design Notes i `location:` w Code Map (low, poprawka wymaga edycji planu; korektę zapisano w Implementation Notes).

**Patched counts by verdict:** high 0, medium 0, low 3.

**Follow-up review recommendation:** `false`.

**Verification:** `dotnet test MediaStation.slnx -c Release` 493/493 (0 failed, 0 skipped; `OrchestratorLoopTests` 12/12); `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` z 0 ostrzeżeń i 0 błędów, `probe-existing.schema.json`/`probe-existing.ts` wyeksportowane przez `BuildWeb`; publish self-contained + uruchomienie z configiem (pid 38640): proces żył po 10 s, `snapshotPublished {rev: 1, focusRequest {default, 1}}`, `inputRouting {cmdId: 1, shell, blocked, epoch: 1}`, `stop {exitCode: 0}`, zero `warn`/`error` (sondy w `MissingHostPort`).

## Recovery

Recovered manually on 2026-10-22 after `bmad-loop resolve --reverify` refused a finished tickets-mode run: fast-forwarded `master` to the kept branch `bmad-loop/20261021-235612-3018/epic-orchestrator-lifecycle/2.7` (commit `6add563`), re-ran `dotnet test MediaStation.slnx -c Debug` (493/493), and marked the ticket done with `tickets.py mark 2.7 done`. The original run had deferred the ticket after a dev-session timeout even though the plan was already `built`.

**Residual risks:** ścieżka adopcji nie jest osiągalna w opublikowanej aplikacji bez prawdziwego hosta, który na `ProbeExisting` odpowie `AppWindowAttached`, więc jej dowodem są testy Core i pętli na fake hostach; kolejność „odpowiedź sondy po wyborze kafelka” nie ma testu, bo aktywacja znalezionego okna jest zamierzonym skutkiem nowszej decyzji użytkownika; spóźniony attach po `Failed` nadal jest ignorowany (DW-4 zostaje `open`, adres wskazuje symbol w `OnAppWindowAttached`); całościowy test UM760 całego epiku wykonuje użytkownik przy `epic-boundary`.

