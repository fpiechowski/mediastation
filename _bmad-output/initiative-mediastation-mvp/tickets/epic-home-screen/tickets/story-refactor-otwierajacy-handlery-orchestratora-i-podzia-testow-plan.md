---
title: 'Refactor otwierający: handlery Orchestratora i podział testów'
type: 'refactor'
ticket: '2'
created: '2026-10-26'
status: done
baseline_revision: 'd7780e4d3f80f5cf5700cf0949fe83be8a095286'
route: 'full'
route_source: 'auto'
risk: 'low'
review: 'quick'
review_source: 'pinned'
lenses_ran: [quick]
review_loop_iteration: 0
followup_review_recommended: false
context: ['{project-root}/AGENTS.md']
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** `Orchestrator.Handle` ma 12 gałęzi, a cała logika (750 linii) i testy (`OrchestratorTests.cs`, 1909 linii, jedna klasa) leżą w jednym pliku; kolejne epiki (DW-19) dokładają gałęzie dispatchu.

**Approach:** Rozdzielić `Orchestrator` na `partial class` z jednym plikiem na grupę handlerów (start/wybudzenie, launch, attach/adopcja, Home, stop/restart, awarie, timery, zarządzane okna), tak by `Handle` tylko delegował, oraz rozbić testy na pliki według tych samych grup ze wspólnymi helperami. Zachowanie się nie zmienia.

## Boundaries & Constraints

**Always:** Zachowanie, kolejność komend i numeracja `cmdId`/`epoch`/`transitionId` bez zmian. Wszystkie dotychczasowe testy zostają z tymi samymi nazwami i asercjami (`dotnet test` = 949 testów, w tym 118 z dawnego `OrchestratorTests`). Pole `_pendingStops` zostaje polem klasy `Orchestrator` (test sięga po nie refleksją). Komentarze XML metod przenoszą się bez zmian treści. Żaden plik testów Orchestratora nie przekracza ok. 600 linii.

**Never:** Nowe zachowanie, nowe gałęzie dispatchu, zmiany kontraktów, zmiana publicznego API `Orchestrator`/`OrchestratorResult`. Nie zmieniać asercji ani danych testów. Nie wprowadzać interfejsów ani nowych klas handlerów (stan zostaje w jednej klasie).

</intent-contract>

## Code Map

- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` -- dziś całość; po zmianie `public sealed partial class`: `OrchestratorResult`, pola, ctor, `Handle`, `Snapshot`, `Next*`, `Describe`, `AppState`, `PendingStop`, `AnotherAppIsActive`.
- Nowe pliki `Orchestrator.<Grupa>.cs` w tym samym katalogu (ta sama przestrzeń nazw, `using` jak w oryginale, `using ActionMessage = ...`):
  - `Startup` -- `OnOrchestratorStarted`, `OnDefaultFocus`, `OnSystemResumed`
  - `Launch` -- `OnLaunchApp`, `OnResume`
  - `Attach` -- `OnAppWindowAttached`, `OnAdopt`
  - `Home` -- `OnHome`
  - `Stop` -- `OnStop`, `OnCommandCompleted`, `OnStopped`, `FindPendingStop`
  - `Failures` -- `OnCommandFailed`, `OnProcessExited`, `OnCdpLost`, `EnterFailed`, `DropPendingStarts`, `DropPendingStops`
  - `Timers` -- `OnTimerElapsed`, `ScheduleTimeout`, `CancelTimeout`, `FindTimeout`, `ScheduleStartTimeout`, `CancelStartTimeout`
  - `ManagedWindows` -- `WithManagedWindows`
- `tests/MediaStation.Core.Tests/OrchestratorTests.cs` -- 1909 linii, 118 testów, helpery w liniach 11–90; linia ~1393 czyta `_pendingStops` refleksją (`typeof(CoreOrchestrator)`), więc pole musi zostać w klasie.
- Granice grup testów (po komentarzach i nazwach): start/wybudzenie ~92–325, launch + attach/adopcja ~326–690, Home ~695–880, stop/restart ~880–1230, awarie ~1230–1600, timery ~1600–1800, zarządzane okna i cykl życia ~1800–1909. Dokładne cięcie według tematu testu, nie numeru linii.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs`, `OrchestratorActionSinkTests.cs` -- osobne klasy, nie ruszać.

## Tasks & Acceptance

