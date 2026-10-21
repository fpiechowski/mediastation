---
title: 'Refactor sweep'
type: 'refactor'
ticket: '6'
created: '2026-10-20'
status: done
baseline_revision: 'f68450d75eae0283505de6ef0cc2eba5f7302342'
route: 'full'
route_source: 'auto'
risk: 'low'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/epic-foundation-contracts/epic-foundation-contracts-retrospective.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Retro epiki (A5–A7, F23, F25) zostawiło cleanup: czysta logika startu żyje w App bez testów (F20), kilka ścieżek błędu crashuje albo loguje dwa razy (F13–F15), shell i build Debug nie łapią dryfu typów (F16, F19), `BannedSymbols.txt` nie pilnuje AD-14 i obiektów OS (F5), `Action`/`InputMode` kolidują z BCL/WPF (F23), a rejestr komunikatów nie chroni nazw koperty (F25).

**Approach:** Przenieść decyzję startu, fabrykę snapshotu i mapowanie wyjątków I/O do Core z testami i przypiąć `ExitCode`; dołożyć tanie guardy w App, Core i web; dopisać zakazy i globalne aliasy; zachowanie widoczne dla użytkownika i linie logu bez zmian poza naprawionymi defektami.

## Boundaries & Constraints

**Always:** te same linie logu (`msg`, poziom, `data`) i kody wyjścia na każdej ścieżce startu; schematy w `web/contracts/schema` bajt w bajt; Core bez nowego I/O (AD-3); `dotnet build -c Release` 0 ostrzeżeń; testy w stylu `Snake_case_opis`.

**Never:** zmiany z 1.7 (koperta, WebView2, `FileLogSink`); deferred z A8 (F2, F6, F11, F12, F17, F18, F21); projekt `App.Tests`; zmiana nazw `Action`/`InputMode`; zmiana `ShutdownMode` ani `bridge.ts`; przenoszenie eksportu schematów configu z `ContractJson` do `Config` (F4: decyzja autora 2026-10-20, cykl `Contracts` ↔ `Config` zostaje).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Złe argumenty | `--foo` | `Error invalidArguments {reason}`, kod 2, mutex nie sprawdzany | — |
| Zajęta instancja | `[]` / `--smoke netflix`, mutex zajęty | `Warn instanceRejected {mode}`, kod 3 | — |
| Tryb nieobsługiwany | `--smoke netflix` / `--calibrate-input`, mutex wolny | `Warn modeUnsupported {mode[, appId]}`, kod 4 | — |
| Tryb główny | `[]`, mutex wolny | decyzja `Run` | — |
| Mutex niedostępny | `new Mutex` rzuca `UnauthorizedAccessException`/`WaitHandleCannotBeOpenedException`/`IOException` | `Error mutexUnavailable` z wyjątkiem, potem jak zajęta instancja (kod 3) | wyjątek łapany |
| Odczyt pliku | `FileNotFound`/`DirectoryNotFound` → `Missing`; `IO`/`UnauthorizedAccess`/`Security` → `Unreadable(Message)` | stan configu | inny wyjątek leci dalej |
| Snapshot | `ConfigResult` bez błędów / z błędami | `Placeholder` / `ConfigError` z tą samą listą | — |
| Wyjątek UI | nieobsłużony wyjątek w Dispatcherze | dokładnie jeden wpis `dispatcherUnhandledException` | — |
| Rzucający wyjątek | `Message` albo `ToString()` rzuca | linia logu powstaje, `err` z zastępczym tekstem | — |
| Nazwa koperty | rekord `Snapshot` albo `Ready` w rejestrze | `InvalidOperationException` przy budowie indeksu | — |

</frozen-after-approval>

## Code Map

