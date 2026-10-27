# Prepare

Outcome: an acceptance plan that the user approves and that `run.md` executes without rewriting. Every scenario names who acts, what the log must show and how it is judged, so nothing is interpreted after the act.

`A` below is `uv run {skill-root}/scripts/acceptance.py`, and `LOGS` is `--logs "{workflow.log_glob}"`.

An epic already closed (status done, or its retrospective's decision recorded as accepted) was closed without this acceptance: say so and ask whether a retroactive plan is wanted before drafting one.

## Sources

Read them and cite them in the plan's Sources section:

- the epic file: Done when; any acceptance checklist in Notes; open questions marked for the acceptance; Decisions that fix an expectation (a deliberate warn entry, a threshold, an environment switch);
- the retrospective `epic-<slug>-retrospective.md`: Behavior verification, which shows what a good log looks like on the development machine and what was not exercised end to end. That gap is the scope;
- `deferred-work.md` (the nearest one above the epic folder): open entries whose reason defers to this acceptance;
- `{workflow.check_scripts}`, globs under `{project-root}`: a scenario one of them covers runs it, and its exit code is the assertion;
- earlier `*-acceptance.md` plans in sibling epic folders: match their tone; where their tables differ, the format below wins.

Scope is what only the acceptance machine can show. A mechanism that the tests and the retrospective's runs already prove gets no scenario; say so in one line.

## Format

Frontmatter: `epic`, `type: acceptance-plan`, `machine`, `base_head` (HEAD at preparation; the run records the HEAD it accepts), `created`, `status: planned`.

Sections, in this order: title; What we accept; Demo; Sources; Preparation `| # | Step | Expected |` (P0.1…); Scenarios `| # | Scenario | Actor | Expected | Assertion | Kind | Done when |` (S1…); Notes (fallbacks the user may need mid-run); Acceptance criterion; Results `| # | Result | Evidence |` with one empty row for P0 and one per scenario. Translate headings and column names into the plan's language but keep the column order, and keep Actor values literal, since `run.md` reads them. Keep the results table last: the script finds it as the last table whose first column is `#`. The three purpose sections below are prose, outside the tables, so the script and earlier plans are unaffected; a Why list goes directly under the scenarios table.

- **What we accept**: written for a stakeholder, in plain words with no log vocabulary. What the epic delivers, told as what the product's user can now do that they could not before, feature by feature (not a story list); why the project needs it, what it makes possible next, what it deliberately leaves for later, and what accepting it commits the project to. Draw it from the epic, the PRD and the architecture you already read; where they do not say, ask the user rather than invent a rationale.
- **Demo**: what `run.md` presents, in segments. A segment is one feature the product's user would recognise by name (moving around the tiles with the remote, recovering an app that hangs), never a mechanism or a single press. Per segment: what the feature does for the product's user, in a sentence or two; the invitation, phrased as trying the feature ("try moving the focus with the right arrow when I count down from 3"), or for something to look at, what to look at; what the user should see when it works; and the rows it covers. A press and the observation of its outcome are one moment of the same segment, though they stay two rows. Order the scenarios table by segment, since the run takes rows in table order; `agent` rows that set a segment up belong to it and are not presented. A row that serves no segment is a candidate for removal; raise it with the user.
- **Why each scenario**: for the plan's readers rather than the demo; one to three sentences per scenario, in the same voice: what is tested, which promise of the epic it confirms, and what the project loses or is blocked from if it fails. A scenario whose purpose you cannot state is a candidate for removal; raise it with the user.

- **Preparation** covers the build (`{workflow.build_command}`), config, attached hardware, the self-tests of the check scripts the plan uses and, when `{workflow.voice}` is set, that the target speaks it (`A say "<line>" --voice "{workflow.voice}"`). The first scenario that launches the app also asserts the build is fresh: when the app logs its version, the HEAD in it with `~=`.
- **Actor**: `agent` when you act and a script judges, `user-hardware` when the user acts on a device and the log shows the act (through `wait`, or a check script that cues them itself), `observation` when the user's judgment decides, whoever acts. A row that needs both a script verdict and the user's judgment is split into two: one judge per row, and a filled row is never overwritten.
- **Assertion**: the exact `check` arguments, with `--forbid-level {workflow.forbidden_levels}`, or the check script command and its pass condition. A `user-hardware` row also names the `wait --for` SPEC that detects the act. A row whose path never reaches `{workflow.ready_entry}` (a start that fails by design) names its own `--ready`. Build every SPEC from entries the current build writes, as `A inventory --version-contains <short HEAD> LOGS` lists them (the retrospective's Behavior verification shows them in context). When no run at that HEAD exists yet, take the newest of its `recent_versions` and say which. A message or field only an older build logged fails the assertion for the wrong reason. An entry no current log shows is taken from the code that writes it and listed in Notes as unverified. Shared arguments may sit in a legend above the table; a window over earlier rows takes `--since "row:S1" --plan <plan>`.
- **Kind**: pass/fail, measurement (a value is recorded and nothing fails), pass/fail with a measurement, or observation.
- **Done when**: the item numbers the row covers. Every item the machine can show is covered, or named out of scope.
- **Acceptance criterion**: which rows must pass, which may differ with an explanation, which are observations, and which outcome is a decision rather than a failure. Close it with one sentence on what "accepted" means for the project.

Resolve contradicting expectations now, in the assertion, not after a result: a deliberate warn in one row against "no warn in the whole run" in another becomes an `--allow`.

## Finish

Write the plan with `status: planned` and run `A next <plan>`: fix every entry under `problems`, then show the user What we accept first, then the Demo as the agenda, its criterion and every bound or choice you made yourself as a question; offer the scenarios with their Why for the detail. Apply their changes to the file. When the user approves it, `A meta <plan> --set status=approved`.

To complete an existing plan, add only what is missing: the Demo (built on the table's order, since filled rows are never moved), the Actor and Assertion columns, scenarios the plan describes outside its table (in Notes, in a halt note), and an empty results row for each new scenario in run order. Keep every line the user wrote, filled results and halt notes above all. A row that a later decision contradicts is rewritten only with the user's consent, its old text quoted. An aborted or halted plan goes back to `planned` with `base_head` updated.
