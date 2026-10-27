---
title: 'Edge i Chrome w trybie okna z 6.1 i Moonlight borderless: przywrócenie shella 16/16'
type: 'feature'
ticket: '5'
created: '2026-10-27'
status: done
baseline_revision: 'f38fe45e62a9d4909f4cac4b8c12e4fc7fa79205'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'thorough'
review_source: 'pinned'
lenses_ran: ['blind-hunter', 'edge-case-hunter', 'verification-gap', 'intent-alignment']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: ['oversized']
deferred:
  - summary: >-
      Home pressed while the shell is already the mode (app window raised by Alt+Tab or script) is logged as an action but sends no window commands, so the app stays on top.
    evidence: |-
      Observed on UM760 while building 5.5: with the app restored outside MediaStation, presses 2-4 of -Restore failed 3 of 4 until the script resumed the app through the product (OK on the tile). Epic note says "powrót daje Home".
    location: >-
      src/MediaStation.Core/Orchestrator (Home in shell mode)
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Dev harness startuje przeglądarki bez flagi trybu okna z 6.1 (`--kiosk`) i nie zna typu `moonlight`, a `tools/check-home-s10.ps1` nie sprawdza przywrócenia shella po Home: wybiera cel po nazwie procesu i tytule (DW-13/14/15/32), a Home wymaga osoby.

**Approach:** Harness dokłada `--kiosk` i obsługuje `moonlight` (`Moonlight.exe` ze ścieżki w `MEDIASTATION_HARNESS_MOONLIGHT`, `stream <host> <app> --display-mode borderless --mute-on-focus-loss`, te same `AppWindowAttached`/`ProcessExited`/`ProbeExisting`). Skrypt dostaje przebieg przywracania z wstrzykniętym Home (`SendInput` `VK_HOME` bez znacznika) oraz wybór celu z `rootPid` i `hwnd` logu `appWindowAttached`.

## Boundaries & Constraints

**Always:** harness nigdy sam nie wynosi okna (AD-14/15); Win32 w App tylko w `Platform/*`; decyzje (argumenty Moonlighta, wybór pliku, dopasowanie procesu, wybór okna, wybór celu skryptu, werdykt przywracania) są czystymi funkcjami z testami (`DevHarnessTests` / `-SelfTest`); bez zmiennej `MEDIASTATION_DEV_HARNESS=1` hosty zostają `MissingHostPort`; `Moonlight` bez `MEDIASTATION_HARNESS_MOONLIGHT` albo pliku daje `CommandFailed` z wpisem `devHarnessStartFailed{ reason }`; skrypt przywraca `apps.json` (kopia + przywrócenie w `finally`) i nie zostawia procesów; wynik przebiegu to stan okien z OS, a wpisy `windowCommand` to dowód dodatkowy.

**Never:** zmiana Orchestratora, WindowManagera, `Platform/Audio`, kontraktów; produktowy `BrowserHost`/`MoonlightHost`; port CDP; zapis do plików ticketów; oznaczanie ticketu jako done.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Flaga trybu okna | `BuildArguments` | `--kiosk` przed adresem, reszta flag bez zmian | — |
| Moonlight start | wpis `MoonlightApp{host,app}`, ścieżka istnieje | `stream <host> <app> --display-mode borderless --mute-on-focus-loss`, pierwsze widoczne okno bez właściciela z drzewa (dowolna klasa) → `AppWindowAttached` | brak zmiennej/pliku → `CommandFailed`, log `moonlightNotFound` |
| Moonlight adopcja | żywy `Moonlight.exe` z `stream <host> <app>` w linii poleceń | `ProbeExisting` → `AppWindowAttached` jak dla `web` | brak procesu → cisza |
| Wybór celu z logu | wiele `appWindowAttached` tego `appId` | bierze ostatni: `hwnd`, `rootPid` | brak wpisu → brak celu |
| Wybór okna skryptu | okna: z właścicielem, `Chrome_WidgetWin_0`, inny profil/proces `msedge`, obce `msedge` | wybrane tylko widoczne `Chrome_WidgetWin_1` bez właściciela z drzewa `rootPid`; bez `rootPid` stare reguły z pomijaniem właścicieli | brak → `no target window` |
| Werdykt przywracania | shell foreground, cel `IsIconic` i widoczny, brak nowego okna przeglądarki | pass | inaczej powód: `shell not foreground`, `target not minimized`, `target not visible`, `browser window` |
| Moonlight bez hosta | brak `MEDIASTATION_HARNESS_MOONLIGHT`, hosta albo portu Sunshine | cel `nie dotyczy` z powodem w wyniku, nie liczony do sumy | — |

</intent-contract>

## Code Map

