---
title: 'Logger JSON Lines'
type: 'feature'
ticket: '3'
created: '2026-10-20'
status: 'built'
baseline_revision: 'cd8b650bdb2dc881af20805d6026c2b62abf5530'
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

**Problem:** MediaStation nie zostawia żadnego śladu na dysku; start i nieobsłużone wyjątki na UM760 giną, a kolejne epiki (checki CAP-3, CAP-9, story 4 i 5) potrzebują wspólnego logu.

**Approach:** Port logu w Core (`ILog`) z implementacją JSON Lines, która bierze czas z `TimeProvider`, sama wybiera dzienny plik po dacie UTC i oddaje gotową linię do portu zapisu; App/Logging dopisuje linie do `%LOCALAPPDATA%\MediaStation\logs`, a composition root loguje start, koniec i nieobsłużone wyjątki.

## Boundaries & Constraints

**Always:** Core bez `System.IO` i Windows (AD-3) — nazwa pliku, rotacja i format linii to czysta logika w Core; czas tylko przez `TimeProvider` (AD-14); każda linia to jeden kompaktowy obiekt JSON z `ts` w UTC ISO-8601 z `Z`, `level`, `src`, `msg`; enumy jako stringi camelCase; rotacja po dacie UTC; błąd zapisu logu nigdy nie przerywa aplikacji; `FakeTimeProvider` z `Microsoft.Extensions.TimeProvider.Testing` tylko w `tests/MediaStation.Core.Tests`.

**Never:** projekt `App.Tests`; retencja/kasowanie starych plików; poziom logowania z `settings.json`; logowanie błędów configu (story 4), odrzucenia drugiej instancji (story 5), zdarzeń strony; asynchroniczna kolejka zapisu; zewnętrzne biblioteki logowania (Serilog, M.E.Logging).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Zapis | czas 2026-10-20T21:15:03.123Z, `Info("app", "start")` | linia w `mediastation-2026-10-20.jsonl`: `{"ts":"2026-10-20T21:15:03.123Z","level":"info","src":"app","msg":"start"}` | — |
| Północ UTC | zapis o 23:59:59.900Z, czas +200 ms, zapis | dwa pliki: `…-10-20.jsonl` i `…-10-21.jsonl`, po jednej linii | — |
| Czas lokalny ≠ UTC | `TimeProvider` ze strefą UTC+2, 00:30 lokalnie | plik i `ts` wg UTC (poprzedni dzień) | — |
| Dane i wyjątek | `data` ze słownikiem, `Error(..., ex)` | pola `data` i `err {type, message, stack}`; brak tych pól, gdy null | — |
| Znaki nowej linii | `msg`/stack z `\n` | wciąż jedna linia (escapowane) | — |
| Zapis niemożliwy | brak uprawnień / plik zablokowany | wpis pominięty | wyjątek I/O łapany w App/Logging, bez crasha |

</frozen-after-approval>

## Code Map

