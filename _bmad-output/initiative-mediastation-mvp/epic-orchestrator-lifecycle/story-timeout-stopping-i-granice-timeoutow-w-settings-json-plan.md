---
title: 'Timeout Stopping i granice timeoutów w settings.json'
type: 'feature'
ticket: '11'
created: '2026-10-22'
status: done
baseline_revision: '06d8d8b93a238ebbb9f4662c29e32804833eba17'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Aplikacja, której `StopApp` nigdy nie potwierdzi się ani nie da `ProcessExited`, zostaje w `Stopping` na zawsze — dziś tylko `Starting` ma timeout. Jednocześnie `startingTimeout` i nowy `stoppingTimeout` nie mają górnej granicy (F18): walidacja przyjmuje wartość, dla której `TimeProvider.System` odrzuci termin dalszy niż ~49,7 dnia, więc timer w ogóle nie powstaje i kafelek znowu wisi.

**Approach:** Nowe `settings.stoppingTimeout` (domyślnie 45 s, dłużej niż 30 s limit `quit` Moonlighta; schemat, walidacja, `tools/samples/config/settings.json`) planuje ten sam scheduler ze story 5 przy wejściu w `Stopping` i jest anulowane po `CommandCompleted` na `StopApp` albo `ProcessExited`; `TimerElapsed` w `Stopping` prowadzi w `Failed`. Walidacja ogranicza `startingTimeout` i `stoppingTimeout` do 600 s, bez reguły „`stoppingTimeout` > 30 s”.

## Boundaries & Constraints

**Always:** czas wyłącznie przez wstrzyknięty `TimeProvider` (AD-14); handler czysty i synchroniczny, bez `await`; `cmdId` i `timerId` nadaje wyłącznie Orchestrator; korelacja `timerId → appId` żyje w Orchestratorze i znika **przed** `EnterFailed`, więc timeout nie anuluje sam siebie; komendy timera idą na koniec batcha; `Failed` po timeoucie stopu nie wysyła `ShowShell` ani nowego `focusRequest`; po `Failed` spóźniony `CommandCompleted`/`ProcessExited`/`CommandFailed` nie wskrzesza kafelka; kontrakty `ScheduleTimer`/`CancelTimer`/`TimerElapsed` bez zmian (AD-4/AD-16); schemat `settings.json` wynika z rekordu `Settings` (AD-11).

