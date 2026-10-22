---
title: 'Jedna instancja i wejście CLI'
type: 'feature'
ticket: '5'
created: '2026-10-20'
status: done
baseline_revision: '7567ec4ccca764b90039ad12b15ca50445347d1a'
route: 'full'
route_source: 'auto'
risk: 'low'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `MediaStation.exe` startuje dowolnie wiele razy i ignoruje argumenty; druga instancja dałaby dwa hooki wejścia i dwóch klientów CDP (AD-2), a epiki 3 (`--smoke`) i 11 (`--calibrate-input`) nie mają punktu wejścia.

**Approach:** Czysty parser argumentów w Core zwraca tryb albo błąd, enum kodów wyjścia w Core; composition root po logu startu parsuje argumenty, bierze named mutex przed jakimkolwiek trybem i dopiero wtedy dispatchuje: tryb główny jak dziś, `--smoke`/`--calibrate-input` kończą się kodem „nieobsługiwane”.

## Boundaries & Constraints

**Always:** parser bez I/O w Core (AD-3); mutex w App, trzymany przez cały czas życia procesu każdego trybu (smoke też blokuje instancję główną); odrzucenie i każde zakończenie bez okna przez `Application.Shutdown(kod)`, więc linia `stop` z `exitCode` nadal powstaje; każde odrzucenie ma osobny wpis w logu z trybem; okno shella i walidacja configu tylko w trybie głównym; argumenty dokładne (wielkość liter ma znaczenie).

**Never:** `--supervise` ani `--rollback-to` (epika Deploy — dziś to nieznany argument); implementacja smoke i kalibracji; przywracanie/aktywowanie okna działającej instancji; sprawdzanie `appId` względem `apps.json`; projekt `App.Tests`; zmiana `ShutdownMode`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Tryb główny | brak argumentów, brak innej instancji | okno shella jak dziś, mutex trzymany | — |
| Druga instancja | brak argumentów, instancja działa | bez okna, kod `AlreadyRunning`, `Warn("app","instanceRejected",{mode:"run"})` | — |
| Smoke przy działającej | `--smoke netflix`, instancja działa | kod `AlreadyRunning`, wpis z `mode:"smoke"` | — |
| Smoke sam | `--smoke netflix`, brak instancji | kod `Unsupported`, `Warn("app","modeUnsupported",{mode:"smoke", appId})` | — |
| Kalibracja sama | `--calibrate-input` | kod `Unsupported`, wpis z `mode:"calibrateInput"` | — |
| Złe argumenty | `--smoke` bez appId, `--smoke --x`, nieznany `--foo`, nadmiarowy argument, `--SMOKE` | kod `InvalidArguments`, `Error("app","invalidArguments",{reason})` po polsku, bez mutexu | — |
| Porzucony mutex | poprzedni właściciel padł z otwartym uchwytem u innego procesu | mutex przejęty, start normalny, `Warn("app","mutexAbandoned")` | `AbandonedMutexException` łapany |

</frozen-after-approval>

## Code Map

