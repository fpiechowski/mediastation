---
title: 'Routing z lokalnej kopii SetInputRouting i Actions do Orchestratora'
type: 'feature'
ticket: '5'
created: '2026-10-22'
status: done
baseline_revision: '7944608580ca0427cc9627f5b8b168102a3d6b35'
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
deferred:
  - summary: >-
      Checklista odbioru epiku („Jedno naciśnięcie daje jedną Action”, „każde naciśnięcie … daje w logu dokładnie jeden wpis `action`”) nie mówi, że D-pad i OK w `app` + `moonlight`/`native` są celowo przepuszczane do aplikacji bez żadnej Action.
    evidence: |-
      Zweryfikowane: `architecture-mediastation.md:202` (wiersz tabeli AD-8) każe przepuszczać D-pad i OK w `app` + `moonlight`/`native`, a `epic-input.md:23` (Done when 1) i `:70` (checklista po story 2) nadal obiecują jedną Action i dokładnie jeden wpis `action` na naciśnięcie. Nowy test `App_with_moonlight_or_native_passes_dpad_and_ok_without_actions_or_grid` pinuje zero Actions. Ten sam typ luki (checklista nie opisuje `repeat`) jest już zapisany jako DW-7 z uzasadnieniem „żeby operator nie odczytał checklisty jako porażki”; poprawka edytuje dokumentację architektury i notatkę odbioru epiki, więc należy do passa dokumentacyjnego epiki, nie do tej story.
    location: >-
      _bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input.md:23,70
    severity: low
---

<intent-contract>

## Intent

**Problem:** `InputPolicy` dostaje tryb wejścia raz, na stałe (`InputMode.Shell` z composition rootu), a `SetInputRouting` z Orchestratora ląduje wyłącznie w logu jako `inputRouting`. Skutek: D-pad i OK nigdy nie są przepuszczane w `app` + `moonlight`/`native`, `long` OK poza `shell` nie istnieje, a Actions z wątku Input nie docierają do Orchestratora, więc Home działa tylko przez przypadek (brak drogi do pętli).

**Approach:** `InputRoutingState` w Core trzyma lokalną kopię `{mode, appType, pointer, epoch}` i przyjmuje wyłącznie `epoch` ściśle większy od bieżącego; `InputPolicy` czyta kopię przy key-down i decyduje według tabeli AD-8 (połknij + Action albo przepuść), a decyzję zapisuje w trzymanym klawiszu, więc key-up i autorepeat idą za nią także po zmianie trybu. Adapter `InputHubRoutingPort` zastępuje `LogInputRoutingPort` (zapis kopii + wpis `inputRouting`), a `OrchestratorActionSink` loguje `action` i wkłada Action do kolejki Orchestratora bez czekania.

## Boundaries & Constraints

**Always:** decyzja o połknięciu i `kind` po samym VK ze scalonego mapowania; Home połknięty w każdym wierszu tabeli; `long` tylko dla OK w `shell` i `osk`; `repeat` tylko strzałki (story 4 bez zmian); przepuszczenie wyłącznie D-pad i OK w `app` + `moonlight`/`native`; decyzja z key-down rządzi key-upem i autorepeatem; decyzja w Core, App tylko uzbraja timer; czas wyłącznie przez `TimeProvider` (AD-14); wpis `action` zostaje na miejscu; kopia lokalna, zero czekania na Orchestrator (AD-8); `epoch` ściśle większy.

