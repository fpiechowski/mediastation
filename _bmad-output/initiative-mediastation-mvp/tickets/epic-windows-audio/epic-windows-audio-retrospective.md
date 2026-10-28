---
epic: epic-windows-audio
date: 2026-10-27
verdict: rejected
criteria: declared
headless: true
---

# Retrospective: epic-windows-audio (Windows & audio focus)

Machine: **UM760** (`MEDIASTATION` / `F7BSI` / `EliteMini Series`, identified with the AGENTS.md command). All evidence below was gathered on UM760.

## Epic summary

Epic file: `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio.md` (Requirements R1–R8, Done when 1–4: declared). `pending_tickets`: none. No ticket is left at `built`.

| Ticket | Title | Status / state | Range (plan `baseline_revision` → end) | Commit |
|---|---|---|---|---|
| 5.1 | Tracer bullet: Home przywraca shell nad oknem Edge z dev harnessu | done / done | `d2fbb94..9a9ac4b` | `9a9ac4b` (21 files) |
| 5.2 | AudioRouter: wyciszenie drzewa procesów od rootPid i nowe sesje audio | done / done | `9a9ac4b..98b63af` | `98b63af` (38 files) |
| 5.3 | Sekwencja AD-5 na wszystkich ścieżkach Orchestratora | done / done | `98b63af..b955fc4` | `b955fc4` (22 files) |
| 5.4 | ForegroundChanged z WinEvent i właściciel okna w logu | done / done | `b955fc4..7861077` | `7861077` (14 files) |
| 5.5 | Edge i Chrome w trybie okna z 6.1 i Moonlight borderless: przywrócenie shella 16/16 | done / done | `f38fe45..793eb0f` | `793eb0f` (14 files) |
| 5.6 | Jeden właściciel okien: reguła buildu poza Platform/Window | done / done | `793eb0f..fcb4a3f` | `fcb4a3f` (5 files) |
| 5.7 | Refactor sweep | done / done | `fcb4a3f..2730972` | `2730972` (2 files) |

Range notes: each plan has one commit, so the plan baselines give exact ranges. The 5.4 → 5.5 gap (`7861077..f38fe45`) holds spike 6.1 (`2ea151f`, epic-browserhost), the browserhost retro and docs commits; it is not epic work and is excluded. 5.5's baseline is `f38fe45` (after the gap), not `7861077`. Whole epic: `d2fbb94..2730972`, 66 files, +5882/−216 across `src tests tools web`.

Evidence inventory:
- Available: epic file, `tickets.toml`, 7 plans (frontmatter, intent contracts, Implementation Notes, Review Triage Logs, Verification), `git_evidence.py` output per range, `deferred-work.md`, the previous retro (`epic-home-screen-retrospective.md`), live build/test results from this run.
- Missing: story files (all tickets were built from plans; no pulled story file is separate from the plans), session logs (no per-ticket conversation records in the tree). Process-lesson analysis is therefore limited to what the plans record. No UM760 log of a live 16/16 run exists in the tree; that belongs to the user's whole-epic acceptance.

## Findings

Disposition key: fix now / defer / accept.

### Spec-to-implementation reconciliation

**F1 — Home while the shell is already the mode sends no window commands (medium, defer to a new story).** The orchestrator logs the action but does not raise the shell, so an app window that came to the front outside MediaStation (Alt+Tab, script) stays on top. Source: ledger `DW-41` (`_bmad-output/initiative-mediastation-mvp/deferred-work.md:349`), 5.5 plan line 88 ("Home in shell mode is a no-op"), 5.5 plan frontmatter `deferred`. This conflicts with the epic Note "powrót daje Home" and with R3's wording; it is not covered by R2, which only speaks of Home from an app. Effect measured on UM760 during 5.5: presses 2–4 of `-Restore` failed 3 of 4 until the script resumed the app through the product with OK on the tile. The 5.5 harness adapted the scenario instead (resume via OK), so the script's 4/4 per target does not exercise "Home over an externally raised window". Disposition: defer. Fix is a behavior change in the Orchestrator and needs a decision on AD-8 ("mode does not change when the user switches window") against "return gives Home"; the sweep rightly excluded it (`story-refactor-sweep-plan.md` closed list, 5.5 row). Prevention: when a ticket's verify script needs a workaround to reach the next press, record the gap as an epic-level open question at plan time rather than at review.

