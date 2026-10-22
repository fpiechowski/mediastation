---
epic: epic-input
date: 2026-10-22
verdict: accepted-with-open-items
criteria: declared
headless: true
---

# Retrospective: Remote, keyboard & gamepad input

## Epic summary

Epic `epic-input` (CAP-3) delivers the input path: a pure `Core/Input` policy that turns hook, Raw Input, XInput and pointer events into one `Action` per press under the current routing row (AD-8), an `InputHub` adapter on its own thread (`WH_KEYBOARD_LL`, `WH_MOUSE_LL`, `RIDEV_INPUTSINK`, XInput polling, `WM_TIMER`, `ClipCursor`), `--calibrate-input`, the `SendKey` command with its marker, and the S10 acceptance script. All 10 tickets are finished; nothing is at `built`.

| Ref | Ticket | status / state | Plan baseline | Range | Notes |
|-----|--------|----------------|---------------|-------|-------|
| 3.1 | Input policy in Core: one Action and the `long` rule | done / done | `a51da32` | `a51da32..18a1304` | one squashed loop commit; two thorough review passes |
| 3.2 | Tracer bullet: remote on the dongle gives Actions from the Input thread | done / done | `18a1304` | `18a1304..cc7e843` | one commit; thorough review + follow-up, 0 patches in the follow-up |
| 3.3 | `--calibrate-input` and the Keyboard button | done / done | `cc7e843` | `cc7e843..e07b9ba` | one commit; quick review, 3 patches |
| 3.4 | Any keyboard and arrow repeat | done / done | `e07b9ba` | `e07b9ba..7944608` | one commit; `bad_plan` loopback (repeat liveness) then re-derived |
| 3.5 | Routing from the local `SetInputRouting` copy, Actions to the Orchestrator | done / done | `7944608` | `7944608..88a81ea` | one commit; replaced the log-only routing adapter and the action sink |
| 3.6 | Gamepad through XInput and Home from the pad | done / done | `88a81ea` | `88a81ea..31a62e0` | one commit; `Available` snapshot added after review |
| 3.7 | Key injection with the marker | done / done | `31a62e0` | `31a62e0..0aa88e7` | `bad_plan` loopback (`SendInput` on the Orchestrator thread) then re-derived |
| 3.8 | Pointer: mouse hook and `ClipCursor` in `osk` | done / done | `0aa88e7` | `0aa88e7..a760de0` | one commit; `clipHwnd` added to `SetInputRouting` |
| 3.9 | Home always: hung UI and the S10 script | done / done | `a760de0` | `a760de0..6dae28a` | one commit; independent review session + follow-up (3 medium patches) |
| 3.10 | Refactor sweep | done / done | `6dae28a` | `6dae28a..c6e794f` | inferred end = HEAD; no review ran (low risk); token budget exceeded |

- **`pending_tickets`: empty.** `tickets.py status epic-input` reports 10/10 `done`, none at `built` (2026-10-22). The completeness gate for the machine verdict passes.
- **Ranges.** One range per plan, ordered by baseline ancestry. Every baseline is the loop's previous squash commit, each range contains exactly one single-parent commit whose subject reads `Merge bmad-loop/<run>/epic-input/<ref> into master (bmad-loop)`, and `git_evidence.py` reports `is_merge: false` for all of them. No cross-epic commit sits inside any range this time (contrast epic 2's F11), so the only judgement call is the inferred end of the last range, cut at HEAD `c6e794f` because no work landed after it.
- **Churn** (sum of `files` across the ten ranges, each path counted once per commit, 80 paths): **+12,189 / −520**. `src` +4,265/−287, `tests` +4,911/−174, `tools` +997/−54 (the S10 script), plans +2,016. Test-to-production ratio ≈ 1.15:1. No `binary_revisions`, no measured merges (`merge_count: 0`), so the ranking is complete.
- **Top churn:** `InputPolicyTests` +1,168/−71, `InputHub` +963/−41, `check-home-s10.ps1` +943/−52, `InputPolicy` +878/−95, `InputCalibration` +494, `GamepadPolicyTests` +473, `InputCalibrationTests` +463.

### Evidence inventory

