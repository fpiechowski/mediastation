---
title: 'AD-8 foreground gate: swallow input only over MediaStation windows'
type: 'feature'
ticket: ''
created: '2026-10-23'
status: 'built'
baseline_revision: 'd456f83e6fb17447cdd34e54c429da66b7e61bb4'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'quick'
review_source: 'pinned'
lenses_ran: [quick-inline]
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
- [x] `Core/Input` -- add `IForegroundProbe`, `ManagedWindows`, `ManagedForegroundProbe`, `IForegroundWindow` -- testable gate without Win32
- [x] `Core/Contracts`, `Ports.cs`, `CommandRouter.cs` -- `SetManagedWindows` command and routing -- contract
- [x] `InputPolicy.cs` -- gate key-down, key-up, gamepad -- core behaviour
- [x] `Orchestrator.cs` -- publish managed set on attach/adopt/close/fail -- AD-15
- [x] `App/Platform/Input`, `CompositionRoot.cs`, `ShellWindow` -- Win32 probe, port, shell hwnd -- wiring
- [x] schemas/generated TS/contract tests -- new command registered
- [x] tests for the I/O matrix and Orchestrator publication
- [x] acceptance plan note -- new Alt+Tab scenario and S2-S12 need shell in foreground (do not touch results table)
- [x] AD-8 text: `shellHwnd` is set by the App (`ManagedWindows.SetShell`), not carried by the command (Core has no shell hwnd)

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

## Implementation Notes

- `SetManagedWindows` ma własny licznik `epoch` (`_managedEpoch`), niezależny od epoch routingu: zbiór okien i tryb wejścia to osobne kopie w InputHub. Komenda idzie **na końcu** wyniku, który zmienia zbiór (jedno miejsce: `Orchestrator.WithManagedWindows` wokół `Handle`), po `ActivateWindow` i routingu. Okno bez wpisu jest dla hooka obce, więc w tej mikroszczelinie klawisze przechodzą do aplikacji zamiast ginąć.
- Uchwyt shella: `CompositionRoot` robi `ManagedWindows.SetShell(WindowInteropHelper(window).Handle)` zaraz po `window.Show()`, więc `ShellWindow` nie został zmieniony. AD-8 (`architecture-mediastation.md`) poprawiony: komenda bez `shellHwnd`.
- Schemat `web/contracts/schema/set-managed-windows.schema.json` i `generated/set-managed-windows.ts` napisałem najpierw ręcznie (generator blokował Application Control, 0x800711C7). Przy późniejszym `dotnet publish` `MediaStation.SchemaExport` jednak się uruchomił i wygenerował schemat; kształt jest identyczny z ręcznym. Blokada jest więc przypadkowa (zależy od skrótu pliku), DW-28 w `initiative-mediastation-mvp/deferred-work.md` zostaje otwarty. Katalog `web/contracts` nie jest w gicie.
- Nowy `Execute(SetManagedWindows)` w `IInputRoutingPort` i publikacja z Orchestratora przesunęły `cmdId` w ~40 istniejących testach Orchestratora (komenda zajmuje id po każdej zmianie zbioru); asercje zaktualizowane, dodany helper `AssertManaged`.
- Notatka o S15 (Alt+Tab) i o scenariuszach wymagających shella na wierzchu dopisana w `epic-input-um760-acceptance.md` (sekcja Uwagi); tabela wyników nietknięta.

## Verification Evidence (ALDER, 2026-10-23)

- `dotnet test tests/MediaStation.Core.Tests -c Release --no-restore -p:SkipWebBuild=true`: 892 zielone, 0 błędów. Wiersze macierzy I/O pokrywa `ForegroundGateTests` (foreign w każdym trybie, managed, remote Home, key-up po przełączeniu okna, pad foreign + baseline, brak sondy), publikację zbioru `OrchestratorTests.Managed_windows_are_published_only_when_the_set_changes`, port `InputHubRoutingPortTests`, kontrakt `CoreMessageContractTests`.
- `npm run typecheck` i `npm run build` w `web/`: bez błędów.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true -p:SkipWebBuild=true` plus kopia `web/shell/dist` do `publish/shell`: OK.
- Uruchomienie opublikowanego `MediaStation.exe` na 6 s: log ma `start`, `inputRouting {shell, epoch 1}`, `powerSignalsRegistered`, `snapshotPublished`, `stop {exitCode 0}`; zero `inputHubError`, `warn`, `error`. Scenariuszy z fizycznym wejściem nie uruchamiano; Alt+Tab z klawiaturą zostaje na odbiór.
- UM760: nie dotyczy per ticket (AGENTS.md); do sprawdzenia przy odbiorze całej epiki (S15 i scenariusze z shellem na wierzchu).

## Review Triage Log

Review `quick` wykonany przeze mnie przez inspekcję diffu, bez subagenta (narzędzie Agent wymaga wyraźnej prośby użytkownika).

| Finding | Verdict | Evidence |
|---------|---------|----------|
| Sonda wołana na każdy key-down zmapowanego klawisza i każdą migawkę pada (hook LL) | false | Trzy tanie wywołania Win32 (`GetForegroundWindow`, `GetAncestor`, `GetWindowThreadProcessId`) bez blokad; klawisze niezmapowane wracają przed sondą. |
| Key-up nie wraca do decyzji key-down po zmianie okna | false | `HeldKey.Swallow` zapisany na key-down; testy `Key_up_follows_the_key_down_decision_after_a_window_switch` i `Key_up_of_a_passed_key_passes_after_returning_to_the_shell`. |
| Okno aplikacji może być na wierzchu przed publikacją zbioru | low | Komenda jest na końcu batcha; do jej wykonania klawisze przechodzą do aplikacji zamiast ginąć; WindowManager i hosty jeszcze nie istnieją. Bez zmiany. |
