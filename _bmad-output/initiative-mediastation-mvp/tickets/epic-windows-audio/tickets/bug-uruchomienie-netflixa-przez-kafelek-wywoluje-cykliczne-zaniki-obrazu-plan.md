---
title: 'Stabilne uruchamianie Netflixa bez zaniku obrazu'
type: 'bugfix'
ticket: '18'
created: '2026-10-29'
status: 'built'
baseline_revision: '617688a'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran:
  - blind-hunter
  - edge-case-hunter
  - verification-gap
  - intent-alignment
review_loop_iteration: 0
context:
  - '_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio.md'
  - 'tools/check-web-start.ps1'
warnings: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Uruchomienie Netflixa z kafelka poprawnie tworzy i podpina okno Edge, ale na UM760 ekran cyklicznie gaśnie i wraca do static. Objaw znika po zamknięciu MediaStation, więc trzeba rozdzielić konflikt Z-order/aktywacji okna od audio, profilu Edge i zmiany trybu wyświetlania.

**Approach:** Ustabilizować aktywację żywego okna Chromium kiosk: uzyskać foreground, a następnie jawnie utrzymać okno przeglądarki nad paskiem zadań bez zmiany geometrii ani trybu monitora. Rozszerzyć smoke test o trzy cykle start → Home → ponowna aktywacja, pomiary geometrii/Z-order/taskbara, wpisy JSONL i zrzuty, oraz zachować testy audio i kontraktów okna.

## Boundaries & Constraints

**Always:** tylko WindowManager zmienia widoczność, Z-order i foreground; `--kiosk` oraz istniejący profil Netflixa pozostają bez zmian; logi muszą potwierdzać pojedynczy attach, aktywację i rzeczywistą regułę audio.

**Never:** nie zmieniać routingu audio jako substytutu naprawy obrazu, nie dodawać globalnego `TOPMOST` dla shella/Moonlighta, nie maskować braku okna przez sztuczne `appWindowAttached`, nie traktować testów automatycznych jako odbioru UM760.

</frozen-after-approval>

## Code Map

- `src/MediaStation.App/Platform/Window/IWindowApi.cs` -- kontrakt operacji podniesienia okna; rozszerzyć semantykę tak, by adapter mógł rozróżnić Chromium kiosk od pozostałych okien.
- `src/MediaStation.App/Platform/Window/Win32WindowApi.cs` -- jedyne wywołania Win32 Z-order; bezpiecznie podnosić wyłącznie klasy `Chrome_WidgetWin_*`, zachowując `SWP_ASYNCWINDOWPOS`.
- `src/MediaStation.App/Platform/Window/WindowCommandExecutor.cs` -- `ActivateWindow`: restore, foreground-unlock i raportowanie błędu; kolejność musi zakończyć się podniesieniem okna dopiero po udanym foreground.
- `tests/MediaStation.Core.Tests/WindowCommandExecutorTests.cs` -- fake `IWindowApi` i asercje kolejności/odmowy podniesienia.
- `tools/check-web-start.ps1` -- istniejący real-process smoke Edge; dodać dowód stabilności obrazu/monitora/taskbara dla pierwszego wejścia, Home i co najmniej trzech re-entry.
- `src/MediaStation.App/Hosts/DevHarness/DevHarnessHostPort.cs` -- loguje `appLaunch`, `appWindowAttached`, etap procesu i root PID; nie zmieniać odpowiedzialności hosta za foreground.

## Tasks & Acceptance

**Execution:**
- [x] Zmienić adapter WindowManagera i wykonawcę aktywacji, aby Chromium kiosk był wynoszony nad taskbar dopiero po udanym foreground; zachować błędy `CommandFailed`.
- [x] Rozszerzyć testy jednostkowe o kolejność `restore → foreground → raise` oraz odmowę `raise`.
- [x] Rozszerzyć `check-web-start.ps1` o trzy cykle, pomiary geometrii/Z-order/taskbara, JSONL/PNG i asercje braku zmiany trybu monitora.
- [x] Uruchomić testy, publish i smoke; zapisać dokładne komendy, kody wyjścia, log assertions i ścieżkę dowodów.

