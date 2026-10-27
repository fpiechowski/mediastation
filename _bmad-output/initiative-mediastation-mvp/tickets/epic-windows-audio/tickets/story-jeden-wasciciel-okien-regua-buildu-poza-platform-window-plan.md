---
title: 'Jeden właściciel okien: reguła buildu poza Platform/Window'
type: 'feature'
ticket: '6'
created: '2026-10-27'
status: done
baseline_revision: '793eb0f42b5d9f167b886c4af756270b523d5a82'
route: 'oneshot'
route_source: 'auto'
risk: 'low'
review: 'thorough'
review_source: 'pinned'
lenses_ran: [blind-hunter, edge-case-hunter, verification-gap, intent-alignment]
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** R4 (AD-14, AD-15): tylko WindowManager zmienia widoczność, Z-order i foreground okien, ale build App tego nie egzekwuje; host, dev harness albo `ShellWindow` mogą dopisać `SetForegroundWindow` czy `Window.Activate()` bez błędu kompilacji.

**Approach:** Dodać do `MediaStation.App` `Microsoft.CodeAnalysis.BannedApiAnalyzers` z `BannedSymbols.txt` (wzór: Core, RS0030, `TreatWarningsAsErrors` już włączone) zakazującym wywołań okiennych Win32 (CsWin32 `PInvoke`) i WPF `Window`; folder `Platform/Window` wyłączony `.editorconfig` dla RS0030, a pierwsze `window.Show()` shella w `CompositionRoot.cs` jedynym wyjątkiem z uzasadnieniem (`#pragma`). Reguła jest sprawdzona przez tymczasowe dopisanie zakazanego wywołania w `Hosts/DevHarness` i `Shell`, opis w planie.

</intent-contract>

## Implementation Notes

Oneshot: zmiana to jeden plik zakazów, jedna linia csproj, jeden `.editorconfig` i jeden `#pragma`, poniżej 100 linii.

## Verification

**Commands:**
- `dotnet build` -- expected: bez ostrzeżeń RS0030 w App
- dopisanie `PInvoke.SetForegroundWindow` i `Window.Activate()` w `Hosts/DevHarness` i `Shell` -- expected: błąd RS0030, potem cofnięte
- `dotnet test` -- expected: przechodzi

### Implementation log (ran on UM760)

- Files: `src/MediaStation.App/BannedSymbols.txt` (new, 18 entries), `MediaStation.App.csproj` (BannedApiAnalyzers 5.6.0 + AdditionalFiles), `src/MediaStation.App/Platform/Window/.editorconfig` (RS0030 none for the folder), `CompositionRoot.cs` (`#pragma warning disable RS0030` around the first `window.Show()` with justification).
- CsWin32 `PInvoke` methods need full signatures in the doc ID (name-only matched nothing); WPF entries match by name. Only `SetForegroundWindow`, `ShowWindowAsync`, `SetWindowPos`, `AttachThreadInput` exist in the generated code today; the rest (`ShowWindow`, `BringWindowToTop`, ...) are listed so that adding them to `NativeMethods.txt` is still caught.
- Negative check (done and reverted): temporary files in `Hosts/DevHarness` and `Shell` calling the four generated `PInvoke` methods plus `Window.Activate/Topmost/WindowState/Show` gave 16 RS0030 errors (8 per folder); files deleted afterwards.
- `dotnet build`: 0 warnings, 0 errors. `dotnet test`: 1191 passed, 0 failed.
- Machine: `powershell Get-CimInstance` identity check printed `MEDIASTATION`, board `F7BSI`, model `EliteMini Series` = **UM760**.
- Review patch re-run (UM760): `dotnet build` 0 warnings / 0 errors, `dotnet test` 1191 passed. Extra check: temporarily adding `ShowWindow`, `BringWindowToTop`, `SwitchToThisWindow`, `SetActiveWindow`, `SetFocus`, `AllowSetForegroundWindow`, `OpenIcon`, `CloseWindow` to `NativeMethods.txt` plus calls and `Window.Hide/ShowDialog` in `Shell` gave 10 RS0030 errors, so all 18 entries resolve; reverted.

## Review Triage Log