- `src/MediaStation.App/CompositionRoot.cs:44-79` -- `Start`: parse → `Invalid` (log, `Shutdown(2)`) → `SingleInstance.TryAcquire` (log, `Shutdown(3)`) → `StartRun` / `RejectUnsupportedMode`; zastąpić jednym wywołaniem decyzji z Core i `log.Write(level, "app", msg, data)` + `Shutdown(code)`. `StartRun:85-104` buduje snapshot `Rev: 1` inline. `AttachLifecycleLogging:121-142`: `UnhandledException` i `DispatcherUnhandledException` logują ten sam wyjątek (F14).
- `src/MediaStation.Core/Cli/CliArgs.cs` -- `CliParseResult.Invalid(Reason)`/`Parsed(Mode)`, `CliMode.LogName`, `CliMode.Smoke(AppId)`; wzorzec rekordu bazowego z prywatnym ctor.
- `src/MediaStation.Core/Cli/ExitCode.cs` -- `Ok 0, InvalidArguments 2, AlreadyRunning 3, Unsupported 4, ShellStartFailed 5` (od 1.7); brak testu wartości.
- `src/MediaStation.App/SingleInstance.cs:26` -- `new Mutex(...)` przed `try`, `catch` tylko `AbandonedMutexException`.
- `src/MediaStation.App/Config/ConfigFileReader.cs:20-36` -- `Read`: filtry wyjątków do przeniesienia; `ReadAll` bez zmian.
- `src/MediaStation.Core/Config/ConfigPreflight.cs:12-24` -- `ConfigFileState` (`Present`/`Missing`/`Unreadable(Detail)`); miejsce na statyczne mapowanie wyjątku.
- `src/MediaStation.Core/Config/ConfigValidation.cs:12-15` -- `ConfigResult(Config, Errors)`; doc `ConfigJson` (l.377-381) już mówi „struktura zgodna ze schematem, reguły semantyczne tylko w runtime” (zrobione w 1.7) — nie ruszać.
- `src/MediaStation.Core/Logging/JsonLinesLog.cs:43-44` -- `error.Message`/`error.ToString()` przed `try`.
- `src/MediaStation.Core/Contracts/MessageTypes.cs:38-48` -- `BuildIndex` (prywatny, `index.Add`); stałe `Envelope.SnapshotType`/`ReadyType` w `Envelope.cs`. Core nie ma `InternalsVisibleTo`; `StrictEnumConverterFactory` już jest `internal`.
- `src/MediaStation.Core/BannedSymbols.txt` -- format `T:`/`M:`/`N:` + `;AD-n: powód`; `MediaStation.Core.csproj` ma `TreatWarningsAsErrors`, więc RS0030 łamie build.
- `src/MediaStation.Core/Contracts/ContractJson.cs:9,113-128` -- `using MediaStation.Core.Config` tylko dla `ConfigTypes` w `ExportSchemas()` (cykl F4); konsumenci `ExportSchemas()`: `tools/MediaStation.SchemaExport/Program.cs:11`, `CoreMessageContractTests.cs:188,269,272`, `ConfigValidationTests.cs:834`.
- `src/MediaStation.App/MediaStation.App.csproj:39` -- Debug: `npm run contracts` bez typecheck; `web/package.json` ma `typecheck` (`tsc -p tsconfig.json`, obejmuje `contracts/generated`).
- `web/shell/src/App.tsx:17-22` -- `switch (snapshot.screen)` bez `default`.
- `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` -- `<Using Include="Xunit" />`, `ImplicitUsings` włączone; `CoreMessageContractTests.cs:4` lokalny alias `Action`. App dziś nie używa `System.Action` ani `InputMode`.
- `tests/MediaStation.Core.Tests/CliArgsTests.cs`, `JsonLinesLogTests.cs` (fake sink z listą linii) -- wzorce testów.

## Tasks & Acceptance

