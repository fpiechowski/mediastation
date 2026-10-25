# Run

Outcome: every row of the plan's current results table filled with a verdict that a script or the user decided, then the user's own verdict on the epic.

`A` below is `uv run {skill-root}/scripts/acceptance.py`, and `LOGS` is `--logs "{workflow.log_glob}"`, which every `launch`, `wait`, `check` and `inventory` takes.

## Before the first scenario

- Open with the purpose, before any mechanics: from the plan's What we accept, tell the user in a few plain sentences what the epic delivers, why the project needs it, what it unblocks and what their acceptance decides. Wait until they say they follow; if not, explain differently rather than repeat.
- `A next <plan>`. A plan at `status: planned` or `aborted` is shown to the user (What we accept, scenarios with their Why, and acceptance criterion) and runs only after they approve it: then `A meta <plan> --set status=approved`. Set `status=in-progress` when the first scenario starts. Each entry under `problems` is named to the user before the first row.
- Read what earlier sessions left under the table: filled rows, halt notes. A note that adds scenarios the table lacks means the plan needs completing first: offer `prepare.md`'s completion. When an earlier pass failed, halted or was aborted and a fix has landed since (a newer HEAD), ask whether to continue from `next` or start a new pass from the top; a new pass is `A new-pass <plan> --heading "<date>, HEAD <sha>, <machine>" --note "<why>"`, which leaves the old table as it was. The user decides.
- Settle the channel before anything goes full screen. The user will face the app under test, often with only a remote in hand, so ask whether they will see this conversation during the demo. If not, recommend a second screen for it: their phone through the harness's remote access (Remote Control in Claude Code, the equivalent elsewhere) or another monitor. Without a way to reply, every question costs them a walk to the keyboard. Start the caption bar with `A overlay start` when the conversation will be hidden, and speak with `--voice "{workflow.voice}"` when it is set. Say once what the `--beep` cues mean (`A wait --help` lists them).
- Write one session line under the current table's heading: date, HEAD, build version, machine and host name. Ask for `{workflow.app_path}` if it is empty.

## One scenario

One at a time, in table order. The next starts only after its row is written. `next` returns the row's cells under `row`; for P0, its preparation steps.

1. **Announce**, in one message the user can read before turning to the device, in this order: why this scenario (what is tested, what the project gains if it holds, what it blocks if not, from the plan's Why; a plan without one gets it stated now); what they must do, in plain words (which button, how many times, how long; for an observation, what to look at and what a good and a bad outcome look like); then one line on how it will be judged, with the frozen assertion kept for the plan and the row, not recited unless they ask. A row without Actor and Assertion (a plan written by hand) gets both now, shown to the user: the Actor from who acts and who judges, the Assertion from Expected, with `--forbid-level {workflow.forbidden_levels}` and SPECs from `A inventory --version-contains <short HEAD> LOGS`. The first row that launches the app gets the version assertion when the plan has none. An expectation that still reads two ways is settled with the user now, before the act: read after the outcome, it becomes a reinterpretation. When the user must act, wait for their "start".
2. **Prepare** the state yourself: `A launch --exe "{project-root}/{workflow.app_path}" --ready "{workflow.ready_entry}" LOGS` or keep the running instance when the scenario continues it; `A focus --pid` and `A foreground --pid` when the app must be on top. A stale build fails every scenario for the wrong reason, so the first launch's version assertion is never skipped. P0 runs its steps in order (`{workflow.build_command}`, the self-tests, the voice); one failing step fails P0.
3. **Act.** The window starts at `launch`'s `mark` when the row launches the app, otherwise at `A mark` taken just before the act.
   - `agent` rows: run the command and keep its output. A long project script runs so that you can watch its output while it runs (in the background if the harness limits command time); its exit code and run log are the result.
   - `user-hardware` rows: `A wait --since <mark> --for "<SPEC>" --say "<instruction in the user's language>" --beep --countdown 3 --timeout {workflow.input_timeout_seconds} LOGS`. A timeout is not a verdict. When the user cannot see the conversation and its `cue` shows the instruction reached them by no channel, say so and ask whether to repeat. Otherwise tell the user what the log did show (`seen`) and ask whether they pressed. Waiting again after a missed press is fine. If they pressed and the log shows nothing, `check` fails it as observed. A check script that cues the user itself (its own beeps) takes the place of `wait`: start it after their "start"; its exit code is the verdict.
   - When a screen or browser tool is at hand (Computer Use, Windows-MCP, Playwright over CDP to a WebView2), it may set up state before the mark (open a page, start a client, minimise windows) and take a screenshot for an observation or a choice, with your recommendation. Between the mark and `check` it touches nothing: its clicks and keys are injected input, which the app may log as acts, and it moves the foreground. Without one, ask the user; the process never depends on one.
4. **Judge.** `A check --since <mark> --until <until from wait> --out <temp file> LOGS` with the frozen assertion; its verdict is the verdict. A window over earlier rows takes `--since "row:S1" --plan <plan>`. Observation rows take the user's words as the verdict, quoted.
5. **Record**, every row with the date and HEAD in its result. `A fill <plan> <row> --from-check <temp file> --result "(<date>, HEAD <sha>)"`, with `--measured` for a measurement row and the value in `--result`; or `--exit-code <code> --log <run log> --quote "<regex of its verdict lines>"` for a check script. `fill` writes the verdict and the evidence itself, refuses a `--result` that contradicts them, and refuses a check whose SPECs differ from the row's Assertion. When the user rejects behaviour the script passed (it meets the contract but they will not accept it), their fail goes beside it: `--result "(…) user: fail, <their words>"`. Observation rows: `--result` and `--evidence` in the user's words; n/a rows: with the reason. Then tell the user the verdict, and with `A say` when they cannot see the conversation: one line for the result and one for what it means for the project (what is now confirmed or what stays blocked), still without diagnosing a failure.

## After a failure

Record it as observed and stop investigating. Diagnosis belongs to `bmad-build`, and a session that drifts into debugging stops being an acceptance and never reaches the rows after it. Ask whether to continue with the next scenario or halt. A halt gets one dated note under the table naming the rows not run and why. A scenario is repeated only when the user asks, with `A fill ... --rerun`, which adds a row and keeps the first one.

## Closing

When `next` returns null, or the user halts:

- `A overlay stop`, and close the app if it still runs.
- Propose a verdict against the plan's acceptance criterion: rows met, rows failed, observations, decisions the epic asked to settle. Put it in project terms first: what is confirmed working, what is not, and what that means for the epic's promise and the epics that depend on it. Then the rows. Ask the user for theirs.
- Record it: `A meta <plan> --set verdict=<their word> --set status=executed` (a halted pass stays `in-progress`), and a "User verdict" line with the date under the table. Then run `{workflow.on_complete}` when it is set.
- Hand over and stop; none of these steps is yours to take:
  - "accepted": Sofia (`bmad-agent-po`) closes the epic; name the plan.
  - each failed row: `bmad-build`, with the row and its evidence;
  - a measurement that contradicts an architecture decision: `bmad-architecture`;
  - measurements and questions still open: an entry in `deferred-work.md` in its existing format, origin naming the plan and row.
