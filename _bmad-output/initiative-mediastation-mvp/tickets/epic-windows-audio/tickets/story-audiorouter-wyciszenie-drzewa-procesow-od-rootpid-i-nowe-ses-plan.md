---
title: 'AudioRouter: wyciszenie drzewa procesów od rootPid i nowe sesje audio'
type: 'feature'
ticket: '2'
created: '2026-10-26'
status: done
baseline_revision: '9a9ac4b5b229cac21c47c91b112d4cb7da02dc62'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: ['blind-hunter', 'edge-case-hunter', 'verification-gap', 'intent-alignment']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: ['oversized']
deferred:
  - summary: >-
      Sessions muted by a MediaStation instance that crashed or was killed stay muted after restart, because the engine ledger is empty and Unmute only restores what it muted.
    evidence: |-
      ISimpleAudioVolume mute persists for the life of the browser session; after supervised restart and adoption, UnmuteProcessTree finds no ledger entries. Settle by deciding in story 3 whether an explicit Unmute forces unmute of in-tree sessions outside the ledger.
    location: >-
      src/MediaStation.App/Platform/Audio/AudioRouterEngine.cs
    severity: medium
---

<intent-contract>

## Intent

**Problem:** Nic nie wycisza aplikacji w tle: po Home przeglądarka gra dalej, a nowa sesja audio w jej drzewie (Edge/Chrome trzymają sesję w procesie potomnym, S9) nie podlega żadnej regule.

**Approach:** Core dostaje komendy `MuteProcessTree`/`UnmuteProcessTree` i port `IAudioRouterPort`; Orchestrator wysyła je na ścieżkach Home z aplikacją na pierwszym planie (mute przed `MinimizeWindow`) i aktywacji (unmute po `ActivateWindow`). `Platform/Audio` na własnym wątku trzyma regułę drzewa, wylicza sesje na wszystkich aktywnych urządzeniach, przypisuje je do drzewa po łańcuchu PID i wycisza (`ISimpleAudioVolume`, fallback `IChannelAudioVolume`), a nową sesję/urządzenie obejmuje regułą w ok. 1 s.

## Boundaries & Constraints

**Always:** COM i Win32 tylko w `Platform/Audio` przez CsWin32 (nazwy do `Platform/System/NativeMethods.txt`); wątek AudioRoutera osobny (STA/MTA zgodnie z COM, AD-14), nigdy wątek Orchestratora ani UI; wykonawca nie rzuca przez port (AD-4), odpowiada `CommandCompleted` albo `CommandFailed{AudioUnavailable}`; dopasowanie drzewa, reguła (zbiór wyciszonych/odciszonych korzeni) i wybór mute vs kanały są czystą logiką poza typami COM, testowane przez linkowane źródła; logi `audioRuleApplied{ rootPid, muted, sessions }` i `audioSessionMuted{ rootPid, pid }`; fallback `IChannelAudioVolume`: wszystkie kanały na 0, odcisz przywraca zapamiętane poziomy; `MuteProcessTree`/`UnmuteProcessTree`/`AudioUnavailable` w `MessageTypes` i schematach (AD-16, build odświeża `web/contracts`, bez ręcznej edycji); działa wyłącznie na `rootPid` z Command (AD-15).