**Execution:**
- [ ] `src/MediaStation.Core/Orchestrator/Orchestrator*.cs` -- zrobić z `Orchestrator` klasę `partial`, przenieść metody do plików grup z Code Map bez zmiany ciał; `Handle` zostaje przełącznikiem delegującym po jednej linii do metody grupy -- DW-19
- [ ] `tests/MediaStation.Core.Tests/OrchestratorTestSupport.cs` -- `internal static class` ze wspólnymi helperami (`T0`, `TwoApps`, `Create`, `WebAndMoonlight`, `TwoWebAndMoonlight`, `StartedWith`, `Started`, `AssertManaged`, `StateOf`, `Foreground`, `Background`), używanymi przez `using static`
- [ ] `tests/MediaStation.Core.Tests/Orchestrator<Grupa>Tests.cs` -- przenieść testy 1:1 do klas `Orchestrator{Startup,Launch,Attach,Home,Stop,Failure,Timer,Lifecycle}Tests` (komentarze nad testami zostają); usunąć `OrchestratorTests.cs`; podział tak, by żaden plik nie przekraczał ok. 600 linii

**Acceptance Criteria:**
- Given stan przed zmianą, when `dotnet test MediaStation.slnx`, then wynik to 949 testów, 0 niepowodzeń, 0 pominiętych
- Given nowe pliki testów, when policzyć linie, then każdy plik `Orchestrator*Tests*.cs` ma ok. 600 linii lub mniej, a testów w nich jest 118 (dawny `OrchestratorTests`)
- Given `Orchestrator.cs`, when przeczytać `Handle`, then każda gałąź tylko wywołuje metodę grupy, a metody grup leżą w osobnych plikach `Orchestrator.<Grupa>.cs`

## Implementation Notes

Plan narzucony przez ticket (`risk: low`). Wybór `partial class` zamiast osobnych klas handlerów: stan sesji jest jeden, a test sięga po `_pendingStops` refleksją; partial nie zmienia kształtu typu.

## Plan Change Log

## Review Triage Log

### 2026-10-26 — Review pass
- verdicts: 1 findings — high 0, medium 0, low 0, false 1, maybe-false 0
- findings:
  - `[false]` `[reject]` Section comment `// ── Bramka foreground ...` in `OrchestratorLifecycleTests.cs` lost its indentation — the original `OrchestratorTests.cs` line 1793 was also at column 0 (checked with `git show HEAD:...`), so the move preserved it.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx` -- expected: 0 błędów, 0 nowych ostrzeżeń
- `dotnet test MediaStation.slnx` -- expected: suma 949, niepowodzeń 0
- `wc -l tests/MediaStation.Core.Tests/Orchestrator*Tests*.cs` -- expected: żaden plik > ok. 600

## Auto Run Result

Machine: ran on UM760 (host MEDIASTATION, board F7BSI, model EliteMini Series).

**Summary:** `Orchestrator` is now a `partial class` split by handler group (DW-19); `Handle` only delegates one call per branch. `OrchestratorTests.cs` (1909 lines, 118 tests) is split into eight test classes with shared helpers in `OrchestratorTestSupport`. No behavior change.

**Files changed:**
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` -- partial, core state, `Handle`, snapshot, ids (170 lines)
- `src/MediaStation.Core/Orchestrator/Orchestrator.{Startup,Launch,Attach,Home,Stop,Failures,Timers,ManagedWindows}.cs` -- handler groups, bodies moved unchanged
- `tests/MediaStation.Core.Tests/OrchestratorTestSupport.cs` -- shared helpers (`internal static`)
- `tests/MediaStation.Core.Tests/Orchestrator{Startup,Launch,Attach,Home,Stop,Failure,Timer,Lifecycle}Tests.cs` -- tests moved 1:1 (largest 391 lines); `OrchestratorTests.cs` removed

**Review:** quick lens, 1 finding, rejected as false (indentation was already column 0 in the original). Patches 0, deferred 0.

**Verification:** `dotnet build MediaStation.slnx` -- 0 errors, 0 warnings. `dotnet test MediaStation.slnx` -- 949 passed, 0 failed (baseline 949). Sorted-line comparison of old vs new sources and tests shows only header/visibility/class-name differences. Per-file test sizes all under 600 lines. UM760 hardware checks: not applicable (pure refactor).

**Follow-up review recommended:** false. **Residual risks:** none known; git warns LF to CRLF on new files (repo autocrlf).
