---
title: 'Pilot steruje tym, co jest na wierzchu, także po Alt+Tab'
type: 'feature'
ticket: '9'
created: '2026-10-27'
status: done
baseline_revision: 'f6b0a955578f0aaa1903b0435093a11ace59a20c'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: ['blind-hunter', 'edge-case-hunter', 'verification-gap', 'intent-alignment']
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md'
warnings: []
deferred:
  - summary: >-
      A window command whose result is lost keeps evaluation blocked until the next transitionId.
    evidence: |-
      Window-command results are tracked in a pending set cleared only by CommandCompleted/CommandFailed or a transitionId change; executors always answer (AD-4), so unverified. Settled by a test or run where the executor drops a result.
    location: >-
      src/MediaStation.Core/Orchestrator/Orchestrator.Foreground.cs (IsHardBlocked)
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** Po Alt+Tab (albo skrypcie) Orchestrator tylko loguje właściciela okna na wierzchu: tryb wejścia, stan kafelków i dźwięk zostają stare, więc pilot steruje niewidocznym oknem, a poprzednia aplikacja gra w tle.

**Approach:** Polityka `ForegroundChanged` z AD-15 w Core: każda obserwacja restartuje timer ustalenia `foregroundSettleMs`; ostatnia ustalona obserwacja niezgodna z zamiarem Orchestratora daje przełączenie z zewnątrz (AD-5) z nowym `transitionId` i wpisem `foregroundSwitch`; oceny pilnuje śledzenie wyników komend okiennych, stanu `Starting` i zbioru okien zarządzanych, z `ReportForeground` po ustaniu przyczyny.

## Boundaries & Constraints

**Always:** Orchestrator nie woła Win32 ani zegara ambientowego (czas tylko przez `TimeProvider` i `ScheduleTimer`/`TimerElapsed`, AD-14); logika ustalenia i decyzji w Core; kolejność komend z AD-5 (`MuteProcessTree` → `MinimizeWindow`, odciszenie po opuszczeniu poprzedniej); `foregroundSettleMs` w `settings.json` (domyślnie 300, zakres 100–2000, walidacja jak inne pola); nowe zdarzenia logu (`foregroundSwitch{ owner, appId, transitionId }`, `foregroundTransitionFailed`) idą przez `OrchestratorResult`, loguje je `OrchestratorLoop`.

**Never:** `ShowShell`, `ActivateWindow`, `restore` i foreground w przełączeniu z zewnątrz; `focusRequest` (UI zostaje na ostatnim kafelku); zmiana trybu, stanu lub dźwięku po `foreign` (także `hwnd` 0) albo po echu; `HideOsk` (epic-osk) i `pause` (epic-netflix-dpad); zmiany w `Platform/*` (kontrakt i `ReportForeground` są gotowe ze story 11); ocena przy komendzie okiennej bez wyniku albo przy aplikacji w `Starting`.

## I/O & Edge-Case Matrix