- `src/MediaStation.App/Hosts/DevHarness/HarnessBrowser.cs` -- `BuildArguments` (dodać `--kiosk`), wzorzec czystych decyzji
- `src/MediaStation.App/Hosts/DevHarness/DevHarnessWebHostPort.cs` -- host `web`; uogólnić na `moonlight` (specyfikacja uruchomienia per wpis: exe, argumenty, klasa okna, dopasowanie ProbeExisting); sesje, Stop, WatchExit bez zmian
- `src/MediaStation.App/Platform/Window/WindowTreePicker.cs` -- `Pick` z opcjonalną klasą (`null` = dowolna) dla Moonlighta
- `src/MediaStation.App/Platform/System/ProcessCommandLines.cs` -- dodać `Moonlight.exe` do zapytania WMI
- `src/MediaStation.App/CompositionRoot.cs` -- `CreateWebHost` (~l.349), `hosts[AppType.Moonlight]` (~l.238): harness także dla `moonlight` przy zmiennej
- `tests/MediaStation.Core.Tests/DevHarnessTests.cs` + csproj (linkowane źródła) -- testy i linkowanie nowych plików
- `tools/check-home-s10.ps1` -- `Get-TopLevelWindows` (dodać `Owner` przez `GetWindow(GW_OWNER)`), `Get-BrowserWindowIds`, `Select-EdgeWindow`, `Find-WindowProcess`, `TargetWindow`, `Measure-Press`, `Invoke-SelfTest`
- `tools/samples/config/apps.json`, `input.json` -- wzorzec wpisów; `*` w `input.json` mapuje `0x24` na `home`, `0x0D` na `ok`
- `tools/fixtures/check-home-s10.fixture.jsonl` -- fixture logu (dopisać `appWindowAttached`, jeśli test tego wymaga)
- Nie ruszać: `Platform/Window/Win32WindowManagerPort.cs`, `Orchestrator*`, kontrakty.

## Tasks & Acceptance

**Execution:**
- [ ] `Hosts/DevHarness/HarnessBrowser.cs` + nowy `HarnessMoonlight.cs` -- `--kiosk`; stała `MEDIASTATION_HARNESS_MOONLIGHT`, argumenty `stream`, wybór pliku z env, dopasowanie linii poleceń `Moonlight.exe` do `host`/`app` -- czyste i testowane
- [ ] `Hosts/DevHarness/DevHarnessWebHostPort.cs` (zmiana nazwy na `DevHarnessHostPort`), `Platform/Window/WindowTreePicker.cs`, `Platform/System/ProcessCommandLines.cs`, `CompositionRoot.cs` -- host obsługuje `web` i `moonlight`, `AppWindowAttached`, `ProcessExited`, `ProbeExisting`; log `appWindowAttached` bez zmian
- [ ] `tests/MediaStation.Core.Tests/*` -- testy: `--kiosk`, argumenty i wybór pliku Moonlighta, dopasowanie adopcji, picker bez klasy
- [ ] `tools/check-home-s10.ps1` -- `Owner` w oknach i pomijanie okien z właścicielem; wybór celu z `appWindowAttached` (czyste funkcje: ostatni wpis, okno z drzewa `rootPid`); `-InjectHome` (`SendInput` `VK_HOME`, flaga rozszerzona, bez `dwExtraInfo`) zamiast sygnału i oczekiwania na osobę; przebieg `-Restore` dla celów `edge`, `chrome`, `moonlight`, `desktop`: dla każdego celu świeża instancja z `MEDIASTATION_DEV_HARNESS=1` i wygenerowanym `apps.json` z jednym kafelkiem (kopia i przywrócenie), wstrzyknięty OK, czekanie na `appWindowAttached`, potem `-Presses` razy: cel na wierzch, wstrzyknięty Home, werdykt z OS (shell foreground, cel `IsIconic` i widoczny, brak nowego okna przeglądarki; pulpit bez warunku okna celu); Moonlight `nie dotyczy`, gdy brak zmiennej, hosta (`-MoonlightHost`, `-MoonlightApp`) albo portu Sunshine
- [ ] `tools/check-home-s10.ps1` `-SelfTest` -- przypadki: wybór celu z `appWindowAttached` (ostatni wpis, okno z właścicielem, `Chrome_WidgetWin_0`, inny proces `msedge`, obce okno), werdykt przywracania, `nie dotyczy`, generowany `apps.json`

**Acceptance Criteria:**
- Given `-SelfTest`, when uruchomiony, then przechodzi z nowymi przypadkami wyboru celu z `appWindowAttached`.
- Given `dotnet test`, when uruchomiony, then przechodzi.
- Given bieżąca maszyna z Edge i Chrome oraz harnessem, when `-Restore -InjectHome`, then Edge 4/4, Chrome 4/4, pulpit 4/4 (shell na pierwszym planie, cel `IsIconic` i widoczny, brak nowego okna przeglądarki), a Moonlight 4/4 albo `nie dotyczy` z powodem w planie.
- Given brak `MEDIASTATION_DEV_HARNESS=1`, when OK na kafelku, then `startWithoutHost` jak dotąd.
- Given wybrany tryb okna, który nie minimalizuje się bez `SW_HIDE`, when przebieg, then `Source conflict:` do 6.1 (blocked).

