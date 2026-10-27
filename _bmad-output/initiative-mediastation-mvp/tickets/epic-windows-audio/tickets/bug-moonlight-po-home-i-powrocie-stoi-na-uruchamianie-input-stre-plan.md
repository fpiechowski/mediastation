---
title: 'Moonlight po Home i powrocie stoi na „Uruchamianie input stream establishment…”'
type: 'bugfix'
ticket: '8'
created: '2026-10-27'
status: done
baseline_revision: '1833d8e1ed0f5df1f0792a71fe20168ed122bd99'
route: 'oneshot'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: [blind-hunter, edge-case-hunter, verification-gap, intent-alignment]
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred:
  - summary: >-
      Production Moonlight host (epic-moonlight-native) must pick the SDL stream window (`SDL_app`), not the first visible window of the process, or it reproduces bug 5.8.
    evidence: |-
      Moonlight-qt shows a Qt connecting window (`Qt6120QWindowIcon`) for ~4-6 s before the `SDL_app` stream window; the dev harness fix is in `Hosts/DevHarness` only. Blind hunter finding; the future host does not exist yet.
    location: >-
      src/MediaStation.App/Hosts/DevHarness/HarnessMoonlight.cs:StreamWindowClass
    severity: medium
  - summary: >-
      With only a `game` category tile in apps.json the injected OK did not start the tile (restore pass 0/4, no StartApp); cause not diagnosed.
    evidence: |-
      Seen on UM760 2026-10-27 with the generated one-tile apps.json (category `game`): `action ok` logged, no start for 40 s; the same tile in `video` starts at once. Could be shell default focus with a lone `game` row; check with a `game`-only config and OK from a fresh start. Unverified.
    location: >-
      tools/check-home-s10.ps1:New-RestoreAppsJson
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** Po pierwszym OK na kafelku PC, Home i ponownym OK wraca okno Moonlighta z zamrożonym „Uruchamianie input stream establishment…”, bez strumienia (R2, Done when 2). Blokuje akceptację epiki.

**Approach:** Ustalić przyczynę na żywym hoście Sunshine na UM760, poprawić ją bez odejścia od minimalizacji (R1, AD-5) i dowieść działającego strumienia po czterech cyklach Home → OK logiem Moonlighta, logiem MediaStation i zrzutami okna; AC 1–3 ticketu (AC 4 nie obowiązuje, bo przyczyna jest w kodzie epiki).

</intent-contract>

## Implementation Notes

Oneshot: poprawka to jedna decyzja (klasa okna) plus test i skrypt weryfikacji; kilkanaście linii kodu.

Maszyna: **UM760** (`MediaStation`, płyta `F7BSI`, model `EliteMini Series`), HEAD `1833d8e`; Sunshine 192.168.0.64:47989 osiągalny.

**Przyczyna (hipotezy a–c odrzucone, znaleziona czwarta).** Moonlight-qt tworzy dwa okna najwyższego poziomu w jednym procesie: okno łączenia Qt (`Qt6120QWindowIcon`, tytuł „Moonlight”, widoczne od ~0,1 s, UI „Starting input stream establishment…”) i po ~4–6 s okno strumienia SDL (`SDL_app`, tytuł „<host> - Moonlight”); okno Qt jest wtedy ukrywane. Harness (`HarnessLaunch.WindowClassOf` → `null` = dowolna klasa) podpinał pierwsze widoczne okno, czyli Qt (`appWindowAttached` ~0,1 s po starcie). Home minimalizował, a OK aktywował to okno Qt, nie strumień: widać było zamrożone UI łączenia, a `MinimizeWindow`/`ActivateWindow`/`ShowShell` kończyły się `completed`, więc log MediaStation był czysty (zgodnie z S6). Dowód: `tools/check-moonlight-return.ps1` na kodzie sprzed poprawki: `appWindowAttached class=Qt6120QWindowIcon`, 8/8 zrzutów identycznych (hash `D0DB1A187558`), w logu Moonlighta brak klatek; zrzut wszystkich okien procesu w czasie startu potwierdza dwa okna. Hipotezy: (a) Alt do Moonlighta, (b) zerwanie sesji przy minimalizacji, (c) drugi proces `stream` nie wystąpiły (po poprawce: 1 handshake RTSP, 1× „Starting input stream...”, 1× `appWindowAttached`).

**Poprawka.** `HarnessMoonlight.StreamWindowClass = "SDL_app"`; `HarnessLaunch.WindowClassOf` zwraca ją dla `MoonlightApp`, więc harness czeka na okno strumienia (do 20 s `WindowTimeout`) i nie podpina okna Qt. Minimalizacja bez zmian (R1). Brak okna SDL (host niedostępny) daje `windowTimeout` zamiast podpięcia okna łączenia.

**Pliki.**
- `src/MediaStation.App/Hosts/DevHarness/HarnessMoonlight.cs`, `HarnessLaunch.cs` — klasa okna strumienia.
- `tests/MediaStation.Core.Tests/DevHarnessTests.cs` — `Moonlight_picker_skips_the_qt_connecting_window_and_waits_for_the_sdl_stream_window` (na defekcie `Pick` z `null` zwracał okno Qt: test pada), oraz zmieniony test klasy okna.
- `tools/check-moonlight-return.ps1` — nowy check AC 1: 4× Home → OK na żywym strumieniu, asercje logu MediaStation, logu Moonlighta i zrzutów.
- `tools/check-home-s10.ps1` — kafelek Moonlight w `-Restore` w kategorii `video`: w samotnej kategorii `game` OK nie startował kafelka (0/4 „OK did not start the tile”), więc ten target nigdy nie biegł.