**Acceptance Criteria:**
- Given świeży profil Netflixa i działający preflight, when MediaStation uruchamia kafelek, then log ma dokładnie jeden `appWindowAttached`, zakończone `ActivateWindow` i `audioRuleApplied muted=false` z sesją, bez błędów startu.
- Given aktywne okno Edge kiosk, when wykonane są co najmniej trzy cykle Home → ponowne wejście, then geometria okna i monitor pozostają stałe, okno jest widoczne/sterowalne, a taskbar nie zasłania obrazu.
- Given testy WindowManagera i AudioRoutera, when wykonują ścieżki aktywacji, foreground, mute/unmute i nowej sesji, then przechodzą bez dowodu niezamierzonej zmiany display mode ani wpływu audio na obraz.
- Given świeży publish na bieżącej maszynie, when użytkownik wykona odbiór UM760, then zapisze obserwację obrazu oraz odpowiadające wpisy JSONL; ten check pozostaje manualnym handoffem całej epiki.

## Implementation Notes

Implemented foreground-first activation with a Chromium-window guard and a post-foreground raise. The smoke now requires three cycles, validates Home monitor/display invariants, checks full taskbar coverage, records virtual-screen PNG/JSONL evidence, and fails on invalid window/monitor reads. On UM760 (`MEDIASTATION`, `F7BSI`, `EliteMini Series`) the clean and leftover/adoption passes completed with one attach, zero process exits and zero unexpected warnings. `EnumDisplaySettings` frequency/bits are unavailable on this host, so evidence records `available: false` and compares reliable monitor width/height instead.

## Plan Change Log

## Review Triage Log

- **medium — patch:** `check-web-start.ps1` accepted fewer than three cycles; changed `ValidateRange` to require 3.
- **medium — patch:** geometry, monitor, taskbar and display API failures could become zero/default evidence; added explicit validity checks and recorded display-mode availability.
- **medium — patch:** taskbar check only compared one adjacent Z-order window; changed it to require the restored browser rectangle to cover the taskbar rectangle.
- **medium — patch:** screenshot failures were swallowed; failures now add a smoke problem, and capture uses the virtual-screen origin.
- **medium — patch:** Home measurements were unused; Home monitor and display dimensions are now compared with first-entry evidence.
- **medium — patch:** foreground-denied activation did not pin the no-raise invariant; test now asserts neither Chromium detection nor raise runs after denial.
- **false — reject:** Chromium class-prefix detection was claimed to raise arbitrary popups. `ActivateWindow` receives the managed `AppWindowAttached` root window, and the adapter does not apply global TOPMOST; popup discovery is outside this call path.
- **medium — defer:** full display mode frequency/bits and visual black/static confirmation remain unavailable to automation on this host; settle with the user's whole-epic UM760 visual acceptance and a host where `EnumDisplaySettings` is available.

## Verification

**Commands:**
- `dotnet test MediaStation.slnx --configuration Release` -- expected: all tests pass.
- `dotnet publish src/MediaStation.App/MediaStation.App.csproj --configuration Release --runtime win-x64 --self-contained false -p:PublishSingleFile=true` -- expected: publish succeeds.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-web-start.ps1 -Cycles 3` -- expected: Edge remains stable and evidence is written for first entry, Home and each re-entry.
- `git diff --check` -- expected: no whitespace errors.

**Manual checks (if no CLI):**
- On UM760, inspect the three-cycle screen result and JSONL/PNG evidence; confirm no black/static cycle and confirm closing MediaStation leaves display mode unchanged. Whole-epic acceptance remains with the user via `fp-acceptance`.

Verification evidence: `dotnet test MediaStation.slnx --configuration Release --no-restore` passed (1294 passed, 1 pre-existing skipped); publish succeeded; `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-web-start.ps1 -Cycles 3` passed clean and leftover/adoption runs with one `appWindowAttached` each, zero `processExited`, zero unexpected warnings, three Home/re-entry cycles, stable monitor/window dimensions and 14 PNG/JSONL records in `C:\Users\Użytkownik\AppData\Local\MediaStation\logs\web-start-evidence-20261029-130226`; `git diff --cached --check` passed. Current machine was UM760 (`MEDIASTATION`, `F7BSI`, `EliteMini Series`).
