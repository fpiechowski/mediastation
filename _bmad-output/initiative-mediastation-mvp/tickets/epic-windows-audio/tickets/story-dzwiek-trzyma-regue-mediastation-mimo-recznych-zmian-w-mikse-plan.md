---
title: 'Dźwięk trzyma regułę MediaStation mimo ręcznych zmian w mikserze'
type: 'feature'
ticket: '10'
created: '2026-10-27'
status: 'built'
baseline_revision: 'dd14a6963436110cc80c1b8af663c01c6b227c5d'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: ['blind-hunter', 'edge-case-hunter', 'verification-gap', 'intent-alignment']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** AudioRouter odciszał tylko to, co sam wyciszył, i skanował sesje tylko przy wyciszonym drzewie, więc ręczna zmiana w mikserze Windows zostaje (odciszone tło gra, wyciszona aplikacja na wierzchu milczy), a wyciszenie dziedziczone po zamkniętej aplikacji zostaje w nowej instancji (DW-45, DW-37).

**Approach:** Reguła audio to stan docelowy w obie strony: silnik czyta rzeczywisty stan sesji drzew z regułą przy każdym skanie (komenda, nowa sesja i urządzenie, okresowo co ~1 s, dopóki istnieje reguła) i poprawia różnicę; koniec reguły odcisza każdą sesję przypisaną do drzewa, a Orchestrator odcisza drzewo przed `StopApp` przy `CloseApp` i `RestartApp`.

## Boundaries & Constraints

**Always:** logika uzgadniania w `AudioRouterEngine` bez typów COM, testowana na fake'ach; skan tylko na wątku AudioRoutera, callbacki Core Audio (`OnSimpleVolumeChanged`, `OnChannelVolumeChanged`) tylko zlecają skan przez `RequestSweep` i mogą go tylko przyspieszyć (gwarancją jest okres ~1 s); własne zmiany głośności (`SetMute`, `SetAllVolumes`) idą z własnym GUID-em kontekstu, a powiadomienie z tym GUID-em nie zleca skanu (decyzja w czystej funkcji poza COM, z testem); `UnmuteProcessTree` odcisza każdą sesję drzewa, także wyciszoną poza silnikiem i spoza rejestru; fallback `IChannelAudioVolume`: wyciszona = wszystkie kanały 0, poziomy sprzed wyciszenia tylko w pamięci, odciszenie bez zapamiętanych poziomów ustawia 1.0; zewnętrzna zmiana sesji wraca do reguły z jednym wpisem `audioSessionOverridden{ rootPid, pid, muted }` na sesję i wartość reguły (`muted` = wartość reguły przywrócona), bez zalewu logu; koniec reguły (`ForgetProcessTree`, śmierć korzenia, zamknięcie MediaStation/`Dispose`) raz odcisza każdą sesję ostatnio przypisaną do drzewa; `CloseApp` i `RestartApp` w Orchestratorze wysyłają `UnmuteProcessTree` (gdy `RootPid > 0`) przed `StopApp`; wykonawca nie rzuca (AD-4).

