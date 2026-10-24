# Project instructions

## Acceptance and verification

- Per ticket, verify on the current Windows machine (ALDER, B760 Pro RS/D4, Windows 11 Home) with automated tests, builds, app runs, and log assertions. Record the evidence in the plan.
- Do not park or block a ticket for UM760 or other operator-only hardware checks. If a check needs unavailable hardware, mark it not applicable with the reason and continue with the strongest automated evidence.
- The user performs the whole-epic acceptance on UM760 after delivery, testing the entire epic at once. Do not mark the epic accepted or close it; pause at the epic boundary with a short handoff: current-machine evidence plus the UM760 checks the user should run for the whole epic.
- When a ticket text still names UM760, treat the per-ticket part as current-machine/best-effort; reserve the UM760 wording for the user's whole-epic acceptance.
- Whole-epic acceptance on UM760 runs through the `fp-acceptance` skill: it prepares `epic-<slug>-um760-acceptance.md` and leads the session; the retro's machine verdict is not the user's acceptance, and only the user's "accepted" lets Sofia close the epic.

## Agent tooling (BMad)

- The BMad skills are tracked in `.agents/skills/` (pinned by `skills-lock.json`); `_bmad/` holds the module config. `.claude/` and `.codex/` are git-ignored: they contain junctions with absolute paths and `bmad-loop` hooks that point at one user's `uv` install.
- On a fresh clone (e.g. UM760) run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/link-agent-skills.ps1` to create the `.claude/skills` junctions. `bmad-loop` hooks are only needed for unattended loop runs; set them up with `bmad-loop-setup`.