| Scenariusz | Stan / wejście | Wynik | Obsługa |
|---|---|---|---|
| Echo | `ActivateWindow` albo `ShowShell` wysłane, `ForegroundChanged` zgodne z zamiarem | po ustaleniu brak komend i snapshotu | — |
| Ostatnia wygrywa | dwa `ForegroundChanged` w oknie `foregroundSettleMs` | ocena tylko ostatniej, po 300 ms od ostatniej | stary `TimerElapsed` nie ocenia |
| Komenda bez wyniku | obserwacja, a `MinimizeWindow` bez `CommandCompleted` | brak oceny; po wyniku `ReportForeground`, ocena świeżej obserwacji | zapisana obserwacja porzucona |
| Starting | obserwacja przy aplikacji w `Starting` | brak oceny; po wyjściu ze `Starting` `ReportForeground` | — |
| Zbiór okien | obserwacja rozpoznana przed zmianą zbioru okien zarządzanych | po zmianie `ReportForeground` zamiast oceny starej | — |
| Porażka komendy | `CommandFailed` okiennej, zapisana obserwacja niezgodna z zamiarem | tylko log `foregroundTransitionFailed`, bez przełączenia | obserwacja porzucona |
| Shell | owner `shell`, aplikacja A w `Foreground` | `foregroundSwitch`; A → `Background`; `MuteProcessTree` A, `MinimizeWindow` A, `SetInputRouting` shell; bez `ShowShell` i `focusRequest`; snapshot | nowy `transitionId` |
| App w tle | owner `app` B w `Background`, A w `Foreground` | A opuszczona (mute, minimize), potem `UnmuteProcessTree` B, `SetInputRouting` app dla B; B w `Foreground`; bez `ActivateWindow` | — |
| App w tle, shell na wierzchu | owner `app` B w `Background`, nic w `Foreground` | `UnmuteProcessTree` B, routing app, B w `Foreground` | — |
| App nieaktywna | owner `app` w `NotRunning`/`Stopping`/`Failed` | tylko log | — |
| Foreign | owner `foreign`, także `hwnd` 0 | brak zmian | — |
| Stary transitionId | `TimerElapsed` ustalenia po nowym przejściu (np. Home) | brak oceny; ocena świeża po wynikach komend nowego przejścia | — |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Orchestrator/Orchestrator.Foreground.cs` -- dziś `OnForegroundChanged` tylko rozpoznaje właściciela (`ForegroundOwnerResolver.Resolve`) i zwraca `Foreground` do logu; tu dochodzą stan ustalenia i decyzja.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` -- `Handle` (`WithManagedWindows(message switch …)`), `OrchestratorResult` (pole `Foreground`), `Route`, `NextTransitionId`, `NextCmdId`; `_timerId` wspólny licznik timerów. Tu: rejestracja komend okiennych z wyniku, obsługa `TimerElapsed` ustalenia, nowe pola wyniku.
- `src/MediaStation.Core/Orchestrator/Orchestrator.Timers.cs` -- `OnTimerElapsed` (korelacje `_startTimers`/`_stopTimers`); dodać gałąź timera ustalenia po `_settleTimerId`. `ScheduleTimer(cmdId, timerId, owner, due)` i `CancelTimer(cmdId, timerId)`.
- `src/MediaStation.Core/Orchestrator/Orchestrator.Stop.cs` -- `OnCommandCompleted`; `Orchestrator.Failures.cs` -- `OnCommandFailed`: tu wynik komendy okiennej zdejmuje ją ze zbioru oczekujących i restartuje timer, `CommandFailed` loguje `foregroundTransitionFailed`.
- `src/MediaStation.Core/Orchestrator/Orchestrator.Home.cs` -- wzorzec opuszczenia aplikacji: `MuteProcessTree` → `MinimizeWindow` → … → `Route(InputMode.Shell, null, PointerPolicy.Blocked)`; `Orchestrator.Launch.cs` `OnResume` -- wzorzec aktywacji (`ForegroundTransition = NextTransitionId()`, `Route(InputMode.App, app.Type, app.Pointer)`).
- `src/MediaStation.Core/Orchestrator/Orchestrator.ManagedWindows.cs` -- `WithManagedWindows`: zmiana zbioru okien to powód unieważnienia oczekującej oceny.
- `src/MediaStation.Core/Orchestrator/ForegroundOwnerResolver.cs` -- bez zmian (`ForegroundObservation`, `ForegroundOwner`).
- `src/MediaStation.Core/Config/Settings.cs`, `ConfigValidation.cs` (`CheckSettings`), `tools/samples/config/settings.json` -- nowe pole `ForegroundSettleMs` (domyślnie 300, 100–2000); schematy/typy w `web/contracts/*` są generowane (git-ignored), sprawdza je `SchemaExportTests`.
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` (~l.133) -- loguje `foregroundChanged`; dodać wpisy `foregroundSwitch` i `foregroundTransitionFailed` z wyniku.
- Testy: `tests/MediaStation.Core.Tests/OrchestratorTestSupport.cs` (`FakeTimeProvider`, `Foreground`, `Background`), `OrchestratorFailureTests.cs` (l.~259 obecny test „ForegroundChanged tylko log” do przepisania), `ForegroundOwnerTests.cs`, `ConfigValidationTests.cs`, `OrchestratorLoopTests.cs`. Nowy plik `OrchestratorForegroundTests.cs`. Fake `IForegroundLookup` z `ShellHwnd` (konstruktor `Orchestrator(config, time, foreground)`).
- Nie ruszać: `src/MediaStation.App/Platform/**`, `Contracts/*` (kontrakt gotowy), `OnHome`/`OnResume` poza użyciem wzorców.

## Tasks & Acceptance

**Execution:**
- [x] `Config/Settings.cs`, `ConfigValidation.cs`, `tools/samples/config/settings.json`, `ConfigValidationTests.cs` -- `ForegroundSettleMs` (300; 100–2000 inkluzywnie, błąd poza zakresem na ścieżce `$.foregroundSettleMs`) -- AD-11
- [x] `Orchestrator/Orchestrator.cs`, `Orchestrator.Foreground.cs`, `Orchestrator.Timers.cs`, `Orchestrator.Stop.cs`, `Orchestrator.Failures.cs` -- stan ustalenia: ostatnia obserwacja, timer (id, `transitionId`), zbiór oczekujących komend okiennych bieżącego `transitionId` (rejestrowany centralnie z komend wyniku `Handle`, w tym `ReportForeground`), flaga „potrzebny raport”; restart timera po `ForegroundChanged` i po wyniku komendy okiennej (gdy jest obserwacja); `TimerElapsed` ustalenia: brak oceny przy zablokowaniu (zbiór niepusty albo aplikacja w `Starting`; ustawia flagę i porzuca obserwację), inaczej decyzja -- AD-15
- [x] `Orchestrator.Foreground.cs` -- decyzja: echo no-op (owner `shell` bez aplikacji `Foreground`; `app` X już `Foreground`), `foreign`/`hwnd` 0 bez zmian, `shell` → opuszczenie `Foreground` (Mute, Minimize, `Route(Shell, null, Blocked)`), `app` X w `Background` → opuszczenie poprzedniej `Foreground`, potem `UnmuteProcessTree` X i `Route(App, X.Type, X.Pointer)`, `X.ForegroundTransition` = nowy `transitionId`, snapshot bez zmiany `_focus`; `app` w `NotRunning`/`Stopping`/`Failed` tylko log -- AD-5, AD-8
- [x] `Orchestrator.cs`, `Orchestrator.ManagedWindows.cs` -- po każdym `Handle`: nowy `transitionId` unieważnia oczekującą ocenę (anuluj timer, porzuć obserwację, ustaw flagę), zmiana zbioru okien robi to samo; gdy flaga i brak blokady, dołóż `ReportForeground` (czyści flagę, porzuca obserwację) -- AD-15
- [x] `OrchestratorResult` + `OrchestratorLoop.cs` -- pola `ForegroundSwitch{ Owner, AppId, TransitionId }` i `ForegroundTransitionFailed` (obserwacja) i ich wpisy logu `foregroundSwitch`, `foregroundTransitionFailed` -- obserwowalność
- [ ] testy `tests/MediaStation.Core.Tests` (`OrchestratorForegroundTests.cs`, przepisany test w `OrchestratorFailureTests.cs`, `OrchestratorLoopTests.cs`) -- wszystkie wiersze macierzy na `FakeTimeProvider`, w tym Alt+Tab A → B (A wyciszona i zminimalizowana, B odciszona, snapshot: B `Foreground`, A `Background`, `Focus` bez nowego `seq`) -- weryfikacja

**Acceptance Criteria:**
- Given `dotnet test`, when uruchomione, then wszystkie testy przechodzą, a macierz ma pokrycie testami (echo, ostatnia obserwacja, brak oceny przy komendzie bez wyniku i w `Starting` z `ReportForeground`, `foregroundTransitionFailed`, shell, aplikacja w `Background`, Alt+Tab A→B, `foreign` i `hwnd` 0, stary `transitionId`).
- Given dev harness na bieżącej maszynie i Edge na wierzchu, when Alt+Tab (wstrzyknięty albo `SetForegroundWindow`) na shell, then log ma `foregroundSwitch owner:shell`, `audioRuleApplied muted:true`, `MinimizeWindow completed`, `inputRouting shell`, a wstrzyknięte strzałki ruszają fokus shella bez Home.
- Given shell na wierzchu i okno Edge w tle, when Alt+Tab na Edge, then `foregroundSwitch owner:app`, `muted:false`, `inputRouting app`; Alt+Tab na Notatnik daje tylko `foregroundChanged owner:foreign`.

## Implementation Notes

Machine: ran on UM760 (COMPUTERNAME MEDIASTATION, board F7BSI, model EliteMini Series; AGENTS.md command, output read at the start of the run).

- Done: `foregroundSettleMs` (Settings, validation 100–2000, sample); settle/decision policy in `Orchestrator.Foreground.cs` with window-command tracking centralized in `Handle` (`ApplyForegroundPolicy`), so `OnCommandCompleted`/`OnCommandFailed` in `Stop.cs`/`Failures.cs` are untouched; `ForegroundSwitch`/`ForegroundTransitionFailed` on `OrchestratorResult`, logged by `OrchestratorLoop`.
- Evidence (UM760): `dotnet build MediaStation.slnx` 0 warnings/errors; `dotnet test MediaStation.slnx` 1220/1220 (rerun after the review patch). Matrix rows covered in `OrchestratorForegroundTests.cs`, log entries in `OrchestratorLoopTests.cs`.
- Not run: the live dev-harness Alt+Tab scenario (shell, Edge, Notepad). No existing tool injects Alt+Tab or `SetForegroundWindow` onto the shell (`tools/check-home-s10.ps1` covers Home only); building one is out of this ticket's diff. Strongest evidence is the Core and loop tests; the user's whole-epic acceptance on UM760 (`fp-acceptance`) covers the live path.

## Plan Change Log

## Review Triage Log

### 2026-10-27 — Review pass
- verdicts: 29 findings — high 0, medium 0, low 1, false 20, maybe-false 8
- findings:
  - `[low]` `[patch]` Pointer policy of the switched-to app never asserted (verification-gap) — added `routing.Pointer` assertions (web `Blocked`) to the two switch tests; `dotnet test` 1220/1220.
  - `[maybe-false]` `[defer]` A window command whose result is lost blocks evaluation until the next `transitionId` (edge, blind) — executors always answer (AD-4), unverified medium; settled by a test with a dropped result or a deadline timer (deferred).
  - `[maybe-false]` `[reject]` Failed `ReportForeground` is not retried / observation stranded when only a report is pending (edge, blind) — a persistent failure would loop; `Execute` answers `CommandFailed` and the next `ForegroundChanged` re-arms the timer; low.
  - `[false]` `[reject]` Failed branch clears `_reportNeeded` (edge, blind) — intended: AD-15 says a mismatching observation after `CommandFailed` goes only to the log.
  - `[false]` `[reject]` Failure coinciding with transition or window-set change goes unlogged (edge, intent-alignment) — a window command's result never changes `transitionId` or the window set in the same step.
  - `[false]` `[reject]` Minimizing the previous app activates another app (edge) — `MinimizeWindow` is `SW_SHOWMINNOACTIVE` (AD-5).
  - `[false]` `[reject]` Stale state across `SystemResumed`/`OrchestratorStarted` (edge) — both bump `transitionId`, which discards the pending evaluation.
  - `[false]` `[reject]` Shell owner with Foreground app but mode shell (edge) — Home sets `Background` synchronously, so the state cannot occur.
  - `[false]` `[reject]` Machine label, unchecked boxes, unrecorded evidence, plan/Code Map file mismatch (blind) — fixed in Implementation Notes and Execution; edits to the plan are not a code finding.
  - `[maybe-false]` `[reject]` Live Alt+Tab not run (blind, intent-alignment) — recorded in Implementation Notes with the reason; left to the whole-epic UM760 acceptance.
  - `[false]` `[reject]` Remaining blind and test-coverage notes (`_windowsChanged` during `ForegroundChanged`, `NotRunning`/`Failed` apps, custom `foregroundSettleMs` `Due`, `OwnerName` helper, weakened `Assert.All`, `Decide` hwnd null) — same code paths as covered tests or negligible; low and not worth added complexity.

## Design Notes

Stan ustalenia (jedno miejsce, bez Win32): `_observation` (ostatnia), `_settleTimerId`/`_settleTransition`, `_pendingWindow` (`cmdId → transitionId`), `_reportNeeded`. Reguły: (1) `ForegroundChanged` zapisuje obserwację i restartuje timer; jeśli jest zablokowane (oczekująca komenda bieżącego `transitionId` albo `Starting`), ustawia `_reportNeeded`; (2) wynik komendy okiennej zdejmuje ją ze zbioru, restartuje timer, a `CommandFailed` loguje niezgodną obserwację; (3) po każdym `Handle`: komendy okienne z wyniku wchodzą do zbioru, zmiana `transitionId` albo zbioru okien porzuca ocenę i ustawia `_reportNeeded`, a gdy flaga i brak blokady, wynik dostaje `ReportForeground`; (4) `TimerElapsed` ustalenia ocenia tylko, gdy jego `transitionId` jest bieżący, nic nie blokuje i obserwacja istnieje.

Zbiór oczekujących starszych `transitionId` czyścić po każdym `Handle`, żeby nie rósł.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx` -- expected: 0 błędów, 0 ostrzeżeń
- `dotnet test MediaStation.slnx` -- expected: wszystkie testy przechodzą

**Manual checks:**
- Na bieżącej maszynie z `MEDIASTATION_DEV_HARNESS=1` (Release, jak `tools/check-home-s10.ps1`): scenariusz Alt+Tab z kryteriów (shell, Edge, Notatnik) i asercje logu; zapisać dowód w Implementation Notes.

## Auto Run Result

Status: built

- Summary: Orchestrator applies the AD-15 `ForegroundChanged` policy: each observation is logged and restarts the `foregroundSettleMs` timer, only the last settled observation is judged, evaluation is withheld while a window command has no result or an app is `Starting` (then `ReportForeground`), and a mismatch becomes an external switch with a new `transitionId` and `foregroundSwitch` log entry (shell: Mute → Minimize → routing shell; background app: leave the previous one, then Unmute and routing app; no `ShowShell`, `ActivateWindow` or focus request).
- Files: `Config/{Settings,ConfigValidation}.cs`, `tools/samples/config/settings.json` (setting); `Orchestrator/{Orchestrator,Orchestrator.Foreground,Orchestrator.Timers,Orchestrator.ManagedWindows}.cs` (policy); `App/Orchestrator/OrchestratorLoop.cs` (log entries); tests in `OrchestratorForegroundTests` (new), `OrchestratorLoopTests`, `OrchestratorFailureTests`, `ForegroundOwnerTests`, `ConfigValidationTests`.
- Review: 29 findings, 1 patched (low: pointer assertions), 1 deferred (maybe-false, unverified medium: lost window-command result blocks evaluation until the next transition), 27 rejected with reasons in the Review Triage Log.
- Follow-up review recommended: false (one low patch).
- Verification (UM760): `dotnet build MediaStation.slnx` 0 warnings/errors; `dotnet test MediaStation.slnx` 1220/1220. Live dev-harness Alt+Tab scenario not run (no injection tool; see Implementation Notes).
- Residual risks: the live path (real hook events, real `TimerScheduler`, executors, audio log lines) is covered only by Core and loop tests; evaluation has no deadline if a window command's result is lost (deferred).