**Never:** nie dotykać głośności sesji (poziomu), głośności systemu ani sesji spoza drzew z regułą (w tym `pid` 0); bez zmian WindowManagera i kontraktów Core poza kolejnością komend w `OnStop`; bez `bmad-architecture`; bez nowych Events w Core dla poprawki sesji (to wpis w logu, AD-4); bez ręcznej edycji `web/contracts`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Odciszenie z miksera przy regule wyciszenia | sesja drzewa była wyciszona przez regułę, teraz `Muted=false`, skan | sesja znowu wyciszona; jeden `audioSessionOverridden{muted:true}`; kolejne skany bez zmian i bez wpisu | — |
| Wyciszenie z miksera przy regule odciszenia | sesja drzewa zgodna z regułą `false`, teraz `Muted=true` | sesja odciszona; jeden `audioSessionOverridden{muted:false}` | — |
| `UnmuteProcessTree` | sesja w drzewie wyciszona poza silnikiem (nie ma jej w rejestrze) | odciszona; bez `audioSessionOverridden` | — |
| Własna zmiana | powiadomienie głośności z własnym GUID-em | brak zlecenia skanu; z obcym GUID-em skan zlecony | — |
| Kanały bez zapamiętanych poziomów | sesja bez `ISimpleAudioVolume`, kanały 0 (po awarii poprzedniej instancji), reguła odciszenia | wszystkie kanały 1.0 | — |
| Fallback kanałów, zmiana z zewnątrz | sesja z kanałami, reguła wyciszenia, kanały podniesione z zewnątrz | poziomy zapamiętane ponownie, kanały 0, `audioSessionOverridden{muted:true}` | — |
| Koniec reguły | `ForgetProcessTree`, śmierć korzenia albo `Dispose` | każda sesja ostatnio przypisana do drzewa (także wyciszona z zewnątrz) odciszona raz, rejestr pusty | niedostępne audio: sesje zostają przypisane do następnego skanu |
| Sesje spoza drzew z regułą | sesja obcego PID i `pid` 0 | nietknięte w każdym scenariuszu | — |
| `CloseApp` / `RestartApp` | aplikacja `Background` albo `Failed`, `RootPid > 0` | komendy `[UnmuteProcessTree, StopApp, timer]` w tej kolejności | `RootPid == 0`: bez `UnmuteProcessTree` |
| Okresowy skan | istnieje jakakolwiek reguła (także `false`) | port planuje skan co ~1 s; bez reguł i bez przypisanych sesji skan tylko na żądanie | — |

</intent-contract>

## Code Map

- `src/MediaStation.App/Platform/Audio/AudioRouterEngine.cs` -- księga `_ledger` (tylko sesje wyciszone przez nas, `Muted(Root, Levels)`) zamienić na rejestr sesji przypisanych do drzew z regułą (korzeń, ostatnia wymuszona wartość, poziomy sprzed wyciszenia); `Apply` czyta stan (`TryGetMute`, a bez interfejsu kanały == 0) i poprawia różnicę w obie strony; `Execute`, `Forget`, `Dispose`, `Mute`/`Restore` do przerobienia; nowe właściwości dla portu (np. `AnyRule`); istniejące logi `audioRuleApplied`, `audioSessionMuted`, `audioRuleForgotten`, `audioChannelFallback`, `audioSessionSkipped` zostają
- `src/MediaStation.App/Platform/Audio/AudioRule.cs` -- `Set` powinno zgłaszać zmianę wartości (reset deduplikacji `audioSessionOverridden` korzenia); `Roots`, `PruneDead`, `Governing` bez zmian
- `src/MediaStation.App/Platform/Audio/IAudioSessionBackend.cs` -- `IAudioSession`: dodać `bool? TryGetMute()` (`null` bez `ISimpleAudioVolume`)
- `src/MediaStation.App/Platform/Audio/AudioChangeContext.cs` (nowy, bez COM) -- stały własny GUID kontekstu i czysta decyzja `ShouldSweep(Guid context)`
- `src/MediaStation.App/Platform/Audio/CoreAudioSessionBackend.cs` -- `ComAudioSession`: `GetMute`, `SetMute`/`SetAllVolumes` z własnym GUID-em; per sesja rejestracja `IAudioSessionEvents` (`OnSimpleVolumeChanged`, `OnChannelVolumeChanged`; callback sprawdza GUID przez `AudioChangeContext` i woła `onChange`), trzymana między skanami, wyrejestrowana przy zniknięciu sesji i w `Dispose`; wzorzec klas `[ClassInterface(None)]` jak `SessionWatcher`
- `src/MediaStation.App/Platform/System/NativeMethods.txt` -- dopisać `IAudioSessionEvents` (CsWin32)
- `src/MediaStation.App/Platform/Audio/AudioSweepSchedule.cs`, `Win32AudioRouterPort.cs` -- `NextDelay(..., anyMutedRoot)` ma znaczyć „istnieje reguła lub przypisane sesje”; wywołanie w porcie przestawić na nowe właściwości silnika
- `src/MediaStation.Core/Orchestrator/Orchestrator.Stop.cs` (`OnStop`) -- przed `StopApp` dodać `UnmuteProcessTree(NextCmdId(), app.RootPid)` gdy `RootPid > 0`; `OnStopped`/`ForgetRule` bez zmian
- Testy: `tests/MediaStation.Core.Tests/AudioRouterEngineTests.cs` (rozszerzyć `FakeSession` o `TryGetMute`, zmiana zewnętrzna; przerobić testy zależne od starej semantyki „tylko to, co wyciszyłem”), `AudioSweepScheduleTests` (jeśli istnieje, albo dopisać), nowy test `AudioChangeContext`, `OrchestratorStopTests.cs` (+ inne testy z `StopApp(15` i kolejnymi `cmdId`: `grep -rn "StopApp(" tests`); testy kontraktów bez zmian

