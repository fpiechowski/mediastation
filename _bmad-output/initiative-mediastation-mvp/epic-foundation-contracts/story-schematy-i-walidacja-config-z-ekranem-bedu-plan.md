---
title: 'Schematy i walidacja config/ z ekranem błędu'
type: 'feature'
ticket: '4'
created: '2026-10-20'
status: 'built'
baseline_revision: '65b99d77387ee63ea88f7d3129f21d6952fbfe45'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 1
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Start sprawdza dziś tylko, czy `apps.json`, `input.json` i `settings.json` istnieją i są poprawnym JSON (`ConfigPreflight`); nieznane pola, kolizje i złe wartości `cdpPort` przechodzą, a kolejne epiki nie mają rekordów configu, z których mogłyby czytać (AD-11).

**Approach:** Rekordy trzech plików w `MediaStation.Core/Config` jako jedyne źródło schematu (eksport JSON Schema → TS w istniejącym pipeline), czysta funkcja walidacji w Core: preflight → ścisła deserializacja (nieznane pola odrzucane) → reguły semantyczne, zwracająca załadowany config albo listę `ConfigError`; App podaje stany plików jak dziś, pokazuje ekran błędu i loguje każdy błąd.

## Boundaries & Constraints

**Always:** Core bez I/O (AD-3); nieznane pole i zduplikowany klucz JSON to błąd w każdym pliku i na każdym poziomie; `cdpPort` w 9220–9239 i unikalny; `id` aplikacji kebab-case i unikalne (AD-11, Consistency Conventions); brakujące pola `settings.json` dostają wartości domyślne; powód błędu po polsku, ścieżka JSON jak w preflight (`$.apps[1].cdpPort`); kolejność błędów apps → input → settings; każdy błąd to osobny wpis `Error("config", "configError", {file, path, reason})` w logu; istniejące testy `ConfigPreflight` zostają zielone; ekran i most bez zmian kształtu.

**Never:** zmiana `ContractJson.Options` (koperta nadal ignoruje dodatkowe `id`); sprawdzanie, czy driver `service` istnieje (epika Netflix); użycie configu przez Orchestrator/InputHub/Shell; pola `back` dla `web` (Back idzie do strony, AD-8); komentarze w JSON; edycja wygenerowanych TS.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Poprawny config | trzy poprawne pliki, `settings.json` = `{}` | config załadowany, settings z wartościami domyślnymi, ekran placeholder | — |
| Nieznane pole | `"foo": 1` w wpisie apps / urządzeniu input / settings | `{file, "$.apps[0].foo", "nieznane pole „foo”"}` | ekran błędu, log |
| Duplikat `cdpPort` | dwa wpisy web z 9222 | błąd na drugim: `$.apps[1].cdpPort`, powód wskazuje `$.apps[0]` | ekran błędu, log |
| Port poza zakresem | `cdpPort: 9300` | `$.apps[0].cdpPort`, „poza zakresem 9220–9239” | ekran błędu, log |
| Brak / zły `type` | wpis bez `type` albo `"type": "x"` | błąd ze ścieżką wpisu | bez crasha (także `NotSupportedException`) |
| `type` nie pierwszy | `{ "id": …, "type": "web", … }` | wpis odczytany | — |
| Brak pola wymaganego | wpis web bez `cdpPort` | „brak wymaganego pola „cdpPort”” | ekran błędu |
| Kilka plików złych | błąd w apps i settings | dwa wpisy, apps przed settings | — |
| Nieczytelny JSON / brak pliku | jak w preflight | bez zmian względem story 1 | — |