### 2026-10-27 — Review pass
- verdicts: 13 rows (reviewer findings grouped by identical claim; each row names the lenses) — high 0, medium 0, low 9, false 4, maybe-false 0
- findings:
  - `[low]` `[patch]` `Window.Show` reason text names CompositionRoot as the only exception while Platform/Window is exempt too (edge-case) — reason text corrected in `BannedSymbols.txt`.
  - `[low]` `[reject]` No committed test pins the rule; an entry that stops matching after a CsWin32 bump is silent (blind, edge-case, verification-gap, intent-alignment) — the ticket's `verify` asks for a manual negative check recorded in the plan; a source-scan test adds a new test surface beyond the intent. All 18 entries were resolved by probe (see log).
  - `[low]` `[reject]` Ban list lacks `Visibility`, `Owner`, `Focus`, `Close`, `SetParent`, `MoveWindow`, `SetWindowLong`, `SetWindowPlacement`, `FlashWindow`, `WindowInteropHelper` (blind, edge-case) — ticket lists symbols with "m.in."; none of these are in `NativeMethods.txt` or used in App; `UIElement.Visibility` is used on controls so banning it is overbroad.
  - `[low]` `[reject]` Hand-written `DllImport`/`LibraryImport` bypasses the `PInvoke` ban (blind, edge-case, verification-gap, intent-alignment) — none exist outside generated code (grep), and CsWin32 output itself uses these attributes, so a ban needs exemptions; not caused by this change.
  - `[low]` `[reject]` `.editorconfig` exempts every file in `Platform/Window`, not only WindowManager (blind, edge-case) — the ticket defines the exemption as the folder.
  - `[false]` `#pragma` silences the whole line / later additions (blind, edge-case) — it wraps exactly one statement (`CompositionRoot.cs:221-223`); any extra call there shows in the diff.
  - `[low]` `[reject]` Reads of `Topmost`/`WindowState` are banned too (blind) — conservative by design, no reads exist; fix would add guards.
  - `[false]` `ticket: '6'` does not match 5.6 (intent-alignment) — step-01 sets `ticket` to find's `id` (6), `ref` is 5.6.
  - `[false]` Package version not matching Core (blind) — both use BannedApiAnalyzers 5.6.0.
  - `[false]` Other projects uncovered (edge-case, blind) — `Hosts/DevHarness` and `Shell` live in `MediaStation.App`.
  - `[low]` `[reject]` Reason text gives no pointer to WindowManager API (blind) — cosmetic.
  - `[low]` `[reject]` `ShowDialog`/`Hide` unprobed, Debug vs Release, shadowing `new Show`, machine/test evidence and plan-section wording (blind, intent-alignment) — covered by the extra probe (ShowDialog/Hide), analyzers do not vary by configuration, the rest are plan edits or hypothetical.
  - `[low]` `[reject]` Eight `PInvoke` entries match nothing today and could have wrong signatures (blind, verification-gap, intent-alignment) — verified by probe after review, all resolve.

## Auto Run Result

Status: built

**Summary:** R4 is now a build rule in `MediaStation.App`: `BannedApiAnalyzers` with `BannedSymbols.txt` bans the window-changing CsWin32 `PInvoke` calls and WPF `Window` members. `Platform/Window` is exempt through a folder `.editorconfig`, and the first `window.Show()` in `CompositionRoot.cs` is the one marked exception.

**Files changed:**
- `src/MediaStation.App/BannedSymbols.txt` — 18 banned symbols with AD-14/AD-15 reasons.
- `src/MediaStation.App/MediaStation.App.csproj` — analyzer package and `AdditionalFiles` entry.
- `src/MediaStation.App/Platform/Window/.editorconfig` — RS0030 off for the folder.
- `src/MediaStation.App/CompositionRoot.cs` — justified `#pragma warning disable RS0030` around the first `window.Show()`.

**Review:** patches applied 1 (low); deferred 0; rejected 10 groups with reasons in the triage log.
**Follow-up review recommended:** false (one low patch).
**Verification (UM760):** `dotnet build` 0 warnings / 0 errors; `dotnet test` 1191 passed, 0 failed; negative probes in `Hosts/DevHarness` and `Shell` gave RS0030 errors for the four generated calls and all four `Window` members (16 errors), a second probe for the other eight `PInvoke` entries plus `Hide`/`ShowDialog` gave 10; all probes reverted.
**Residual risks:** the rule is not pinned by a committed test, so a CsWin32 bump that changes generated signatures could make an entry silently stop matching; raw `DllImport` in App is not banned.