- `src/MediaStation.Core/MediaStation.Core.csproj` -- `ImplicitUsings` wyłączone, `TreatWarningsAsErrors`; nowe pliki potrzebują jawnych `using`.
- `src/MediaStation.Core/Contracts/ContractJson.cs` -- wzorzec opcji JSON (Web defaults, `JsonStringEnumConverter(CamelCase)`); log ma własne opcje z `WhenWritingNull`, nie zmieniać `ContractJson`.
- `src/MediaStation.App/CompositionRoot.cs` -- `DataRoot` (`%LOCALAPPDATA%\MediaStation`); dodać `LogsDir`, utworzyć log jako pierwszy krok `Start`.
- `src/MediaStation.App/App.xaml.cs` -- `OnStartup` → `CompositionRoot.Start(this)`.
- `src/MediaStation.App/Shell/ShellWindow.xaml.cs:58` -- `catch` z MessageBox przy błędzie WebView2; ma też zalogować wyjątek.
- `src/MediaStation.App/Config/ConfigFileReader.cs` -- wzorzec łapania wyjątków I/O w App.
- `tests/MediaStation.Core.Tests/*.csproj` -- xunit.v3 4.0.2, `ImplicitUsings` włączone; styl nazw testów `Snake_case_opis`.
- NuGet: `Microsoft.Extensions.TimeProvider.Testing` 10.11.0 (najnowsza).

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Logging/ILog.cs` -- port `ILog.Write(LogLevel, string src, string msg, IReadOnlyDictionary<string, object?>? data = null, Exception? error = null)`, enum `LogLevel { Debug, Info, Warn, Error }`, extension `Info/Warn/Error` -- jeden port dla wszystkich komponentów.
- [x] `src/MediaStation.Core/Logging/ILogLineSink.cs` -- port `Append(string fileName, string line)` implementowany w App -- I/O poza Core.
- [x] `src/MediaStation.Core/Logging/LogFileName.cs` -- `For(DateTimeOffset)` → `mediastation-yyyy-MM-dd.jsonl` po dacie UTC (invariant culture).
- [x] `src/MediaStation.Core/Logging/JsonLinesLog.cs` -- `ILog` z (`TimeProvider`, `ILogLineSink`): pod lockiem bierze `GetUtcNow()`, nazwę pliku i serializuje linię, oddaje do sinka -- rotacja = zmiana nazwy pliku między zapisami.
- [x] `src/MediaStation.App/Logging/FileLogSink.cs` -- dopisuje linię + `\n` do `<logsDir>\<fileName>` (tworzy katalog, `FileShare.ReadWrite`), łapie `IOException`/`UnauthorizedAccessException`/`SecurityException`.
- [x] `src/MediaStation.App/CompositionRoot.cs`, `App.xaml.cs`, `Shell/ShellWindow.xaml.cs` -- `CompositionRoot.Log` tworzony pierwszy w `Start` z `TimeProvider.System`; `Info("app","start", {version, pid, args})`; `app.Exit` → `Info("app","stop", {exitCode})`; `AppDomain.UnhandledException`, `DispatcherUnhandledException` (bez `Handled = true`), `TaskScheduler.UnobservedTaskException` → `Error`; `ShellWindow` dostaje `ILog` i loguje `Error("shell", ...)` w istniejącym `catch`.
- [x] `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj`, `JsonLinesLogTests.cs` -- paczka `FakeTimeProvider`; fake sink zbierający (plik, linia); testy macierzy I/O, w tym każda linia parsuje się `JsonDocument` z `ts` kończącym się `Z`.

**Acceptance Criteria:**
- Given `MediaStation.Core.csproj`, when przeglądam referencje i `using`, then logowanie w Core nie używa `System.IO` ani paczek poza BCL.
- Given build Release uruchomiony lokalnie, when zamykam okno, then `%LOCALAPPDATA%\MediaStation\logs\mediastation-<dzień UTC>.jsonl` ma linie `start` i `stop`.
- Given publish na UM760 (HITL, autor), when uruchamiam `MediaStation.exe`, then w `%LOCALAPPDATA%\MediaStation\logs` jest plik dzienny z wpisem `start`.

## Implementation Notes

- `err.stack` = `Exception.ToString()` (typ, message, stack trace i wyjątki wewnętrzne); dla `UnobservedTaskException` (`AggregateException`) i `TargetInvocationException` sam `StackTrace` gubiłby przyczynę.
- Sink wołany pod tym samym lockiem co `GetUtcNow()` i serializacja, więc kolejność linii = kolejność `ts`, także na granicy dób; `FileLogSink` nie potrzebuje własnego locka.
- Nieserializowalne `data` (cykl, NaN/±Infinity, nieobsługiwany typ, rzucający getter; każdy wyjątek poza `OutOfMemoryException`) nie gubi wpisu: linia idzie z `data: { serializationError }`.
- Encoder `UnsafeRelaxedJsonEscaping`: polskie znaki czytelne w pliku; `\n`, `\r` i `"` nadal escapowane. Plik UTF-8 bez BOM.
- `CompositionRoot.Start(app, args)` dostaje `e.Args` z `OnStartup`; `version` = `AssemblyInformationalVersion` (z hashem commita), fallback `AssemblyName.Version`. `msg` dla wyjątków: `unhandledException` (+ `data.isTerminating`), `dispatcherUnhandledException`, `unobservedTaskException`; shell: `webView2StartFailed`.
- Weryfikacja: `dotnet test` 38/38 zielone (10 nowych w `JsonLinesLogTests`), `dotnet build -c Release` 0 ostrzeżeń; Release exe uruchomiony i zamknięty (`CloseMainWindow`) zapisał `start` + `stop` w `%LOCALAPPDATA%\MediaStation\logs\mediastation-2026-10-20.jsonl`; przy pliku zablokowanym `FileShare.None` aplikacja wystartowała i zamknęła się z kodem 0, wpisy pominięte. UM760 (HITL) czeka na autora.