**F2 — Ticket 5.5 promises 16/16 including Moonlight borderless; the delivered evidence is Edge 4/4, Chrome 4/4, desktop 4/4, Moonlight not applicable (low, accept).** Source: 5.5 plan Verification (line 114: "moonlight n/a (variable not set, no host)") and Implementation Notes (line 89: "16/16 is checked at the epic acceptance"). This follows AGENTS.md (hardware-dependent checks are not-applicable per ticket, the user accepts the whole epic on UM760), so no defect. Consequence for Done when 2: the Moonlight 4/4 is entirely unverified by automated evidence, and the dev harness path (`HarnessMoonlight.cs`, `HarnessLaunch.cs`) was only exercised with unit tests (`DevHarnessTests`, 88 lines added). Disposition: accept; carry into the UM760 checks.

**F3 — The 5.7 sweep plan cites the wrong story for the DW-37 resolution (low, fix now, docs only).** `story-refactor-sweep-plan.md` closed-list row "5.2 deferred" says it was settled in 5.4 (adoption `MuteProcessTree` registers muted sessions in the ledger); the ledger entry `DW-37` (`deferred-work.md:316-323`, resolution) names 5.3 (`b955fc4`), and `b955fc4` is the commit that touches `AudioRouterEngine.cs` (+32) and `Orchestrator.Attach.cs`; the 5.4 diff (`b955fc4..7861077`) touches neither for audio. The outcome (resolved) is the same; only the citation is wrong. Disposition: fix now (PO, reference text); no behavior impact.

### Aggregate views

**F4 — Window-ownership rule works as built (positive, accept).** `src/MediaStation.App/BannedSymbols.txt` (18 rules from 5.6, `fcb4a3f`) bans the Win32 and WPF calls that change visibility, Z-order or foreground, scoped by `Platform/Window/.editorconfig`. A grep over `src` for `ShowWindow|SetForegroundWindow|BringWindowToTop|.Activate()|Topmost|SW_HIDE|SetWindowPos` outside `Platform/Window` finds only a comment in `AppCommands.cs:41` and the single documented exception comment at `CompositionRoot.cs:219` (first show of the shell, matches the epic Assumption of 2026-10-26). Debug build: 0 warnings, 0 errors. R4 holds in the tree.

**F5 — `Platform/Audio` and `Platform/Window` are split into a testable engine and a thin Win32 port (positive, accept).** Decision logic lives in `AudioRouterEngine.cs` (371 lines), `ProcessTreeMatcher.cs`, `AudioRule.cs`, `AudioSweepSchedule.cs`, `WindowCommandExecutor.cs`, `WindowTreePicker.cs`, `ForegroundOwnerResolver.cs`; Win32/COM calls sit in `CoreAudioSessionBackend.cs` (364), `Win32WindowApi.cs`, `Win32ForegroundLookup.cs`. This matches the Consistency Conventions in the epic file. Test volume added: `AudioRouterEngineTests.cs` 492+80 lines, `WindowCommandExecutorTests.cs` 327, `ForegroundOwnerTests.cs` 136, `DevHarnessTests.cs` 122+88.

**F6 — `DevHarnessHostPort.cs` is the largest file the epic leaves (453 lines) (low, accept).** Source: `git_evidence` for `f38fe45..793eb0f` (+453); it replaced `DevHarnessWebHostPort.cs` (−419, which 5.1 added at +419). The epic Notes say the harness is replaced per type by the real hosts of epic-browserhost and epic-moonlight-native, so the size is temporary by design. Disposition: accept; revisit only if the harness outlives those epics.

**F7 — Duplicated window pickers between harness and script (low, defer).** `tools/check-home-s10.ps1` (+678 in 5.5) re-implements window selection that `WindowTreePicker.cs` also implements (5.5 plan Always: both are pure, each tested by `-SelfTest` / `DevHarnessTests`). Source: 5.5 plan "wybór okna / wybór celu skryptu" in Always, and diff of `tools/check-home-s10.ps1`. `DW-13`, `DW-14`, `DW-15` and `DW-32` (same script, Edge target and owner-window selection) stay open in the ledger. Not verified for a real divergence, so this is a structure note only. Disposition: defer to the next story that edits the script.

### Lens findings (bmad-review pass on the epic range, narrowed)

The epic has 66 changed files; `bmad-review` was not invoked in this unattended run. Scope was narrowed to what the seven Review Triage Logs already recorded plus the checks above. Recorded as "checked through the plans' reviews only", not "re-reviewed at epic level".