**Decisions (2026-10-20):**
- `settings.json` (plik wymagany, `{}` poprawne, czasy w pełnych sekundach): `ratingPendingDays` 7, `startingTimeout` 30, `cdpConnectTimeout` 15 (musi być < `startingTimeout`), `cdpReconnectTimeout` 30, `update.repo` `"fpiechowski/mediastation"` (format `owner/name`); wartości liczbowe > 0.
- `input.json`: `{ "devices": [ { "signature": "VID_1915&PID_1025", "source": "remote", "keys": { "0x26": "up", "0x0D": "ok", "0xAC": "home", … } } ], "gamepadHome": ["back", "start"] }`; klucz = VK hex `0x..` (z hooka LL, AD-8); `signature` unikalna; `appMenu` niedozwolone jako wartość mapowania (AppMenu to `ok` + `phase: long` interpretowane w Shell, AD-8); `gamepadHome` ≥ 2 różne przyciski XInput (enum nazw przycisków).
- Plan zostaje w całości mimo ~2700 tokenów: trzy pliki dzielą jeden walidator i jedną ścieżkę błędu.

</frozen-after-approval>

## Code Map

- `src/MediaStation.Core/Config/ConfigPreflight.cs` -- `ConfigFileState`, `Check`, `CheckJson`, `JsonErrorPath`; zostawić publiczne API, wyciągnąć sprawdzenie jednego pliku do reużycia przez walidator.
- `src/MediaStation.Core/Contracts/ContractJson.cs` -- `Options` (Web, strict, required ctor params, enumy camelCase bez liczb), `ExportSchemas()` (dziś `ShellSnapshot` + `MessageTypes.All`), `ExportSchema(Type)` z `title`/`additionalProperties:false`; dopisać typy configu do eksportu.
- `src/MediaStation.Core/Contracts/SetInputRouting.cs` -- enumy `AppType`, `PointerPolicy` do reużycia w `apps.json`.
- `src/MediaStation.Core/Contracts/Action.cs` -- `ActionKind`, `ActionSource` do reużycia w `input.json`.
- `src/MediaStation.App/CompositionRoot.cs` -- `ConfigFileReader.ReadAll` + `ConfigPreflight.Check` → zamienić na walidator, zalogować błędy, zachować `LoadedConfig` w statycznej właściwości.
- `src/MediaStation.App/Config/ConfigFileReader.cs` -- bez zmian.
- `tests/MediaStation.Core.Tests/CoreMessageContractTests.cs:166` -- test dokładnej listy schematów; dopisać nowe pliki.
- `tests/MediaStation.Core.Tests/ConfigPreflightTests.cs` -- styl testów configu (słownik `ConfigFileState.Present`).
- `web/shell/src/ConfigErrorScreen.tsx`, `web/scripts/contracts.mjs`, `tools/MediaStation.SchemaExport` -- bez zmian (generują się automatycznie).
- Fakty STJ (.NET 10): `UnmappedMemberHandling.Disallow`, `AllowDuplicateProperties = false`, `AllowOutOfOrderMetadataProperties = true` dla `type` nie na początku; abstrakcyjna baza bez dyskryminatora rzuca `NotSupportedException`.
- Pobłażliwość dziedziczona po `ContractJson.Options` (sprawdzone probe'em): `JsonSerializerDefaults.Web` daje `PropertyNameCaseInsensitive = true` (`"CdpPort"`, `"StartingTimeout"` przechodzą); `JsonStringEnumConverter` czyta bez względu na wielkość liter i przyjmuje listy po przecinku sumowane bitowo także bez `[Flags]` (`"audio, game"` → `(AppCategory)3`, `"up, down"` → `Down`); członek z `[JsonIgnore]` liczy się dla `UnmappedMemberHandling.Disallow` jako znany, więc pole o jego nazwie przechodzi po cichu z dowolną wartością. Konwerter rzucający `new JsonException()` bez komunikatu dostaje od STJ „The JSON value could not be converted to … Path: …”.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Config/AppsConfig.cs` -- `AppsConfig(IReadOnlyList<AppEntry> Apps)`; abstrakcyjny `AppEntry(Id, Category, Title, Icon)` z `[JsonPolymorphic("type")]`: `WebApp(+Service, Browser, CdpPort, Pointer = Blocked)`, `MoonlightApp(+Host, App, Back = "key:Escape", Pointer = Allowed)`, `NativeApp(+Exe, Args = [], Back = "home", Pointer = Allowed)`; enumy `AppCategory`, `BrowserKind`; żadnych członków `[JsonIgnore]` ani innych właściwości poza polami pliku (patrz Code Map) -- AD-11.
- [x] `src/MediaStation.Core/Config/InputConfig.cs` -- `InputConfig(Devices, GamepadHome)`, `InputDevice(Signature, ActionSource Source, IReadOnlyDictionary<string, ActionKind> Keys)`, enum `GamepadButton` (przyciski XInput) -- kształt z Decisions.
- [x] `src/MediaStation.Core/Config/Settings.cs` -- `Settings` z init-właściwościami i wartościami domyślnymi z Decisions, zagnieżdżone `UpdateSettings { Repo }` -- AD-11, AD-13, AD-17.
- [x] `src/MediaStation.Core/Config/ConfigValidation.cs` -- `ConfigJson.Options` (kopia `ContractJson.Options` + trzy flagi STJ z Code Map + `PropertyNameCaseInsensitive = false` + wewnętrzny ścisły konwerter enumów wstawiony przed odziedziczonym: tylko dokładna nazwa camelCase z `JsonNamingPolicy.CamelCase`, inaczej `new JsonException()`; runtime przyjmuje dokładnie to, co eksportowany schemat); `Validate(files) → ConfigResult(LoadedConfig? Config, IReadOnlyList<ConfigError> Errors)`; per plik: preflight → deserializacja (pierwszy błąd struktury → `ConfigError` z `ex.Path` i polskim powodem) → reguły semantyczne (wszystkie błędy): id kebab-case/unikalne, `cdpPort` zakres/unikalność, `back` = `home` | `key:<nazwa>`, reguły input i settings z Decisions.
- [x] `src/MediaStation.Core/Contracts/ContractJson.cs` -- `ExportSchemas()` obejmuje `AppsConfig`, `InputConfig`, `Settings`.
- [x] `src/MediaStation.App/CompositionRoot.cs` -- walidacja zamiast preflight, wpis w logu na każdy błąd, `Config` dostępny dla kolejnych epik.
- [x] `tests/MediaStation.Core.Tests/ConfigValidationTests.cs` -- macierz I/O, nieznane pole w każdym z trzech plików, wartości domyślne settings, schematy (polimorfizm `type`, `additionalProperties:false`); pole w innej wielkości liter (`"CdpPort"`, `"Apps"`, `"StartingTimeout"`) → „nieznane pole”; enum w innej wielkości liter (`"VIDEO"`, `"Up"`, `"START"`) i lista po przecinku (`"audio, game"`, `"start, back"`) → „zła wartość albo typ”.
- [x] `tests/MediaStation.Core.Tests/CoreMessageContractTests.cs` -- lista schematów z nowymi plikami.

**Acceptance Criteria:**
- Given czysty checkout, when `dotnet build -c Release`, then `web/contracts/generated/` ma `apps-config.ts`, `input-config.ts`, `settings.ts` i typecheck web przechodzi.
- Given w `%LOCALAPPDATA%\MediaStation\config` nieznane pole albo zły `cdpPort`, when uruchamiam `MediaStation.exe`, then ekran „Coś nie gra w konfiguracji” z nazwą pliku, ścieżką i powodem, proces żyje, a log dnia ma linię `configError` z tym samym plikiem.

## Implementation Notes

- **Odstępstwo od KEEP (`null` przez `RespectNullableAnnotations`):** STJ (.NET 10) nie egzekwuje nullability dla init-właściwości rekordów z konstruktorem parametrycznym — `"back": null` (moonlight/native) i `"args": null` przechodziły deserializację i wywalały reguły semantyczne (NRE/`ArgumentNullException`, wykryte testem `Null_field_is_rejected`). Na `Settings` (bez ctor parametrycznego) STJ egzekwuje poprawnie. Poprawka: reguła semantyczna w `CheckApps`/`CheckBack` daje ten sam powód „pole nie może być null” pod tą samą ścieżką.
- Ścieżka błędu brakującego/nieznanego `type` to ścieżka wpisu (`$.apps[1]`), zgodnie z macierzą; STJ podaje ją tak samo dla obu przypadków.
- Konwersja enumów przez ctor param daje w komunikacie STJ typ rekordu, nie enuma (`could not be converted to …WebApp`), więc powód „zła wartość albo typ” nie podaje oczekiwanego typu.
- `ConfigJson` żyje w `ConfigValidation.cs` (obok walidatora); schematy eksportuje nadal `ContractJson.Options` — ścisły konwerter nie ma opisu schematu, a nazwy i wartości są identyczne. Wygenerowane schematy zgodne bajt w bajt z rundą 1.
- Weryfikacja: `dotnet test` 202/202; `dotnet build -c Release` 0 ostrzeżeń, trzy nowe pliki TS, typecheck web zielony; lokalnie Release exe: nieznane pole + `startingTimeout: 0` → ekran błędu z dwoma wpisami i dwie linie `configError` (apps przed settings), `cdpPort: 9300` → ekran + log, poprawny config (z `type` na końcu) → placeholder bez `configError`; proces żyje we wszystkich przypadkach. Tymczasowy `config/` usunięty po sprawdzeniu. UM760 (HITL) nie sprawdzone.

## Plan Change Log

- **2026-10-20, review loop 1** — Trigger: quick review (potwierdzone probe'em) — nazwy pól i wartości enumów czytane bez względu na wielkość liter, listy enumów po przecinku dają wartości spoza enuma w `LoadedConfig`, a `[JsonIgnore]` `AppEntry.Type`/`AppEntry.PointerPolicy` po cichu połykały pola `type`/`pointerPolicy` w innej wielkości liter. Zmiana: Code Map dostał fakty o pobłażliwości dziedziczonej po `ContractJson.Options`; zadania `AppsConfig.cs`, `ConfigValidation.cs` i testy rozszerzone o ścisłe nazwy, ścisły konwerter enumów i zakaz członków spoza pól pliku. Unikany stan: runtime przyjmujący pliki, które eksportowany schemat odrzuca (dwa schematy jednego pliku, AD-11). **KEEP:**
  - opcjonalne pola z domyślną wartością (`pointer`, `back`, `args`, wszystkie pola `settings.json`) jako init-właściwości, nie parametry ctor — schemat nie oznacza ich jako wymagane, `null` odrzucany przez `RespectNullableAnnotations`;
  - `ContractJson.ExportSchema`: typ pochodny polimorficzny dostaje dyskryminator w `required` (TS: rozłączna unia `type: "web"`);
  - `ConfigPreflight.CheckFile` (internal) wyciągnięte z `Check`, `JsonErrorPath.Property`/`Index` jako wspólny format ścieżek; ścieżki STJ `$.devices[0].keys.0x26` normalizowane do `$.devices[0].keys['0x26']`;
  - klasyfikacja komunikatów STJ: nieznane pole, zduplikowany klucz, brak wymaganego pola/pól (lista), brak `type` (`NotSupportedException`, ścieżka z treści komunikatu), nieznany `type` z listą dozwolonych, `null`, „zła wartość albo typ”, fallback „niezgodne ze schematem”; dokument `null` → „oczekiwano obiektu, a nie null”;
  - dodatkowe reguły: wpis `null` w tablicy, pusta sygnatura, sygnatury unikalne bez względu na wielkość liter, ten sam VK w innej wielkości liter (`0x0d`/`0x0D`) jako duplikat, VK 0x01–0xFE, `null` w `args`, `cdpConnectTimeout < startingTimeout` sprawdzane tylko gdy oba > 0;
  - `CompositionRoot.Config` (static, `LoadedConfig?`), log `Error("config", "configError", {file, path, reason})` na każdy błąd przed zbudowaniem snapshotu;
  - zestaw testów z poprzedniej rundy (146 zielonych, w tym macierz I/O, duplikaty kluczy w każdym pliku, `Preflight_errors_are_unchanged`, `Comments_are_not_allowed`, schematy), z testami `Type`/`PointerPolicy` przepisanymi na pola `Pointer` rekordów.

## Review Triage Log

| # | Lens | Lokalizacja | Verdict | Route | Evidence |
|---|------|-------------|---------|-------|----------|
| 1 | quick | `ConfigValidation.cs` `ConfigJson.CreateOptions` | medium | bad_plan (grupa A) | Probe: `"CdpPort": 9222` i `{ "StartingTimeout": 40 }` → config OK, zero błędów; `Web` defaults dają `PropertyNameCaseInsensitive = true`, schemat odrzuca te pliki. |
| 2 | quick | `AppsConfig.cs` `[JsonIgnore] Type`/`PointerPolicy` | medium | bad_plan (odstępstwo od planu) | Probe: `"pointerPolicy": {"x":1}` na wpisie web → config OK; członkowie spoza planu, użytkownik nie dostaje błędu za mylące pole. |
| 3 | quick | enumy configu (`JsonStringEnumConverter` z `ContractJson.Options`) | medium | bad_plan (grupa A) | Probe: `"category": "audio, game"` → `(AppCategory)3` w `LoadedConfig`, `"VIDEO"`/`"Edge"` przechodzą; wartość spoza enuma wywali późniejszą serializację (`allowIntegerValues: false`). Poprawka wymaga nowego konwertera, nie jest trywialna. |
| 4 | quick | AC2 (ekran błędu) | false | reject | Ścieżka `result.Errors` → `ShellSnapshot.ConfigErrors` → ekran identyczna jak w story 1; plan z założenia oddaje obejrzenie ekranu HITL na UM760, więc to status weryfikacji, nie defekt. |
| 5 | quick (loop 1) | `ConfigValidation.cs` `CheckInput` (`VirtualKey` + `Convert.ToInt32`) | medium | patch (grupa B) | `$` w .NET dopasowuje się przed końcowym `\n`: klucz `"0x26\n"` przechodzi regex, `Convert.ToInt32("26\n", 16)` rzuca `FormatException` — crash startu zamiast ekranu błędu (probe recenzenta). Poprawka: kotwica `\z`. |
| 6 | quick (loop 1) | `ConfigValidation.cs` regexy `KebabCase`, `BackPolicy`, `GitHubRepo` | medium | patch (grupa B) | Ta sama przyczyna: `"netflix\n"`, `"home\n"`, `"a/b\n"` ładują się bez błędu; `"netflix"` i `"netflix\n"` omijają unikalność id. |
| 7 | quick (loop 1) | `ConfigValidation.cs` `CheckInput` unikalność sygnatur | low | reject | `"a"` i `"a "` przechodzą jako różne sygnatury; przypadkowa spacja wewnątrz cudzysłowu w ręcznie pisanym JSON mało prawdopodobna, a poprawka dokłada nową gałąź. |

## Design Notes

Polski powód zamiast komunikatu STJ: klasyfikacja wyjątku (nieznane pole, brak wymaganego pola, zła wartość/typ, brak `type`) z fallbackiem `niezgodne ze schematem`; testy przypinają każdą klasę, więc zmiana tekstów STJ wyjdzie w `dotnet test`. Struktura daje jeden błąd na plik (STJ przerywa), semantyka — wszystkie. Domyślne `back`/`pointer` siedzą w rekordach, więc konsumenci nie powtarzają polityk AD-11.

## Verification

**Commands:**
- `dotnet test` -- wszystkie zielone (38 dotychczasowych + nowe).
- `dotnet build -c Release` -- 0 ostrzeżeń; trzy nowe pliki TS.

**Manual checks (if no CLI):**
- Lokalnie Release exe z configiem z nieznanym polem → ekran błędu + linia `configError` w logu; poprawny config → placeholder.
- UM760 (HITL, autor): każdy z trzech błędów z `verify` daje ekran błędu z nazwą pliku i wpis w logu.
