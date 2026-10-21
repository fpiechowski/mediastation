---
title: 'Tracer bullet: Orchestrator na własnym wątku publikuje snapshot do shella'
type: 'feature'
ticket: '1'
created: '2026-10-21'
status: blocked
baseline_revision: 'c727620fba2c8d3b1f9b90ebc792e3713352f5cc'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
followup_review_recommended: false
warnings: []
deferred: []
operator_actions:
  - 'On the dev machine, publish a Release self-contained win-x64 build with `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`.'
  - 'Copy the publish directory to the UM760 and run `MediaStation.exe` with valid `config/apps.json`, `config/input.json` and `config/settings.json`.'
  - 'Confirm on the UM760 that the shell shows the placeholder screen.'
  - 'In `%LOCALAPPDATA%\MediaStation\logs\mediastation-<UTC day>.jsonl`, confirm the last lines contain `snapshotPublished` with `focusRequest {target: "default", seq: 1}`.'
  - 'Record the dated UM760 result with HEAD and machine name in the epic acceptance note.'
blocked_at: "2026-10-21"
blocked_reason: "awaiting operator: On the dev machine, publish a Release self-contained win-x64 build with `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`.; Copy the publish directory to the UM760 and run `MediaStation.exe` with valid `config/apps.json`, `config/input.json` and `config/settings.json`.; Confirm on the UM760 that the shell shows the placeholder screen.; In `%LOCALAPPDATA%\\MediaStation\\logs\\mediastation-<UTC day>.jsonl`, confirm the last lines contain `snapshotPublished` with `focusRequest {target: \"default\", seq: 1}`.; Record the dated UM760 result with HEAD and machine name in the epic acceptance note."
---

<intent-contract>

## Intent

**Problem:** Snapshot powstaje raz ze stałym `rev: 1`, a `ShellBridge.Publish` wysyła z wątku wywołującego (F12); brak Orchestratora i kontraktu stanu aplikacji.

**Approach:** Czysty `Orchestrator` w Core obsługuje `OrchestratorStarted` z kolejki wątku w App i zwraca Commands oraz snapshot z monotonicznym `rev`, `apps[]` i `focusRequest{default}`. Most publikuje latest-wins na wątek UI z logiem `snapshotPublished`; dedupe wpisów UI trafia do Core; błąd configu nie startuje Orchestratora i daje `ConfigError`.

## Boundaries & Constraints

**Always:** Core czysty `net10.0` (AD-3), bez UI, I/O i czasu ambientowego; `rev` monotoniczny; `seq` rośnie tylko przy nowym fokusie; jedna kolejka i jeden konsument; publikacja nie blokuje Orchestratora.

**Never:** porty i wykonawcy, `SetInputRouting`, kolejne Eventy/Intenty (story 3+); kafelki w shellu (tylko guard); timery (story 5); testy App; ręczna edycja generowanych schematów/TS; zapis plików ticketów.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| First snapshot | `OrchestratorStarted`, valid config | `rev: 1`, `placeholder`, `apps[]` + `focusRequest{default,1}`, no Commands | — |
| Other events | `CommandCompleted`/`CommandFailed` | no snapshot, no Commands | ignored until story 3/4 |
| Config error | invalid config | no Orchestrator; `configError`, `rev: 0`, `apps: []`, `focusRequest: null` | error screen, process alive |
| Busy UI | several publishes | `BeginInvoke` flush sends newest only; one `snapshotPublished` per flush | caller never blocks |
| UI exception | dispatcher then AppDomain | mark + consume → one log entry | distinct exceptions independent |

</intent-contract>

## Code Map

- `Core/Contracts/ShellSnapshot.cs:15` — 3-field record; positional order sets JSON/schema order (tests assert verbatim).
- `Core/Config/AppsConfig.cs:12-18` — `AppCategory` moves to Contracts (already shared `PointerPolicy`).
- `Core/Contracts/MessageTypes.cs:14-20` — registry; exact camelCase list asserted.
- `Core/Config/ShellSnapshotFactory.cs:11-19` — sole production ctor; seed `apps[]` from `result.Config`.
- `App/CompositionRoot.cs:26,85-90,96-118` — static dedupe dict, `rev: 1`, factory→bridge→window.
- `App/Shell/ShellBridge.cs:35-39,83-86` — `Publish` sends on caller thread; `Send` owns `PostWebMessageAsJson`.
- Reuse `ILog` and `Envelope.Snapshot`; don't touch `web/contracts/**`, tickets, `ConfigValidation` rules.

## Tasks & Acceptance

**Execution:**
- [ ] `Contracts/AppCategory.cs` — move enum from `Config/AppsConfig.cs`; consumers already import Contracts.
- [ ] `Contracts/ShellSnapshot.cs` — `AppLifecycleState`, `AppSnapshot`, `FocusRequest(Target, Seq)` + `DefaultTarget`; snapshot gains `Apps`, nullable `FocusRequest`.
- [ ] `Contracts/Messages.cs`, `MessageTypes.cs` — `IOrchestratorMessage` on bases, `OrchestratorStarted : Event`, register.
- [ ] `Orchestrator/Orchestrator.cs` — apps from `LoadedConfig`, `_rev`/`_focusSeq`, `Handle` → `OrchestratorResult(Commands, Snapshot?)`; only started snapshots.
- [ ] `Logging/DispatcherExceptionLog.cs` — thread-safe mark/consume pair.
- [ ] `Config/ShellSnapshotFactory.cs` — configured apps to `notRunning`, `focusRequest` null.
- [ ] `App/Orchestrator/OrchestratorLoop.cs` — background `Thread` + `BlockingCollection`; `Start` enqueues started event; catch logs `orchestratorFailed`; `Dispose` completes adding.
- [ ] `App/Shell/ShellBridge.cs` — capture `Dispatcher`, latest-wins pending flag, flush sends newest + logs `snapshotPublished {rev, focusRequest}`; `ready` re-send without log.
- [ ] `App/CompositionRoot.cs` — pass dispatcher, bootstrap `rev: 0`, start loop after `Show()`, dispose on `Exit`, Core dedupe.
- [ ] `tests/…` — orchestrator (first snapshot, rev/seq monotonic, ignored events), dedupe, contract/schema/registry, factory on valid `LoadedConfig`; `web/shell/src/bridge.ts` validates `apps[]` and `focusRequest`.