- Review depth: all seven plans carry `review: thorough`, `review_source: pinned`, including 5.6 and 5.7 (low risk). The medium/high findings were patched in-ticket: 5.1 nine medium patches (thread death of WindowManager, StopApp kill, ProbeExisting by command line, still-iconic reported as completed), 5.2 three medium patches (dead root pruned with muted sessions, failed Unmute never retried, one failing device failing enumeration), 5.5 one medium patch (untested dispatch → `HarnessLaunch`), 5.7 one medium patch (baseline without `focusRequest.seq`). Source: Review Triage Log of each plan. No high-severity finding in any ticket.
- Boundary between tickets: 5.2 deferred "sessions muted by a killed MediaStation stay muted"; 5.3 fixed it (ledger registration in `AudioRouterEngine.Mute`, DW-37 `done`). The audio mute/unmute pair and the window `minimize`/`restore` pair now meet in the Orchestrator sequence of 5.3 (`Orchestrator.Failures.cs`, `Orchestrator.Attach.cs`, `Orchestrator.Stop.cs`) with `OrchestratorFailureTests.cs` +135. No cross-ticket defect found beyond F1.
- Residual accepted rejects with reasons in the plans: `Restore` ignoring the bool from `TrySetMute` (5.2), `Win32AudioRouterPort` loop untested (5.2; exercised live on UM760 with Home/OK/kill, per plan), Moonlight command-line matching (5.5, path unexercised).

### Process lessons

**F8 — The tracer bullet was large (low, accept).** 5.1 changed 21 files and about 1.5k lines (`Win32WindowManagerPort.cs` 214, `DevHarnessWebHostPort.cs` 419, executor and tests) and its review produced about 60 findings with 9 medium patches (5.1 plan Review Triage Log, line 89). It still shipped as one commit with every medium patched, so the cost was review noise, not escaped defects. Prevention: none needed; the epic Note says the Audio layer was deliberately kept out of the tracer.

**F9 — Plans still record only a start revision (low, defer, repeat).** Each plan has `baseline_revision` and the commit subject is generic ("story 5.x: implemented and reviewed via bmad-loop"). Ranges were derivable here only because every story is exactly one commit. Same lesson as previous retro A5.

## Behavior verification

Run on UM760 during this retro:
- `dotnet build MediaStation.slnx -c Debug`: succeeded, 0 warnings, 0 errors.
- `dotnet test MediaStation.slnx`: 1191 passed, 0 failed, 0 skipped (matches the 1191/1191 in the 5.5 plan).
- `npm test` in `web/`: 118 passed (10 files).
- `powershell tools/inject-power.ps1 -SelfTest`: `OK: 9 SelfTest cases.` (DW-34 fix from 5.7).

Not exercised in this run: a live Home press over Edge, Chrome, Moonlight and the desktop, and audible switching. The headless retro session has no operator at the TV and starting browsers and injecting keys would disturb the machine. Evidence of live runs from the tickets (reported by the plans, not repeated here): 5.2 live on UM760 with injected Home/OK/Home/OK and a kill of the audio service gave `audioRuleApplied` false/true/false/true and `audioSessionMuted` after the kill; 5.5 `-Restore -InjectHome` gave edge 4/4, chrome 4/4, desktop 4/4, Moonlight n/a. Done when 2 (16/16) and 4 (audible Netflix ↔ Moonlight, new session on episode change) are the user's whole-epic checks.

## Previous-retro follow-through

Previous retro: `epic-home-screen-retrospective.md` (its Action items A1–A4).

| Item | Owner | Landed? | Source |
|---|---|---|---|
| A1 enforce `review: thorough` for medium/high | loop config / PO | yes | all seven plans of this epic have `review: thorough`, `review_source: pinned` (frontmatter) |
| A2 close DW-19 and DW-28 with evidence | PO | yes | `deferred-work.md:157` and `:241` (`status: done 2026-10-26`) |
| A3 one boundary review of the shell bridge/window and `tokens.css` before the next web epic (DW-35) | author | not due | `git diff --stat d2fbb94..2730972 -- src/MediaStation.App/Shell web/shell/src/tokens.css` is empty; `DW-35` still open |
| A4 user's whole-epic acceptance of epic-home-screen on UM760 | user | yes | commits `56265ec` (run recorded) and `342c404` (epic closed) |

