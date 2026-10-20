---
title: 'Kontrakty Core dla Orchestratora i InputHub'
type: 'feature'
ticket: '2'
created: '2026-10-20'
status: 'built'
baseline_revision: 'dfe666c3a7cf2926d3fbd5fa4b4ebc8c6c6a6b64'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Epiki Orchestrator (2) i Input (3) mają iść równolegle, ale Core ma tylko `ShellSnapshot`; brakuje wspólnych rekordów Action, SetInputRouting, Command/Event/Intent z `ErrorCode`, a granica Core (AD-3) nie jest niczym egzekwowana poza dyscypliną.

**Approach:** Rekordy w `MediaStation.Core/Contracts` na wspólnych `ContractJson.Options`, koperta `{ v, type, payload }` z `type` wyznaczanym z rejestru typów komunikatów (polimorfizm w kopercie, nie w payloadzie), eksport schematu każdego rekordu i generowanie TS w istniejącym pipeline, plus BannedApiAnalyzers w Core i test granicy czytający metadane assembly.

## Boundaries & Constraints

**Always:** serializacja camelCase, enumy jako stringi, jawne `null` (AD-16); `cmdId` na każdym Command, `CommandCompleted{cmdId}` / `CommandFailed{cmdId, errorCode}` jako Events (AD-4); Action `{ kind: up|down|left|right|ok|back|home|appMenu|keyboard, phase: press|repeat|long, source, tsMono }` (AD-8); `SetInputRouting` to Command (`cmdId`) z `{ mode: shell|app|osk, appType, pointer: blocked|allowed, epoch }`; wygenerowane schema i TS nie są w gicie ani edytowane ręcznie; Core zostaje czystym `net10.0`; paczki z przypiętą wersją.

**Never:** konkretne Intents i Commands/Events poza wymienionymi (należą do epik 2–4); Orchestrator, InputHub, `input.json`; zmiana kształtu `snapshot`/`ready` ani `bridge.ts`; `[JsonPolymorphic]` z dyskryminatorem w payloadzie; osobny projekt `App.Tests`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Zapis w kopercie | `SetInputRouting(7, Shell, null, Blocked, 3)` jako `Command` | `{"v":1,"type":"setInputRouting","payload":{"cmdId":7,"mode":"shell","appType":null,"pointer":"blocked","epoch":3}}` | — |
| Odczyt polimorficzny | koperta `commandFailed` czytana jako `Event` | instancja `CommandFailed` równa oryginałowi | — |
| Nieznany `type` | `{"v":1,"type":"nope","payload":{}}` | brak obiektu | `JsonException` |
| Typ spoza bazy | koperta `setInputRouting` czytana jako `Event` | brak obiektu | `JsonException` |
| Inny major `v` | `{"v":2,…}` | brak obiektu | `JsonException` |
| Enum jako liczba / brak pola | `"phase":0` albo brak `epoch` | brak obiektu | `JsonException` |
| Dodatkowe `id` w kopercie | `{"v":1,"type":"action","id":"x","payload":{…}}` | Action odczytana | — |

**Decisions (2026-10-20):** `Action.source` to enum klasy urządzenia `ActionSource { remote, keyboard, gamepad }`, a sygnatura urządzenia zostaje w `input.json`. `ErrorCode` startuje z `unexpected` (wyjątek złapany na granicy portu, AD-4) i pięcioma kodami z architektury: `portInUse`, `profileInUse`, `cdpUnavailable`, `updateDownloadFailed`, `updateChecksumMismatch`. Plan zostaje w całości mimo ~2700 tokenów, bo ticket łączy kontrakty z egzekwowaniem granicy Core.

</frozen-after-approval>

## Code Map

