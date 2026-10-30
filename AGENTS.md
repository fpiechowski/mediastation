# Project instructions

## Current machine

- Before any verification, plan or document that names a machine, identify the one you are on; never assume it from this file or from earlier notes. Run `powershell -NoProfile -Command "$env:COMPUTERNAME; (Get-CimInstance Win32_BaseBoard).Product; (Get-CimInstance Win32_ComputerSystem).Model"` and record the result in the plan.
- Known machines: **UM760** is the Minisforum UM760 Slim target (host `MediaStation`, board `F7BSI`, model `EliteMini Series`); **ALDER** is the development PC (board `B760 Pro RS/D4`, Windows 11 Home). If the output matches neither, say so and ask the user instead of picking a label.
- Write the identified machine name in plans, tickets and architecture notes (for example "ran on UM760"). "Current machine" below means the machine identified this way, whichever it is.

## Acceptance and verification

- Per ticket, verify on the current machine (see "Current machine") with automated tests, builds, app runs, and log assertions. Record the evidence in the plan.
- Do not park or block a ticket for UM760 or other operator-only hardware checks. If a check needs unavailable hardware, mark it not applicable with the reason and continue with the strongest automated evidence.
- The user performs the whole-epic acceptance on UM760 after delivery, testing the entire epic at once. Do not mark the epic accepted or close it; pause at the epic boundary with a short handoff: current-machine evidence plus the UM760 checks the user should run for the whole epic.
- When a ticket text still names UM760, treat the per-ticket part as current-machine/best-effort; reserve the UM760 wording for the user's whole-epic acceptance.
- Whole-epic acceptance on UM760 runs through the `fp-acceptance` skill: it prepares `epic-<slug>-um760-acceptance.md` and leads the session; the retro's machine verdict is not the user's acceptance, and only the user's "accepted" lets Sofia close the epic.

## Agent tooling (BMad)

- The BMad skills are tracked in `.agents/skills/` (pinned by `skills-lock.json`); `_bmad/` holds the module config. `.claude/` is git-ignored: it contains junctions with absolute paths and `bmad-loop` hooks that point at one user's `uv` install.
- Run `bmad-loop` (`run`, `resume`, `resolve`) only from the PowerShell tool, never from Git Bash: psmux takes its pane shell from the inherited `SHELL` variable, so a Bash launch starts `pwsh -EncodedCommand` inside bash, the dev session crashes and the run pauses. Before the command run `Remove-Item Env:SHELL -ErrorAction SilentlyContinue`, and start it in the background.
- This project is built by `bmad-loop`, unattended, so `hitl` tickets are unwanted: a `hitl = true` entry halts the run (`ticket-hitl` pause) until a person builds it. When slicing, refining or planning tickets, write every entry as `hitl = false`; do not set the flag to mark a ticket that needs real hardware or physical input. Put that check in the ticket's `verify` as best-effort on the current machine and in the epic's UM760 acceptance (see "Acceptance and verification"). If you find a `hitl = true` entry in a ticket you are about to build, drop the flag as in `3d9830d` and `2a7f81e`, and say so in the handoff.
- On a fresh clone (e.g. UM760) run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/link-agent-skills.ps1` to create the `.claude/skills` junctions. `bmad-loop` hooks are only needed for unattended loop runs; set them up with `bmad-loop-setup`.