| Evidence | State |
|----------|-------|
| Epic file with `Done when` (5 items) | present → criteria **declared** |
| Initiative requirements | `initiative-mediastation-mvp.md` has no Requirements section; CAP-3 read from `spec-mediastation.md:47-55`; AD context from `architecture-mediastation.md` AD-2, AD-8, AD-9, AD-13, AD-14 |
| Entries | `status epic-input` rows + `tickets.toml`; every `description`/`verify` was pulled into its plan |
| Story files | **none** (all rows `file: null`); intent lives in each plan's frozen `<intent-contract>` |
| Plans | 10/10 with `status: done`, `baseline_revision`, `## Review Triage Log`, dated current-machine evidence, `## Plan Change Log` (written in 3.4, 3.6, 3.7, 3.9) and `## Auto Run Result` |
| Diff ranges / commits | 10 ranges, all recorded above; `git_evidence.py` run once per distinct range |
| Previous retrospective | `epic-orchestrator-lifecycle/epic-orchestrator-lifecycle-retrospective.md` (verdict `accepted-with-open-items`) |
| Deferred ledger | `deferred-work.md`: DW-1…DW-16; DW-2, DW-5, DW-8 resolved; the rest open |
| Session logs | dev sessions for all 10 tickets and review sessions for 3.1, 3.2, 3.9 under `.bmad-loop/runs/20261022-134911-800a/`; journal records roles, tokens, loop decisions |
| Missing / narrowed | no UM760 run (deliberate, see Assumptions); transcripts were available but only the journal, plan triage logs and `Plan Change Log` were mined line by line — individual session narratives were not read, so "why a session took a turn" is evidenced from the loop journal, not from transcript prose |

## Findings

### Aggregate views

**F1 — Architecture delta: clean, no layering violation, one seam worth keeping.** `Core/Input` added 12 files (policy, calibration, routing state, queue, sinks, pointer, target). `App/Platform/Input` added 11 files (hub, pumps, clock, timer delay, clip gate, raw-input parsing, routing port, injector). Dependencies point App → Core only; `CoreBoundaryTests` still asserts no App/WPF/WebView2/Win32/ambient-time reference from Core, and `IInputTarget` keeps the hub free of the decision logic. No cycle, no new cross-cutting dependency. The `Ports`↔`Input` cross-reference stays inside one assembly and is not a cycle (also rejected in review, 3.2 triage).
*Disposition:* nothing to fix.

**F2 — Size growth: two files crossed the point where per-ticket reading stops seeing them.** `InputHub.cs` is **815 lines** and now owns message-window creation, both hooks, Raw Input parsing and device-path cache, XInput polling, the deadline timer, clip-cursor state and error reporting. `InputPolicy.cs` is **678 lines** and owns the AD-8 tables, `source` correlation, long-press, repeat scheduling, gamepad mapping and pointer decisions. Each ticket added 150–520 lines to the hub (3.2 +518, 3.6 +182, 3.8 +210) and 50–270 to the policy; no session ever saw the total. Neither class is incoherent yet — each method has one job — but the hub is no longer the "thin adapter that decides nothing" its 3.2 design note describes.
*Disposition:* **defer** — split XInput polling and pointer/clip into collaborators of the hub (and gamepad mapping out of `InputPolicy`) before `epic-osk`/`epic-browserhost` add their handling. Trigger: the next ticket that touches either file for a new source.

**F3 — Duplication map: the two pumps are the same 130 lines twice.** `ActionPump` (120 lines) and `KeyInjectionPump` (143 lines) independently implement "own thread + unbounded queue + `Start` guard + 10 s bounded `Dispose` + swallow-and-report loop"; 3.7 named `ActionPump` as its model but copied the scaffolding instead of extracting it. Their shutdown semantics differ on purpose (`ActionPump` drains, `KeyInjectionPump` abandons), so the duplication is in the thread/queue/guard shell, not the payload.
*Disposition:* **defer** — extract a `BackgroundPump<T>` when the third pump appears (`epic-osk` Unicode `TextSink`, `epic-windows-audio` Alt unlock are both planned on the injection pump).

**F4 — Duplication: the AD-8 table is encoded twice with different meanings.** `InputPolicy.ShouldSwallow` (`:555-567`, keyboard: swallow or pass) and `InputPolicy.GamepadProducesActions` (`:574-581`, pad: emit or not) are two hand-written copies of the same row set. That is deliberate and correct — a pad cannot be swallowed — and both are pinned by row-per-test matrices (`InputPolicyTests`, `GamepadPolicyTests`).
*Disposition:* **accept as-is**, recorded so later runs stop treating it as drift.