- `src/MediaStation.App/CompositionRoot.cs` -- `Start(app, args)`: log tworzony pierwszy, potem linia `start` z `args`; dalej config → snapshot → `ShellWindow`. Wstawić parse → mutex → dispatch między logiem startu a configiem; obecny kod configu/okna przenieść do metody trybu głównego. `AttachLifecycleLogging` loguje `stop` z `e.ApplicationExitCode` — zostawić.
- `src/MediaStation.App/App.xaml.cs` -- `OnStartup` → `CompositionRoot.Start(this, e.Args)`; bez zmian.
- `src/MediaStation.App/App.xaml` -- `ShutdownMode="OnMainWindowClose"`; `Shutdown(kod)` bez otwartego okna kończy proces z tym kodem.
- `src/MediaStation.Core/Logging/ILog.cs` -- `Info`/`Warn`/`Error` (uwaga: `Error(src, msg, error, data)` — wyjątek przed `data`).
- `src/MediaStation.Core/Config/ConfigPreflight.cs` -- wzorzec: rekord bazowy z prywatnym ctor i zagnieżdżonymi `sealed record` (do reużycia dla wyniku parsera); Core ma `ImplicitUsings` wyłączone — jawne `using`.
- `src/MediaStation.Core/BannedSymbols.txt` -- `System.Threading.Mutex` nie jest zbanowany, ale named mutex to obiekt OS → tylko w App.
- `tests/MediaStation.Core.Tests/ConfigPreflightTests.cs` -- styl testów (`Snake_case_opis`, xunit.v3, `ImplicitUsings` włączone).

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Cli/ExitCode.cs` -- `enum ExitCode { Ok = 0, InvalidArguments = 2, AlreadyRunning = 3, Unsupported = 4 }` z komentarzem, że epiki dopisują kody (smoke: `CdpUnavailable`, `PortInUse`, `ProfileInUse`) i 1 zostaje na ogólną awarię -- jeden słownik kodów dla autora i `--supervise`.
- [x] `src/MediaStation.Core/Cli/CliArgs.cs` -- `CliMode` (`Run`, `Smoke(AppId)`, `CalibrateInput`) z nazwą trybu do logu (`run`/`smoke`/`calibrateInput`), `CliArgs.Parse(IReadOnlyList<string>) → CliParseResult` (tryb albo polski powód błędu) -- reguły z macierzy.
- [x] `src/MediaStation.App/SingleInstance.cs` -- `TryAcquire(ILog) → bool`: `Mutex(false, @"Local\MediaStation.SingleInstance")` + `WaitOne(0)`, `AbandonedMutexException` = przejęty z wpisem, instancja trzymana w polu statycznym do końca procesu.
- [x] `src/MediaStation.App/CompositionRoot.cs` -- parse → (błąd: log + `Shutdown(InvalidArguments)`) → `SingleInstance` (zajęty: log + `Shutdown(AlreadyRunning)`) → dispatch; `Run` = dotychczasowa ścieżka, `Smoke`/`CalibrateInput` = log + `Shutdown(Unsupported)`.
- [x] `tests/MediaStation.Core.Tests/CliArgsTests.cs` -- wszystkie wiersze parsera z macierzy, w tym `appId` zaczynający się od `-`, puste `appId`, powtórzony tryb i wielkość liter.

**Acceptance Criteria:**
- Given działający Release `MediaStation.exe`, when uruchamiam drugi raz bez argumentów i z `--smoke netflix` (`Start-Process -Wait -PassThru`), then oba kończą się kodem 3 bez okna, a log dnia ma dwie linie `instanceRejected` (`mode` `run` i `smoke`) i ich `stop` z `exitCode: 3`.
- Given brak działającej instancji, when uruchamiam `--smoke netflix`, then kod 4 i linia `modeUnsupported`; potem zwykły start otwiera okno (mutex zwolniony z końcem procesu).
- Given publish na UM760 (HITL, autor), when powtarzam pierwszy scenariusz, then ten sam wynik.

## Implementation Notes

- Parser rozpoznaje dokładnie: brak argumentów, `--calibrate-input` i `--smoke <appId>`; `appId` musi być niepusty i nie zaczynać się od `-`. Powody w logu: `brak appId po --smoke`, `appId nie może być pusty`, `appId nie może zaczynać się od '-'`, `tryb podano więcej niż raz`, `nadmiarowy argument: <arg>`, `nieznany argument: <arg>`. Testy przypinają te teksty.
- Odrzucenie drugiej instancji i trybu nieobsługiwanego loguje `mode` z `CliMode.LogName`, a `modeUnsupported` dla smoke dokłada `appId`. Wszystkie ścieżki bez okna kończą się przez `Application.Shutdown(kod)`, więc `Exit` nadal pisze `stop` z `exitCode`.
- `SingleInstance` trzyma `Mutex` w polu statycznym do końca procesu i nie woła `ReleaseMutex`; `WaitOne(0)` obsługuje porzucony mutex, który jest przejmowany z `mutexAbandoned`.
- Weryfikacja: `dotnet test` 224/224 (18 nowych `CliArgsTests`); `dotnet build -c Release` 0 ostrzeżeń. Lokalnie na Release exe: druga instancja bez argumentów i `--smoke netflix` przy działającej instancji → kod 3 i wpisy `instanceRejected` (`run`, `smoke`) ze `stop` `exitCode: 3`; `--smoke netflix` bez instancji → kod 4, `modeUnsupported` i `stop` `exitCode: 4`; kolejny zwykły start otworzył okno. `--calibrate-input` → 4; `--smoke`, `--foo`, `--SMOKE`, `--smoke --x`, `--smoke netflix extra` → 2 i `invalidArguments`. Porzucony mutex sprawdzony procesem-keepersem: `mutexAbandoned`, potem normalny dispatch. UM760 (HITL, AC3) nie sprawdzone — zostaje dla autora.

## Plan Change Log

## Review Triage Log

| # | Lens | Lokalizacja | Verdict | Route | Evidence |
|---|------|-------------|---------|-------|----------|
| 1 | quick | AC3 (publish na UM760) | false | reject | Plan z założenia oddaje AC3 HITL autorowi; Implementation Notes i Verification wprost zostawiają ją autorowi, a w kodzie nie ma braku — to status weryfikacji, nie defekt. AC1 i AC2 sprawdzone lokalnie na Release exe. |

## Design Notes

- `Local\` (sesja użytkownika): UM760 to jeden zalogowany użytkownik, zadanie „at logon” startuje w jego sesji; `Global\` nic nie daje, a w innych sesjach i tak nie ma hooka tej sesji.
- Złe argumenty przed mutexem: to nie jest tryb, więc nie blokuje działającej instancji ani jej nie wymaga; autor dostaje `InvalidArguments` niezależnie od stanu.
- `WaitOne(0)` zamiast `createdNew`: obsługuje porzucony mutex. Mutex nie jest zwalniany ręcznie — OS zwalnia go z końcem procesu, a `ReleaseMutex` z innego wątku niż właściciel rzuca.
- Kody 2–4 zamiast 1: 1 bywa ogólną awarią, a .NET przy nieobsłużonym wyjątku zwraca `0xE0434352`, więc autor odróżni odrzucenie od crasha.

## Verification

**Commands:**
- `dotnet test` -- wszystkie zielone, w tym nowe `CliArgsTests`.
- `dotnet build -c Release` -- 0 ostrzeżeń.

**Manual checks (if no CLI):**
- Lokalnie scenariusze AC1 i AC2 na `src/MediaStation.App/bin/Release/net10.0-windows/MediaStation.exe`, kody z `$p.ExitCode`, linie w `%LOCALAPPDATA%\MediaStation\logs\mediastation-<dzień UTC>.jsonl`.
- UM760 (HITL, autor): AC3.