## Action items

All are proposals; none was applied.

| ID | Type | Item | Source | Owner |
|---|---|---|---|---|
| A1 | Remediation (defer) | Decide and implement Home while the shell is the mode (raise the shell without changing `mode`, or record the exact rule in AD-8), then extend `check-home-s10.ps1` to press Home over an externally raised window; ledger `DW-41` | F1 | PO (new story) / Winston for the AD-8 call |
| A2 | Acceptance (user) | Run the whole-epic UM760 acceptance through `fp-acceptance` (checks below); the Moonlight 4/4 needs `MEDIASTATION_HARNESS_MOONLIGHT` and a reachable host | F2, Behavior | user |
| A3 | Spec reconciliation | Fix the 5.7 sweep plan's citation of the DW-37 resolution: 5.3 (`b955fc4`), not 5.4 | F3 | PO (`bmad-agent-po`) |
| A4 | Tracking | Fold the `fp-acceptance` plan scenarios for `DW-33` (tile states `Starting`/`Foreground`/`Background`, the "w tle" dot, focus after return, AppMenu from long OK on `Background`, `RestartApp` vs `CloseApp` in the log) into the acceptance plan, as the epic Notes promise | epic Notes (Deferred, DW-33) | `fp-acceptance` / user |
| A5 | Process (defer) | Persist the close range (end revision) in each plan so retro ranges do not depend on one commit per story | F9 | loop tooling |

## Decision records

None written (headless run only proposes). No decision passes the test: the choices that are hard to reverse and surprising without context are already recorded in architecture AD-1, AD-5, AD-8, AD-13, AD-14 and AD-15 (the 2026-10-27 update for spike 6.1 is in the epic Notes), and the remaining plan decisions (harness env var `MEDIASTATION_DEV_HARNESS`, single-exception `CompositionRoot` show, per-device catch in audio enumeration) are local and easy to reverse.

## Acceptance verdict
**rejected** (updated after the 2026-10-28 UM760 acceptance; ticket 5.15 remains `in-progress`, and required runtime scenarios failed).

Evidence per criterion:
- Done when 1 (minimize, never `SW_HIDE`): met in code and tests. `BannedSymbols.txt` bans the hiding path outside `Platform/Window`; `WindowCommandExecutorTests.cs` and the 5.5 live run (Edge/Chrome `IsIconic` and visible) support it.
- Done when 3 (`ForegroundChanged` from a WinEvent, host never raises itself): met. `WinEventForegroundHook.cs`, `ForegroundOwnerResolver.cs`, `ForegroundOwnerTests.cs`, `OrchestratorLoopTests.cs`; ownership rule enforced by the build (F4).
- R5–R7 (audio tree mute, new session within about 1 s, AD-5 sequence): met by `AudioRouterEngineTests.cs`, `OrchestratorFailureTests.cs`, and the live Home/OK/kill run recorded in the 5.2 plan.
- Done when 2 (16/16 on Edge, Chrome, Moonlight borderless, desktop): partly evidenced (12/12 on Edge, Chrome, desktop; Moonlight n/a) and the Home-over-externally-raised-window case is open (F1/DW-41). This is a UM760 check for the user.
- Done when 4 (audible switch Netflix ↔ Moonlight, new session after an episode change): not exercisable on the current machine; user acceptance.

Open items that keep this from plain **accepted**: F1/A1 (`DW-41`, medium), the user's UM760 acceptance of Done when 2 and 4 (A2), DW-33 scenarios (A4). This machine verdict is not the user's acceptance; the epic stays open until the user says "accepted" (AGENTS.md).

**UM760 checks for the user's whole-epic acceptance** (`fp-acceptance`, to be presented as features to try):
1. Press Home over Edge, Chrome, Moonlight (borderless) and the desktop, four times each: the shell returns to the front every time, and the app window stays minimized and visible.
2. Home pressed while an app window was raised by Alt+Tab (F1): see what happens; expected today is no change (DW-41).
3. Switch Netflix → Moonlight → Netflix: only the foreground app is audible; change an episode in Netflix and confirm the new audio is still silent while it is in the background (open question in the epic Notes).
4. Switch foreground windows without MediaStation (Alt+Tab): the log shows `ForegroundChanged` with the owner (shell / app / foreign), and `mode`, app state and audio do not change.
5. The DW-33 tile scenarios and `RestartApp` vs `CloseApp` in the log (A4).
6. Run `tools/inject-power.ps1` on an app that has been in use (DW-34 fix from 5.7).