**F5 — Pattern divergence: the Core test project now compiles 19 App files.** `MediaStation.Core.Tests.csproj` links production files from `MediaStation.App` — including the pumps, `TeardownSequence`, `UiHang`, `ClipCursorGate`, `InputClock`, `InputTimerDelay`, `RawInputBody/Report`, `InputHubRoutingPort`, `KeyInjectionPump`. This is the initiative's "no App test project" boundary, and it earned its keep again (3.10 pulled teardown order into a tested class; DW-5 is resolved by it). The residual is unchanged from epic 2's F4 and now larger: these files are validated by a second compilation, not by the App build, and the convention is documented nowhere except the csproj comment.
*Disposition:* **accept as-is** for the boundary; propose documenting the convention (A7) so it stops being rediscovered.

**F6 — Spec-to-implementation reconciliation: three documentation gaps are tracked but unowned.** AD-8 does not document `clipHwnd` (DW-12, `architecture-mediastation.md:194` vs the pinned contract); AD-8 and the epic checklist do not describe `press` + N×`repeat` (DW-7); and the checklist promises "one Action / exactly one `action` entry per press" while AD-8's `app`+`moonlight`/`native` row deliberately emits nothing for D-pad and OK (DW-9, `epic-input.md:23,70` vs `architecture-mediastation.md:202`). All three are correct code with wrong docs, all three were routed to "the epic documentation pass", and no ledger entry names an owner or a trigger.
*Disposition:* **fix now** as one spec-reconciliation item (A2), before `epic-osk` — it is the first consumer of `clipHwnd` and of the `osk`/`app` rows.

**F7 — Accepted deviations that later epics must honour.** (a) `Keyboard` is swallowed and emitted as an Action in **every** AD-8 row, including `app`+`web` without `textFocus`, where AD-8 says "inaczej nic"; the gating is delegated to the Orchestrator, which today ignores every non-Home Action (`Orchestrator.cs:220-225`). Behaviour today equals "nic", but `epic-osk` must gate on `textFocus` or a Keyboard press in a browser page will open the OSK. (b) AD-8's note "Gamepad w Moonlight jest przepuszczany w całości" (`:208`) reads as "no Actions", while the table row and the epic decision give the pad's Home combination a `home` Action in Moonlight (`InputPolicy.EmitGamepadHome` is routing-independent). XInput is never blocked, so "przepuszczany" is literally true, but the wording invites the wrong reading.
*Disposition:* **accept as-is** (both are deliberate), record (a) as an `epic-osk` constraint and (b) as a wording fix inside A2.

**F8 — Robustness: a lost key-up freezes a swallowed key for the rest of the process.** `InputPolicy.OnKey` expires a stale `_held` entry only when `!autorepeat.Swallow` (`:195-207`), because the liveness guard was written for repeatable pass-through keys (3.4). A swallowed key — Home above all — whose key-up is lost (secure desktop, UAC, Win+L, hook removal after `LowLevelHooksTimeout`, DW-6) keeps its entry forever, and every later key-down returns `Swallow: true` with no Action. A temporary probe (two tests, deleted; suite re-run 862/862 clean) confirmed it: Home down gives one `home`, no key-up, then Home down 61 s later is swallowed with **zero** Actions, while the pass-through control recovers. AD-8's own "Prevents" list names "zawieszone klawisze", and CAP-3's success says Home always works.
- *Instance:* **fix now** — extend the stale-entry rule to swallowed non-repeat entries (an entry older than `MaxRepeatHoldMs` with a new key-down is treated as a fresh press), or reset policy state when the hook is re-registered; add the probe as a regression test.
- *Upstream:* DW-6 was written as "low, no caller" and never got an owner; a liveness state machine for held keys would have made the swallowed case obvious.

**F9 — Clip cursor is computed only when `epoch` changes.** `ClipCursorGate.Apply` returns `Unchanged` for a repeated epoch (`:54-57`) and `InputHub.SyncClip` only runs on a routing change or a mouse event. If the OSK window moves or resizes inside one `osk` row, or Windows resets `ClipCursor` (activation changes, `LockWorkStation`), the clip stays stale or disappears while the policy keeps swallowing clicks outside the old rectangle. `ClipCursorGate` also accepts a degenerate `GetWindowRect` (minimised/zero-size window) as a valid clip, so every click in `osk` would be swallowed. No sender of `clipHwnd` exists yet.
*Disposition:* **defer** to `epic-osk` (A6), which ships the first `clipHwnd` sender and must also settle the DPI-space question in DW-11.

