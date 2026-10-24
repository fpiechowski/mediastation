---
name: fp-acceptance
description: 'Leads the user''s own acceptance of a finished epic as a guided live demo on the target machine, with log assertions as the verdict. Use when the user wants to accept an epic themselves on real hardware, or to prepare, complete, run or resume its acceptance plan. Not for the retrospective''s machine verdict (bmad-retrospective) or closing the epic (bmad-agent-po).'
---

# Epic acceptance

Act as the demo lead in the user's acceptance of a whole epic on the acceptance machine. The user is the acceptor: they make every physical press (injected input takes another path through the system, a low-level hook sees it differently, so it proves nothing about the real device), settle every observation and choice, and alone give the verdict. You set up the state, watch the log, and let a script judge.

The outcome is the acceptance plan with its results table filled, one row per scenario. Its readers (the user, Sofia (`bmad-agent-po`) closing the epic, `bmad-build` fixing a failure, `bmad-architecture` settling a conflict) act on it without this session, so every row names what was asserted, quotes the log lines that decided it, and records a failure as observed.

## On activation

1. Resolve customization: `uv run {project-root}/_bmad/scripts/resolve_customization.py --skill {skill-root} --project-root {project-root} --key workflow`. Script not found: BMad is not set up here; offer the `bmad` skill's setup (installing it first with `npx skills add bmad-code-org/BMAD-METHOD --skill bmad` if needed), then run it again. Any other failure: show it and stop.
2. Run each entry of `{workflow.activation_steps_prepend}`, load `{workflow.persistent_facts}` (entries prefixed `file:` are paths or globs under `{project-root}`), then run each entry of `{workflow.activation_steps_append}`.
3. Settle the epic folder from the request. Ask for any of `{workflow.machine}`, `{workflow.log_glob}`, `{workflow.ready_entry}` and `{workflow.build_command}` left empty and use the answers this session; say once that the `bmad-customize` skill stores them in `_bmad/custom/fp-acceptance.toml` (`npx skills add bmad-code-org/BMAD-METHOD --skill bmad-customize` if it is missing). The plan is `<epic folder>/epic-<slug>-<machine, lowercased>-acceptance.md`.
4. Route. Prepare, `{skill-root}/references/prepare.md`: no plan yet, a request to prepare, complete or change one, or a plan whose `next` reports a scenario without a results row. Run, `{skill-root}/references/run.md`: a request to run or resume the plan otherwise. Read the chosen file fully and follow it. When a run or resume request had to go through prepare, continue into run once the user approves the plan.

## The script

`uv run {skill-root}/scripts/acceptance.py <command>` does the deterministic work on the app, its JSONL logs and the plan; `--help` is the reference, SPEC syntax included, and every log command takes `--logs "{workflow.log_glob}"`. A script error (exit 2) goes to the user as it is; your reading of the log never stands in for it.

## Rules for both modes

- The retrospective's machine verdict is evidence, not acceptance; the user's run on real hardware is the gate. The epic's status is Sofia's (`bmad-agent-po`) to change, never yours.
- Write the plan in the language of the epic file; speak to the user in theirs.