## Tasks & Acceptance

**Execution:**
- [ ] `IAudioSessionBackend.cs`, `AudioRule.cs`, `AudioChangeContext.cs` -- `TryGetMute`, wykrywanie zmiany reguły, własny GUID i `ShouldSweep`
- [ ] `AudioRouterEngine.cs` -- uzgadnianie z rzeczywistym stanem w obie strony, `audioSessionOverridden`, fallback kanałów (1.0 bez zapamiętanych poziomów), koniec reguły odcisza przypisane sesje, `Dispose`
- [ ] `AudioSweepSchedule.cs`, `Win32AudioRouterPort.cs` -- okres ~1 s dopóki istnieje reguła lub przypisane sesje
- [ ] `CoreAudioSessionBackend.cs`, `NativeMethods.txt` -- odczyt `GetMute`, własny GUID kontekstu, per-sesyjne powiadomienia głośności zlecające skan
- [ ] `Orchestrator.Stop.cs` -- `UnmuteProcessTree` przed `StopApp`
- [ ] testy -- każdy wiersz macierzy; przerobić istniejące testy zależne od starej semantyki i numeracji `cmdId`

**Acceptance Criteria:**
- Given sesja drzewa z regułą wyciszenia odciszona z miksera, when skan, then wraca do wyciszenia z jednym `audioSessionOverridden muted:true`, a odwrotnie z `muted:false`.
- Given `CloseApp` albo `RestartApp` na `Background`, when Orchestrator obsługuje intencję, then `UnmuteProcessTree` poprzedza `StopApp`.
- Given reguła zakończona albo `Dispose`, when skan, then żadna sesja przypisana do drzewa nie zostaje wyciszona, a sesje spoza drzew są nietknięte.
- Given obce powiadomienie głośności, when callback, then zlecony skan, a z własnym GUID-em nie.

## Implementation Notes

## Plan Change Log

## Review Triage Log

### 2026-10-27 — Review pass
- verdicts: 27 findings (blind 10, edge 13, verification-gap 4 incl. other, intent-alignment descriptive) — high 0, medium 2, low 3, false 4, maybe-false 1, rejected-low 17 (grouped)
- findings:
  - `[medium]` `[patch]` transient gap (empty/partial process snapshot) makes `governing` null and unmutes a session whose root still has a rule (edge) — Unassign now only when the entry's root rule is gone; test `Empty_or_partial_process_snapshot_keeps_a_muted_child_muted`
  - `[medium]` `[patch]` stop timeout leaves a live app in `Failed` with an unmuted rule, audible orphan (edge) — `EnterFailed` re-sends `MuteProcessTree` after a Stopping timeout; timer tests updated
  - `[low]` `[patch]` no test for close/restart with `RootPid == 0` (blind, gap) — test added
  - `[low]` `[patch]` null event context and `VolumeWatcher.Notify` path untested (blind, gap, edge) — `ShouldSweep(Guid?)`, null test
  - `[low]` `[patch]` port scan condition verified only by a copied expression (gap) — `NeedsSweeping` on the engine, test on it
  - `[false]` `[reject]` unmute before stop makes the closing app audible (blind) — mandated by the ticket and AD-5
  - `[false]` `[reject]` override logged once per session and value forever, 1 s poll never stops while a rule exists, Unassign/Dispose override user mutes, all-zero channels read as mute (blind, edge) — each is the ticket's explicit rule
  - `[false]` `[reject]` plan claim that `AudioRule.Set` resets dedup (edge) — fix would edit the plan; dedup lives on the registry entry
  - `[low]` `[reject]` TrySetMute/Unassign return values ignored, channel count change, narrow catch types, stale control re-registration, partial-enumeration registration churn, Dispose retry (blind, edge) — unreachable after `TryGet*` succeeded or only slows notifications; the 1 s poll is the guarantee
  - `[low]` `[reject]` brittle renumbered cmdIds, test placement, duplicate schedule test (blind, gap) — cosmetic
  - `[maybe-false]` `[reject]` `OnChannelVolumeChanged(float[] ...)` marshalling at runtime (edge, gap) — compiles; if it misbehaves only the notification is lost, low if true; check live
  - `carried` intent-alignment readings A-E: descriptive; divergence noted — COM wiring, own-GUID writes, port poll loop and Unmute-before-Stop completion race are not under unit test (live check not run)