- `src/MediaStation.Core/Contracts/ContractJson.cs` -- wspólne `Options` (Strict numbers, required ctor params, enumy camelCase bez liczb) i `ExportSchema(Type)` z `title` + `additionalProperties:false`; reużyć bez zmiany opcji, dodać metody koperty i `ExportSchemas()`.
- `src/MediaStation.Core/Contracts/Envelope.cs` -- `Envelope<T>`, `Version = 1`, `snapshot`/`ready`; nie zmieniać kształtu.
- `src/MediaStation.Core/Contracts/ShellSnapshot.cs`, `ConfigError.cs` -- wzorzec: `sealed record` z pozycyjnymi parametrami, XML doc po polsku.
- `tools/MediaStation.SchemaExport/Program.cs` -- dziś zapisuje tylko `shell-snapshot.schema.json`; zapis tylko przy zmianie.
- `web/scripts/contracts.mjs` -- stała lista `contracts`; json2ts z `additionalProperties:false`, zapis tylko przy zmianie.
- `src/MediaStation.App/MediaStation.App.csproj` -- target `BuildWeb` uruchamia SchemaExport i `npm run contracts`/`build`; bez zmian.
- `tests/MediaStation.Core.Tests/ShellSnapshotContractTests.cs` -- styl testów (dokładny JSON w raw string, `Assert.Throws<JsonException>`).
- `src/MediaStation.Core/MediaStation.Core.csproj` -- `TreatWarningsAsErrors`, więc RS0030 z analyzera łamie build.
- NuGet: `Microsoft.CodeAnalysis.BannedApiAnalyzers` 5.6.0 (opublikowana 2026-07-02). Baseline: `dotnet test` 28/28 zielone.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Contracts/Messages.cs` -- abstrakcyjne `Intent`, `Command(long CmdId)`, `Event`; `CommandCompleted(long CmdId) : Event`, `CommandFailed(long CmdId, ErrorCode ErrorCode) : Event`, enum `ErrorCode` (wartości z Decisions) -- bazy dla epik 2 i 4, AD-4.
- [x] `src/MediaStation.Core/Contracts/Action.cs` -- `Action(ActionKind Kind, ActionPhase Phase, ActionSource Source, long TsMono)` + enumy `ActionKind`, `ActionPhase`, `ActionSource` -- AD-8.
- [x] `src/MediaStation.Core/Contracts/SetInputRouting.cs` -- `SetInputRouting(long CmdId, InputMode Mode, AppType? AppType, PointerPolicy Pointer, long Epoch) : Command` + enumy `InputMode`, `AppType` (web|moonlight|native, story 4 go reużyje), `PointerPolicy` -- AD-8.
- [x] `src/MediaStation.Core/Contracts/MessageTypes.cs` -- jawna lista typów komunikatów (`Action`, `SetInputRouting`, `CommandCompleted`, `CommandFailed`); nazwa w kopercie = camelCase nazwy rekordu -- jedno miejsce dopisywania przez kolejne epiki.
- [x] `src/MediaStation.Core/Contracts/ContractJson.cs` -- `SerializeEnvelope<TBase>(TBase)` (payload po typie runtime) i `DeserializeEnvelope<TBase>(string)` (wg macierzy I/O); `ExportSchemas()` → nazwa pliku kebab-case → schema dla `ShellSnapshot` i każdego typu z rejestru.
- [x] `src/MediaStation.Core/MediaStation.Core.csproj`, `src/MediaStation.Core/BannedSymbols.txt` -- BannedApiAnalyzers (`PrivateAssets=all`) + zakazy: P/Invoke (`DllImport`, `LibraryImport`), `Microsoft.Win32`, `System.Windows`, `Microsoft.Web.WebView2`, typy systemu plików (`File`, `Directory`, `FileInfo`, `DirectoryInfo`, `FileSystemInfo`, `FileStream`, `DriveInfo`, `FileSystemWatcher`, `Path`) i konstruktory `StreamReader`/`StreamWriter` ze ścieżką.
- [x] `tools/MediaStation.SchemaExport/Program.cs` -- zapis wszystkich `ExportSchemas()`; usuwa nieaktualne `*.schema.json` z katalogu wyjściowego.
- [x] `web/scripts/contracts.mjs` -- generuje `<nazwa>.ts` dla każdego `*.schema.json` zamiast stałej listy.
- [x] `tests/MediaStation.Core.Tests/CoreMessageContractTests.cs` -- dokładny JSON i round-trip w kopercie dla każdego rekordu, macierz I/O, schema każdego rekordu (`title`, `required`, wartości enumów), unikalne nazwy w rejestrze.
- [x] `tests/MediaStation.Core.Tests/CoreBoundaryTests.cs` -- `System.Reflection.Metadata` na assembly Core: brak `TargetPlatformAttribute`, brak referencji do `MediaStation.*`, WPF (`PresentationCore`, `PresentationFramework`, `WindowsBase`, `System.Xaml`), `Microsoft.Web.WebView2*`, `Microsoft.Win32*`, `Microsoft.Windows*`; brak `TypeReference` do zakazanych typów systemu plików i namespace'ów; brak metod `PinvokeImpl`.

**Acceptance Criteria:**
- Given czysty checkout, when `dotnet build`, then w `web/contracts/generated/` są `shell-snapshot.ts`, `action.ts`, `set-input-routing.ts`, `command-completed.ts`, `command-failed.ts` z nazwanymi typami (`Action`, `ActionKind`, `SetInputRouting`, `ErrorCode`…) i nagłówkiem „nie edytuj ręcznie”.
- Given dopisane w Core tymczasowo `System.IO.File.Exists("x")`, when `dotnet build`, then build pada na RS0030; po usunięciu przechodzi.
- Given `dotnet build -c Release`, when shell się buduje, then `npm run typecheck` przechodzi z nowymi plikami TS.

## Implementation Notes

- Nullable enum (`AppType?`) eksportowany jako `anyOf [nazwany enum, null]`, żeby json2ts dał `appType: AppType | null` zamiast anonimowej unii; story 4 reużyje nazwanego `AppType`.
- `contracts.mjs` usuwa też nieaktualne `.ts` w `generated/` (symetrycznie do SchemaExport, który usuwa nieaktualne `*.schema.json`).
- `cmdId` jest pierwsze dzięki `[property: JsonPropertyOrder(-1)]` na `Command`.
- BannedApiAnalyzers nie widzi zakazanych typów użytych tylko w sygnaturach i polach; to łapie `CoreBoundaryTests`, ale dopiero w `dotnet test`. `[LibraryImport]` w Core i tak pada wcześniej na SYSLIB1062 (brak unsafe).

## Plan Change Log

## Review Triage Log

Pass 1 (quick): high 0, medium 0, low 1, false 0, maybe-false 0; 1 odrzucony jako out of scope.

| # | Finding (quick) | Verdict | Route | Evidence |
|---|---|---|---|---|
| 1 | API systemu plików spoza list (`System.IO.Enumeration`, `RandomAccess`, `ZipFile`, `MemoryMappedFiles`, `IsolatedStorage`) kompilują się w Core bez RS0030 i przechodzą test granicy | low | patch | Reviewer zbudował kopię Core z `FileSystemEnumerable` i `ZipFile.CreateFromDirectory` bez RS0030; żaden z tych typów ani namespace'ów nie jest w `BannedSymbols.txt` ani w `ForbiddenFileSystemTypes`/`ForbiddenNamespaces`. Poprawka = dopisanie wpisów do obu list. |
| 2 | `MediaStation.Core.Contracts.Action` koliduje z `System.Action` przy `ImplicitUsings` (CS0104 w App) | low | reject | Problem jest realny (test już używa aliasu), ale nazwę `Action` narzucają zamrożony intent i Consistency Conventions architektury; zmiana nazwy wymaga renegocjacji intentu, nie poprawki kodu. |

## Design Notes

Polimorfizm żyje w kopercie: `type` wybiera rekord z `MessageTypes`, a payload to zwykły rekord bez dyskryminatora, więc ten sam kształt służy mostowi i logom. Szkic:

```csharp
var json = ContractJson.SerializeEnvelope<Command>(new SetInputRouting(7, InputMode.App, AppType.Web, PointerPolicy.Blocked, 3));
Event e = ContractJson.DeserializeEnvelope<Event>(failedJson); // CommandFailed
```

`tsMono` to milisekundy zegara monotonicznego (liczba całkowita, jak `time` z hooka LL). `appType` jest `null`, gdy nie ma aktywnej aplikacji. Bazy (`Intent`, `Command`, `Event`) nie mają własnego schematu ani typu TS — eksportujemy tylko konkretne rekordy. Jeśli kolejność właściwości dziedziczonych daje `cmdId` na końcu, ustawić `[JsonPropertyOrder]` na bazie, żeby `cmdId` było pierwsze.

## Verification

**Commands:**
- `dotnet test` -- wszystkie testy zielone (28 dotychczasowych + nowe).
- `dotnet build -c Release` -- sukces; pięć plików w `web/contracts/generated/`, typecheck web zielony.

**Manual checks (if no CLI):**
- Tymczasowe użycie `File.Exists` w Core daje RS0030 (sprawdzić i cofnąć).