**Never:** pointer/żyroskop i `ClipCursor` (story 8), gamepad (story 6), gałęzie Orchestratora dla `action`/`back`/OSK (epic-home-screen, epic-netflix-dpad, epic-osk), zmiany `ContractJson`, `MessageTypes`, schematów i `web/**`, zmiany w `InputHub`, w `RawInput*` i w Win32, zapis `input.json`, czekanie na Orchestratorze.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| `shell` | key-down D-pad/OK/Back/Home/Keyboard | połknięte i jedna Action (`press`; OK `press` na key-up przed progiem) | brak błędu |
| `osk` | key-down OK + upływ progu | połknięte, samo `long`, zero `press` | brak błędu |
| `app` + `web` | key-down OK | `press` od razu, brak terminu long | brak błędu |
| `app` + `moonlight`/`native` | key-down D-pad albo OK | `Swallow=false`, zero Actions, `NextDeadline` bez siatki | brak błędu |
| `app` + `moonlight` | Back, Home, Keyboard | połknięte z Action | brak błędu |
| każdy wiersz | Home | połknięty, Action `home` | brak błędu |
| epoch równy albo starszy | `SetInputRouting{Epoch <= bieżący}` | kopia bez zmian, decyzja wg starego wiersza, `warn inputRoutingStale` | wpis `warn`, brak wyjątku |
| zmiana trybu w trakcie trzymania | strzałka z key-down w `shell`, potem `app` + `moonlight` | autorepeat i key-up połknięte, zero nowych Actions; siatka repeat z key-down działa dalej | brak błędu |
| lustrzany przypadek | strzałka z key-down w `app` + `moonlight`, potem `shell` | autorepeat i key-up przepuszczone bez Action | brak błędu |
| klawisz spoza mapowania | dowolny tryb | przepuszczony, zero Actions | brak błędu |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Input/InputRoutingState.cs` — nowy: record `InputRouting(Mode, AppType, Pointer, Epoch)` i stan początkowy `shell/null/blocked/0`; `Apply(SetInputRouting)` zwraca `false` dla `Epoch <=` bieżącego (bez zmiany czegokolwiek), `Current` czyta pod `lock` (wzorzec `Core/Orchestrator/LatestSnapshotSlot.cs`). Wątek Orchestratora pisze, wątek Input czyta.
- `src/MediaStation.Core/Input/InputPolicy.cs` — ctor `(config, time, InputRoutingState? routing = null)` (`null` → świeży stan = `shell`, więc stare testy i `InputClockTests`/`ActionPumpTests` kompilują się); `OnKey` czyta `routing.Current` raz na zdarzenie (134-182); `ShouldSwallow(kind, routing)`: Home zawsze `true`, D-pad/OK `false` wyłącznie w `App` + `Moonlight|Native`, poza tym `true` (także `App` bez `appType` i `Osk`); `RequiresLongPress` (238) = `Ok` i `Shell|Osk`; `HeldKey` (404-410) + `bool Swallow`, siatka repeat (`NewHeldKey` 241-256) i `PendingLong` tylko dla połkniętych; key-up/autorepeat zwraca `held.Swallow`, a key-up bez wpisu `ShouldSwallow` z bieżącej kopii.
- `src/MediaStation.Core/Input/OrchestratorActionSink.cs` — nowy: `IActionSink` + `IInputHubErrorSink`; `Deliver` = `LogActionSink.Deliver` (wpis `action {kind, phase, source}` bez zmian) + `post(action)` (`Action<IOrchestratorMessage>`), bez czekania i bez catch (wyjątek `post` łapie `ActionPump` i melduje `inputHubError {operation: actionSink}`); `Report` deleguje do `LogActionSink`.
- `src/MediaStation.App/Platform/Input/InputHubRoutingPort.cs` — nowy, zastępuje `LogInputRoutingPort.cs`: `IInputRoutingPort`, `Src = "input"`; wpis `inputRouting {cmdId, mode, appType?, pointer, epoch}` w tej samej kolejności co dziś, potem `routing.Apply`; odrzucony `epoch` → `warn inputRoutingStale {cmdId, epoch}`; ctory `(InputRoutingState, ILog)` i `(ILog)` (własny stan, dla testów pętli).
- `src/MediaStation.App/CompositionRoot.cs` — jeden `InputRoutingState` przed routerem (203-209: `new InputHubRoutingPort(routing, log)`), sink `new OrchestratorActionSink(log, loop.Post)` (281-282), `new InputPolicy(config.Input, time, routing)` (285), komentarz 280.
- `src/MediaStation.Core/Input/LogActionSink.cs` — zostaje bez zmian jako warstwa logu dla nowego sinka (nazwa, `Src` i wpisy niezmienione).
- `tests/MediaStation.Core.Tests/InputPolicyTests.cs` — helper `Create` (70-78) buduje `InputRoutingState` i wiersz `Route(...)`; wiersze tabeli AD-8, epoch, key-up/autorepeat po zmianie trybu, OK w `app` + `web`, `long` w `osk`.
- `tests/MediaStation.Core.Tests/InputRoutingStateTests.cs` — nowy: stan początkowy, przyjęcie większego `epoch` (wszystkie pola), odrzucenie równego i starszego.
- `tests/MediaStation.Core.Tests/OrchestratorActionSinkTests.cs` — nowy: wpis `action` + dokładnie jedno przekazanie, kolejność, `Report` bez przekazania.
- `tests/MediaStation.Core.Tests/LogInputRoutingPortTests.cs` → `InputHubRoutingPortTests.cs` — dwa istniejące testy wpisu bez zmian + kopia lokalna i odrzucony `epoch`.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs` — 176, 229, 271, 325, 397, 467: `new InputHubRoutingPort(log)`; plus jeden test Action → pętla → `SetInputRouting{shell}`.
- Reuse: `FakeTimeProvider`, `RecordingLog`, `LogActionSink`, `InputTimerDelay`, `MissingHostPort`, `LogWindowManagerPort`. Nie ruszać: `InputHub`, `InputCalibration`, `ActionQueue`, `ActionPump`, `ContractJson`, `MessageTypes`, `web/**`, `RawInput*`.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Input/InputRoutingState.cs` — nowa kopia lokalna z regułą `epoch` — podstawa decyzji bez Orchestratora (AD-8).
- [x] `src/MediaStation.Core/Input/InputPolicy.cs` — `ShouldSwallow`/`RequiresLongPress` po kopii, `HeldKey.Swallow`, key-up i autorepeat z decyzji key-down — tabela AD-8.
- [x] `src/MediaStation.Core/Input/OrchestratorActionSink.cs` — log `action` + wkładanie do kolejki Orchestratora — Actions do pętli (AD-8).
- [x] `src/MediaStation.App/Platform/Input/InputHubRoutingPort.cs` i usunięcie `LogInputRoutingPort.cs` — adapter InputHub zamiast tylko-log — `SetInputRouting` działa.
- [x] `src/MediaStation.App/CompositionRoot.cs` — jeden stan routingu dla portu i polityki, sink Orchestratora — wiring bez `null`.
- [x] `tests/MediaStation.Core.Tests/{InputPolicyTests,InputRoutingStateTests,OrchestratorActionSinkTests,InputHubRoutingPortTests,OrchestratorLoopTests}.cs` — wiersze macierzy, epoch, sink, adapter, pętla — `verify`.

**Acceptance Criteria:**
- Given kopia w `shell`, when trzymana strzałka i przełączenie na `app` + `moonlight`, then key-up i autorepeat są połknięte bez nowych Actions, a siatka repeat z key-down działa dalej; w odwrotną stronę (przepuszczona w `moonlight`, potem `shell`) key-up i autorepeat zostają przepuszczone bez Action.
- Given kopia z `epoch` 3, when `SetInputRouting` z `epoch` 3 albo 2, then decyzja polityki idzie starym wierszem, a log ma `warn inputRoutingStale`; `epoch` 4 zmienia wiersz.
- Given Action dostarczona przez `ActionPump`, when `Deliver`, then log ma jedno `action {kind, phase, source}` i ten sam komunikat trafia do kolejki Orchestratora w tej samej kolejności.
- Given aplikacja `web` w `Foreground` (po `LaunchApp` i `AppWindowAttached`), when Home wchodzi do pętli przez `OrchestratorActionSink`, then dispatch ma `MinimizeWindow`, `ShowShell` i `SetInputRouting{shell}`, a kopia lokalna jest w `shell`.

## Implementation Notes

**Stan:** zaimplementowane; poniżej decyzje, pliki i dowody z tego przebiegu (ALDER, Windows 11 Home).

**Decyzje:**
- `InputRoutingState` (Core/Input) trzyma `record InputRouting(Mode, AppType, Pointer, Epoch)` i stan początkowy `shell/null/blocked/0`; `Apply` przyjmuje wyłącznie `Epoch` ściśle większy i zwraca `bool` (odrzucenie bez zmiany kopii), a `Current` czyta pod `lock` wzorem `LatestSnapshotSlot`. Jeden `lock` obejmuje tylko przypisanie referencji, więc wątek Input nie czeka na Orchestrator.
- `InputPolicy` dostał ctor `(config, time, InputRoutingState? routing = null)`: `null` tworzy świeży stan `shell`, więc `InputClockTests`, `ActionPumpTests` i `LogActionSinkTests` działają bez zmian. Stary parametr `InputMode mode` usunięty; testy budują wiersz przez `Create(mode, appType)` + `Route(epoch, mode, appType)`.
- Decyzja key-down (`ShouldSwallow`) ląduje w `HeldKey.Swallow`; pierwszy key-down zawsze dopisuje wpis (także przepuszczony), więc autorepeat i key-up idą za decyzją key-down. Key-up bez wpisu rozstrzyga bieżący wiersz kopii (hook startujący w trakcie trzymania).
- Siatka repeat i `PendingLong` uzbrajają się tylko dla połkniętych klawiszy; przepuszczony klawisz nie ma terminu i nie produkuje `repeat`, a mimo to jego key-up/autorepeat zostają przepuszczone.
- `ShouldSwallow`: Home zawsze `true`; D-pad/OK `false` wyłącznie w `App` + `Moonlight|Native`; `App` bez `appType`, `Osk` i `Shell` połykają. Back/Keyboard/AppMenu połknięte w każdym wierszu (decyzję o OSK podejmuje Orchestrator).
- `RequiresLongPress`: tylko OK w `Shell|Osk`; w `App` OK dostaje `press` na key-down bez terminu long.
- `OrchestratorActionSink` loguje przez `LogActionSink` i woła `post(action)` bez `catch` i bez czekania; `Report` tylko loguje. Wyjątek `post` zostaje złapany przez `ActionPump` jako `inputHubError {operation: actionSink}`.
- `InputHubRoutingPort` najpierw loguje `inputRouting` (ta sama kolejność kluczy), potem `Apply`; odrzucony `epoch` daje dodatkowy `warn inputRoutingStale {cmdId, epoch}`. Dwa ctory: współdzielony stan i własny (testy pętli).
- `CompositionRoot`: jeden `InputRoutingState` przed routerem, `InputHubRoutingPort(routing, log)`, `OrchestratorActionSink(log, loop.Post)` i `InputPolicy(config.Input, time, routing)`.
- `LogActionSink` został bez zmian funkcjonalnych; poprawiono tylko odwołanie w komentarzu `LogInputRoutingPort.Src` → `InputHubRoutingPort.Src`, bo stara klasa zniknęła.

**Pliki:**
- Nowe: `src/MediaStation.Core/Input/InputRoutingState.cs`, `src/MediaStation.Core/Input/OrchestratorActionSink.cs`, `src/MediaStation.App/Platform/Input/InputHubRoutingPort.cs`, `tests/MediaStation.Core.Tests/{InputRoutingStateTests,OrchestratorActionSinkTests,InputHubRoutingPortTests}.cs`.
- Zmienione: `src/MediaStation.Core/Input/InputPolicy.cs`, `src/MediaStation.App/CompositionRoot.cs`, `src/MediaStation.Core/Input/LogActionSink.cs` (komentarz), `tests/MediaStation.Core.Tests/{InputPolicyTests,OrchestratorLoopTests}.cs`, `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` (link `Compile Include`).
- Usunięte: `src/MediaStation.App/Platform/Input/LogInputRoutingPort.cs`, `tests/MediaStation.Core.Tests/LogInputRoutingPortTests.cs` (zastąpione przez `InputHubRoutingPort*`).

**Zaskoczenia / pułapki:**
- Odrzucony `epoch` zostawia dwa wpisy, nie jeden: `inputRouting` (nowa komenda jest logowana zawsze) i po nim `inputRoutingStale`; test adaptera pinuje tę kolejność.
- Przepuszczony klawisz nadal potrzebuje wpisu w `_held`, inaczej key-up po zmianie wiersza na `shell` zostałby nagle połknięty — lustrzany wiersz macierzy wymusił trzymanie decyzji w `HeldKey`.
- `Create` w `InputPolicyTests` zwraca teraz trójkę `(Policy, Time, Routing)`; wszystkie dotychczasowe rozpakowania uzupełnione o trzeci element.

**Dowody (ten przebieg, ALDER, Windows 11 Home):**
- `dotnet build MediaStation.slnx -c Release` — Build succeeded, 0 ostrzeżeń, 0 błędów.
- `dotnet test MediaStation.slnx -c Release` — 694/694 passed, 0 failed, 0 skipped (baseline 655; +39 przypadków: macierz routingu, `epoch`, sink, adapter, pętla). Każdy wiersz macierzy I/O ma przechodzący test: `shell` (istniejące testy polityki), `osk` (`Osk_ok_*`), `app` + `web` (`Ok_in_app_gives_press_on_key_down_and_arms_no_timer`), `app` + `moonlight`/`native` (`App_with_moonlight_or_native_*`), Home w każdym wierszu (`Home_is_swallowed_with_action_in_every_row`), `epoch` (`InputRoutingStateTests`, `InputHubRoutingPortTests`, `Rejected_stale_epoch_keeps_the_old_routing_row_for_the_policy`), zmiana trybu w trakcie trzymania (`Course_change_during_hold_*`, `Passed_key_stays_passed_after_the_row_changes_to_shell`), klawisz spoza mapowania (`Unmapped_key_passes_through_without_actions_in_every_row`).
- `dotnet run --project tools/MediaStation.SchemaExport -c Release` — schematy wyeksportowane, brak zmian w `web/contracts` (git status czysty dla `web/**`).
- `cd web && npm run build` — contracts + typecheck + Vite, zielone.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z `tools/samples/config` (pid 47908, proces żył po 10 s, `CloseMainWindow` dał kod 0): log `mediastation-2026-10-22.jsonl` ma dokładnie pięć nowych wpisów `start` → `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}` → `powerSignalsRegistered` → `snapshotPublished {rev: 1, focusRequest: {default, 1}}` → `stop {exitCode: 0}`; zero `configError`/`warn`/`error`. Katalog publish i kopia zapasowa usunięte.
- Config użytkownika `%LOCALAPPDATA%\MediaStation\config` podmieniony na sample, a po przebiegu odtworzony bajt w bajt: SHA-256 przed i po identyczne (`apps.json 325B57C9…`, `input.json 4F832612…`, `settings.json B5DDF4BE…`).
- Sprzętowo N/A: fizyczny D-pad w Moonlightcie i OK w `osk` na UM760 — odbiór epiku (na ALDER wstrzyknięte wejście nie dociera do hooka LL, ustalenie story 2); zachowanie pokrywają testy na `FakeTimeProvider` i stanie routingu.

**Niezależna weryfikacja w sesji głównej (ten sam build Release):** `dotnet build` — 0 ostrzeżeń, 0 błędów; `dotnet test` — 709/709 passed, 0 failed, 0 skipped; `SchemaExport` bez zmian w `web/contracts`, `cd web && npm run build` zielone; `dotnet publish` + `MediaStation.exe` z `tools/samples/config` — kod 0, log `start` → `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}` → `powerSignalsRegistered` → `snapshotPublished` → `stop {exitCode: 0}`; config użytkownika odtworzony (SHA-256 trzech plików zgodne, `config/invalid` odtworzony jako pusty katalog). Dopisany test `Swallowing_row_gives_exactly_one_press_action_per_key_press` domyka lukę audytu macierzy: wiersze `shell`, `osk` i `app` + `web` dla D-pad, OK, Back, Home i Keyboard dawały dotąd osobne testy poza Keyboardem w `shell` (15 przypadków, +15 do 709). Zaobserwowane jednorazowo `shell webViewProcessFailed {kind: browserProcessExited, reason: unexpected, exitCode: -1073741819}` w pierwszym przebiegu `MediaStation.exe`; powtórzone 6× ten sam build i 3× build baseline `7944608` — czysto, więc to flake WebView2 (renderer), a nie regresja wejścia.

**Po review (patch):** `HeldKey` przepuszczonego klawisza nie jest już wieczny — przy key-down wpis `Swallow=false` starszy niż `MaxRepeatHoldMs` (`key.TimeMs - DownTimeMs`) jest usuwany i klawisz startuje nowe trzymanie według bieżącego wiersza, więc zgubiony key-up nie zostawia VK uwięzionego w „przepuść” (test `Passed_key_with_missing_key_up_is_stale_after_max_hold_and_swallows_again`). Doszedł test przez prawdziwą `ActionPump` (`Action_from_the_pump_is_logged_once_and_posted_once`), a `InputRoutingState` jest wymagany w `InputPolicy` i w `InputHubRoutingPort` (usunięty ctor `(ILog)`), żeby wpięcie bez wspólnej kopii nie kompilowało się po cichu; call site'y testów dostały jawne `new InputRoutingState()`.

**Weryfikacja po patchu (ALDER):** `dotnet build` — 0 ostrzeżeń, 0 błędów; `dotnet test` — 711/711 passed, 0 failed, 0 skipped; `dotnet publish` + `MediaStation.exe` z `tools/samples/config` 2×: kod 0, dokładnie pięć wpisów `start` → `inputRouting` → `powerSignalsRegistered` → `snapshotPublished` → `stop {exitCode: 0}`, zero `warn`/`error`; config użytkownika odtworzony bajt w bajt (SHA-256 zgodne), `config/invalid` na miejscu.

## Plan Change Log

## Review Triage Log

### 2026-10-22 — Review pass (quick)
- verdicts: 4 findings — high 0, medium 0, low 4, false 0, maybe-false 0
- findings:
  - `[low]` `[patch]` Zgubiony key-up przepuszczonego klawisza uwięził VK w decyzji „przepuść” na zawsze: wpis w `_held` nie ma terminu, a autoprepeat zwraca `held.Swallow` — potwierdzone: `NewHeldKey` uzbraja siatkę tylko dla połkniętych (`InputPolicy.cs:293-300`), więc przepuszczony wpis nie ma ani `NextRepeatAt`, ani `RepeatEndsAt`, a usuwa go wyłącznie key-up (`:192`). Skutek: w `shell` jedno naciśnięcie tego VK nie daje Action, tylko wycieka do pulpitu, do następnego key-upa. Naprawa: wpis przepuszczony starszy niż `MaxRepeatHoldMs` jest nieświeży przy key-down (usuń i obsłuż klawisz jak świeży; wpisy połknięte bez zmian) + test `Passed_key_with_missing_key_up_is_stale_after_max_hold_and_swallows_again`.
  - `[low]` `[patch]` AC „Given Action dostarczona przez `ActionPump`” nie było pokryte: żaden test nie łączył prawdziwej pompy z `OrchestratorActionSink`, więc zdanie o wyjątku `post` łapanym przez `ActionPump` było wnioskiem z kodu, nie dowodem — potwierdzone: `OrchestratorActionSinkTests` i nowy test pętli wołają `Deliver` wprost, a wszystkie `new ActionPump(` w testach używają `LogActionSink` lub sinka nagrywającego. Naprawa: test `Action_from_the_pump_is_logged_once_and_posted_once` (start pompy, jedna Action w kolejce, dokładnie jeden wpis `action` i jedno przekazanie).
  - `[low]` `[defer]` Checklista odbioru epiku obiecuje jedną Action i dokładnie jeden wpis `action` na naciśnięcie, a D-pad i OK w `app` + `moonlight`/`native` celowo nie dają Actions — potwierdzone: `epic-input.md:23,70` vs `architecture-mediastation.md:202` i nowy test przepuszczania. Poprawka edytuje dokumentację architektury i notatkę odbioru epiki (ten sam precedens co DW-7), więc finding poszedł do `deferred`.
  - `[low]` `[patch]` Stan routingu był opcjonalny w obu typach (`InputPolicy(config, time)` i `new InputHubRoutingPort(log)` tworzyły własną kopię), więc wpięcie bez wspólnej kopii kompilowało się, logowało `inputRouting` i po cichu zostawiało politykę w `shell` — dokładnie defekt tej story — a żaden test by tego nie złapał (brak testu `CompositionRoot`, DW-5). Naprawa: `InputRoutingState` wymagany w `InputPolicy` (`ArgumentNullException.ThrowIfNull`), usunięty ctor `InputHubRoutingPort(ILog)`, call site'y testów przekazują `new InputRoutingState()`.

## Design Notes

Kopia routingu żyje w Core, bo czyta ją polityka na wątku Input, a pisze port na wątku Orchestratora: `lock` obejmuje jedno przypisanie referencji, więc wątek Input nigdy nie czeka na Orchestrator, a `Apply` odrzuca `epoch` równy i starszy w jednym miejscu (dzisiejszy adapter ma tylko log, więc reguła `epoch` nie miała gdzie mieszkać). Jedna kopia obsłuży też `pointer` w story 8.

Decyzję o połknięciu zapisujemy w `HeldKey`, bo tryb może się zmienić w trakcie trzymania klawisza: key-up i autorepeat muszą iść za key-down (AD-8), inaczej klawisz połknięty w `shell` wyciekłby do Moonlighta po starcie aplikacji, a klawisz przepuszczony stałby się nagle Actions. Siatka repeat i `PendingLong` są częścią tej samej decyzji, więc uzbrojone przy key-down działają do key-up niezależnie od późniejszych `SetInputRouting`.

`app` bez `appType` połknięty, bo tabela AD-8 nie ma takiego wiersza, a Orchestrator zawsze wysyła `appType` w trybie `app`; domyślne przepuszczenie oddawałoby klawisze nieznanemu okno bez wiedzy, kto je dostał. Keyboard w każdym wierszu emituje Action, bo o OSK decyduje Orchestrator (`textFocus`, AD-9) — hub nie zna tego stanu.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń, 0 błędów.
- `dotnet test MediaStation.slnx -c Release` — zielone (baseline 655, bez skipped; po tej zmianie 711).
- `dotnet run --project tools/MediaStation.SchemaExport` i `cd web && npm run build` — bez zmian w kontraktach, regresja zielona.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z `tools/samples/config` — `start` → `inputRouting` → `powerSignalsRegistered` → `snapshotPublished` → `stop {exitCode: 0}`, kod 0, zero `configError`/`warn`/`error`; config użytkownika odtworzony po przebiegu.
- Sprzętowo N/A: fizyczny D-pad w Moonlightcie i OK w `osk` na UM760 — odbiór epiku (wstrzyknięte wejście nie dociera do hooka LL, ustalenie story 2).

## Auto Run Result

**Status:** built.

**Summary:** `InputPolicy` czyta teraz lokalną kopię routingu z `InputRoutingState` (Core), do której `InputHubRoutingPort` zapisuje `SetInputRouting` wyłącznie z rosnącym `epoch` — więc hook decyduje według tabeli AD-8 bez czekania na Orchestrator: `shell`, `osk` i `app` + `web` połykają D-pad, OK, Back, Home i Keyboard i emitują Actions, `app` + `moonlight`/`native` przepuszczają D-pad i OK bez Actions, Home jest połknięty zawsze, a `long` należy wyłącznie do OK w `shell` i `osk`. Decyzja key-down jest zapisana w trzymanym klawiszu, więc key-up i autorepeat idą za nią także po zmianie trybu, a przepuszczony wpis starszy niż `MaxRepeatHoldMs` jest nieświeży (zgubiony key-up nie uwięzi VK). Adapter tylko-log routingu z 2.3 zastąpił `InputHubRoutingPort` (wpis `inputRouting` zostaje, odrzucony `epoch` daje `warn inputRoutingStale`), a `OrchestratorActionSink` loguje `action` i wkłada tę samą Action do kolejki Orchestratora bez czekania.

**Files changed:**
- `src/MediaStation.Core/Input/InputRoutingState.cs` — nowa kopia lokalna `{mode, appType, pointer, epoch}` z regułą ściśle rosnącego `epoch` i bezpiecznym odczytem spod `lock`.
- `src/MediaStation.Core/Input/InputPolicy.cs` — decyzja `ShouldSwallow`/`RequiresLongPress` z kopii, `HeldKey.Swallow`, key-up i autorepeat za decyzją key-down, nieświeży wpis przepuszczonego klawisza po `MaxRepeatHoldMs`; `InputRoutingState` wymagany w ctorze.
- `src/MediaStation.Core/Input/OrchestratorActionSink.cs` — wpis `action` + przekazanie komunikatu do kolejki Orchestratora, `Report` tylko do logu.
- `src/MediaStation.App/Platform/Input/InputHubRoutingPort.cs` — adapter InputHub zastępujący `LogInputRoutingPort`: wpis `inputRouting`, zapis kopii, `warn inputRoutingStale` dla równego i starszego `epoch`.
- `src/MediaStation.App/Platform/Input/LogInputRoutingPort.cs` — usunięty (zastąpiony wyżej).
- `src/MediaStation.App/CompositionRoot.cs` — jeden `InputRoutingState` dla portu i polityki, `OrchestratorActionSink(log, loop.Post)` jako sink pompy.
- `src/MediaStation.Core/Input/LogActionSink.cs` — komentarz odwołuje się do nowej nazwy portu; wpisy i zachowanie bez zmian.
- `tests/MediaStation.Core.Tests/InputPolicyTests.cs` — wiersze tabeli AD-8, `epoch`, zmiana trybu w trakcie trzymania (w obie strony), OK w `app` + `web`, `long` w `osk`, nieświeży wpis przepuszczonego klawisza.
- `tests/MediaStation.Core.Tests/InputRoutingStateTests.cs` — stan początkowy, przyjęcie większego `epoch`, odrzucenie równego i starszego.
- `tests/MediaStation.Core.Tests/OrchestratorActionSinkTests.cs` — log + jedno przekazanie, kolejność, `Report` bez przekazania, przebieg przez prawdziwą `ActionPump`.
- `tests/MediaStation.Core.Tests/InputHubRoutingPortTests.cs` (zastąpił `LogInputRoutingPortTests.cs`) — wpis `inputRouting` bez zmian, kopia lokalna, `warn inputRoutingStale`.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs` — port z jawnym stanem; test Home przez sink → `MinimizeWindow`/`ShowShell`/`SetInputRouting{shell}` i kopia w `shell`.
- `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` — link `Compile Include` na nowy adapter.

**Review findings:** 4 findings (high 0, medium 0, low 4). Patches applied (3 `low`): nieświeży wpis przepuszczonego klawisza po zgubionym key-upie, test przebiegu przez `ActionPump`, wymagany `InputRoutingState` w `InputPolicy`/`InputHubRoutingPort`. Deferred (1 `low`): checklista odbioru epiku (`epic-input.md:23,70`) nie opisuje przepuszczania D-pada i OK w `app` + `moonlight`/`native`. Rejected: 0.

**Follow-up review recommended:** false — pierwszy przebieg, patchowane były wyłącznie trzy wpisy `low` (progi: `high` albo dwa `medium`).

**Verification:** `dotnet test MediaStation.slnx -c Release` — 711/711 passed, 0 failed, 0 skipped (baseline 655). `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń, 0 błędów. `dotnet run --project tools/MediaStation.SchemaExport` — brak zmian w `web/contracts`; `cd web && npm run build` zielone. `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z `tools/samples/config` (2 przebiegi po patchu): `start` → `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}` → `powerSignalsRegistered` → `snapshotPublished` → `stop {exitCode: 0}`, kod 0, zero `configError`/`warn`/`error`; config użytkownika odtworzony bajt w bajt (SHA-256 trzech plików zgodne), `config/invalid` na miejscu. Każdy wiersz macierzy ma przechodzący test (m.in. `Swallowing_row_gives_exactly_one_press_action_per_key_press` dla `shell`, `osk` i `app` + `web`).

**Residual risks / checks for UM760 (whole-epic acceptance):** fizyczny D-pad i OK w Moonlightcie przechodzą do aplikacji bez wpisu `action` (to poprawny wiersz AD-8 — checklista `epic-input.md:70` mówi inaczej, patrz `deferred`); Home nad Moonlightem, pulpitem i Edge daje dokładnie jedno `action {kind: home}` i routing wraca do `shell`; OK w `osk` daje `long` po progu, a w `app` + `web` `press` od razu; trzymana strzałka w `shell` daje `press` + `repeat` także po starcie aplikacji. Na ALDER wstrzyknięte wejście nie dociera do hooka LL, więc te punkty są nieosiągalne automatem.

