---
epic: epic-orchestrator-lifecycle
date: 2026-10-22
verdict: accepted-with-open-items
criteria: declared
headless: true
---

# Retrospective: Orchestrator & app lifecycle

## Epic summary

Epic `epic-orchestrator-lifecycle` (CAP-2) delivers `Core/Orchestrator` as the single writer of session state, the App-side loop, ports and composition-root wiring, host adoption, the `Stopping`/`Starting` timers, `transitionId`, `focusRequest`, and the production power adapter (`SystemEvents` + `WM_POWERBROADCAST`). All 11 tickets are finished; nothing is at `built`.

| Ref | Ticket | status / state | Plan baseline | Range | Notes |
|-----|--------|----------------|---------------|-------|-------|
| 2.1 | Tracer bullet: snapshot from the Orchestrator thread | done / done | `c727620` | `c727620..4296ec3` | 4 commits, 0 merges; range also holds three loop/operator commits (`d54b0b8`, `f7ffc21`, `4296ec3`) |
| 2.2 | SystemEvents: Modern Standby wake | done / done | `4296ec3` | `4296ec3..14285f8` | single squashed build commit labelled "Merge …" by the loop |
| 2.3 | Ports and the app launch path | done / done | `14285f8` | `14285f8..2244b07` | recorded |
| 2.10 | Home, switching and closing apps | done / done | `2244b07` | `2244b07..0eb92bd` | recorded |
| 2.4 | Failed: entry, AppMenu exit, foreground crash | done / done | `0eb92bd` | `0eb92bd..e2253fd` | recorded |
| 2.5 | Timers in Core and the `Starting` timeout | done / done | `e2253fd` | `e2253fd..06d8d8b` | recorded |
| 2.11 | `Stopping` timeout and timeout bounds | done / done | `06d8d8b` | `06d8d8b..2084c3b` | recorded |
| 2.6 | `transitionId` and Home during `Starting` | done / done | `2084c3b` | `2084c3b..a3afae8` | one `bad_plan` loopback, re-derived |
| 2.7 | Adopting live apps after restart | done / done | `a3afae8` | `a3afae8..9b01c6d` | build `6add563` + recovery `9b01c6d`; see F12 |
| 2.8 | Wake: full policy | done / done | `9b01c6d` | `9b01c6d..ad03f93` | recorded |
| 2.9 | Refactor sweep | done / done | `ad03f93` | `ad03f93..bf9bb86` | inferred end = HEAD `bf9bb86`; range also holds epic-input commits `692b118`/`cf7bd79`, excluded here (see F11) |

