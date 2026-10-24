# FP knowledge

This document covers the skills of the `fp` module: what each one gives the user, when to recommend it, and what to offer next.

## The skills

- `fp-acceptance`: the user's own acceptance of a finished epic on the acceptance machine, as a live demo the agent leads one scenario at a time. It prepares `epic-<slug>-<machine>-acceptance.md` in the epic folder from the epic's Done when, the retrospective and the deferred ledger, then runs it: the agent launches the app, asks for each physical press by caption and voice, detects it in the app's JSONL log, and lets a log assertion decide pass or fail. Recommend it after `bmad-retrospective` has given its machine verdict on an epic and before Sofia (`bmad-agent-po`) closes the epic: the retrospective's verdict is not the user's acceptance. Also recommend it to resume an acceptance that halted. Afterwards offer Sofia to close the epic when the user said "accepted", `bmad-build` for each failed scenario, and `bmad-architecture` when a measurement contradicts a decision.

## Where things land

The plan and its results table sit in the epic folder; open measurements go to the initiative's `deferred-work.md`. The acceptance machine, log glob, build command and app path are customization of `fp-acceptance` (`_bmad/custom/fp-acceptance.toml`, written with `bmad-customize`).