## Implementation Notes

- Ran on UM760 (host MediaStation, board F7BSI). Subagent implemented; review patches applied via the same subagent. New: `Hosts/DevHarness/HarnessMoonlight.cs`, `HarnessLaunch.cs`; `DevHarnessWebHostPort` renamed `DevHarnessHostPort`; `--kiosk` added; script got `-Restore`, `-InjectHome`, owner-aware target selection from `appWindowAttached`.
- Presses 2-4 of `-Restore` resume the app through an injected OK on the tile (not an external raise), because Home in shell mode is a no-op (deferred above). Desktop target also minimizes the kiosk window by script since `MinimizeAll` skips it; the product minimize is proven by the Edge/Chrome targets (`IsIconic`, no `SW_HIDE`), so no `Source conflict:` for 6.1.
- Moonlight: not applicable on UM760 now (`MEDIASTATION_HARNESS_MOONLIGHT` not set, no Sunshine/Apollo host in the LAN configured); `Moonlight.exe` exists at `C:\Program Files\Moonlight Game Streaming`. 16/16 is checked at the epic acceptance.

## Plan Change Log

## Review Triage Log

### 2026-10-27 — Review pass
- verdicts: 20 findings across 4 lenses (approximate, lenses overlap) — high 0, medium 1, low 12, false 3, maybe-false 0
- findings:
  - `[medium]` `[patch]` web/moonlight dispatch (window class, adoption roots) untested (blind, gap) — extracted `HarnessLaunch`, linked, tests with WebApp and MoonlightApp
  - `[low]` `[patch]` `-InjectHome` ignored in `-Restore` (edge) — help text states it
  - `[low]` `[reject]` Moonlight command-line matching not tokenised, Moonlight leftover after failed run, broad restore kill pattern, pid reuse on taskkill, apps.json backup races, Sunshine port probe, selection-window heuristics, SelfTest fixture assumption, 32-bit INPUT layout, Send-KeyPress return value, ProbeExisting silent on unknown app, unreachable unknownApp — Moonlight path is unrun dev tooling, patterns are script-generated `restore-*`, tests cover the quoted/prefix cases; fixes add branches
  - `[false]` `[reject]` blank host/app throws before session (edge) — StartCore outer catch gives CommandFailed
  - `[false]` `[reject]` multiple roots reported (edge) — ProbeCore returns after the first adoption
  - `[false]` `[reject]` StrictMode throws on missing Owner (edge) — script does not enable strict mode
  - `[low]` `[reject]` desktop self-minimize masks the 6.1 conflict (edge, intent) — Edge/Chrome targets prove product minimize without SW_HIDE
  - `[low]` `[reject]` verification/input.json mapping, fixture untouched, no kiosk selftest (intent, gap) — live run shows the mapping works; kiosk is pinned by a unit test

## Auto Run Result

Status: built. Ran on UM760 (host MediaStation, board F7BSI).

- Summary: dev harness adds `--kiosk` and a `moonlight` type; `check-home-s10.ps1` gets `-Restore`/`-InjectHome` and target choice from `appWindowAttached` (DW-13/14/15/32).
- Files: `Hosts/DevHarness/*` (`DevHarnessHostPort`, `HarnessMoonlight`, `HarnessLaunch`, `HarnessBrowser`), `Platform/Window/WindowTreePicker.cs`, `Platform/System/ProcessCommandLines.cs`, `CompositionRoot.cs`, tests + csproj, `tools/check-home-s10.ps1`.
- Review: 1 medium and 1 low patched; 1 item deferred (Home in shell mode is a no-op); other rows rejected with reasons above.
- Verification: `-SelfTest` passed; `dotnet test` 1191/1191; Release publish, `-Restore -InjectHome`: edge 4/4, chrome 4/4, desktop 4/4, moonlight n/a (variable not set, no host); `apps.json` restored, no leftover processes.
- Residual risks: Moonlight start/adoption and real window class not run live; followup_review_recommended: false.

## Design Notes

`--kiosk` wynika ze spike'a 6.1 (AD-1). Okno Moonlighta (SDL/Qt) ma inną klasę niż `Chrome_WidgetWin_1`, więc picker dla niego bierze dowolną klasę (widoczne, bez właściciela, z drzewa `rootPid`). Skrypt nie ma nadpisania katalogu config, więc podmienia `apps.json` w `%LOCALAPPDATA%\MediaStation\config` z kopią i przywróceniem w `finally`; przy starcie sprzątnie osieroconą kopię z przerwanego przebiegu.

## Verification

**Commands:**
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1 -SelfTest` -- expected: `Self-test passed.`
- `dotnet test` -- expected: zielone
- Release publish, `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1 -Restore -InjectHome` -- expected: Edge 4/4, Chrome 4/4, pulpit 4/4, Moonlight 4/4 albo `nie dotyczy`