## Design Notes

Rejestr sesji przypisanych zastępuje księgę wyciszonych: sesja trafia do rejestru, gdy rządzi nią reguła (także `false`), i zostaje w nim do końca reguły albo zniknięcia sesji; `audioSessionOverridden` powstaje tylko dla sesji, którą poprzedni skan zostawił zgodną z regułą (zmiana reguły przez komendę to zwykłe zastosowanie, nie nadpisanie), raz na sesję i wartość reguły (zbiór czyszczony przy zmianie wartości reguły korzenia). Okres skanu jest gwarancją, powiadomienia tylko go skracają, więc błąd rejestracji powiadomień nie psuje reguły.

## Verification

**Commands:**
- `dotnet test` -- expected: zielone, w tym nowe testy z macierzy
- `dotnet build -c Release` -- expected: bez błędów i ostrzeżeń analizatora (CsWin32 `IAudioSessionEvents`)

**Manual checks (current machine, UM760):**
- Dev harness (`MEDIASTATION_DEV_HARNESS=1`) z `tools/fixtures/harness-audio.html` w Edge: ręczne odciszenie Edge w tle w mikserze (albo `ISimpleAudioVolume` ze skryptu) wraca do wyciszenia w ok. 1 s z `audioSessionOverridden muted:true`; wyciszenie aplikacji na wierzchu wraca z `muted:false`; po zamknięciu aplikacji z AppMenu jej sesje nie są wyciszone. Gdy sprawdzenie na żywo jest nieosiągalne bez fizycznego wejścia, zapisać powód i oprzeć się na testach.

Status: built. Ran on UM760 (host MediaStation, board F7BSI).

Summary: audio rule is now a two-way target state. The engine reads real session state each scan and corrects the difference, logging `audioSessionOverridden`. Fallback channels restore 1.0 when no levels are remembered. End of rule or `Dispose` unmutes every assigned session. `CloseApp` and `RestartApp` send `UnmuteProcessTree` before `StopApp`, and a stop timeout re-mutes.

Files: `Platform/Audio/AudioRouterEngine.cs` (reconciliation, registry), `AudioChangeContext.cs` (own GUID, `ShouldSweep`), `CoreAudioSessionBackend.cs` (`GetMute`, own-GUID writes, `IAudioSessionEvents`), `IAudioSessionBackend.cs`, `AudioSweepSchedule.cs`, `Win32AudioRouterPort.cs`, `NativeMethods.txt`, `Orchestrator.Stop.cs`, `Orchestrator.Failures.cs`, plus tests.

Review: 5 patches applied (2 medium, 3 low), 0 deferred, rest rejected as recorded above.

Verification: `dotnet test` 1238 passed, 0 failed; `dotnet build -c Release` 0 errors, 0 warnings.

Residual risks: live mixer check with Edge and `harness-audio.html` was not run (needs a manual mixer action); the real Core Audio paths (`IAudioSessionEvents` callbacks, own-GUID writes, handle ownership via `KeepControl`) are unexercised. `UnmuteProcessTree` and `StopApp` run on different executors, so completion ordering is by emission only. Operator-only hardware check, if wanted: on UM760 unmute the harness Edge in the mixer, expect `audioSessionOverridden muted:true` within ~1 s.