**F10 — Calibration: releasing one remote button clears the "held" record of another.** `InputCalibration.Release` clears `_heldUsages` wholesale when any remote VK goes up (`:431-442`), so with two remote buttons held, releasing one lets the other's repeated HID report produce a second `rawInput` entry. Rejected as `low` in the 3.3 review for reachability; still true.
*Disposition:* **defer** (A8) — calibration-only, and the fix is a VK→usage map.

**F11 — Config: a repeat delay at or above the liveness guard silently disables repeat.** `ConfigValidation` only requires `> 0` (`:374-377`), while `InputPolicy` ends the repeat grid at `MaxRepeatHoldMs` (60 s), so `repeatDelayMs ≥ 60000` validates and never repeats. Rejected as `low` in the 3.4 review as an unrealistic hand edit.
*Disposition:* **defer** (A9) — one validation rule, `repeatDelayMs < MaxRepeatHoldMs`.

**F12 — A debug switch that blocks the UI ships in the production build.** `MEDIASTATION_DEBUG_HANG_UI` works in every build (epic decision 2026-10-22) and blocks the UI thread for up to `int.MaxValue` ms. It is deliberate, documented, bounded at parse time, and observable as `warn debugHangUi {seconds}`.
*Disposition:* **accept as-is**, recorded so later runs do not re-litigate it.

### Diff-scope review (inline lenses)

`bmad-review` remains unavailable in this project: `render_skill.py` halts with `render entry is missing: .claude/skills/bmad-review/workflow.md` (the skill also reports its `bmod-core-tools` module record as not installed). The adversarial, edge-case and verification-gap lenses were therefore run **inline** by this retrospective over the code diff (`a51da32..c6e794f`, `src` + `tests` + `tools`, +10,173 lines), weighted to ticket boundaries. **Narrowing recorded:** the per-ticket `Review Triage Log`s were read as the per-story record rather than re-running 10 review passes; the `web/**` bundle and the unmodified parts of plan docs were not lens-reviewed.

Results of the inline pass beyond F8–F11: no new defect found in `InputPolicy`'s timer arithmetic (deadline, missed-grid skip, `MaxRepeatHoldMs`), the gamepad baseline/unavailable state machine, the marker rule's position before `_held`/`_kinds`, `RawInputBody` bounds checks, `InputTimerDelay` clamping, the routing port's epoch rule, or `TeardownSequence` ordering. The per-story logs show the same pattern: the three independent review sessions (3.1, 3.2, 3.9) found the medium issues of this epic (the over-strict `DevicePath` probe, `Dispose` without a bound, the `SendInput`-on-the-Orchestrator-thread `bad_plan`, the S10 foreground/`dwm` ghost check), while the self-reviewed tickets found mostly `low` items.

### Process findings

**F13 — Independent review covered 3 of 10 tickets, and lens depth ignored the medium rule again.** The journal records `role=review` sessions only for 3.1, 3.2 and 3.9; the other seven tickets wrote their `## Review Triage Log` inside the dev session that wrote the code, and **3.10 ran no review at all** (`review-skipped`, low risk) although it rewrote 211 lines of `check-home-s10.ps1` and added `TeardownSequence`. Lens depth: both `high` tickets ran `thorough`, but **all six `medium` plans say `review: 'quick'`** (`3.3, 3.5, 3.6, 3.7, 3.8, 3.9`), against the initiative decision that `medium` gets full lenses (`initiative-mediastation-mvp.md:58`, epic-2 retro A2). Traceability drift recurs: 3.7 is `low` in `tickets.toml` and `medium` in its plan, so a policy keyed off the tree would skip it while the plan's own risk demanded lenses.
- *Instance:* **fix now** — enforce "`risk: medium|high` ⇒ full lenses" and "no review skip for diffs over N lines" where plans are generated (A3).
- *Upstream:* the same lesson as epic 2's F10: a policy in a document is advisory until the plan generator refuses the deviation.