**Never:** reguła „`stoppingTimeout` > 30 s”; zmiany w hostach, Moonlight `quit`, `CommandRouter`, `CompositionRoot`, `MessageTypes`, `web/**`; nowe wpisy logu; parkowanie ticketu na UM760.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Zamknięcie z tła | `CloseApp` w `Background` | `Stopping`; `StopApp` + `ScheduleTimer{timerId, appId, T0+45s}`; `rev`+1 | — |
| Stop potwierdzony | `CommandCompleted` stopu | `NotRunning`; tylko `CancelTimer{timerId}` | — |
| Proces zniknął | `ProcessExited` w `Stopping` z korelacją | `NotRunning`; tylko `CancelTimer{timerId}` | — |
| Timeout stopu | `TimerElapsed` w `Stopping` | `Failed`; brak komend; `rev`+1; `focusRequest` bez zmian | — |
| Spóźniony timer | `TimerElapsed` po potwierdzonym stopie | brak komend i snapshotu | korelacja już usunięta |
| Spóźniony wynik stopu | `CommandCompleted`/`ProcessExited` po timeoucie | brak komend i snapshotu | korelacja stopu zdjęta przy wejściu w `Failed` |
| Restart po timeoucie stopu | `Failed` → `RestartApp` → `Stopping` | nowy `ScheduleTimer` z nowym `timerId`; stary `TimerElapsed` ignorowany | — |
| Restart potwierdzony | `CommandCompleted` stopu przy restarcie | `StartApp`, `ScheduleTimer` startu, `CancelTimer` stopu | — |
| Granica 600 | `startingTimeout` albo `stoppingTimeout` = 600 | config poprawny | — |
| Powyżej granicy | 601 dla obu pól | brak configu | `$.<pole>`: „wartość musi być nie większa niż 600” |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Config/Settings.cs` — po `StartingTimeout`: `public int StoppingTimeout { get; init; } = 45;` z doc „Timeout stanu `Stopping` w Orchestratorze, w sekundach (AD-14)”; w doc klasy dopisz, że `startingTimeout` i `stoppingTimeout` nie przekraczają 600 s. Kolejność właściwości wyznacza kolejność w schemacie.
- `src/MediaStation.Core/Config/ConfigValidation.cs` — w `CheckSettings` (`:345`): `CheckPositive(settings.StoppingTimeout, "stoppingTimeout", errors)` po `StartingTimeout`; po czterech `CheckPositive` dwa `CheckAtMost(..., MaxTimeoutSeconds, ...)` dla `startingTimeout` i `stoppingTimeout`; stała `public const int MaxTimeoutSeconds = 600;` obok `CdpPortMin/Max` (`:24`); nowy helper `CheckAtMost(int value, int max, string name, FileErrors errors)` (obok `CheckPositive`, `:369`): `value > max` → `JsonErrorPath.Property("$", name)`, reason `$"wartość musi być nie większa niż {max}"`. Relacja `cdpConnectTimeout < startingTimeout` bez zmian.
- `src/MediaStation.Core/Contracts/AppCommands.cs` — doc `ScheduleTimer` (`:39-40`) mówi tylko o korelacji `Starting`; dopisz, że dotyczy też timeoutu `Stopping`. Zero zmian w rekordach.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — wzór story 5: `_startTimers` (`:30`), `ScheduleStartTimeout` (`:386`), `CancelStartTimeout` (`:397`), `FindStartTimeout` (`:410`).
  - pola: `Dictionary<long, string> _stopTimers = []` obok `_startTimers`, `TimeSpan _stoppingTimeout`; ctor (`:40`) ustawia `_stoppingTimeout = TimeSpan.FromSeconds(config.Settings.StoppingTimeout)`.
  - uogólnij helpery: `ScheduleTimeout(Dictionary<long,string> timers, string appId, TimeSpan timeout)` (nowy `++_timerId`, `Due = _time.GetUtcNow() + timeout`, `ScheduleTimer` z `NextCmdId()` na końcu), `CancelTimeout(dict, appId)` → `CancelTimer?` z nowym `cmdId`, `FindTimeout(dict, appId)`; `ScheduleStartTimeout`/`CancelStartTimeout` zostają cienkimi wrapperami na `_startTimers` + `_startingTimeout`.
  - `OnStop` (`:212`): batch `[StopApp(cmdId), ScheduleTimeout(_stopTimers, app, _stoppingTimeout)]`.
  - `OnStopped` (`:424`): zamknięcie → `[CancelTimeout(_stopTimers, appId)]` (pomiń `null`); restart → `[StartApp, ScheduleStartTimeout]` + `CancelTimeout` stopu **na końcu** (konwencja „komendy timera na końcu batcha”).
  - `OnTimerElapsed` (`:322`): najpierw `_startTimers.Remove` → `Failed` gdy `Starting`; potem `_stopTimers.Remove` → `EnterFailed(app, wasForeground: false)` gdy `Stopping`; inaczej `None`. Usunięcie korelacji przed `EnterFailed` daje „brak komend” przy timeoucie.
  - `EnterFailed` (`:341`): po `DropPendingStarts` także `DropPendingStops(app.AppId)` (nowy prywatny helper na wzór `:364`), żeby spóźniony wynik `StopApp` nie zamknął `Failed` w `NotRunning`/`Starting`.
  - doc `OnCommandFailed` (`:253`, „timeout należy do story 11”) i doc `OnTimerElapsed` aktualizują się do dwóch timeoutów.
- `tools/samples/config/settings.json` — `"stoppingTimeout": 45,` po `startingTimeout`.
- `tests/MediaStation.Core.Tests/ConfigValidationTests.cs` — `:108-113` defaults: `Assert.Equal(45, config.Settings.StoppingTimeout)`; `:124` i `:133-142` explicit values dostają `"stoppingTimeout": 50` / `StoppingTimeout = 50`; `:631-634` teoria pozytywności + `[InlineData("stoppingTimeout", "0")]`; nowe teorie: 600 przyjęte i 601 odrzucone dla obu pól (reason jak wyżej), `stoppingTimeout: 10` przyjęte (brak reguły > 30 s), `{}` daje 30/45; `:842-844` lista właściwości schematu + `"stoppingTimeout"` i `Assert.Equal("integer", ...)`.
- `tests/MediaStation.Core.Tests/OrchestratorTests.cs` — churn `cmdId`/`timerId` po każdym stopie (nowy `ScheduleTimer` przesuwa licznik) w `:463-474`, `:477-489`, `:492-509`, `:512-523`, `:526-551`, `:554-572`, `:721-736`, `:798-813`, `:862-900`, `:905-924`, `:927-948`, `:1058-1092`, `:1095-1121`: `CloseApp`/`RestartApp` w tle dają **dwie** komendy (`StopApp` + `ScheduleTimer` stopu, `T0+45s`), a potwierdzony stop dokłada `CancelTimer`; helper `Foreground`/`Background` bez zmian. Nowe testy: timeout stopu → `Failed` bez komend i bez nowego `seq`; stop potwierdzony przed terminem + spóźniony `TimerElapsed` ignorowany; `CommandCompleted`/`ProcessExited` po timeoucie ignorowane; restart po timeoucie stopu z nowym `timerId` i ignorowanym starym.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs` — wzór `Start_timeout_without_host_response_publishes_failed_snapshot` (`:203`): `SilentHost` + `TimerScheduler` + `postsTo`; nowy test: `LaunchApp` → `AppWindowAttached` → Home → `CloseApp` bez odpowiedzi hosta, `time.Advance(45 s)` → `Failed` bez nowego `focusRequest`.
- Reuse: `FakeTimeProvider`, `RecordingLog`, `OrchestratorResult.None`, `AppLifecycleState.Failed`. Nie ruszać: `TimerScheduler.cs`, `ConfigJson`, `ConfigPreflight`, `web/**`.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Config/Settings.cs` — pole `StoppingTimeout = 45` w schemacie i nagłówku klasy — domyślna wartość z ticketu, dłuższa niż 30 s `quit` Moonlighta.
- [x] `src/MediaStation.Core/Config/ConfigValidation.cs` — walidacja stopu i górna granica 600 s dla obu timeoutów — F18 zamyka się bez reguły „`stoppingTimeout` > 30 s”.
- [x] `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — timeout `Stopping` i sprzątanie korelacji w `EnterFailed` — żaden stop nie zostaje w `Stopping` na zawsze.
- [x] `src/MediaStation.Core/Contracts/AppCommands.cs` — doc `ScheduleTimer` obejmuje oba timeouty — komentarz nie kłamie po tej story.
- [x] `tools/samples/config/settings.json` — `stoppingTimeout: 45` — wzorcowy config przechodzi walidację.
- [x] `tests/MediaStation.Core.Tests/{ConfigValidationTests,OrchestratorTests,OrchestratorLoopTests}.cs` — każdy wiersz macierzy plus `verify` ticketu — dowód na `FakeTimeProvider` i pętli.