**Acceptance Criteria:**
- Given valid config with two apps, when `dotnet test`, then first `OrchestratorStarted` yields `rev: 1`, apps `notRunning` in order, `focusRequest{target: "default", seq: 1}`, and a second event yields `rev: 2`, `seq: 2`.
- Given valid config, when App starts, then `snapshotPublished` carries `rev` and `focusRequest{target: "default", seq: 1}` and the shell renders the placeholder.
- Given invalid config, when App starts, then no Orchestrator thread exists and the shell gets `ConfigError`, process alive.
- Given snapshot JSON missing `apps`/`focusRequest`, when the bridge receives it, then `isSnapshot` rejects it.
- Given one UI exception in dispatcher and AppDomain handlers, when both run, then the helper allows exactly one log entry.

## Implementation Notes

- `AppCategory` moved to `Contracts/AppCategory.cs` so the snapshot contract owns it; config records compile unchanged because they already import `MediaStation.Core.Contracts`.
- Latest-wins bookkeeping was extracted to `Core/Orchestrator/LatestSnapshotSlot` so the App-only bridge path has a unit-tested core (matrix row "Busy UI").
- `IOrchestratorMessage` marks the three inbound bases (`Intent`, `Event`, `Action`); `Command` stays outbound.
- Bootstrap snapshot uses `rev: 0` and is never logged; the first `snapshotPublished` comes from the Orchestrator snapshot at `rev: 1`.

## Plan Change Log

## Review Triage Log

### 2026-10-21 — Review pass
- verdicts: 3 findings — high 0, medium 0, low 2, false 1, maybe-false 0
- findings:
  - `[low]` `[patch]` `snapshotPublished` is logged before `Send`, so a throwing `Send` leaves a false success entry (the cold-start no-op itself is by design — delivery is completed by the `ready` re-send) — moved the log entry after `Send(pending)` in `Flush`; the log now records only applied sends.
  - `[low]` `[reject]` `isSnapshot` validates value types but not enum/target values — the host can only emit values from the Core-generated schema, no user meets this today, and adding value unions would add branches beyond a direct correction.
  - `[false]` `[reject]` a throwing `Dispatcher.BeginInvoke` latches `LatestSnapshotSlot` permanently — the dispatcher only throws once shut down, which coincides with `Application.Exit`, where `OrchestratorLoop.Dispose` already stops the producer, so no later publish exists to drop.

## Design Notes

Bootstrap `rev: 0` is never logged; Orchestrator revs start at 1. `seq` advances only on a new focus request. Commands stay empty until story 3. Latest-wins is one pending flush, not a snapshot queue (AD-14); dispatcher marks the exception, AppDomain consumes it.

## Verification

**Commands:**
- `dotnet test` -- all tests green.
- `dotnet build MediaStation.slnx -c Release` -- schema + TS regenerate (untracked), shell bundle builds; Debug App build covers typecheck.

## Auto Run Result

Status: awaiting-operator

**Summary:** Core gained a pure `Orchestrator` that turns `OrchestratorStarted` into the first full snapshot (`rev: 1`, `apps[]` in `notRunning`, `focusRequest{default, 1}`), with lifecycle/app/focus contracts exported to JSON Schema and TS. App runs it on a dedicated thread with one queue, publishes latest-wins on the UI dispatcher and logs `snapshotPublished {rev, focusRequest}`; invalid config still yields `ConfigError` without an Orchestrator. Dispatcher/AppDomain log dedupe moved to a tested Core helper.

**Files changed:**
- `src/MediaStation.Core/Contracts/` — `AppCategory`, `ShellSnapshot` (`AppLifecycleState`, `AppSnapshot`, `FocusRequest`), `IOrchestratorMessage`/`OrchestratorStarted`, registry.
- `src/MediaStation.Core/Orchestrator/` — `Orchestrator`, `LatestSnapshotSlot`.
- `src/MediaStation.Core/Logging/DispatcherExceptionLog.cs`, `Config/ShellSnapshotFactory.cs`, `Config/AppsConfig.cs`.
- `src/MediaStation.App/` — `CompositionRoot`, `Shell/ShellBridge`, new `Orchestrator/OrchestratorLoop`.
- `web/shell/src/bridge.ts` — snapshot guard covers `apps[]`/`focusRequest`.
- `tests/MediaStation.Core.Tests/` — orchestrator, latest-slot, dispatcher-log, updated contract/schema/factory tests.

**Review findings:** quick lens, 3 findings — 1 low patched (log after send), 1 low rejected (value-level guard), 1 false rejected (latched flush on shutdown). Deferred: none. Patched: 1 low. Follow-up review recommended: false.

**Verification:**
- `dotnet test` — 281/281 passed.
- `dotnet build MediaStation.slnx -c Release` — success, 0 warnings; schema/TS regenerated and Vite shell bundle built.
- `dotnet build src/MediaStation.App -c Debug` — success, 0 warnings; contracts + `tsc` passed.

**Residual risks:** App-only threading/dispatcher wiring has no unit tests (verified by build); `apps[]`/`focusRequest` are not rendered yet by design; the UM760 self-contained run and log check are outstanding operator actions.