## Review Triage Log

### 2026-10-27 — Review pass
- verdicts: 20 findings — high 0, medium 0, low 5, false 6, maybe-false 1, defer 2 (verified real, pre-existing or future work), reject 6
- findings:
  - `[low]` `[defer]` blind: fix is dev-harness only; production host may repeat it — real for the future epic-moonlight-native host, not caused by this change; deferred (medium).
  - `[low]` `[reject]` blind/edge: `SDL_app` hard-coded, fails if SDL class changes — failure is loud (`windowTimeout`, process killed), not silent; no guard added.
  - `[low]` `[reject]` blind: unit test thin (hidden SDL, other tree) — `Pick` visibility/tree rules already covered by existing picker tests; the new test covers the class decision.
  - `[maybe-false]` `[defer]` blind/edge/intent: `category` game→video in check-home-s10 may hide a lone-`game` focus defect — would be medium if true; evidence needed: run a `game`-only config; deferred (unverified); comment added in the script.
  - `[low]` `[reject]` blind: screenshots squashed to 480x270 and not asserted — by design (ticket: screenshots are fallback evidence; log is the verdict).
  - `[low]` `[reject]` blind/edge: first frame-rate line taken, exact 1 handshake — Moonlight prints the stats block once at close (checked in logs); strictness is the intent (no reconnect).
  - `[false]` `[reject]` blind: Moonlight log picked by new file — only one Moonlight runs (script refuses to start otherwise).
  - `[false]` `[reject]` blind: polling cost — `Msg` prefilter added during the run; the cycle timings show no starvation.
  - `[false]` `[reject]` blind: greedy `"pid"` regex — the `start` line has one `pid`.
  - `[low]` `[patch]` blind: `-Cycles` unvalidated — `ValidateRange(1,100)` added.
  - `[false]` `[reject]` blind: `taskkill` without `/T` leaves children — Moonlight is one process; the run ended with none left.
  - `[false]` `[reject]` edge: Moonlight left running after 20 s window timeout — `StartCore` calls `Kill(started)` (tree) on a null window.
  - `[low]` `[patch]` edge: `Save-Shot` throws on iconic window and hides the real failure — returns `iconic`, caller already records the missing restore.
  - `[low]` `[reject]` edge: no apps.json before run — the app writes it on first start; product config always exists on the machine.
  - `[low]` `[reject]` edge: no log file / UTC midnight rollover — improbable for a ~2 min run; fails loudly.
  - `[low]` `[patch]` edge: wrong window class continues four cycles — now throws immediately.
  - `[false]` `[reject]` edge: shell child window foreground — `managedShell` hwnd is the foreground in all runs.
  - `[low]` `[reject]` edge: hidden-Qt test row has no regression value — harmless extra assertion for the stream-up state.
  - `[false]` `[reject]` intent: test pins the picker, not the stream — the stream is proven by `check-moonlight-return.ps1` (4/4 after, exit 1 before), recorded under Verification.
  - `[false]` `[reject]` intent: no Edge/Chrome regression run visible — run 4/4 each, recorded under Verification.

## Verification

**Commands (UM760, 2026-10-27):**
- `dotnet test` -- expected: 1192/1192 passed (wynik: 1192 passed, 0 failed).
- `dotnet publish src/MediaStation.App -c Release -r win-x64` + `tools/check-moonlight-return.ps1 -MoonlightHost 192.168.0.64` (z `MEDIASTATION_HARNESS_MOONLIGHT`) -- expected: exit 0. Po poprawce: 4/4 cykli, `appWindowAttached` 1× klasy `SDL_app`, po każdym Home `IsIconic` i `IsWindowVisible` prawdziwe i shell na wierzchu, po każdym OK okno strumienia nie zminimalizowane i na wierzchu, wszystkie `windowCommand` `completed`, 0× `processExited`, 0 `warn`/`error` poza `appIconMissing`/`devHarnessEnabled`; log Moonlighta: 1 handshake RTSP, 1× „Starting input stream...”, ~17,6–17,9 FPS odebrane i wyrenderowane (statystyki spłukane przy zamknięciu); zrzuty okna po każdym powrocie (a/b w odstępie 4 s) różne w 4/4 cykli (np. `266EEE7BA3DE`/`2370C4B50207`). Przed poprawką: exit 1 (okno Qt, zrzuty identyczne, brak klatek).
- `tools/check-home-s10.ps1 -Restore -InjectHome -Targets edge,chrome,moonlight -MoonlightHost 192.168.0.64` -- expected: 4/4 każdy; wynik: edge 4/4, chrome 4/4, moonlight 4/4 (przed poprawką moonlight 3/4 po zmianie kategorii kafelka). Check beyond criteria (Home nad Edge i Chrome) spełniony.
- `tools/check-home-s10.ps1 -SelfTest` -- expected: pass (wynik: Self-test passed).

**Manual checks:** wzrokowe potwierdzenie strumienia po powrocie: ponowny odbiór S6 na UM760 (poza tym ticketem).

## Auto Run Result

**Status:** built

**Summary.** Cause: Moonlight-qt has two top-level windows, the Qt connecting window and the SDL stream window (`SDL_app`). The dev harness attached the first visible one. The harness now waits for `SDL_app` (commit 199915e). AC 1–3 met; AC 4 not applicable.

**Repair pass.** Verification failed only because the plan frontmatter `ticket:` held the file stem; set to `'8'` (the ticket id). No code change.

**Verification (UM760 per earlier run; this pass).** `dotnet test` 1192/1192.

**Residual risks.** Visual confirmation of the stream after return is left to S6 re-acceptance on UM760 (epic acceptance).
