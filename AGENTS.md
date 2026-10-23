# Project instructions

## Acceptance and verification

- Per ticket, verify on the current Windows machine (ALDER, B760 Pro RS/D4, Windows 11 Home) with automated tests, builds, app runs, and log assertions. Record the evidence in the plan.
- Do not park or block a ticket for UM760 or other operator-only hardware checks. If a check needs unavailable hardware, mark it not applicable with the reason and continue with the strongest automated evidence.
- The user performs the whole-epic acceptance on UM760 after delivery, testing the entire epic at once. Do not mark the epic accepted or close it; pause at the epic boundary with a short handoff: current-machine evidence plus the UM760 checks the user should run for the whole epic.
- When a ticket text still names UM760, treat the per-ticket part as current-machine/best-effort; reserve the UM760 wording for the user's whole-epic acceptance.

### Whole-epic acceptance on UM760

- Plan: `epic-<slug>-um760-acceptance.md` in the epic folder (P0 preparation, scenarios S1…, acceptance criterion, results table). The retro's machine verdict (`accepted-with-open-items`) is not the user's acceptance.
- Format: an agent-guided session. The agent leads one scenario at a time: says what it will do and what the user must press, runs the app/script itself (launch, log tail, `tools/check-*.ps1`), then asks the user to act (remote, pad, window clicks). Do not hand the user the plan to execute alone.
- After each scenario the agent reads the logs (`%LOCALAPPDATA%\MediaStation\logs\*.jsonl`), asserts the expected entries, states pass/fail with evidence, and fills that row of the plan's results table before moving on. Failed scenarios are recorded as observed; do not retry silently or reinterpret the expectation.
- The agent proposes the verdict against the plan's acceptance criterion; the user decides. Only after the user's explicit "accepted" may the epic be closed (Sofia, `bmad-agent-po`). Failures go to `bmad-build`, architecture conflicts to `bmad-architecture`, open measurements to `deferred-work.md`.

## Agent tooling (BMad)

- The BMad skills are tracked in `.agents/skills/` (pinned by `skills-lock.json`); `_bmad/` holds the module config. `.claude/` and `.codex/` are git-ignored: they contain junctions with absolute paths and `bmad-loop` hooks that point at one user's `uv` install.
- On a fresh clone (e.g. UM760) run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/link-agent-skills.ps1` to create the `.claude/skills` junctions. `bmad-loop` hooks are only needed for unattended loop runs; set them up with `bmad-loop-setup`.