**Acceptance Criteria:**
- Given `stoppingTimeout` = 45 s i `FakeTimeProvider`, when `CloseApp("netflix")` w `Background`, then stan to `Stopping`, a komendy to `StopApp` i `ScheduleTimer{timerId, "netflix", T0+45s}`.
- Given aplikacja w `Stopping`, when zegar przekroczy `stoppingTimeout`, then snapshot ma `Failed`, brak komend, ten sam `focusRequest` i `seq`.
- Given `CommandCompleted` na `StopApp` przed terminem, when zegar minie termin i przyjdzie `TimerElapsed`, then stan zostaje `NotRunning`, a wynik to brak komend i brak snapshotu.
- Given aplikacja w `Stopping` bez odpowiedzi hosta, when przyjdzie `ProcessExited`, then stan to `NotRunning`, jedyna nowa komenda to `CancelTimer` stopu.
- Given `Failed` po timeoucie stopu, when `CommandCompleted` albo `ProcessExited` starego stopu, then brak komend i brak snapshotu.
- Given `Failed` po timeoucie stopu, when `RestartApp` i potwierdzony stop, then `Starting` dostaje `ScheduleTimer` z nowym `timerId`, a spóźniony `TimerElapsed` starego id nie zmienia stanu.
- Given `settings.json` z `startingTimeout` albo `stoppingTimeout` równym 600 s, when walidacja, then config jest poprawny; dla 601 s, then błąd `$.<pole>` z reason „wartość musi być nie większa niż 600”.
- Given `settings.json` bez `stoppingTimeout` albo z `stoppingTimeout: 10`, when walidacja, then config jest poprawny i `Settings` ma odpowiednio 45 s albo 10 s.
- Given eksport schematów, when `settings.schema.json`, then `properties` zawiera `stoppingTimeout` typu `integer` po `startingTimeout`.