## Plan Change Log

## Review Triage Log

Pass 1 (quick): high 0, medium 1, low 1, false 1, maybe-false 0.

| # | Finding (quick) | Verdict | Route | Evidence |
|---|---|---|---|---|
| 1 | Fallback w `JsonLinesLog.Format` łapie tylko `JsonException`/`NotSupportedException`/`InvalidOperationException`; `double.NaN` w `data` (ArgumentException) albo rzucający getter przechodzą do wołającego | medium | patch | `Utf8JsonWriter.WriteNumberValue(NaN)` rzuca `ArgumentException`, spoza filtra; `_log.Error` w `catch` `ShellWindow.OnLoaded` (async void) wtedy crashuje proces. Filtr poszerzony do każdego nie-fatalnego wyjątku + test z NaN. |
| 2 | `FileMode.Append` + `FileShare.ReadWrite` nie jest atomowym appendem między procesami; dwa procesy mogą nadpisać sobie linię | low | reject | Dziś drugi proces (przed mutexem story 5) loguje `start` w innym momencie niż idle'ujący pierwszy; kolizja wymaga zapisów w tym samym oknie open→seek→write. Poprawka (`FileSystemRights.AppendData` przez ACL API) to dodatkowa złożoność; do rozważenia, gdy `--supervise` (epika Deploy) zacznie logować równolegle z dzieckiem. |
| 3 | AC3 (UM760) niespełnione, AC2 tylko z notatki | false | reject | AC3 to check HITL autora, nie defekt kodu (zostaje w Verification). AC2 potwierdzone: `%LOCALAPPDATA%\MediaStation\logs\mediastation-2026-10-20.jsonl` ma linie `start` (args `--test-arg`) i `stop` `{exitCode:0}`. |

## Design Notes

Zapis synchroniczny: wpis z `UnhandledException` musi trafić na dysk przed śmiercią procesu, a wolumen jest mały (start, błędy). Przy kolejkach Orchestratora/InputHub (AD-14: brak blokujących wywołań) można później podmienić sink na buforowany bez zmiany portu. Otwieranie pliku per zapis (bez trzymanego uchwytu) upraszcza rotację i pozwala autorowi czytać log na żywo. `src` to krótka nazwa źródła lowercase (`app`, `shell`; później np. `page`).

## Verification

**Commands:**
- `dotnet test` -- wszystkie testy Core zielone, w tym `JsonLinesLogTests`.
- `dotnet build -c Release` -- sukces bez ostrzeżeń.

**Manual checks (if no CLI):**
- Uruchomić `src/MediaStation.App/bin/Release/net10.0-windows/MediaStation.exe`, zamknąć (Alt+F4), sprawdzić linie `start`/`stop` w pliku dziennym.
- UM760 (HITL, autor): plik dzienny z `start` po uruchomieniu publish.