- **`pending_tickets`: empty.** Ticks the machine verdict's completeness gate (`tickets.py status`, 2026-10-22). No ticket at `built`.
- **Ranges.** One range per plan, ordered by baseline ancestry (`c727620` oldest). Ranges 2.2–2.11 each contain a single commit with a loop-generated `Merge bmad-loop/...` subject; `git_evidence.py` reports `is_merge: false` for them, i.e. they are squashed ticket commits, not merges. The last range is inferred, as the workflow requires, and was cut at the last commit that belongs to this epic (`bf9bb86`); `692b118` (epic-input inception) and `cf7bd79` (epic-input's merge of master) are inside the raw range and are not epic-2 work — `git_evidence.py` accordingly reports `merge_count: 1` / `merge_files: 68` for that range, all of it extrapolated churn, not epic-2 churn.
- **Churn** (`git diff c727620..bf9bb86`): 55 code files, 7 336 insertions, 61 deletions, 35 new files — `src` 2 419 lines, `tests` 4 509 lines (1.86 test lines per production line), `tools/inject-power.ps1` 387, `web/shell/src/bridge.ts` 21.

### Evidence inventory

| Evidence | State |
|----------|-------|
| Epic file with `Done when` (2 items) | present → criteria **declared** |
| Initiative requirements | `initiative-mediastation-mvp.md` has no Requirements section; CAP-2 read from `spec-mediastation/spec-mediastation.md` (Capabilities, CAP-2) and `architecture-mediastation.md` (AD-3, AD-4, AD-5, AD-10, AD-14, AD-15, AD-16) |
| Entries | 11 rows, every `verify`/`description` pulled into a plan; none empty |
| Story files | 3 (`2.1`, `2.2`, `2.9`); the rest keep intent in the frozen `<intent-contract>` block |
| Plans | 11/11 with `## Verification`, dated `Current-machine evidence` (HEAD + machine), `## Review Triage Log`, `## Auto Run Result`; `## Plan Change Log` records the two re-derivations |
| Session logs | present for all 11 tickets under `.bmad-loop/runs/{20261021-232838-4db8,20261021-235612-3018,20261022-085423-2cbe}/logs/*.sse.jsonl`, including `2.7-dev-1` and `2.7-dev-2` |
| Previous retrospective | `epic-foundation-contracts/epic-foundation-contracts-retrospective.md` (verdict `accepted`) |
| Deferred ledger | `_bmad-output/initiative-mediastation-mvp/deferred-work.md`: DW-1 open, DW-2 resolved, DW-3 open, DW-4 open |
| Missing | no re-run of the UM760 checks (deliberate, see Assumptions); no session-log analysis of individual transcripts beyond the loop journal and plan `Plan Change Log`/`Recovery` sections (narrowed, not skipped silently) |

## Findings

### Aggregate views

**F1 — Architecture delta: clean, no new violation.** Core gained `Ports/`, `Orchestrator/{Orchestrator,TimerScheduler,LatestSnapshotSlot}` and new contracts; App gained `Orchestrator/{OrchestratorLoop,CommandRouter}`, `Hosts/MissingHostPort` and log-only adapters in `Platform/{Window,Input,System}`. The dependency direction stays App → Core; `tests/MediaStation.Core.Tests/CoreBoundaryTests.cs` asserts it on assembly metadata (no `MediaStation.*`, Win32, WPF or WebView2 reference from Core, no P/Invoke, no ambient time) and `Core/Contracts/*` never mentions an App type. No cycle, no layering regression introduced across the epic.
*Disposition:* nothing to fix.

**F2 — Duplication map: `AppEntry → AppType` is written twice.** `src/MediaStation.App/CompositionRoot.cs:227-233` (`AppTypeOf`) and `src/MediaStation.Core/Orchestrator/Orchestrator.cs:678-684` (`Describe`) map the same three closed records to the same enum; the two sessions (2.3 and 2.6/2.10) could not see each other. The 2.3 review rejected sharing the mapper, because the harm requires a fourth `AppEntry` subtype, and `AppEntry` is a closed hierarchy.
*Disposition:* **accept as-is**, recorded here so later runs stop re-flagging it; revisit only if a fourth app category is added.

**F3 — Size growth: `OrchestratorTests` and the switch in `Orchestrator`.** Over the epic `Orchestrator.cs` grew from 84 to 626 lines and now has a 12-arm `switch` (`Orchestrator.cs:71-87`) plus 30 private members; `tests/MediaStation.Core.Tests/OrchestratorTests.cs` is 1 549 lines with ~60 cases in one class, and `OrchestratorLoopTests.cs` 591. Reading both: the production class is a coherent state machine with one responsibility per handler, so it is not a god class yet. The test class is the real growth risk, and the epic Notes commit every later epic to "each adds its own branch in the Orchestrator" (`epic-orchestrator-lifecycle.md:58-59`), so the switch is the designated growth point.
*Disposition:* **defer** with a concrete trigger — split `OrchestratorTests` by lifecycle area at the next test refactor, and extract per-feature handler groups before `epic-home-screen`/`epic-input` add their arms.

**F4 — Pattern divergence: App sources are compiled into `MediaStation.Core.Tests`.** `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj:24-38` links 11 App files (`OrchestratorLoop`, `CommandRouter`, `MissingHostPort`, `PowerSignalRouter`, `PowerSourcesAttachment`, the log-only ports, …) into the Core test assembly, because the initiative forbids an App test project. It is deliberate and documented, it forced the Win32-free extraction in 2.9 (a win), and it keeps the untested surface down to the HWND/WPF shim (DW-1). The residual divergence is that these files are validated by a second compilation, not by the App build, and the pattern is not stated as a project convention anywhere outside that csproj comment.
*Disposition:* **accept as-is**; record the convention in the architecture/notes rather than re-litigating it per ticket.

**F5 — Spec-to-implementation reconciliation.**
- CAP-2's UA row "Na UM760 jedno OK na kafelku `Failed` i jedno OK na „Uruchom ponownie” (J4)" is **not** delivered by this epic: the epic file and the initiative move it to `epic-browserhost`, because it needs AppMenu and a real host (`epic-orchestrator-lifecycle.md:38`, `initiative-mediastation-mvp.md:53`). **Accepted deviation**, owner `epic-browserhost`.
- CAP-2's "adopcję żywej przeglądarki i Moonlighta po restarcie" is delivered against fake hosts only (`MissingHostPort.Probe` is silent); real probes belong to the host epics. **Accepted deviation** by the epic's Boundaries.
- AD-15 says the host reports `AppWindowAttached{appId, hwnd, rootPid}` and that WindowManager/AudioRouter act on `hwnd` **and** `rootPid`. The record carries `RootPid` (`Messages.cs:71`) but `Orchestrator` stores only `Hwnd` (`Orchestrator.cs:698-699`) and every window command carries only `hwnd`. Correct for this epic (no audio routing yet), but `epic-windows-audio` will need the Orchestrator to retain `rootPid`, and no deferred entry tracks that. *Disposition:* **fix now** as a spec reconciliation — add a deferred entry owned by `epic-windows-audio`.

### Diff-scope review (inline lenses)

`bmad-review` was invoked and is unavailable in this project: `render_skill.py` halts with `render entry is missing: .claude/skills/bmad-review/workflow.md`. The three code lenses (adversarial, edge-case, verification-gap) were therefore run inline by this retrospective over the epic's code diff (`c727620..bf9bb86`, `src` + `web/shell/src` + `tools`, 2 827 added lines), weighted to ticket boundaries; the 11 per-story `Review Triage Log`s supply the per-story coverage that was not re-run. **Narrowing recorded:** plans/docs were not lens-reviewed (they were read as evidence), and no lens was re-run over the linked test files line by line.

**F6 — A launch of a `NotRunning` app while another app is in `Foreground` produces two `Foreground` apps.** `OnLaunchApp` (`Orchestrator.cs:171-193`) checks only the target app's own state; unlike `OnResume` (`:203`), `OnStop` (`:347`) and their shared `AnotherAppIsActive` (`:365-367`), it never asks whether another app owns the first plane. Story 3 wrote the launch path, story 10 later added the invariant guard to resume/close/restart and did not revisit launch — a boundary no single session saw. The state is reachable: `OrchestratorTests.cs:821-849` first calls `LaunchApp("netflix")` while `plex` is `Foreground`, and only covers Home arriving **before** the new window attaches. A probe (temporary test, deleted after the run) drove the other order: after `AppWindowAttached("netflix")` the snapshot reads `netflix=Foreground, plex=Foreground`, no `MinimizeWindow` for `plex`. Followed through, `OnHome` (`:234-246`) then minimizes whichever `Foreground` app comes first in `apps.json` and routes `shell`, leaving the other app `Foreground` on top — Home would appear broken. Reachability today is limited (the shell UI and InputHub do not exist yet), but `epic-home-screen` is exactly the epic that will let a tile be chosen with an app in front.
- *Instance:* **fix now** — add the `AnotherAppIsActive` guard to the `NotRunning` branch (or route it explicitly into the future switch branch) plus a test that attaches the second window without Home in between.
- *Upstream:* the invariant "one app on the first plane" was introduced story by story instead of as one guarded entry point; a shared `TryEnterForeground`/guard used by every transition that puts an app in front would prevent the next repetition.

**F7 — Composition root has no automated test and now carries real ordering logic.** `CompositionRoot.StartRun` (`CompositionRoot.cs:117-185`) owns the lazy `postsTo` wiring, the exit disposer order (adapter → reporter → timers → loop), and the power-adapter `try/catch`. That is 120+ lines whose failure modes (a throwing adapter, a wrong dispose order, a loop started before the handler is registered) are exactly the class the 2.2 thorough review spent two passes on, and the only evidence is a smoke run. The initiative's "no App test project" boundary makes this structural; `CoreBoundaryTests` does not reach it and no linked-compile test covers it.
- *Instance:* **defer** — add a deferred-work entry ("composition root wiring and shutdown order are smoke-tested only") so it is tracked instead of re-discovered; alternatively extract the disposable ordering into a Core helper, which is the same trick that made `LatestSnapshotSlot` testable in 2.1.
- *Upstream:* decide once whether "no App test project" survives `epic-deploy-resilience`, which adds `--supervise` and update wiring on the same seam.

**F8 — `snapshotPublished` is not proof of delivery.** `ShellBridge.Flush` (`ShellBridge.cs:91-111`) calls `Send`, whose body is `_webView?.PostWebMessageAsJson(...)` (`:121-122`), and logs `snapshotPublished` unconditionally afterwards. Before `Attach`, a flush therefore drops the snapshot silently and still logs a publication. Behaviour is correct — the `ready` handshake re-sends `_slot.Latest` (`:87`) — and the 2.1 review triaged this deliberately (log moved after `Send`, cold start covered by the re-send). The residual is evidential: Done-when 2 of this epic is verified **by that log line**, so the epic's acceptance evidence means "flush attempted", not "shell received".
*Disposition:* **accept as-is** (deliberate, delivery is covered by the handshake), and read the Done-when evidence with that qualifier; optionally log only when attached, or add a `snapshotDeferred` entry, if a later epic needs delivery-level evidence.

**F9 — Deferred work is recorded in three places and drifts.** The same item appears in a plan's `deferred` frontmatter, in `deferred-work.md`, and in the epic's `Notes`; DW-4's title is truncated mid-sentence (`deferred-work.md:24`), and the epic Notes still carry epic-1 sourced items (F12/F18/R4/R7 lines 41-45) whose conditions were consumed during this epic without the note being closed. Nothing was lost this time (DW-3/DW-4 still point at their owners), but a reader cannot tell whether an epic-note item landed.
- *Instance:* **defer** — reconcile the epic Notes against the ledger at epic close (F12's condition, "first story publishing a snapshot from the Orchestrator", was met by 2.1; F18's by 2.11; R7's ambient-time item by 2.5; the `ShellSnapshotFactoryTests` item by 2.9, while the `CompositionRoot` item on line 43 is still live — that is F7); fix DW-4's title.
- *Upstream:* one ledger with `status`/`evidence`, and epic Notes that link to ledger ids instead of restating them.

### Process findings

**F10 — The review-depth policy did not hold.** The initiative decided that every `risk = "medium"` ticket gets full lenses instead of `quick` (`initiative-mediastation-mvp.md:58`, previous retro R5/A12). Every plan except 2.2 carries `review: 'quick'` — 10 of 11 tickets — including all four tickets the plans themselves raised to `risk: 'high'` (2.3, 2.4, 2.5, 2.10) and the medium 2.1/2.6/2.7/2.8/2.11; only 2.2 (high) ran `thorough` with four lenses. The evidence that this matters is in this epic's own diffs: the 2.2 thorough pass produced the monotonic-clock fix, the 21-byte over-read fix and the boundary fixtures, while the quick passes concentrated on docs and test-strength and left the invariant hole F6 found. A related traceability drift: the plans raise the risk of five tickets above what `tickets.toml` records (2.3/2.4/2.5/2.10 medium → high, 2.7/2.11 low → medium), so a policy keyed off the ticket tree would have missed them too.
- *Instance:* **fix now** — enforce the policy in the loop's plan template/handoff (refuse `review: quick` for `risk: medium|high`).
- *Upstream:* a policy written into a document but not into the tool that generates plans is advisory only; put the rule where plans are created.

**F11 — Per-ticket diff attribution depends on manual range cuts.** `baseline_revision` made per-ticket ranges derivable and that worked for 10 of 11 tickets. The loop's commit subjects are `Merge bmad-loop/<run>/epic-orchestrator-lifecycle/<ref> into master (bmad-loop)` on a **single-parent** commit, so `git` and `git_evidence.py` report `is_merge: false`; 2.7's build commit `6add563` sits after 2.8's baseline, and 2.9's raw range contains another epic's inception and a real merge (`cf7bd79`, `merge_files: 68` of restated churn). Every one of those needed a human/agent judgement to cut. Sources: `.bmad-loop/runs/*/journal.jsonl`, `git rev-list --parents`, the `2.9` `git_evidence.py` output.
- *Instance:* **defer** — record the close HEAD (or a tag) in the plan when the loop closes a ticket, and stop labelling squashed commits "Merge".
- *Upstream:* a retro should not have to infer ranges from subjects; the loop knows the range when it closes and can persist it.

**F12 — One ticket needed manual recovery after a dev-session timeout.** The 2.7 session timed out, the run deferred the ticket although its plan was already `built`, and `bmad-loop resolve --reverify` then refused the finished tickets-mode run; the ticket was recovered by fast-forwarding `master` to the kept branch and re-running the suite (`story-adopcja-zywych-aplikacji-po-restartie-plan.md`, `## Recovery`; commit `9b01c6d`). The work itself was sound (493/493 at recovery), so the cost was process, not quality.
*Disposition:* **defer** — let the loop reconcile "plan is `built`" with a timed-out session instead of deferring the ticket, and record the recovery in the ticket rather than as a separate commit.

## Behavior verification

Run by this retrospective on the current machine (ALDER, B760 Pro RS/D4, Windows 11 Home), HEAD `bf9bb86671cfe559d13fd7e59d3b9390d8ad958b`, working tree clean:

- `dotnet test MediaStation.slnx -c Release` — **524/524 passed**, 0 failed, 0 skipped.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` — success (Vite shell bundled, contracts generated).
- Published `MediaStation.exe` started with the real `%LOCALAPPDATA%\MediaStation\config` (pid 2716, hwnd 2165358), stayed alive, then `tools/inject-power.ps1` injected three synthetic `PBT_APMRESUMEAUTOMATIC` messages: **`OK: 3 powerSignal, 2 systemResumed (src system, PBT_APMRESUMEAUTOMATIC), 3 snapshotPublished (seq 1..3), 3 powerSignalInjected`**, `stop: exitCode 0`.
- Log `%LOCALAPPDATA%\MediaStation\logs\mediastation-2026-10-22.jsonl`, 15 lines for the run, **0 `warn`/`error`**: `start`, `inputRouting {cmdId 1, mode shell, pointer blocked, epoch 1}`, `powerSignalsRegistered {displayState, windowHook, powerModeChanged: true}`, `snapshotPublished {rev 1, focusRequest {default,1}}`, per injected signal `powerSignal` → `systemResumed` → `snapshotPublished {rev 2/3, seq 2/3}`, second signal inside the 5 s window suppressed, `stop {exitCode 0}`.
- `powercfg /a`: Modern Standby (S0 Low Power Idle) unavailable on this firmware (only S3 and Hibernate), so the real wake remains synthetic evidence plus the Core tests, as the epic allows.
- Ad-hoc probe (temporary test, deleted; suite re-run 524/524 clean afterwards): the F6 two-`Foreground` state — `launch commands: StartApp{netflix}, ScheduleTimer{owner netflix}` then `netflix=Foreground, plex=Foreground` after attach.

**Not exercised end to end:** Home, Close, Restart, AppMenu, `Failed`, adoption and tile switching — they need InputHub/AppMenu and real hosts that arrive in `epic-input`, `epic-home-screen` and the host epics; their evidence is the fake-port and loop tests. Real Modern Standby wake and everything UM760 is the user's whole-epic acceptance.

## Previous-retro follow-through

Previous retrospective: `epic-foundation-contracts/epic-foundation-contracts-retrospective.md`, action items R1–R8 (2026-10-21; verdict raised to `accepted` by the author).

| # | Item | Landed? | Evidence |
|---|------|---------|----------|
| R1 | Re-run the three 1.6 checks on UM760 or accept the dev-machine evidence; fix the 404 expectation | **yes** | `story-refactor-sweep-plan.md:112` records the accepted dev-machine decision; `story-scisa-koperta-i-diagnostyka-hosta-plan.md:98` now expects `httpStatusCode 0` |
| R2 | Correct 1.6 `baseline_revision` to `65665bd` | **yes** | `story-refactor-sweep-plan.md:7` = `65665bdf…` |
| R3 | Harden `LogAppendCheck` (barrier, timeout, keep dir on failure, pad, full line) | **yes** | `tools/MediaStation.LogAppendCheck/Program.cs` (`TimeoutSeconds`, `PadLength`, `WaitForAll` with cancellation); backlog ticket `story-logappendcheck-i-sample-configi.md` done |
| R4 | Cover App-only paths, extract testable helpers to Core where cheap | **partially** | landed for the new code: `CommandRouter`, `OrchestratorLoop`, `MissingHostPort`, log-only ports, `PowerSignalRouter`, `PowerSourcesAttachment`, `LatestSnapshotSlot` (linked into `MediaStation.Core.Tests.csproj:24-38`); `ShellBridge`, `ShellWindow`, `SingleInstance`, `CompositionRoot` still have no test — see F7 |
| R5 | Full lenses for `risk = medium`; canonical HITL location; sample configs in the repo | **partially** | canonical location **landed**: every plan has dated `Current-machine evidence` with HEAD and machine; samples **landed**: `tools/samples/config/{apps,input,settings}.json` used by 2.3's verification; review depth **not landed** — see F10 |
| R6 | `AllowDuplicateProperties = false`; alias guard in `StrictEnumConverter` | **yes** | `ContractJson.cs:41-47`; backlog `bug-duplikaty-kluczy-w-kopercie.md` done; `CoreMessageContractTests.cs:438-442` pins deterministic alias reads |
| R7 | Deferred low findings routed to their epics (modal MessageBox, `OperationCanceled`, ambient time, factory test) | **yes for this epic's share** | ambient time: `BannedSymbols.txt` + `CoreBoundaryTests.Core_references_no_ambient_time_sources` (story 2.5); `ShellSnapshotFactoryTests` now builds a consistent `ConfigResult`; the modal/`OperationCanceled` entries belong to other epics and were not re-checked here (no evidence found either way) |
| R8 | Close epic 1 through `bmad-ticket`; refresh the verdict note | **yes** | `tickets.py` shows `epic-foundation-contracts` status `done`; commit `fb0042f` closes epic 1 and records the follow-ups |

A9/A10 from the earlier round are folded into R1/R5 above and are now both satisfied.

**Nothing there was applied by this retrospective**; the record is the follow-through only.

## Action items

All items are **proposed, not applied**. Remediation and spec reconciliations wait for the human/`bmad-ticket`; the retrospective changed no code, plan, story or status.

| # | Kind | What | Findings | Owner |
|---|------|------|----------|-------|
| A1 | Remediation (fix now) | Guard the `NotRunning` launch path with `AnotherAppIsActive` (or route it into the future switch branch) and add a test that attaches the second window while another app is `Foreground`, asserting one `Foreground` at most | F6 | author |
| A2 | Process (fix now) | Enforce full lenses for `risk = medium|high` where plans are generated (loop template/handoff), so `quick` is only for `low` | F10 | author / loop config |
| A3 | Spec reconciliation (fix now) | Add a deferred entry owned by `epic-windows-audio`: the Orchestrator must retain `rootPid` from `AppWindowAttached` and pass it in window/audio commands (AD-15) | F5 | author / `bmad-ticket` |
| A4 | Remediation (defer) | Split `OrchestratorTests` (1 549 lines) by lifecycle area and extract per-feature handler groups out of the `Orchestrator` switch before `epic-home-screen`/`epic-input` add their arms | F3 | author, at the next test refactor |
| A5 | Tracking (defer) | Add a deferred-work entry for `CompositionRoot` wiring/shutdown order (smoke-tested only), or extract the disposable ordering into a tested Core helper | F7 | author |
| A6 | Process (defer) | Persist each ticket's close HEAD/range in its plan at close, and stop labelling squashed ticket commits "Merge"; reconcile epic `Notes` against `deferred-work.md` at epic close (fix DW-4's truncated title) | F11, F9 | loop tooling / `bmad-ticket` |
| A7 | Process (defer) | Let the loop reconcile a `built` plan with a timed-out dev session instead of deferring the ticket; keep the recovery note in the ticket | F12 | loop tooling |

**Accepted as-is, recorded so later runs stop re-flagging them:** F2 (duplicated `AppEntry → AppType` mapping), F4 (App sources linked into Core tests), F8 (`snapshotPublished` logs a flush attempt, delivery is the `ready` re-send), and the CAP-2 J4 / real-host deviations in F5.

## Acceptance verdict

**Machine verdict: `accepted-with-open-items`.** Criteria: **declared** (the epic file has `Done when`). Headless run, so no human decision was taken; nothing under here overrides the user's whole-epic UM760 acceptance.

| # | Done when | State | Evidence |
|---|-----------|-------|----------|
| 1 | Orchestrator fake-port tests with `FakeTimeProvider` cover the five CAP-2 success scenarios | **met** | 524/524 on `bf9bb86`; `OrchestratorTests.cs` with `FakeTimeProvider(T0)` — `Failed` transitions (`Close_in_failed_*`, `Restart_in_failed_*`, `Close_or_restart_in_failed_is_ignored_*`), Home during `Starting` (`Home_while_app_is_starting_*`, `Late_attached_window_after_home_is_minimized_*`), browser/Moonlight adoption (`Adoption_while_another_app_is_active_*`, `Moonlight_attached_without_start_*`), wake with `focusRequest{default}` (2.8 tests + `OrchestratorLoopTests`), foreground crash with focus on the tile (`Failure_in_foreground_shows_shell_routes_shell_and_focuses_the_tile`) |
| 2 | On the current machine the Orchestrator runs in the composition root, the shell receives snapshots, and the Modern Standby check is best-effort | **met** | `CompositionRoot.cs:130-185` builds loop, router, timers, power adapter; run of the published build logs `inputRouting`, `powerSignalsRegistered`, `snapshotPublished {rev 1..3, focusRequest default/seq 1..3}` and `stop {exitCode 0}`, 0 `warn`/`error`; `tools/inject-power.ps1` OK (3 `powerSignal`, 2 `systemResumed`); `powercfg /a` shows no S0ix, so the synthetic signal plus tests are the allowed evidence |

Why not `accepted`: F6 is a real defect on a transition boundary (fix-now), and five deferred/accept items have owners but no landing. Why not `rejected`: every Done-when has direct evidence, `pending_tickets` is empty, and no finding breaks a declared criterion — F6 needs a UI path that this epic does not ship.

## Open questions

- **Q1 — F6 ownership.** Should the `AnotherAppIsActive` guard on launch ship now (one-line guard + test), or is launch-while-foreground the intended seam of the future "switch between two live apps" branch? The epic Notes delegate switching to a later branch, but not the invariant violation.
- **Q2 — App test project.** R4 landed only where the new code could be extracted; `CompositionRoot`, `ShellBridge`, `ShellWindow` and `SingleInstance` remain untested. Does the boundary survive `epic-deploy-resilience`, which adds supervision and update wiring on the same seam?
- **Q3 — Review depth.** Is the `risk = medium|high` rule to be enforced mechanically (A2), or was the unanimous `quick` in this epic an accepted trade for unattended throughput? The decision changes the cost of every later epic.
- **Q4 — F8 evidence semantics.** Is `snapshotPublished` good enough as the acceptance signal for "the shell receives snapshots", or should the epic's evidence point at the WebView-level handshake?

## Assumptions

Choices made without the user, per headless mode:

- **Epic resolution.** `-H epic-orchestrator-lifecycle` matched the initiative's `epics` entry by slug; the folder came from the first `tickets` row's `epic` field (`tickets.py find 2.1` → `_bmad-output/initiative-mediastation-mvp/tickets/epic-orchestrator-lifecycle`).
- **Completeness.** `status <folder>` reported all 11 tickets `done`, `pending_tickets` empty, none at `built`; no unfinished-ticket acceptance was needed.
- **Verdict.** Rendered on evidence alone as `accepted-with-open-items`; no human override was invented. Criteria are declared, not profiled.
- **Review narrowing.** `bmad-review` is unavailable (missing render entry), so the adversarial / edge-case / verification-gap lenses were run inline over the code diff and weighted to ticket boundaries; the per-story review logs were used as the per-story record instead of re-running them.
- **Proposed items.** A1–A7 and the accepted-as-is list (F2, F4, F8, CAP-2 deviations) are this run's proposals; none was applied, and no status, plan, story or epic file was changed.
- **UM760.** No hardware check was attempted: the epic and AGENTS.md assign it to the user's whole-epic acceptance, and this run only recorded what the current machine can prove.