## Implementation Notes

- Bez odstępstw od Code Map. `OnStopped` (zamknięcie) buduje listę lokalnie i dokłada `CancelTimeout`, gdy korelacja stopu jeszcze żyje; publicznie ten `null` nie występuje, bo `OnStop` zawsze planuje termin, ale gałąź jest odporna na stan z ręcznie usuniętą korelacją.
- `EnterFailed` nie anuluje timeoutu `Stopping`: termin stopu może istnieć wyłącznie w `Stopping`, a `OnTimerElapsed` zdejmuje korelację **przed** `EnterFailed`, więc `CancelTimeout` zwróciłby tam zawsze `null`. Zamiast tego `DropPendingStops` zamyka drogę spóźnionemu wynikowi `StopApp`.
- Testy `cmdId`/`timerId`: `CloseApp`/`RestartApp` w tle mają teraz dwie komendy, więc `Restart_in_failed_stops_then_starts_and_foreground_on_attached_window` nie wylicza już `stop.CmdId` z `Assert.Single` — asercje to stałe identyfikatory (`StopApp 9`, `ScheduleTimer 10/2`, `StartApp 11`, `ScheduleTimer 12/3`, `CancelTimer 13/2`, attach `14/15/16`). `Stale_start_result_after_restart_in_failed...` bierze `restart.Commands[0]`, a `Restart_after_start_timeout...` zachowuje `TimerElapsed(1)` (stary timeout startu) i dokłada `CancelTimer(8, 2)` dla terminu stopu.
- `OrchestratorLoopTests` mają nowy test pętli na `SilentHost` z pełnym przejściem `LaunchApp` → `AppWindowAttached` → Home → `CloseApp` → `Advance(45 s)` → `Failed` bez nowego `focusRequest`; komentarze `T0` w obu plikach mówią o 30 s i 45 s.

## Plan Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 2 findings — high 0, medium 0, low 2, false 0, maybe-false 0
- findings:
  - `[low]` `[patch]` Doc `MaxTimeoutSeconds` twierdził, że „dalszy termin odrzuca `TimeProvider.System`”, a ten provider odrzuca dopiero termin dalszy niż ~49,7 dnia (~4 294 968 s); 600 s jest decyzją polityki epiki (F18), więc komentarz mylił powód granicy — poprawione: doc mówi o „politycznej górnej granicy … daleko poniżej limitu terminu akceptowanego przez wstrzykiwany zegar”.
  - `[low]` `[patch]` `Stop_result_after_stop_timeout_is_ignored` pokrywał po timeoucie stopu tylko `CommandCompleted` i `ProcessExited`, choć Always kontraktu wymienia też `CommandFailed`; gałąź `OnCommandFailed` nie miała tu żadnego testu — poprawione: teoria ma trzeci wariant `"failed"` → `CommandFailed(10, ErrorCode.Unexpected)`.

## Design Notes

Timeout `Stopping` korzysta z tego samego mechanizmu co `Starting`, ale z osobną mapą `_stopTimers`: korelacje dwóch timeoutów tej samej aplikacji nie mogą się nawzajem nadpisywać (restart: stop → start). `timerId` liczy jeden wspólny licznik, więc identyfikatory są unikalne globalnie i spóźniony `TimerElapsed` odpada po samym `Remove`, bez zaglądania w stan schedulera.

`EnterFailed` zdejmuje też korelację stopu, bo timeout stopu wchodzi w `Failed` z żywą korelacją `_pendingStops`; bez tego spóźniony `CommandCompleted` starego `StopApp` przeniósłby kafelek z `Failed` w `NotRunning` albo `Starting`.

Walidacja boundsów jest niezależna od relacji `cdpConnectTimeout < startingTimeout`: 600 s dla obu pól, a pole `stoppingTimeout` nie ma dolnej relacji do limitu `quit` Moonlighta (decyzja epiki: bez reguły „> 30 s”), bo limit żyje w hoście.