**F14 — The deferred ledger duplicated itself twice and carries annotations instead of resolutions.** 3.10's review harvest created **DW-15** and its spec-deferral harvest created **DW-16** for the same `GW_OWNER` issue, and DW-16's own reason says "wpis w ledgerze jako DW-15". DW-7 and DW-9 are two entries for one class of documentation gap. DW-13 and DW-14 were not resolved by 3.10 but were appended with a "Nota 3.10" paragraph, so the `reason` now mixes the original finding with a remediation log. Nothing was lost; a reader cannot tell which entry is canonical.
*Disposition:* **fix now** (A4) — dedupe at harvest, keep one entry per issue, and put resolution notes in `evidence` with `status: resolved` (the pattern DW-2/DW-5/DW-8 already use).

**F15 — The previous epic's action items expired as one prose line.** Epic 2's retro items A2–A7 were recorded in `epic-orchestrator-lifecycle.md:77` as a single `Deferred (retro epiki 2)` note and never became ledger entries with owners. Four of them silently failed to land this epic (A2, A3, A4, A6 — see follow-through), which is exactly the drift epic 2's F9 predicted.
*Disposition:* **fix now** (A4, same item) — epic Notes must carry ledger ids, not restated prose.

**F16 — The refactor sweep is unbounded and expensive.** 3.10's scope was "deferred findings from stories 1–9" with no list, it consumed 20.5 M tokens (2.28 M weighted against the 2 M budget, `token-budget-exceeded` in the journal) and still expanded into tooling (`check-home-s10.ps1` +159 net) that then needed its own review — which was skipped.
*Disposition:* **defer** (A10) — scope refactor sweeps by ledger ids and split when the list exceeds one session's budget.

**F17 — Per-ticket attribution held this time, but epic 2's A6 is still not landed.** All ten baselines are loop squash commits, one commit per range, no other epic's work inside a range, so no manual cut was needed (contrast epic 2 F11). The underlying tooling gap is unchanged: subjects still say `Merge …` for single-parent squashes, and no plan records the commit it closed at, so the next epic that interleaves with another will need the same manual judgement.
*Disposition:* **defer** (A5) — persist the close HEAD in the plan at close; stop labelling squashes `Merge`.

## Behavior verification

Run by this retrospective on the current machine (ALDER, B760 Pro RS/D4, Windows 11 Home), HEAD `c6e794f0796bdd1bef717d5dafe92f05245f6548`, working tree clean before and after (the probe was removed and the suite re-run):

