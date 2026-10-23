---
title: 'AD-8 foreground gate: swallow input only over MediaStation windows'
type: 'feature'
ticket: ''
created: '2026-10-23'
status: 'ready-for-dev'
baseline_revision: ''
route: 'full'
route_source: 'auto'
risk: 'high'
review: ''
review_source: ''
lenses_ran: []
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md'
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input-um760-acceptance.md'
  - '{project-root}/AGENTS.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** InputHub swallows keys and emits Actions by routing `mode` alone, so after Alt+Tab to another window Enter/Esc/arrows are still consumed (UM760 acceptance, second finding). The user must be able to use the PC normally without quitting MediaStation.

**Approach:** Per AD-8 (f2a529e): keys and gamepad Actions are handled only while the foreground top-level window is MediaStation's shell, OSK or a window attached by the Orchestrator (match by `GA_ROOT` hwnd, fallback `rootPid`). The only exception is Home from the `remote` device, which works over any window. The hook decides at key-down from a local copy; the Orchestrator publishes the managed set with a new `SetManagedWindows` command.

## Boundaries & Constraints

**Always:** hook never waits on the Orchestrator and stays fast (LL timeout); key-up follows its key-down decision; unknown foreground (null, secure desktop, Alt+Tab switcher) counts as foreign; `mode` is not changed by user window switches; pointer behaviour from 9ba0c29 stays; own `SendInput` marker still passes.

**Never:** WinEvent hooks, reacting to `ForegroundChanged` (it stays log-only), changing user-visible acceptance results, running physical-input scenarios, editing the acceptance results table.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Managed foreground | key mapped, foreground = shell/attached app | Today's table (swallow + Action) | none |
| Foreign foreground | Enter/Esc/arrows from keyboard, foreground = desktop | passes, no Action | none |
| Remote Home, foreign | Home with `remote` source, foreign foreground | swallowed, `home` Action | none |
| Key-up after window switch | key-down swallowed over shell, then Alt+Tab, key-up | key-up swallowed (decision of key-down) | none |
| Gamepad, foreign | any pad button or `gamepadHome` | no Actions; baseline re-adopted on return | none |
| No probe configured | `InputPolicy` built without probe | acts as always managed | none |

</frozen-after-approval>

## Code Map

- `src/MediaStation.Core/Input/InputPolicy.cs` -- `OnKey` (key-down gate before `ShouldSwallow`, after `ResolveSource`), `OnGamepad` (foreign => `DropGamepadState` + pass), key-up without key-down; add optional ctor arg `IForegroundProbe? foreground = null`.
- `src/MediaStation.Core/Input/` -- new `IForegroundProbe`, `ManagedWindows` (thread-safe copy: shell hwnd, osk hwnd, apps; `Apply` with epoch; `Contains(hwnd,pid)`), `ManagedForegroundProbe` over `IForegroundWindow`; model on `InputRoutingState`.
- `src/MediaStation.Core/Contracts/` -- new `SetManagedWindows(CmdId, OskHwnd?, Apps[{Hwnd,RootPid}], Epoch)`; register in `MessageTypes.cs`; `Ports.cs` add `Execute(SetManagedWindows)` to `IInputRoutingPort`; `CommandRouter.cs` case.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` -- `AppState.Hwnd` set at 281/318, cleared at 482/603; add `RootPid`; emit `SetManagedWindows` when the set changes (attach, adopt, close, fail).
- `src/MediaStation.App/Platform/Input/` -- `InputHubRoutingPort` applies `SetManagedWindows` (log entry `managedWindows`); new Win32 `IForegroundWindow` (GetForegroundWindow, GetAncestor GA_ROOT, GetWindowThreadProcessId; add to `NativeMethods.txt`); `CompositionRoot.cs` ~216-278 wiring; `ShellWindow` publishes its hwnd to `ManagedWindows`.
- `web/contracts/schema/set-managed-windows.schema.json`, `web/contracts/generated/set-managed-windows.ts`, `CoreMessageContractTests.cs` schema list -- schema export is blocked here (Application Control), so generate by another route or hand-write in the generated format.
- Tests: `InputPolicyTests`, `GamepadPolicyTests`, `OrchestratorTests`, `CommandRouterTests`, `InputHubRoutingPortTests`, new `ManagedWindowsTests`.
- Do not change: pointer policy, `ForegroundChanged` handling (log only, confirmed in `OrchestratorLoop.cs:126`).

## Tasks & Acceptance

**Execution:**
- [ ] `Core/Input` -- add `IForegroundProbe`, `ManagedWindows`, `ManagedForegroundProbe`, `IForegroundWindow` -- testable gate without Win32
- [ ] `Core/Contracts`, `Ports.cs`, `CommandRouter.cs` -- `SetManagedWindows` command and routing -- contract
- [ ] `InputPolicy.cs` -- gate key-down, key-up, gamepad -- core behaviour
- [ ] `Orchestrator.cs` -- publish managed set on attach/adopt/close/fail -- AD-15
- [ ] `App/Platform/Input`, `CompositionRoot.cs`, `ShellWindow` -- Win32 probe, port, shell hwnd -- wiring
- [ ] schemas/generated TS/contract tests -- new command registered
- [ ] tests for the I/O matrix and Orchestrator publication
- [ ] acceptance plan note -- new Alt+Tab scenario and S2-S12 need shell in foreground (do not touch results table)
- [ ] AD-8 text: `shellHwnd` is set by the App (`ManagedWindows.SetShell`), not carried by the command (Core has no shell hwnd)

**Acceptance Criteria:**
- Given foreign foreground, when Enter/Esc/arrows are pressed, then `InputDecision` passes with no Actions in every mode.
- Given remote Home over a foreign window, then it is swallowed and yields `home`.
- Given the Orchestrator attaches or removes an app window, then a `SetManagedWindows` with the current set is emitted.

## Design Notes

`shellHwnd` deviates from the AD-8 wording: the Orchestrator lives in Core without a window handle, so the shell hwnd goes straight from `ShellWindow` into `ManagedWindows`. Today only the shell exists as a real window (no WindowManager, hosts or OSK yet), so the managed set is effectively the shell until epic-windows-audio and epic-osk; the contract is ready for them.

## Verification

**Commands:**
- `dotnet test tests/MediaStation.Core.Tests -c Release --no-restore -p:SkipWebBuild=true` -- expected: all green
- `npm run typecheck` and `npm run build` in `web/` -- expected: success
- `dotnet publish ... -p:SkipWebBuild=true` -- expected: success

**Manual checks (if no CLI):**
- Launch, Alt+Tab to the desktop: no `action` log lines for Enter/Esc (left to the user at acceptance).