## Verification

**Commands:**
- `dotnet test MediaStation.slnx -c Release` -- zielone, w tym nowe testy walidacji, timeoutu stopu w `OrchestratorTests` i pętli.
- `dotnet build MediaStation.slnx -c Release` -- 0 ostrzeżeń; `SchemaExport` pisze `settings.schema.json` z `stoppingTimeout`, `npm run contracts`, `npm run typecheck` i Vite build zielone.
- `dotnet build src/MediaStation.App -c Debug` -- 0 ostrzeżeń.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + uruchomienie z ważnym configiem -- proces żyje, log bez `warn`/`error`, snapshot `rev: 1`.
- Sonda: tymczasowy `settings.json` z `stoppingTimeout: 601` → ekran błędu i wpis `configError` z `$.stoppingTimeout`; po sprzątaniu config wraca.

**Current-machine evidence (2026-10-22, HEAD `06d8d8b` + working tree, ALDER / B760 Pro RS/D4 / Windows 11 Home):**
- `dotnet test MediaStation.slnx -c Release` -- 469/469 passed, 0 failed. Nowe/rozszerzone: `ConfigValidationTests` (domyślne 45 s, `stoppingTimeout: 50` w explicit values, dodatnia wartość, 600 s przyjęte i 601 s odrzucone dla obu pól z reason „wartość musi być nie większa niż 600”, `stoppingTimeout: 10` bez relacji do 30 s, schemat z `stoppingTimeout` po `startingTimeout` jako `integer`), `OrchestratorTests` (dwie komendy stopu z `T0+45s`, `CancelTimer` po potwierdzonym stopie i po `ProcessExited`, timeout stopu → `Failed` bez komend i z tym samym `focusRequest`, spóźniony elapsed po potwierdzonym stopie ignorowany, `CommandCompleted`/`CommandFailed`/`ProcessExited` po timeoucie ignorowane, restart po timeoucie stopu z nowym `timerId` i ignorowanymi starymi, całe ścieżki cyklu życia), `OrchestratorLoopTests` (host przyjmuje `StopApp` bez odpowiedzi, `Advance(45 s)` → szósty snapshot `Failed` bez nowego `focusRequest`). Po poprawce przeglądu (doc `MaxTimeoutSeconds` + wariant `CommandFailed` w teorii) `ConfigValidationTests` i `OrchestratorTests` przebiegnięte ponownie: 242/242 passed.
- `dotnet build MediaStation.slnx -c Release` -- success, 0 warnings, 0 errors; `web/contracts/schema/settings.schema.json` ma `stoppingTimeout` typu `integer` między `startingTimeout` i `cdpConnectTimeout`, `npm run contracts`, `npm run typecheck` i Vite build zielone.
- `dotnet build src/MediaStation.App -c Debug` -- success, 0 warnings.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` -- success. Uruchomiony `MediaStation.exe` (pid 37828) z configiem bez `stoppingTimeout` (domyślne 45 s): proces żył po 9 s, log `%LOCALAPPDATA%\MediaStation\logs\mediastation-2026-10-21.jsonl` ma `start`, `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}`, `powerSignalsRegistered`, `snapshotPublished {rev: 1, focusRequest: {default, 1}}` i `stop {exitCode: 0}`, zero `warn`/`error` w tym przebiegu.
- Sonda: `settings.json` z `stoppingTimeout: 601` → proces żył (pid 15936), log ma `configError {file: "settings.json", path: "$.stoppingTimeout", reason: "wartość musi być nie większa niż 600"}`, brak `snapshotPublished` (ekran błędu). Config przywrócony do stanu użytkownika i ponowny przebieg (pid 38652) znowu daje `snapshotPublished {rev: 1}` i `stop {exitCode: 0}` bez `warn`/`error`.
- Timeout `Stopping` nie jest osiągalny w opublikowanej aplikacji bez prawdziwego hosta (dziś hosty albo odpowiadają, albo brak hosta daje natychmiastowy `CommandFailed`), więc jego dowodem są testy `OrchestratorTests` i pętli na `FakeTimeProvider`. Całościowy odbiór epiku na UM760 zostaje u użytkownika.
- Powtórka weryfikacji po implementacji i po poprawkach przeglądu (2026-10-22, ten sam baseline, ALDER): `dotnet test MediaStation.slnx -c Release` 470/470 (0 skipped), `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` z 0 ostrzeżeń i 0 błędów; `web/contracts/schema/settings.schema.json` ma `stoppingTimeout` typu `integer` między `startingTimeout` i `cdpConnectTimeout`, `additionalProperties: false` bez zmian.

## Auto Run Result

**Status:** built.

**Summary:** Orchestrator dostał timeout bezpieczeństwa `Stopping` → `Failed` z nowego `settings.stoppingTimeout` (domyślnie 45 s): termin planuje ten sam scheduler co timeout `Starting`, a anulowanie przychodzi po `CommandCompleted` na `StopApp` albo `ProcessExited`. Timeout stopu używa osobnej mapy `timerId → appId`, a wejście w `Failed` zdejmuje też korelację stopu, więc spóźniony wynik starego `StopApp` nie wskrzesza kafelka. Walidacja `settings.json` ogranicza `startingTimeout` i `stoppingTimeout` do 600 s (F18) bez reguły „`stoppingTimeout` > 30 s”, a schemat i `tools/samples/config/settings.json` niosą nowe pole.

**Files changed:**
- `src/MediaStation.Core/Config/Settings.cs` — `StoppingTimeout` = 45 s po `StartingTimeout`, doc klasy o granicy 600 s.
- `src/MediaStation.Core/Config/ConfigValidation.cs` — `CheckPositive` dla `stoppingTimeout`, `MaxTimeoutSeconds = 600` i `CheckAtMost` dla obu timeoutów.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — `_stopTimers`/`_stoppingTimeout`, uogólnione helpery timeoutów, `ScheduleTimer` w `OnStop`, `CancelTimer` w `OnStopped`, `TimerElapsed` dla `Stopping`, `DropPendingStops` w `EnterFailed`.
- `src/MediaStation.Core/Contracts/AppCommands.cs` — doc `ScheduleTimer` obejmuje oba timeouty (bez zmian rekordów).
- `tools/samples/config/settings.json` — `stoppingTimeout: 45`.
- `tests/MediaStation.Core.Tests/ConfigValidationTests.cs` — domyślne 45 s, nadpisanie, dodatniość, 600/601 dla obu pól, brak relacji do 30 s, kolejność i typ w schemacie.
- `tests/MediaStation.Core.Tests/OrchestratorTests.cs` — nowe identyfikatory po stopie, timeout stopu, spóźnione `TimerElapsed`/`CommandCompleted`/`CommandFailed`/`ProcessExited`, restart po timeoucie.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs` — pętla: `CloseApp` bez odpowiedzi hosta → `Advance(45 s)` → `Failed` bez nowego `focusRequest`.