- `dotnet test MediaStation.slnx -c Release` — **862/862 passed**, 0 failed, 0 skipped (matching the loop's final verification).
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` — success (contracts, `tsc` and Vite bundled).
- Published `MediaStation.exe` with the real `%LOCALAPPDATA%\MediaStation\config` (pid 31692): alive after 10 s, `CloseMainWindow` → exit **0**. Log `mediastation-2026-10-22.jsonl`: `start {version c6e794f}` → `inputRouting {cmdId 1, mode shell, pointer blocked, epoch 1}` → `powerSignalsRegistered` → `snapshotPublished {rev 1}` → `stop {exitCode 0}`; **zero** `inputHubError`, `warn` or `error`, i.e. both low-level hooks, Raw Input and the XInput timer registered on this machine.
- `MediaStation.exe --calibrate-input` (pid 47520): started with `args: ["--calibrate-input"]`, exited **0 after 60.1 s** with `stop`, zero `rawInput` (no remote attached), zero `warn`/`error`, no `modeUnsupported`.
- `MEDIASTATION_DEBUG_HANG_UI=5`: `warn debugHangUi {seconds 5}` after `snapshotPublished`, process survived the block, `CloseMainWindow` → exit **0**. `Process.Responding` read `True` at +3 s, consistent with the caveat the 3.9 plan documents (a 5 s block ends with the 5 s `WM_NULL` probe; the `False` observation came from a 20 s block).
- `powershell -File tools/check-home-s10.ps1 -SelfTest` — **Self-test passed**, exit 0: missing/correct/double/outside fixtures give `fail/pass/fail/pass`, and the new pure checks pass (two windows of one pid → two ids, tooltip → none, same handle → foreground, other window of the same process → not foreground, `dwm` ghost passes only for `shell-hung`, `explorer` `Progman`/`WorkerW` only for `desktop`).
- Ad-hoc probe (two temporary tests, deleted; full suite re-run 862/862 clean afterwards): F8 — Home down → lost key-up → Home down 61 s later is swallowed with zero Actions, while the pass-through control recovers.

**Not exercised end to end:** any physical remote, keyboard or gamepad press; Home over Edge, Moonlight, the desktop and a hung shell; S10 16/16; choosing the Keyboard button in `--calibrate-input`; a real `SendInput` returning through the hook; OSK clip; D-pad/OK pass-through in Moonlight. On ALDER, injected input does not reach the low-level hook (established in 3.2 and unchanged), and no pad or remote is attached, so these stay with the user's whole-epic UM760 acceptance. The epic's mechanisms are covered by the 862 tests and the runs above; its hardware observations are not.

## Previous-retro follow-through

Previous retrospective: `epic-orchestrator-lifecycle/epic-orchestrator-lifecycle-retrospective.md` (2026-10-22, `accepted-with-open-items`), action items A1–A7.

| # | Item | Landed? | Evidence |
|---|------|---------|----------|
| A1 | Guard the `NotRunning` launch path with `AnotherAppIsActive` + a test | **yes** | `Orchestrator.cs:174` (`OnLaunchApp` rejects when another app is active), doc comment at `:169`; commit `21633a2` "fix(orchestrator): reject LaunchApp while another app is active"; `_bmad-output/backlog/bug-launch-przy-aplikacji-na-pierwszym-planie-daje-dwa-foreground.md` is `done` |
| A2 | Enforce full lenses for `risk = medium\|high` where plans are generated | **no** | all seven `medium` plans carry `review: 'quick'`; 3.10 ran no review; see F13 |
| A3 | Deferred entry owned by `epic-windows-audio`: Orchestrator retains `rootPid` (AD-15) | **partially** | recorded only as prose in `epic-orchestrator-lifecycle.md:77`; no DW entry names `rootPid` in `deferred-work.md` (grep, 2026-10-22), so there is no owner or status — the item is open and untracked |
| A4 | Split `OrchestratorTests` and extract per-feature handler groups before `epic-home-screen`/`epic-input` add arms | **no** | `OrchestratorTests.cs` is 1,553 lines (was 1,549); the dispatch switch still has 12 arms (`Orchestrator.cs:71-84`) — `epic-input` added a sink and a command, not an arm, so the split did not become urgent, but it did not happen |
| A5 | Deferred entry for `CompositionRoot` wiring/shutdown order, or extract it into a tested Core helper | **yes** | `TeardownSequence.cs` + `TeardownSequenceTests.cs` (3.10); DW-5 is `resolved` with evidence (`deferred-work.md:39`) |
| A6 | Persist each ticket's close HEAD/range in its plan at close, stop labelling squashes `Merge`, reconcile epic Notes with the ledger | **no** | no plan records a close commit; all ten subjects still read `Merge bmad-loop/…`; the ledger now duplicates itself (F14) and epic-2 Notes remain prose (F15) |
| A7 | Let the loop reconcile a `built` plan with a timed-out dev session; keep the recovery note in the ticket | **no evidence found** | no dev session timed out in this run; 3.9's follow-up was a review cycle, not a recovery. Neither landed nor contradicted — not exercised |

Epic 2's accepted-as-is list still holds (F2 `AppEntry → AppType` duplication unchanged at `CompositionRoot.cs:335-341` vs `Orchestrator.cs:675-681`; F8 `snapshotPublished` semantics unchanged). Its F3 trigger — "extract per-feature handler groups before `epic-home-screen`/`epic-input` add their arms" — fired with this epic and was not acted on (A4), which is itself evidence that triggers in a retro do not execute themselves.

## Action items

All items are **proposed, not applied**. This retrospective changed no code, plan, story, status or epic file.

| # | Kind | What | Findings | Owner |
|---|------|------|----------|-------|
| A1 | Remediation (fix now) | Expire a stale `_held` entry for swallowed keys too (Home/Back/OK/Keyboard): a key-down more than `MaxRepeatHoldMs` after an entry's `DownTimeMs` starts a fresh press, or reset policy state on hook re-registration; add the lost-key-up probe as a regression test | F8 | author |
| A2 | Spec reconciliation (fix now) | One documentation pass before `epic-osk`: add `clipHwnd` to AD-8 (`:194`, DW-12), the `repeat` rule (`:189,191`, DW-7), the deliberate pass-through of D-pad/OK in `app`+`moonlight`/`native` to the epic checklist (`epic-input.md:23,70`, DW-9), and the "gamepad in Moonlight" wording (`:208`, F7b); record that `Keyboard` gating by `textFocus` belongs to `epic-osk` (F7a) | F6, F7 | author / `bmad-ticket` |
| A3 | Process (fix now) | Enforce review depth where plans are generated: full lenses for `risk: medium\|high`, and no `review-skipped` for a diff above a size threshold (3.10) | F13 | author / loop config |
| A4 | Process (fix now) | Dedupe the deferred ledger at harvest (one entry per issue; resolutions in `evidence`), give epic 2's parked A2–A7 real DW entries with owners, and make epic Notes carry ledger ids instead of prose | F14, F15 | loop tooling / `bmad-ticket` |
| A5 | Process (defer) | Persist each ticket's close HEAD/range in its plan at close and stop labelling single-parent squashes `Merge` | F17 | loop tooling |
| A6 | Remediation (defer, `epic-osk`) | Re-measure the clip on OSK window move/resize and on activation change, reject a degenerate `GetWindowRect`, and settle the DPI space together with DW-11 | F9 | author, in `epic-osk` |
| A7 | Convention (defer) | Document the "App sources linked into `MediaStation.Core.Tests`" pattern (19 files) in the architecture as a project convention, replacing the csproj-only comment | F5 | author |
| A8 | Remediation (defer) | Key calibration's held-usage dedupe by VK→usage instead of clearing all usages on any remote key-up | F10 | author, next time calibration is touched |
| A9 | Validation (defer) | Reject `timing.repeatDelayMs` (and `repeatDelayMs + repeatIntervalMs`) at or above `MaxRepeatHoldMs`, so a config cannot silently disable repeat | F11 | author |
| A10 | Process (defer) | Scope refactor sweeps by ledger ids, split when the list exceeds one session's token budget, and keep tooling rewrites out of a sweep unless a ledger entry asks for them | F16 | loop tooling / PO |
| A11 | Structure (defer) | Split `InputHub` (XInput polling, pointer/clip) and, if it grows again, `InputPolicy` (gamepad mapping) before `epic-osk`/`epic-browserhost` extend them; extract a shared background-pump helper at the third pump | F2, F3 | author |

**Accepted as-is, recorded so later runs stop re-flagging them:** F4 (the AD-8 table in two forms), F5 (App sources linked into Core tests), F7 (Keyboard emitted in every row and gated by the Orchestrator; gamepad-in-Moonlight wording), F12 (debug UI hang in production builds).

## Acceptance verdict

**Machine verdict: `accepted-with-open-items`.** Criteria: **declared** (the epic file has five `Done when` items). Headless run: no human decision was taken, and this verdict is **not** the user's UM760 acceptance — it records what the current machine can prove and names everything that remains.

| # | Done when | State | Evidence |
|---|-----------|-------|----------|
| 1 | One press from remote, keyboard or gamepad gives one Action (AD-8) | **met in the declared rows; one reachable exception** | `InputPolicyTests` (swallow/kind per row, autorepeat gives no second Action, unknown VK no Action, epoch and mid-hold mode switches), `GamepadPolicyTests` (edge per button, Home combo once, reconnect baseline), `InputCalibrationTests`; the exception is F8 after a lost key-up (A1) |
| 2 | A key with `long` (OK in `shell` and `osk`) gives `press` on key-up before the threshold or only `long` after it, from Story 1 | **met** | `InputPolicyTests` on `FakeTimeProvider` (configured 450 ms threshold, press on key-up, `long` after threshold, late timer, `long` in `osk`, `press` in `app`); `InputPolicy.cs:220-256` |
| 3 | On UM760 Home is swallowed and delivered as an Action over Edge, Moonlight, the desktop and a hung shell; S10 with the dongle remote gives 16/16 | **mechanism met; hardware observation open** | `ShouldSwallow(Home)` is unconditional (`InputPolicy.cs:557-560`); `tools/check-home-s10.ps1` ships the 4 targets × 4 presses run plus the "shell on top" pass, and `-SelfTest` passes here (16/16 logic, foreground/degenerate checks); the 16/16 observation needs UM760 + remote — user's acceptance (DW-13/14/15) |
| 4 | Logs contain only Actions; raw codes only in `--calibrate-input` | **met** | `LogActionSinkTests` (no `vk`/`scan`/`usage`, `inputHubError` without key codes), `LogRawInputSinkTests`, calibration's `remote`-signature filter; the published run's log has `action`-free input lines and no `inputHubError` |
| 5 | The remote button chosen in `--calibrate-input` gives Action `Keyboard`, and its mapping is in `input.json` | **mechanism met; button choice open** | `InputCalibration` entry shape (`hasVk`), `StartDecisionTests`/`ExitCodeTests`, mapping test `"0x.." : "keyboard"` → `action {kind keyboard}`, samples carry the mapping; the actual G20S Pro button can only be chosen with the remote in hand — user's acceptance |

Why not `accepted`: F8 is a real defect reachable from a Windows-side lost key-up, three Done-when observations (3, 5 and the F8 exception) are not demonstrable on this machine, and eight open ledger items have owners but no landing. Why not `rejected`: every declared row has passing tests and the published build runs clean with a clean shutdown; `pending_tickets` is empty; F8's state needs a lost key-up that the epic already tracks (DW-6) and the fix is local; and the unverified items are hardware observations that this project explicitly reserves to the user's whole-epic acceptance, not code the epic failed to deliver.

## Open questions

- **Q1 — F8 severity.** Should the swallowed-key freeze be fixed now as a small liveness change (A1), or is it acceptable to wait for the hook watchdog that DW-6 defers to? Home is the one key whose failure is user-visible; the fix needs a rule for a genuinely held key at the 60 s mark.
- **Q2 — Review independence.** Independent review ran for three tickets. Is dev-session self-review the intended flow for the rest, or a configuration accident? The epic's medium findings came from the independent sessions, which is weak but consistent evidence that it matters.
- **Q3 — `Keyboard` gating.** AD-8's `app`+`web` row says "inaczej nic", the hub always emits; the Orchestrator currently ignores non-Home Actions. Which component owns the `textFocus` gate in `epic-osk` — the hub (needs page state it does not have) or the Orchestrator (has the Intents)? A2 records the constraint; the choice is open.
- **Q4 — Calibration discovery.** `--calibrate-input` filters by the signatures already in `input.json`, so a replacement remote with a new VID/PID produces no output at all. Is manual signature discovery (from `spike/`) acceptable, or should calibration fall back to logging unknown HID devices?
- **Q5 — Clip lifetime.** Is epoch-only clip recalculation (F9) sufficient for `epic-osk`'s fixed, non-activating OSK window, or should `epic-osk` own a re-measure on move/resize and on activation change?

## Assumptions

Choices made without the user, per headless mode:

- **Epic resolution.** `-H epic-input` matched the initiative's `epics` entry by slug (`tickets.py status`), and the folder came from the tree: `_bmad-output/initiative-mediastation-mvp/tickets/epic-input`.
- **Completeness.** `status epic-input` reported all 10 tickets `done`, `pending_tickets` empty, none at `built`; no unfinished-ticket acceptance was needed and the machine verdict was not forced to `rejected`.
- **Verdict.** Rendered on evidence alone as `accepted-with-open-items`; no human override was invented. Criteria are declared, not profiled. Done-when 3 and 5 are recorded as mechanism-met with the hardware observation open, per AGENTS.md (no parking for UM760 checks); this is not the user's acceptance.
- **Ranges.** The ten plan baselines were used as recorded, ordered by ancestry; the last range was cut at HEAD `c6e794f` as inferred. No commit needed manual cutting.
- **Review narrowing.** `bmad-review` is unavailable (missing render entry, module record not installed), so the adversarial/edge-case/verification-gap lenses ran inline over the code diff and were weighted to ticket boundaries; the per-story review logs were read as the per-story record instead of re-running ten passes.
- **Process evidence.** Session logs exist for all sessions, but only the loop journal, plan triage logs and `Plan Change Log` sections were mined; transcript narratives were not read, so the process findings rest on the journal's recorded roles, review cycles, token counters and skip decisions.
- **Probe.** The F8 probe was a temporary test file, deleted after the run; the suite was re-run 862/862 and `git status` was clean, so the tree carries no retrospective change.
- **Proposed items.** A1–A11 and the accepted-as-is list are this run's proposals; none was applied, and no status, plan, story or epic file was changed.
- **UM760.** No hardware check was attempted: the epic and AGENTS.md assign those to the user's whole-epic acceptance, and the run recorded only what ALDER can prove (no remote, pad or injected input reaches the hook here).