**Never:** pozostałe ścieżki Orchestratora (adopcja, kompensacja po Home w `Starting`, awaria, wake, CloseApp) to story 3, nie ruszać; bez `pause` strony; bez zmiany WindowManagera; `LogWindowManagerPort` bez zmian; bez `bmad-architecture`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Sesja w potomku | `rootPid` 100, sesja pid 300 (rodzic 200 → 100) | sesja w drzewie, dostaje mute | — |
| Obcy proces | sesja pid poza łańcuchem do `rootPid` | bez zmian | — |
| Brak `ISimpleAudioVolume` | E_NOINTERFACE | kanały `IChannelAudioVolume` na 0, odcisz przywraca zapamiętane | oba brak → pomija sesję, loguje |
| Nowa sesja, drzewo wyciszone | notyfikacja sesji/urządzenia | mute ≤ ok. 1 s, `audioSessionMuted` | — |
| Nowa sesja, drzewo odciszone | j.w. | bez zmian | — |
| Brak usługi audio | enumeracja urządzeń rzuca | `CommandFailed{AudioUnavailable}` | log, stan reguły zachowany |
| Home z aplikacją | `Foreground` | `[MuteProcessTree, MinimizeWindow, ShowShell, SetInputRouting]` | — |
| Aktywacja | `OnResume`, attach po `Starting` | `ActivateWindow` → `UnmuteProcessTree` → routing | — |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Contracts/AppCommands.cs` -- dopisać `AudioCommand` (baza, jak `WindowCommand`), `MuteProcessTree(CmdId, RootPid)`, `UnmuteProcessTree(CmdId, RootPid)`
- `src/MediaStation.Core/Contracts/Messages.cs` -- `ErrorCode.AudioUnavailable`; `MessageTypes.cs` -- rejestr; `tests/.../CoreMessageContractTests.cs`, `MessageTypesTests.cs`, `SchemaExportTests.cs` -- dopasować
- `src/MediaStation.Core/Ports/Ports.cs` -- `IAudioRouterPort { Execute(AudioCommand) }`
- `src/MediaStation.App/Orchestrator/CommandRouter.cs` -- dispatch `AudioCommand` do portu (nowy parametr konstruktora; zaktualizować `CommandRouterTests`)
- `src/MediaStation.Core/Orchestrator/Orchestrator.Home.cs` (`OnHome`), `Orchestrator.Launch.cs` (`OnResume`), `Orchestrator.Attach.cs` (gałąź `Foreground` po `Starting`) -- `AppState.RootPid` jest dostępny; zaktualizować `OrchestratorHome/Launch/Attach/Lifecycle/Loop/Failure/StopTests`
- `src/MediaStation.App/Platform/Window/Win32WindowManagerPort.cs`, `Win32ProcessSnapshot.cs` -- wzorzec wątku z kolejką i `PostThreadMessage`; `Win32ProcessSnapshot.ParentMap()` do reużycia przy drzewie PID
- `src/MediaStation.App/CompositionRoot.cs`, `TeardownSequence.cs` (+ `TeardownSequenceTests`) -- rejestracja i zwolnienie AudioRoutera; `MediaStation.Core.Tests.csproj` -- linkowanie nowych czystych plików
- `src/MediaStation.App/Hosts/DevHarness/HarnessBrowser.cs` -- `MEDIASTATION_HARNESS_URL` już obsługiwane; dodać `tools/fixtures/harness-audio.html` (zapętlony ton WebAudio, start po pierwszym renderze, autoplay flaga harnessu)

## Tasks & Acceptance

**Execution:**
- [x] Core: komendy, `ErrorCode.AudioUnavailable`, `IAudioRouterPort`, rejestr i testy kontraktu
- [x] Orchestrator: Home (`MuteProcessTree` przed `MinimizeWindow`) i aktywacja (`UnmuteProcessTree` po `ActivateWindow`) + poprawione testy
- [x] `Platform/Audio`: czyste `ProcessTreeMatcher` i `AudioRule` (zbiory korzeni, wybór sposobu wyciszenia, pamięć kanałów); wrapper COM za interfejsem; `Win32AudioRouterPort` z wątkiem, notyfikacjami sesji i urządzeń, logami
- [x] `CompositionRoot`/`TeardownSequence`/`CommandRouter`/csproj/`NativeMethods.txt` -- wiring
- [x] `tools/fixtures/harness-audio.html` + testy czystej logiki (drzewo, reguła, fallback kanałów, nowa sesja w wyciszonym/odciszonym drzewie, Home/aktywacja w Orchestratorze)

**Acceptance Criteria:**
- Given sesja w procesie potomnym `rootPid`, when `MuteProcessTree`, then jest wyciszona, a sesja obcego procesu nie.
- Given `Foreground` i Home, then komendy mają kolejność mute → minimize → showShell → routing; given aktywacja, then activate → unmute → routing.
- Given dev harness z `harness-audio.html` na UM760, when Home, then log `audioRuleApplied muted:true` dla `rootPid` Edge; OK na kafelku daje `muted:false`; po zabiciu potomnego `msedge.exe --type=utility` z usługą audio i odtworzeniu sesji przez Edge w wyciszonym drzewie `audioSessionMuted` w ok. 1 s.

## Implementation Notes

Maszyna: MEDIASTATION (UM760, płyta F7BSI, model EliteMini Series).

- `dotnet build` (z odświeżeniem schematów i TS: `mute-process-tree`, `unmute-process-tree`) 0 ostrzeżeń, 0 błędów; `dotnet test`: 1152 zielone.
- Silnik bez COM (`AudioRouterEngine`, `AudioRule`, `ProcessTreeMatcher`, `AudioSweepSchedule`) linkowany do testów; COM (`CoreAudioSessionBackend`) i wątek (`Win32AudioRouterPort`, MTA) tylko w App.
- Sprawdzenie na prawdziwym COM (jednorazowy program poza repo, port + backend z App): (1) potomek `powershell` z zapętlonym tonem pod korzeniem = proces testowy: `audioSessionMuted` dla sesji potomka, `audioRuleApplied muted:true sessions:1`, odcisz `muted:false`; (2) sesja utworzona po `MuteProcessTree`: `audioSessionMuted` ok. 0,2 s po starcie potomka; (3) Edge z `tools/fixtures/harness-audio.html` (osobny profil): reguła na PID głównym wyciszyła sesję procesu `--type=utility` (audio service), po jego zabiciu nowa sesja nowego procesu audio dostała `audioSessionMuted` ok. 1,4 s po zabiciu (czas głównie odtworzenia kontekstu przez fixture), odcisz `muted:false`.
- Nie sprawdzone w pełnej aplikacji (Release z klawiszami Home/OK): wymaga fizycznego wejścia i jest w akceptacji epiki na UM760; ścieżki Home/aktywacja pokryte testami Orchestratora.
- Fixture: okno zasłonięte nie dostaje klatek `requestAnimationFrame`, więc start tonu ma zapas `setTimeout` 1,5 s.
- Zamknięcie aplikacji: `AudioRouterEngine.Dispose` odcisza to, co wyciszył (TeardownSequence zwalnia AudioRouter po WindowManagerze).

## Plan Change Log

## Review Triage Log

### 2026-10-26 — Review pass
- verdicts: 33 findings across 4 lenses (counts approximate: lenses overlap heavily) — high 0, medium 4, low 7, false 5, maybe-false 0
- findings:
  - `[medium]` `[patch]` dead root pruned, its muted sessions stay muted forever (blind, edge) — ledger entries kept on prune, so the same sweep restores them; test `Sessions_muted_under_a_dead_root_are_restored_instead_of_staying_muted`
  - `[medium]` `[patch]` failed Unmute never retried, `AnyMuted` false stops polling; Dispose skips restore when backend was dropped (edge, gap) — `HasMutedSessions` keeps the poll running, Dispose recreates the backend; tests `Failed_unmute_keeps_pending_sessions_so_the_port_keeps_scanning`, `Dispose_recreates_a_dropped_backend_to_restore_muted_sessions`
  - `[medium]` `[patch]` one failing device fails the whole enumeration (blind, edge) — per-device catch in `CoreAudioSessionBackend.Enumerate`, rethrow only when every device failed
  - `[low]` `[patch]` `_sweepRequested` read-then-clear race (edge) — `Interlocked.Exchange`
  - `[low]` `[patch]` fixture restarts the tone on keydown, masking session state (blind) — listener removed
  - `[medium]` `[defer]` sessions muted by a crashed/killed MediaStation stay muted after restart because the ledger is empty (blind) — story 3 adoption path; settle by deciding whether Unmute forces unmute of in-tree sessions outside the ledger
  - `[low]` `[reject]` pre-existing mute of an in-tree session is overwritten on unmute (blind, edge) — kiosk machine, no user mixer mutes; the fix adds an interface method
  - `[low]` `[reject]` Win32AudioRouterPort loop and result mapping untested (gap) — ticket `verify` tests all exist; loop is ~15 lines and was exercised live on UM760 (Home/OK/kill); extraction is a refactor
  - `[low]` `[reject]` mute state never re-verified, polling cost/PID reuse, orphaned descendant with exited intermediate parent (blind, edge) — speculative, no reachable failure shown
  - `[low]` `[reject]` `Execute` may drop a command if `_wake.Set()` throws; `rootPid <= 0` completes silently; mute pruned right after Set for absent root (blind, edge) — only at teardown race or impossible input (root comes from a live attach); no answer-less path in normal use
  - `[low]` `[reject]` test brittleness from cmdId renumbering, doc line length, missing `using System.Linq`, coupling to `Platform.Window` snapshot — cosmetic; implicit usings compile it, snapshot helper is shared on purpose
  - `[false]` `[reject]` HarnessBrowser autoplay flag missing from diff (blind) — already in 5.1 (`HarnessBrowser.cs` unchanged); live run played the tone
  - `[false]` `[reject]` generated schemas/TS and `MessageTypesTests`/`SchemaExportTests` missing (blind, intent) — `web/contracts` is gitignored, build regenerated it; those tests iterate `MessageTypes.All`
  - `[false]` `[reject]` late-attach Home-in-Starting path has no mute (blind) — story 3 by the ticket text
  - `[false]` `[reject]` attach-after-`Starting` activation is an extra path (intent) — it emits `ActivateWindow`, so `UnmuteProcessTree` after it is the "aktywacja" path
  - `[false]` `[reject]` full-app flow not run (intent) — run on UM760 with script-injected keys: OK, Home, OK, Home, kill of the audio service gave `audioRuleApplied` false/true/false/true and `audioSessionMuted` after the kill
  - remaining descriptive items of the intent lens (readings A-D, extras: ledger, prune, Dispose restore) — no defect claimed; carried into the rows above

### 2026-10-26 — Review pass (follow-up)
- verdicts: 46 findings across 4 lenses (counts approximate: lenses overlap heavily and repeat the first pass) — high 0, medium 0, low 3, false 3, maybe-false 0, carried 40
- findings:
  - `[low]` `[patch]` doc comments of `OnSystemResumed` and `OnAdopt` say "same order as Home" though Home now starts with `MuteProcessTree` (gap) — comments now say these paths stay without mute (story 3)
  - `[low]` `[reject]` `Restore` ignores the bool of `TrySetMute`/`TrySetChannelVolumes` and the ledger entry is dropped (blind, edge, gap) — only reachable when the interface that worked at mute time vanished, i.e. the session is gone; a throw already keeps the entry; fix adds a branch and a retry state
  - `[low]` `[reject]` channel levels length differs at restore, shared `_reported` key hides a second problem, `throw lastFailure!` stack trace, `ObjectDisposedException` race of `Execute`/`Dispose`, manager eviction, poll backoff while the service is down (blind, edge) — speculative; no reachable failure shown; fixes add guards
  - `[false]` `[reject]` `CommandFailed{AudioUnavailable}` ignored by the Orchestrator (blind, edge) — `OnCommandFailed` only tracks starts by design; the ticket says the executor answers, and recovery paths are story 3
  - `[false]` `[reject]` Home/late-attach/adopt/resume compensation paths have no mute (gap, blind) — ticket: "pozostałe ścieżki dokłada story 3"; boundary Never in the contract
  - `[false]` `[reject]` generated schemas missing from the diff (blind, intent) — `web/contracts` is gitignored and regenerated by the build
  - `[low]` `[reject]` `Win32AudioRouterPort` untested, harness scenario has no automated assertion (gap, intent) — ticket `verify` tests exist; loop was run live on UM760 (see first pass); extracting the port is a refactor
  - `carried` all other findings (pre-existing mute overwritten, ledger empty after crash, dead-root prune, failed unmute retry, per-device failure, `rootPid <= 0` silent success, snapshot-truncation prune, PID reuse, `_sweepRequested`, cmdId renumbering, cosmetic) — same location and claim as first-pass rows; verdicts and routes kept (the ledger-after-crash one stays deferred)

## Design Notes

Reguła to słownik `rootPid → muted`; nowa sesja jest sprawdzana względem każdego korzenia po łańcuchu rodziców z migawki procesów (świeża migawka przy każdej nowej sesji, bo potomek powstaje po korzeniu). Po sesji, której nie da się przypisać, ponowny skan po krótkim opóźnieniu (pid potomka bywa jeszcze nieznany).

## Verification

**Commands:**
- `dotnet test` -- expected: zielone, w tym nowe testy; build odświeża schematy/TS
- Release publish i uruchomienie z `MEDIASTATION_DEV_HARNESS=1`, `MEDIASTATION_HARNESS_URL=file:///.../tools/fixtures/harness-audio.html`, klawisze Home/OK wstrzyknięte skryptem -- expected: wpisy `audioRuleApplied`/`audioSessionMuted` w `%LOCALAPPDATA%\MediaStation\logs`

## Auto Run Result

Status: built (follow-up review pass). Ran on UM760 (host MEDIASTATION).

- Summary: `MuteProcessTree`/`UnmuteProcessTree`, `ErrorCode.AudioUnavailable`, `IAudioRouterPort` in Core; Orchestrator mutes before minimize on Home and unmutes after `ActivateWindow`; `Platform/Audio` AudioRouter thread with engine, rule, tree matcher, sweep schedule and Core Audio backend (unchanged by this pass).
- Files changed this pass: `Core/Orchestrator/Orchestrator.Startup.cs`, `Orchestrator.Attach.cs` (doc comments only).
- Review: 4 lenses; patches applied: 1 low (stale doc comments); deferred: none new (the ledger-after-crash item from the first pass stays); rejected with reasons in the triage log; 40 findings carried from the first pass.
- Follow-up review recommendation: false (patched: high 0, medium 0, low 1).
- Verification: `dotnet test` 1155/1155 passed on UM760 after the patch; live run evidence from the first pass stands.
- Residual risks: as first pass: pre-existing mute of an in-tree session cleared on unmute; sessions muted by a crashed MediaStation stay muted until story 3; port thread loop has no unit test.