**Review findings (quick):** 2 findings — high 0, medium 0, low 2. Patches applied: 2 (doc `MaxTimeoutSeconds` bez fałszywego uzasadnienia providera; teoria spóźnionego wyniku stopu pokrywa też `CommandFailed`). Deferred: 0. Rejected: 0.

**Patched counts by verdict:** high 0, medium 0, low 2.

**Follow-up review recommendation:** `false`.

**Verification:** `dotnet test MediaStation.slnx -c Release` 470/470 (0 failed, 0 skipped); `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` z 0 ostrzeżeń, w tym `npm run contracts`, `npm run typecheck` i Vite build; `settings.schema.json` z `stoppingTimeout` jako `integer`; publish self-contained i uruchomienie z ważnym configiem: `snapshotPublished {rev: 1, focusRequest {default, 1}}`, `stop {exitCode: 0}`, brak `warn`/`error`; sonda `stoppingTimeout: 601` dała `configError` z `$.stoppingTimeout` i ekran błędu.

**Residual risks:** timeout `Stopping` nie jest osiągalny w opublikowanej aplikacji bez prawdziwego hosta, który przyjmie `StopApp` i nie odpowie, więc jego dowodem są testy Orchestratora i pętli na `FakeTimeProvider`. Zmiana przesuwa `cmdId`/`timerId` wszystkich batchy po stopie; całościowy test na UM760 zostaje w odbiorze epiku użytkownika.
