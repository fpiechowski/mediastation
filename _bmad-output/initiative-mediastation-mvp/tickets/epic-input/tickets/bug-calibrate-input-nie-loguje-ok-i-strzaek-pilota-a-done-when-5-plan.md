---
title: '--calibrate-input nie loguje OK i strzałek pilota'
type: 'bugfix'
ticket: '14'
created: '2026-10-25'
status: done
baseline_revision: '07a5cde8325063506b60e346b7a4cc71540f5889'
route: 'oneshot'
route_source: 'auto'
risk: 'medium'
review: 'none'
review_source: 'auto'
lenses_ran: []
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/AGENTS.md'
warnings: []
deferred: []
operator_actions:
  - 'Decide how to resolve bug 3.14 criterion 2: either (a) approve correcting epic-input Done when 5 and Odbiór na UM760 point 2 so calibration is only required to log remote buttons that arrive as HID Consumer reports (Home, Back, voice assistant), or (b) approve a new story for a hook-free calibration mode that registers keyboard Raw Input (architecture AD-8 allows this only without an LL hook in the same process).'
  - 'If (b) is chosen, run the new calibration on UM760 with the physical G20S Pro and check that OK and the arrows get rawInput entries and that Home and Back still get VK.'
  - 'Resolve the Source conflict for the Keyboard button with bmad-architecture (HID usage 0x00CF as an additional trigger next to VK); it is the same decision that blocks Done when 5.'
---

<intent-contract>

## Intent

**Problem:** On UM760, `--calibrate-input` logs `rawInput` with VK only for Home and Back; OK and the arrows never appear, while epic-input Done when 5 and acceptance point 2 expect at least 7 buttons with VK.

**Approach:** Establish the cause from the 2026-10-25 logs and the code, record whether it is a defect or a channel property, and pin the finding with a test. Changing the epic criterion needs the user's consent, so it is handed over as an operator action.

</intent-contract>

## Implementation Notes

Oneshot: a diagnosis plus one characterization test; no production code changes.

Machine: ran on UM760 (host `MediaStation`, board `F7BSI`, model `EliteMini Series`), HEAD `07a5cde`.

**Cause (criterion 1): a property of the channel after story 3.12, not a filter defect.**

- Calibration creates an entry only from a remote device event: an HID Consumer report (`OnHidConsumer`) or a keyboard `WM_INPUT` (`OnRawKeyboard`). A hook event alone is only buffered and pruned (`InputCalibration.cs`, `OnKey`, `BufferHook`, `PruneHooks`); the class header states that a hook without a `remote` device event never reaches the log.
- Since 3.12, `InputHub` registers only HID Consumer in calibration too, so `OnRawKeyboard` is never called (`rawInputRegistration` keyboard=false, 3.12 plan).
- Home and Back reach Windows as HID Consumer usages (0x0223, 0x0224) that the driver turns into injected VKs (0xAC, 0xA6). The Consumer report identifies the device and the hook adds the VK, so both are logged (`injected true`, `hasVk true`).
- OK and the arrows are ordinary keyboard-collection events: not injected and without a Consumer report. With keyboard Raw Input off, nothing ties them to the dongle, so they are indistinguishable from the ordinary keyboard and are dropped, as the "no ordinary keyboard" rule requires.
- Evidence, `%LOCALAPPDATA%\MediaStation\logs\mediastation-2026-10-25.jsonl`: three runs (12:58, 13:03, 13:08Z) each log only Home and Back as `rawInput` (13:08 adds the voice assistant, usage 0x00CF, `hasVk false`). The same build in normal mode gives one `action` each for OK, Up, Down, Left and Right (S2, `epic-input-um760-acceptance-2.md`), so the hook does see those keys. Only device attribution is missing.
- A signature filter defect is ruled out: the same signature matches Home and Back in the same runs.

**Criterion 2 is not met by code and cannot be closed unattended.** The two remaining routes both need the user:

- Correct Done when 5 and Odbiór na UM760 point 2 (the ticket allows it with consent).
- Add a hook-free calibration mode with keyboard Raw Input (AD-8 permits it without a concurrent LL hook). It would lose the injected VK for Home and Back (they are not keyboard `WM_INPUT`), the hook-based `injected` and `llBeforeRawMs` fields, and the VK Esc exit, and it needs the physical remote to verify, so it is not built here.

Per `AGENTS.md`, physical-remote checks are not parked; they belong to the user's whole-epic acceptance on UM760. The "Keyboard button" candidate (usage 0x00CF) is already a Source conflict in the epic Notes.

Side observation (user, S9): Home opened Edge in calibration. Calibration never swallows keys by design, so this is expected.

Files changed: `tests/MediaStation.Core.Tests/InputCalibrationTests.cs` (`Remote_keyboard_collection_key_without_device_evidence_produces_no_entry`, five theory cases: OK, Left, Up, Right, Down).

Review: not run (single added test, no production change). Criterion 3: the test characterizes the finding and passes now; it does not fail on a defect because there is none to fix. It will fail if calibration starts logging such keys without device evidence.

## Verification

- `dotnet test tests/MediaStation.Core.Tests -v q` — PASS, 904 passed, 0 failed, 0 skipped (899 before plus 5 new).
- Log inspection of the 2026-10-25 runs on UM760 as above.
- Not applicable: running `--calibrate-input` with the physical G20S Pro (needs hands on the remote); left to the user's whole-epic acceptance.

## Auto Run Result

Status: awaiting-operator

Summary: Cause established with log evidence: OK and arrows are absent from calibration because, after 3.12, calibration registers only HID Consumer and those keys carry no device evidence. This is a channel property, not a filter bug. A characterization test was added; Done when 5 and acceptance point 2 still need the user's decision.

Operator actions owed: see `operator_actions` in the frontmatter.

## Operator Confirmation

Confirmed 2026-10-25: the external actions this story owed were carried out.

- Decide how to resolve bug 3.14 criterion 2: either (a) approve correcting epic-input Done when 5 and Odbiór na UM760 point 2 so calibration is only required to log remote buttons that arrive as HID Consumer reports (Home, Back, voice assistant), or (b) approve a new story for a hook-free calibration mode that registers keyboard Raw Input (architecture AD-8 allows this only without an LL hook in the same process).
- If (b) is chosen, run the new calibration on UM760 with the physical G20S Pro and check that OK and the arrows get rawInput entries and that Home and Back still get VK.
- Resolve the Source conflict for the Keyboard button with bmad-architecture (HID usage 0x00CF as an additional trigger next to VK); it is the same decision that blocks Done when 5.

_Appended by the bmad-loop orchestrator (`bmad-loop confirm`, #335): a human confirmed these external actions out of band, and the story was advanced from `awaiting-operator` to `done`._
