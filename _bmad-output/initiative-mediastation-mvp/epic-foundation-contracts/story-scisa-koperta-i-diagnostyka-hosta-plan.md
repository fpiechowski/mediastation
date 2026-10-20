---
title: 'Ścisła koperta i diagnostyka hosta'
type: 'bugfix'
ticket: '7'
created: '2026-10-20'
status: 'built'
baseline_revision: '006eb7a21b0710acd7f0e7f618aba16514a4b308'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/epic-foundation-contracts/epic-foundation-contracts-retrospective.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Retro epiki (A1–A4) wykazało cztery zachowania bez śladu albo z błędnym wynikiem: koperta czyta pobłażliwie (`"up, down"`, `"UP"`, `"Kind"` przechodzą, schemat je odrzuca), awaria startu WebView2 kończy proces kodem 0, most i WebView2 gubią błędy bez logu, a dwa procesy dopisujące do jednego logu gubią i psują linie.

**Approach:** Osobne ścisłe opcje odczytu kontraktów ze ścisłym konwerterem enumów przeniesionym z configu do pliku w `Contracts`; `ExitCode.ShellStartFailed = 5` przez `Application.Shutdown`; `ILog` w `ShellBridge` i handlery `NavigationCompleted`/`ProcessFailed`/`NewWindowRequested` w `ShellWindow`; `FileLogSink` otwiera plik z samym `FileSystemRights.AppendData`, a program sprawdzający w `tools/` mierzy to dwoma procesami.

## Boundaries & Constraints

**Always:** serializacja (`ContractJson.Options`, `Serialize`, `SerializeEnvelope`) i eksport schematów bez zmian (schematy bajt w bajt); każdy odczyt kontraktu (`Deserialize`, `DeserializeEnvelope`, most) przez ścisłe opcje: nazwy pól z rozróżnieniem wielkości liter, enum tylko jako dokładna nazwa camelCase; dodatkowe pola koperty (w tym `id`) i payloadu nadal ignorowane; `ConfigJson` zachowuje obecne zachowanie i wszystkie testy configu zostają zielone; każdy wpis `src: "shell"`; błąd zapisu logu nadal nigdy nie przerywa aplikacji; log czytelny na żywo (`FileShare.ReadWrite`).

**Never:** guard `e.Source` i blokada nawigacji (F11, epic-home-screen); restart/odzyskiwanie WebView2 po `ProcessFailed`; zmiana `bridge.ts` ani kształtu koperty; pole `id` w `Envelope` (F6); cleanup z 1.6 (A5–A7: testy `ExitCode`, `BannedSymbols`, przenoszenie `ConfigJson`); projekt `App.Tests`; zmiana `ShutdownMode`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Lista enumów | `DeserializeEnvelope<Action>` z `"kind":"up, down"` | brak obiektu | `JsonException` |
| Wielkość liter | `"kind":"UP"`, pole `"Kind"`, `"V"`/`"Type"` w kopercie | brak obiektu | `JsonException` |
| Dodatkowe pola | `"id":"x"` w kopercie, nieznane pole w payloadzie | obiekt odczytany | — |
| Start WebView2 pada | `CreateAsync`/`EnsureCoreWebView2Async`/mapowanie rzuca | log `webView2StartFailed`, MessageBox, po OK kod 5 i `stop {exitCode: 5}` | `catch` w `OnLoaded` |
| Nawigacja nieudana | brak `shell/index.html` (404) albo brak dev servera | `Error("shell","navigationFailed",{uri, webErrorStatus, httpStatusCode})`, okno żyje | — |
| Proces WebView2 pada | `ProcessFailed` | `Error("shell","webViewProcessFailed",{kind, reason, exitCode, processDescription})` | bez odzyskiwania |
| `window.open`/`target=_blank` | `NewWindowRequested` | `Handled = true`, brak nowego okna, `Warn("shell","newWindowBlocked",{uri})` | — |
| Odrzucona wiadomość | zły JSON, `v` ≠ 1, `type` ≠ `ready` | `Warn("shell","bridgeMessageRejected",{v, type, reason})`; `v`/`type` = `null`, gdy nieczytelne | `JsonException` łapany |
| `ready` | `{"v":1,"type":"ready","payload":null}` | snapshot wysłany, brak wpisu | — |
| Dwa procesy | 2 × 5000 linii do jednego pliku jednocześnie | 10000 linii, każda parsuje się jako JSON, każda para (writer, seq) dokładnie raz | — |

</frozen-after-approval>

## Code Map

