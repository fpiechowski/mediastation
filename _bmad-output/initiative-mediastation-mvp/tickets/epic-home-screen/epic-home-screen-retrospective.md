---
epic: epic-home-screen
date: 2026-10-26
verdict: accepted-with-open-items
criteria: declared
headless: true
---

# Retrospective: epic-home-screen

Machine: **UM760** (`COMPUTERNAME` MEDIASTATION, board F7BSI, model EliteMini Series), HEAD `2d7cfaa`.

## Epic summary

All 11 tickets are `done` (`status` and `state`); none at `built`; `pending_tickets` is empty.

| Ref | Ticket | Risk | Review |
|---|---|---|---|
| 4.1 | Generowanie kontraktów działa przy Application Control | medium | quick |
| 4.2 | Refactor otwierający: handlery Orchestratora i podział testów | low | quick |
| 4.3 | Tracer bullet: D-pad, OK uruchamia aplikację | high | thorough |
| 4.11 | Host shella: blokada nawigacji, nowe okna i CSP | medium | quick |
| 4.4 | Zakładki Video, Audio i Game z category | medium | quick |
| 4.5 | Kafelek wg DESIGN.md | medium | quick |
| 4.6 | Kropka „w tle” na zakładce | low | quick |
| 4.7 | focusRequest i powrót na ostatni kafelek | medium | quick |
| 4.8 | AppMenu: Uruchom ponownie i Zamknij | medium | quick |
| 4.9 | Nagłówek Home, title-safe i ekran błędu konfiguracji | low | quick |
| 4.10 | Refactor sweep | low | quick |

**Ranges** (each plan's `baseline_revision` is the previous ticket's commit, so the builds form one linear chain): 4.1 `fe0a400..d7780e4`, 4.2 `d7780e4..00c2c1d`, 4.3 `00c2c1d..12e00f3`, 4.11 `12e00f3..7e0c27f`, 4.4 `7e0c27f..7a8ec05`, 4.5 `7a8ec05..d850ca8`, 4.6 `d850ca8..9dfcfcd`, 4.7 `9dfcfcd..cf1fcba`, 4.8 `cf1fcba..10a8a45`, 4.9 `10a8a45..3cb379f`, 4.10 `3cb379f..2d7cfaa` (last one inferred from HEAD). `git_evidence.py` over `fe0a400..HEAD`: 11 commits, 0 merges, 94 files, +7497 / −2716. Largest additions: `OrchestratorFailureTests.cs` (+391, a split of the old 1909-line file), `tokens.css` (+387), `focus.test.ts` (+375), `focus.ts` (+212), `ShellOutbox.cs` (+151).

**Evidence available:** epic file, 11 plans (no story files — tickets were not pulled), diff and commits, previous retros. **Missing:** session logs (`.bmad-loop/runs/` exists but was not read, so process-lesson analysis is skipped); no browser or TV run of the shell.

## Findings

**F1 — Review depth contradicts the epic's own decision (process, fix now).** Epic Notes (last bullet) say risk medium or higher gets full review lenses. Plan frontmatter shows 6 of 7 `medium` plans (4.1, 4.4, 4.5, 4.7, 4.8, 4.11) with `review: 'quick'`; only the `high` tracer 4.3 has `thorough`. This is the third epic with the same gap (epic-input retro A2/A3). Review caught real defects even at `quick` (4.3: three medium patches; 4.7: four test patches), so the gap is a risk, not an observed escape. Source: `epic-home-screen.md` Notes; `tickets/*-plan.md` frontmatter. Disposition: fix now (process).

**F2 — Ledger entries for finished work still `open` (fix now).** `deferred-work.md` DW-19 (line 150) and DW-28 (line 232) stay `open` although 4.2 and 4.1 are done; the plans leave closing to the orchestrator (4.1 plan: "DW-28 nie ruszać"). DW-26 stays open on purpose (4.10 plan, line 41). Disposition: fix now (close DW-19 and DW-28 with the commits `00c2c1d` and `d7780e4` as evidence).

**F3 — Review deferred nothing (accept).** Every plan has `deferred: []`; 4.10's closed list was empty (plan line 66). The deferred items assigned to the epic (F11, F17, F18, R4/N3, R7/N8) were handled in 4.3, 4.11, 4.5 and 4.9 per the epic Notes. Accept: no residual debt from the epic was found in the plans. This rests on plan self-reports, not on a separate code check.

**F4 — Aggregate views.** No god-class growth: the largest new web file is `focus.ts` (188 lines), the Orchestrator is split into partials (`Orchestrator.cs` is 196 lines), and `ShellBridge.cs` and `ShellOutbox.cs` are 151 lines each. `ShellBridge` checks `e.Source` through `_gate.Evaluate` (`ShellBridge.cs:90`), matching R8. No file in `src/MediaStation.App/Platform/Input` was touched. Narrowed scope: no duplication map and no spec-to-implementation reconciliation beyond Requirements R1–R8.

**F5 — Diff-scope review not run.** `bmad-review` was not invoked in this headless run, and the diff was not reviewed line by line. The review basis is the per-ticket reviews recorded in the plans. This leaves a cross-ticket boundary review (4.3/4.11 bridge and window, 4.5/4.9 shared `tokens.css`, which six commits touched) unchecked. Disposition: defer.