**Execution:**
- [ ] `src/MediaStation.Core/Cli/StartDecision.cs` -- `StartDecision.Decide(IReadOnlyList<string> args, Func<bool> tryAcquireInstance)` → `Run` albo `Exit(ExitCode, LogLevel, Msg, Data)` wg macierzy; `tryAcquireInstance` wołane tylko po poprawnym parse -- A5.
- [ ] `src/MediaStation.App/CompositionRoot.cs` -- `Start` przez `StartDecision`; snapshot z fabryki; jeden wpis na nieobsłużony wyjątek UI (`AppDomain` pomija wyjątek już zalogowany przez Dispatcher) -- A5, F14.
- [ ] `src/MediaStation.Core/Config/ShellSnapshotFactory.cs` -- `FromConfig(ConfigResult, long rev)` -- A5.
- [ ] `src/MediaStation.Core/Config/ConfigPreflight.cs`, `src/MediaStation.App/Config/ConfigFileReader.cs` -- `ConfigFileState.FromReadError(Exception) → ConfigFileState?` (null = nie I/O); reader: `catch (Exception ex) when (ConfigFileState.FromReadError(ex) is { } state)` -- A5.
- [ ] `src/MediaStation.App/SingleInstance.cs` -- konstruktor w `try`; wyjątki z macierzy → `Error mutexUnavailable`, `false` -- F13.
- [ ] `src/MediaStation.Core/Logging/JsonLinesLog.cs` -- `LogError` budowany odpornie (każde z `Message`/`ToString` osobno, zastępczy tekst z typem wyjątku) -- F15.
- [ ] `web/shell/src/App.tsx` -- `default` z `const unreachable: never` -- F16.
- [ ] `src/MediaStation.App/MediaStation.App.csproj` -- Debug: `npm run typecheck` po `npm run contracts`; globalne aliasy `Action` i `InputMode` -- F19, F23.
- [ ] `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj`, `CoreMessageContractTests.cs` -- te same aliasy; usunąć lokalny alias -- F23.
- [ ] `src/MediaStation.Core/BannedSymbols.txt` -- `DateTime`/`DateTimeOffset` `Now`/`UtcNow`/`Today` (AD-14), `T:System.Diagnostics.Process`, `N:System.Net`, `T:System.Threading.Mutex`/`Semaphore`/`EventWaitHandle`, `Environment.GetFolderPath` (oba overloady) -- A7.
- [ ] `src/MediaStation.Core/Contracts/MessageTypes.cs`, `MediaStation.Core.csproj` -- `internal static BuildIndex(IEnumerable<Type>)` odrzuca `snapshot`/`ready`; `<InternalsVisibleTo Include="MediaStation.Core.Tests" />` -- F25.
- [ ] `tests/MediaStation.Core.Tests/StartDecisionTests.cs`, `ShellSnapshotFactoryTests.cs`, `ConfigFileStateTests.cs`, `ExitCodeTests.cs` (dokładny zestaw nazwa→wartość), `MessageTypesTests.cs`, `JsonLinesLogTests.cs` -- wiersze macierzy poza Mutex i Dispatcherem -- A5, F15, F25.

**Acceptance Criteria:**
- Given checkout tej story, when `dotnet test`, then wszystko zielone z nowymi testami; when `dotnet build -c Release`, then 0 ostrzeżeń i `git status web/contracts/schema` czysty.
- Given błąd typu w `web/shell/src` (np. `const x: number = 'a'`), when `dotnet build` (Debug), then build pada na `tsc`.
- Given tymczasowe `DateTime.UtcNow` i `System.Net.Http.HttpClient` w Core, when `dotnet build`, then RS0030 dla obu (probe, cofnięty).
- Given publish na UM760 (HITL, autor), when wykonuję Manual checks, then wyniki zgodne z opisem.

## Review Triage Log

Pass 1 (quick): no findings.

## Design Notes

`StartDecision` dostaje `Func<bool>`, bo mutex jest obiektem OS (App), a kolejność „parse przed mutexem” to właśnie logika do przypięcia testem (fake liczy wywołania). `Exit` niesie `LogLevel`/`Msg`/`Data`, więc App pisze `log.Write(d.Level, Src, d.Msg, d.Data)` — linie logu zostają identyczne. Fabryka snapshotu siedzi w `Config`, nie w `Contracts`, żeby nie dokładać zależności `Contracts → Config` (F4).

## Verification

**Commands:**
- `dotnet test` -- expected: wszystkie zielone.
- `dotnet build -c Release` -- expected: 0 ostrzeżeń, schematy bez zmian.

**Manual checks (if no CLI):**
HITL autora na UM760 (PowerShell; `$exe` = `MediaStation.exe` z katalogu publish, `$cfg = "$env:LOCALAPPDATA\MediaStation\config"`, `$log = "$env:LOCALAPPDATA\MediaStation\logs\mediastation-$((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')).jsonl"`):
1. `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained` na maszynie dev, kopia na UM760; Done when 1–5 epiki jak w ich `verify`.
2. Tryb główny: `Start-Process $exe` → okno shella (placeholder albo ekran błędu configu).
3. Przy działającym oknie: `(Start-Process $exe -Wait -PassThru).ExitCode` i `(Start-Process $exe '--smoke','netflix' -Wait -PassThru).ExitCode` → `3` i `3`; `Get-Content $log -Tail 6` → dwa `instanceRejected` (`run`, `smoke`) i `stop` z `"exitCode":3`.
4. Zamknąć okno (Alt+F4). Dla `$f` kolejno `apps.json`, `input.json`, `settings.json`: `Copy-Item $cfg\$f $cfg\$f.bak; $j = Get-Content $cfg\$f -Raw | ConvertFrom-Json; $j | Add-Member x 1; $j | ConvertTo-Json -Depth 20 | Set-Content $cfg\$f; Start-Process $exe` → ekran błędu konfiguracji z `$f` i ścieżką `$.x` (nieznane pole), nie crash; Alt+F4, `Move-Item $cfg\$f.bak $cfg\$f -Force`.