- `src/MediaStation.Core/Contracts/ContractJson.cs` -- `Options` (Web defaults: `PropertyNameCaseInsensitive = true`, `JsonStringEnumConverter` pobłażliwy) zostaje dla zapisu i `ExportSchema`; `Deserialize<T>` (l.36) i `DeserializeEnvelope` (l.60–83, envelope + `Payload.Deserialize(type, Options)`) przełączyć na nowe `ReadOptions`; doc l.107 o `ConfigJson` dostosować.
- `src/MediaStation.Core/Config/ConfigValidation.cs:384-451` -- `ConfigJson` (kopia `ContractJson.Options` + `UnmappedMemberHandling.Disallow`, `AllowDuplicateProperties = false`, `AllowOutOfOrderMetadataProperties = true`, `PropertyNameCaseInsensitive = false`, `StrictEnumConverterFactory` na indeksie 0) oraz prywatne `StrictEnumConverterFactory`/`StrictEnumConverter<T>` (l.405–450) -- konwerter przenieść 1:1 (pusty `JsonException` → komunikat STJ, na którym stoi klasyfikacja „zła wartość albo typ”); `ConfigJson` budować z `ReadOptions` + trzy flagi configu.
- `tests/MediaStation.Core.Tests/ConfigValidationTests.cs:823` -- `Config_options_do_not_change_contract_options` asercje na `ContractJson.Options` muszą dalej przechodzić.
- `tests/MediaStation.Core.Tests/CoreMessageContractTests.cs:110-143` -- styl testów macierzy koperty (`Assert.Throws<JsonException>`, raw string); `Additional_id_in_envelope_is_accepted` zostaje; plik ma alias `Action`.
- `tests/MediaStation.Core.Tests/ShellSnapshotContractTests.cs:54,65` -- parsowanie `ready` przez `ContractJson.Deserialize<Envelope<JsonElement?>>` (także z `id`) musi przejść na ścisłych opcjach.
- `src/MediaStation.App/Shell/ShellBridge.cs:34-55` -- trzy ciche `return`; konstruktor dostaje `ILog`.
- `src/MediaStation.App/Shell/ShellWindow.xaml.cs:35-67` -- handlery podpiąć po `EnsureCoreWebView2Async`, przed `Navigate`, w istniejącym `try`; `catch`: `Close()` → `Application.Current.Shutdown((int)ExitCode.ShellStartFailed)` po MessageBox.
- `src/MediaStation.App/CompositionRoot.cs:104` -- `new ShellBridge(snapshot)` → z `log`; `app.Exit` już loguje `stop {exitCode}`.
- `src/MediaStation.Core/Cli/ExitCode.cs` -- dopisać `ShellStartFailed = 5` z doc.
- `src/MediaStation.App/Logging/FileLogSink.cs:29-31` -- `FileStream(..., FileMode.Append, FileAccess.Write, FileShare.ReadWrite)`; zamienić na `FileSystemAclExtensions.Create(new FileInfo(path), FileMode.Append, FileSystemRights.AppendData, FileShare.ReadWrite, bufferSize, FileOptions.None, null)` (System.Security.AccessControl), zachować `catch`.
- `MediaStation.slnx`, `tools/MediaStation.SchemaExport/*.csproj` -- wzorzec projektu narzędzia (`TreatWarningsAsErrors`, `ImplicitUsings`).
- Baseline: `dotnet test` 224/224 (retro, HEAD `66aace8`).

## Tasks & Acceptance

**Execution:**
- [ ] `src/MediaStation.Core/Contracts/StrictEnumConverter.cs` -- `internal sealed class StrictEnumConverterFactory` (+ generyczny konwerter) przeniesiony z `ConfigValidation.cs` bez zmian zachowania -- wspólny dla kontraktów i configu (F3).
- [ ] `src/MediaStation.Core/Contracts/ContractJson.cs` -- `public static JsonSerializerOptions ReadOptions` = kopia `Options` z `PropertyNameCaseInsensitive = false` i ścisłym konwerterem na indeksie 0, read-only; `Deserialize` i `DeserializeEnvelope` (koperta i payload) używają `ReadOptions`; doc mówi, że `Options` służy zapisowi i schematom -- A1.
- [ ] `src/MediaStation.Core/Config/ConfigValidation.cs` -- `ConfigJson` z `ReadOptions` + `Disallow`/`AllowDuplicateProperties`/`AllowOutOfOrderMetadataProperties`; usunąć prywatne konwertery; doc bez sprzeczności -- A1.
- [ ] `src/MediaStation.Core/Cli/ExitCode.cs` -- `ShellStartFailed = 5` -- A2.
- [ ] `src/MediaStation.App/Shell/ShellBridge.cs`, `CompositionRoot.cs` -- `ILog` w ctor; `bridgeMessageRejected` dla każdej ścieżki odrzucenia z macierzy (powód po polsku; przy `JsonException` z komunikatem wyjątku) -- A3.
- [ ] `src/MediaStation.App/Shell/ShellWindow.xaml.cs` -- `NavigationCompleted` (tylko `!IsSuccess`), `ProcessFailed`, `NewWindowRequested` wg macierzy; `Shutdown(ShellStartFailed)` w `catch` -- A2, A3.
- [ ] `src/MediaStation.App/Logging/FileLogSink.cs` -- append przez `FileSystemRights.AppendData`; doc o atomowości między procesami -- A4.
- [ ] `tools/MediaStation.LogAppendCheck/` (`.csproj` net10.0-windows z `<Compile Include="..\..\src\MediaStation.App\Logging\FileLogSink.cs" Link=... />` + ref do Core, `Program.cs`), wpis w `MediaStation.slnx` -- bez argumentów: tworzy katalog tymczasowy, uruchamia siebie dwa razy (`Environment.ProcessPath`, tryb writer z id i liczbą linii, linie ~200 B), czeka, sprawdza macierz „Dwa procesy”, wypisuje wynik i kończy się kodem 0/1, sprząta katalog -- A4, check z `verify`.
- [ ] `tests/MediaStation.Core.Tests/CoreMessageContractTests.cs` -- wiersze koperty z macierzy (`"up, down"`, `"UP"`, `"Kind"`, `"V"`, `"Type"`, dodatkowe pole payloadu) -- A1.
- [ ] `tests/MediaStation.Core.Tests/ShellSnapshotContractTests.cs` -- `ready` z `"Type"` odrzucany; `ReadOptions` nie jest `Options`, a `Options` dalej `PropertyNameCaseInsensitive` -- A1.