## Behavior verification

On UM760 at HEAD: `dotnet test` gives 1060 passed, 0 failed, 0 skipped. `npm test` in `web/` gives 10 files and 118 tests passed. `npm run build` (typecheck, schema export through `dotnet test`, TS generation, Vite build) passed, and `git status` was clean afterwards, so generated contracts match the committed ones (R8, Done when 6, build side). **Not exercised:** the shell running in WebView2 on the TV, real D-pad and remote input, and the 1920×1080 and title-safe look (Done when 5). Passing tests do not substitute for that run.

## Previous-retro follow-through

Previous retro: `epic-input-retrospective.md` (its Action items, A1–A11).

| Item | Landed? | Source |
|---|---|---|
| A1 expire stale `_held` entry for swallowed keys (author) | no evidence found | no input file in this epic's diff |
| A2 clipHwnd/repeat/gamepad doc pass (author / `bmad-ticket`) | no evidence found | not checked |
| A3 enforce review depth for medium/high (author / loop config) | no | F1: 6 of 7 medium plans are `quick` |
| A4 dedupe the deferred ledger, DW entries with owners for parked items (loop tooling / `bmad-ticket`) | no evidence found | not checked |
| A5 persist close range in plan (loop tooling) | no | the plans still have only `baseline_revision` and the commit subjects are "story 4.x: implemented and reviewed via bmad-loop" |
| A6 re-measure clip in `epic-osk` | not due | belongs to `epic-osk` |
| A7 document the "App sources linked into Core.Tests" pattern (author) | no evidence found | not checked |
| A8 key calibration dedupe by VK to usage (author) | no evidence found | no input change |
| A9 reject `repeatDelayMs` at or above `MaxRepeatHoldMs` (author) | no evidence found | no input change |
| A10 scope sweeps by ledger ids (loop tooling / PO) | yes | 4.10 plan line 35 (closed list) and DW-26 applied; the sweep was empty rather than a 20 M-token rewrite |
| A11 split `InputHub` (author) | no | `InputHub.cs` is 935 lines; not due before `epic-osk` |

Epic-input's own A4 from the earlier lifecycle retro (split `OrchestratorTests` and handler groups) landed in 4.2 (`00c2c1d`, DW-19).

## Action items

| ID | Type | Item | Source | Owner |
|---|---|---|---|---|
| A1 | Process (fix now) | Make loop config enforce `review: thorough` (full lenses) for `risk` medium or high; check plan frontmatter at generation | F1 | loop config / PO |
| A2 | Tracking (fix now) | Close DW-19 and DW-28 in `deferred-work.md` with evidence `00c2c1d` and `d7780e4` | F2 | PO (`bmad-agent-po`) |
| A3 | Review (defer) | One boundary review of the shell bridge/window (4.3/4.11) and `tokens.css` growth before the next web epic | F5 | author |
| A4 | Acceptance (user) | Run whole-epic acceptance on UM760 through `fp-acceptance` (see below) | Behavior | user |

All are proposals; none was applied.

## Decision records

None written (headless run). No decision from the plans passes the test: the mechanisms that were hard to reverse (schema export in the test run, Actions to Shell, bridge guard) are already recorded in architecture AD-8 and AD-16 (`11c7d36`).

## Acceptance verdict

**accepted-with-open-items** (criteria declared in the epic file's Requirements R1–R8 and Done when 1–6; no unfinished tickets). Evidence: all 11 tickets done, 1060 .NET tests and 118 web tests pass, `npm run build` clean on UM760, and the reviews deferred nothing. Open items: F1/A1, F2/A2, F5/A3, plus Done when 5 (TV look and D-pad use), which needs the user's acceptance run. This machine verdict is not the user's acceptance; the epic stays open until the user says "accepted".

**UM760 checks for the user's whole-epic acceptance** (`fp-acceptance`): Video/Audio/Game tabs from `apps.json` with the Audio empty state (fokus on the tab, Down does nothing); tile chip and state line for each lifecycle state; the "w tle" dot; cold start, restart and wake land on the first Video tile, and return from an app lands on the last tile; AppMenu on long-press OK for `Background`/`Failed` and OK on `Failed`, Back and Home closing it; look at 1920×1080 on the TV within title-safe 5%, cursor hidden when idle, header with logo, greeting and clock; the configuration error screen shows that some errors do not fit; a bridge message from outside the shell origin is rejected.

## Open questions

- Is `quick` review for medium risk intended after all (in which case the epic Note is wrong), or a loop-config bug? The answer decides A1.
- Session logs under `.bmad-loop/runs/20261026-140709-57c5` were not read; they could show why 4.10 produced no change.

## Assumptions

- `-H epic-home-screen` resolved to folder `epic-home-screen` by matching the slug in `tickets.py status`.
- `pending_tickets` is empty, so no unfinished-ticket override applies.
- The previous retro is `epic-input-retrospective.md` (the latest earlier retro in the initiative); items I did not check are marked "no evidence found", not "not done".
- Action items and the empty decision-record list were proposed without the user.
- The ledger statuses were read only; nothing in the tree except this file was written.