## Open questions

- Should Home in shell mode raise the shell (F1)? It touches AD-8 ("mode does not change when the user switches window") and the epic Note "powrót daje Home"; the answer changes the behavior of Done when 2 in the externally-raised case.
- Does an episode change in Netflix create a new audio session in the Edge tree? Still unmeasured (S9 `-Watch`); the design handles both answers, the user's check 3 gives the answer.

## Assumptions

- The epic reference `epic-windows-audio` resolved by slug to `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/` (epic id 5).
- `pending_tickets` was empty (all seven tickets `done`), so no unfinished-ticket override applies.
- The previous epic in the `epics` order is `epic-home-screen` (id 4); `epic-browserhost` (id 6) has a retro too but comes after this epic in the order and was not used for follow-through.
- No going-in concerns were asked for (headless); no team discussion was held.
- `bmad-review` was not run on the full epic diff; the narrowing is recorded in Findings. The machine verdict **accepted-with-open-items** was rendered on the evidence alone without a human decision; the user's acceptance is separate.
- All five action items and the "no decision records" call are proposals made without the user.


## Follow-up evidence: UM760 acceptance 2026-10-28

The user's whole-epic acceptance on **UM760** remained **not accepted**. This is newer evidence than the 2026-10-27 machine verdict and changes the machine verdict above to `rejected` because the epic still has an unfinished ticket (`5.15`) and the required runtime scenarios failed.

### Sourced findings

- **F10 — Moonlight launch is still broken in the real acceptance path (high, fix now).** S6 failed on `cf8a39c`: the UI stayed at “Ustanawianie połączenia z komputerem”, then the tile became unresponsive; the log records `appLaunchStage ... windowTimeout`, `devHarnessStartFailed`, and `commandFailed`. Source: `epic-windows-audio-um760-acceptance.md:200`.
- **F11 — Netflix launch is still broken in the real acceptance path (high, fix now).** S9 on `cf8a39c` reports that OK immediately produced “Nie odpowiada” and no Edge window; no `appWindowAttached` was emitted. Source: `epic-windows-audio-um760-acceptance.md:203`.
- **F12 — Home was tested without a valid target window and exposed duplicate input behavior (medium, fix now).** S10 recorded two Home/ShowShell pairs; the rerun on `47af713` recorded no completed `MinimizeWindow`, four warnings, `startWithoutHost`, and `commandFailed`. Source: `epic-windows-audio-um760-acceptance.md:204-205`.
- **F13 — The acceptance could not reach audio verification after host failures (high, fix now).** S18–S21 and S26–S27 were `n/d` because Moonlight and Netflix never attached windows, so Done when 4 was not revalidated. Source: `epic-windows-audio-um760-acceptance.md:213-216` and `:221-222`.
- **F14 — The ticket tree allowed a new repair ticket to remain in progress at the epic boundary (high, process).** `tickets.py status` reports 5.15 `in-progress` while the epic was being accepted; the retrospective workflow makes this a rejected machine verdict. Source: `tickets.py status epic-windows-audio`, 2026-10-28.

### Process conclusion

The evidence supports the user's diagnosis: build-time tests and agent reviews did not exercise the real launch and window-attachment paths that failed on UM760. Screenshots and Computer Use would have made the two visible failures immediately reviewable, but they must be tied to mandatory story scenarios and cannot replace deterministic log assertions or the user's whole-epic acceptance.

### Action items

| ID | Type | Item | Owner |
|---|---|---|---|
| A6 | Remediation | Re-drive 5.14/5.13/5.12 with a live launch gate: start Netflix and Moonlight from the built app, require an attached window and a clean failure path, and preserve screenshots plus JSONL logs in the plan's Verification section. | bmad-build / PO |
| A7 | Process | Customize the build workflow so UI or host stories cannot reach `built` without automated tests, a real process/window smoke test, screenshot evidence for visual criteria, and a regression test for every acceptance failure. | bmad-customize |
| A8 | Acceptance | Do not retry the whole epic until 5.15 is done and S6/S9/S10 pass on the same build revision; then rerun the full `fp-acceptance` scenarios, including audio. | user + PO |
| A9 | Architecture | Resolve whether input routing should follow the foreground window after Alt+Tab; the acceptance records this as a requirements decision, not an incidental bug. | bmad-architecture |