**Acceptance Criteria:**
- Given checkout z tej story, when `dotnet run --project tools/MediaStation.LogAppendCheck -c Release`, then kod 0 i komunikat z 10000 poprawnymi liniami; given ten sam program na `FileLogSink` z baseline, then raportuje zgubione lub uszkodzone linie (sprawdzone raz przy implementacji).
- Given `dotnet build -c Release`, when porównuję `web/contracts/schema/*.schema.json` przed i po, then pliki identyczne.
- Given Release exe uruchomiony lokalnie bez zmian, when okno się otworzy i zamknę je, then log dnia ma `start`, `stop {exitCode: 0}` i nie ma `bridgeMessageRejected`, `navigationFailed` ani `webView2StartFailed`.

## Design Notes

Ścisłość dotyczy całego odczytu kontraktów, nie tylko `DeserializeEnvelope`: most czyta `Envelope<JsonElement?>` przez `Deserialize`, więc `"V"` albo `"Type"` z UI też ma się skończyć `bridgeMessageRejected`, a nie cichym dopasowaniem. `ConfigJson` = `ReadOptions` + reguły tylko dla plików (nieznane pole, duplikat klucza), więc ścisłość enumów ma jedno źródło. `FileSystemRights.AppendData` bez `WriteData` sprawia, że Windows dopisuje każdy `WriteFile` na bieżący koniec pliku atomowo, niezależnie od pozycji strumienia; jedna linia = jeden `Write`. Program sprawdzający linkuje źródło `FileLogSink`, a nie referuje App, żeby nie uruchamiać `BuildWeb` ani WPF.

## Verification

**Commands:**
- `dotnet test` -- expected: wszystkie zielone (224 + nowe).
- `dotnet build -c Release` -- expected: 0 ostrzeżeń, schematy bez zmian.
- `dotnet run --project tools/MediaStation.LogAppendCheck -c Release` -- expected: kod 0.

**Manual checks (if no CLI):**
HITL autora na maszynie dev (PowerShell w root repo, po `dotnet build -c Release`; `$exe = "src\MediaStation.App\bin\Release\net10.0-windows\MediaStation.exe"`, log: `$log = "$env:LOCALAPPDATA\MediaStation\logs\mediastation-$((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')).jsonl"`):
1. Awaria WebView2: `$env:WEBVIEW2_BROWSER_EXECUTABLE_FOLDER = "C:\nie-istnieje"; $p = Start-Process $exe -PassThru; $p.WaitForExit(); $p.ExitCode; Remove-Item Env:WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` → po kliknięciu OK w komunikacie błędu kod `5`; `Get-Content $log -Tail 3` → `webView2StartFailed` i `stop` z `"exitCode":5`.
2. Brak `index.html`: `Rename-Item src\MediaStation.App\bin\Release\net10.0-windows\shell\index.html index.html.bak`, uruchomić `$exe`, zamknąć Alt+F4, przywrócić nazwę → w logu `navigationFailed` z `httpStatusCode` 404.
3. Bez zmian: uruchomić `$exe` → shell (placeholder albo ekran błędu configu), Alt+F4 → w logu brak `bridgeMessageRejected`.

## Review Triage Log

| # | Finding | Verdict | Route | Evidence |
|---|---------|---------|-------|----------|
| 1 | `tools/MediaStation.LogAppendCheck/Program.cs` — final assertion accepts any 10 000 unique `(writer, seq)` pairs; expected ranges are never checked | medium | patch | Code adds each parsed pair to `seen` and passes when `lines.Length == expected`, `broken == 0`, `seen.Count == expected`; a file of 10 000 unique out-of-range pairs therefore prints OK. |
| 2 | `tools/MediaStation.LogAppendCheck/Program.cs` — per-line parsing catches only `JsonException`; missing or non-integer fields crash the tool | medium | patch | `GetProperty`/`GetInt32` throw `KeyNotFoundException`/`InvalidOperationException`, which escape the `catch (JsonException)`, so the tool violates the exit-code-0/1 contract on a parsed-but-structurally-wrong line. |
