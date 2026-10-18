---
title: 'Tracer bullet: MediaStation.exe → Core → WebView2 shell'
type: 'feature'
ticket: '1'
created: '2026-10-18'
status: 'in-review'
baseline_revision: '7292f5d32e18657310b7e870c2987e9056c5b1d7'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md'
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Repo ma tylko planning i `spike/`; brakuje szkieletu, na którym kolejne stories zbudują Core, kontrakty i shell, oraz dowodu, że `MediaStation.exe` z `dotnet publish` działa na UM760.

**Approach:** Solution wg Structural Seed, jeden rekord snapshotu w Core eksportowany do JSON Schema i generowany do TS w buildzie web, pusty host WPF + WebView2, który przez most `postMessage` (`{ v, type, payload }`) pokazuje placeholder albo ekran błędu konfiguracji z listą `{ file, path, reason }`.

## Boundaries & Constraints

**Always:** `MediaStation.Core` = `net10.0` bez referencji do Win32/WebView2/WPF/`System.IO` (AD-3); serializacja camelCase, enumy jako stringi, jawne `null` (AD-16); wygenerowane typy TS i schema nie są w gicie ani edytowane ręcznie; Debug ładuje `http://localhost:5173`, Release pliki z `<exe>/shell/` (AD-13); config czytany z `%LOCALAPPDATA%\MediaStation\config` (`apps.json`, `input.json`, `settings.json`); wersje z tabeli Stack; wygląd z tokenów DESIGN.md (`config-error`, `logo`, `body`, `card-title`, `ground`), copy tytułu „Coś nie gra w konfiguracji”, kursor ukryty.

**Never:** kod skopiowany lub przeniesiony ze `spike/`; walidacja schematów configu, logger, mutex, parser CLI, `install.ps1` (stories 2–5, epika Deploy); Orchestrator; ekran główny; WindowManager (okno shella po prostu startuje pełnoekranowo).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Happy path | trzy pliki istnieją i są poprawnym JSON | snapshot `screen: placeholder`, `configErrors: []` → ekran z logo i wordmarkiem | — |
| Brak pliku | np. brak `settings.json` | `screen: configError`, wpis `{ file: "settings.json", path: null, reason: "brak pliku" }` | bez crasha |
| Nieczytelny JSON | `settings.json` = `{ "a": ` | wpis z `path` z `JsonException` (np. `$.a`) i powodem z numerem linii | bez crasha |
| Kilka błędów | brak `apps.json` + zepsuty `input.json` | dwa wpisy, kolejność apps, input, settings | — |
| Błąd odczytu | plik zablokowany / brak uprawnień | wpis z powodem „nie można odczytać pliku” | wyjątek I/O łapany w App |

**Decisions (2026-10-18):** `global.json` przypina 10.0.402 zgodnie ze Stack (autor zainstalował SDK); Node 24.22.0 zgodnie ze Stack (zainstalowany); eksport schematu w osobnej konsoli `tools/MediaStation.SchemaExport` z referencją do Core, nie jako tryb CLI App.

</frozen-after-approval>

## Code Map

Greenfield: poza `_bmad*` i `spike/` repo jest puste; brak `.gitignore` w root.

- `spike/` -- tylko referencja zachowania; nie budowany, nie referowany.
- `ux-mediastation/mockups/key-home-video.html:147` -- `<symbol id="logo">` (SVG placeholder logo) do odtworzenia w shellu.
- Toolchain na maszynie dev: SDK 10.0.402 (obok 10.0.301, 8.0.412), Node 24.22.0 / npm 11.2.0.
- Paczki potwierdzone w registry: xunit.v3 4.0.2, Microsoft.Web.WebView2 1.0.4258.31, react/react-dom 19.3.1, vite 8.3.4, typescript 6.0.3, json-schema-to-typescript 16.0.0, @vitejs/plugin-react 6.1.2, @fontsource-variable/roboto-flex 5.3.0 (font lokalnie, UM760 może być offline).

## Tasks & Acceptance

**Execution:**
- [x] `.gitignore`, `global.json`, `MediaStation.slnx` -- gitignore (`bin/ obj/ node_modules/ dist/ web/contracts/generated/ web/contracts/schema/`), SDK pin z `rollForward: latestFeature`, solution z czterema projektami.
- [x] `src/MediaStation.Core/MediaStation.Core.csproj`, `Contracts/ShellSnapshot.cs`, `Contracts/ConfigError.cs`, `Contracts/Envelope.cs`, `Contracts/ContractJson.cs`, `Config/ConfigPreflight.cs` -- rekord `ShellSnapshot { rev, screen: placeholder|configError, configErrors }`, `ConfigError { file, path, reason }`, koperta `{ v: 1, type, payload }`, wspólne `JsonSerializerOptions` + `ExportSchema()`; `ConfigPreflight` = czysta funkcja: (nazwa pliku → treść | brak | błąd odczytu) → lista `ConfigError` (parsuje `JsonDocument`).
- [x] `tools/MediaStation.SchemaExport/` -- konsola `net10.0` referująca Core; zapisuje `web/contracts/schema/shell-snapshot.schema.json`.
- [x] `web/package.json`, `web/tsconfig.json`, `web/shell/` (vite.config.ts, index.html, src/main.tsx, App.tsx, Placeholder.tsx, ConfigErrorScreen.tsx, bridge.ts, tokens.css) -- skrypt `contracts` (json2ts → `web/contracts/generated/shell-snapshot.ts`), `build` = `contracts` + `vite build` do `web/shell/dist`, `dev` = Vite na 5173; `bridge.ts` wysyła `ready`, przyjmuje `snapshot`, odrzuca starszy `rev`.
- [x] `src/MediaStation.App/MediaStation.App.csproj` -- `net10.0-windows`, `UseWPF`, WebView2, ref do Core; target `BuildWeb` przed Build: SchemaExport → `npm ci` (gdy brak `node_modules`) → `npm run contracts` (Debug) albo `npm run build` (Release); `web/shell/dist/**` jako Content do `shell/` w output i publish.
- [x] `src/MediaStation.App/App.xaml(.cs)`, `CompositionRoot.cs`, `Config/ConfigFileReader.cs`, `Shell/ShellWindow.xaml(.cs)`, `Shell/ShellBridge.cs` -- composition root: czyta trzy pliki (I/O tylko tu), `ConfigPreflight` → snapshot `rev: 1`; okno bez ramki, maksymalizowane, tło `ground`; WebView2 z user data w `%LOCALAPPDATA%\MediaStation\webview2`; Release: `SetVirtualHostNameToFolderMapping` na `<exe>/shell`; na `ready` host odsyła snapshot `PostWebMessageAsJson`.
- [x] `tests/MediaStation.Core.Tests/` -- xunit.v3: macierz I/O dla `ConfigPreflight`, round-trip `ShellSnapshot` (camelCase, enumy jako string, jawne `null`), schema zawiera `rev`, `screen`, `configErrors`.

**Acceptance Criteria:**
- Given czysty checkout, when `dotnet build -c Release`, then powstaje `web/contracts/generated/shell-snapshot.ts` i `shell/index.html` w output App, bez ręcznych kroków npm.
- Given `MediaStation.Core.csproj`, when przeglądam referencje, then jest tylko `net10.0` bez `UseWPF`, WebView2 ani `Microsoft.Windows.*`.
- Given Debug i działający `npm run dev`, when uruchamiam App, then shell ładuje się z Vite dev servera i HMR działa.
- Given publish self-contained win-x64 skopiowany na UM760 i poprawne trzy pliki config, when uruchamiam `MediaStation.exe`, then pełnoekranowy placeholder z logo z plików lokalnych, bez kursora.
- Given zepsuty `settings.json`, when uruchamiam `MediaStation.exe`, then ekran „Coś nie gra w konfiguracji” z plikiem, ścieżką i powodem, proces żyje.

## Implementation Notes

- `global.json` ma też `"test": { "runner": "Microsoft.Testing.Platform" }` — xunit.v3 4.0.2 zależy od `xunit.v3.mtp-v2`, a `dotnet test` w SDK 10 bez tego nie uruchamia projektów MTP.
- `JsonDocument.Parse` nie podaje `Path` w `JsonException`, więc `ConfigPreflight` liczy ścieżkę (`$.a`, `$.apps[1].id`) jednym przebiegiem `Utf8JsonReader` (`JsonErrorPath`, internal w Core). Przy przecinku na końcu tablicy ścieżka wskazuje ostatni odczytany element.
- `ContractJson`: `NumberHandling = Strict` (inaczej schema daje `rev: string | number`), `RespectNullableAnnotations` + `RespectRequiredConstructorParameters` (pola `required`), `title` na rekordach i enumach oraz `additionalProperties: false` w schemacie, żeby json2ts wygenerował nazwane typy `ShellSnapshot`, `ShellScreen`, `ConfigError` bez index signature.
- Typy TS generuje `web/scripts/contracts.mjs` (API json-schema-to-typescript), a nie CLI — tworzy katalog i zapisuje plik tylko przy zmianie.
- `BuildWeb` buduje SchemaExport przez task `MSBuild` (bez RID/SelfContained z publish) i uruchamia jego dll; pomijany w design-time buildach i przy `-p:SkipWebBuild=true`. Debug (`SHELL_DEV_SERVER`) robi tylko `npm run contracts`, a inne konfiguracje `npm run build` + Content `shell\**`.
- Shell renderuje stałą powierzchnię 1920×1080 skalowaną do okna (`.stage`, `main.tsx`), bo przy skalowaniu Windows ≠ 100% viewport CSS ≠ 1920×1080 (DESIGN.md → Layout & Spacing).
- Release: wirtualny host `https://shell.mediastation.example/` → `<exe>/shell`; DevTools, menu kontekstowe, zoom i akceleratory przeglądarki wyłączone.
- Ekran błędu ma dodatkową linię podpowiedzi „Popraw pliki w %LOCALAPPDATA%\MediaStation\config i uruchom MediaStation ponownie.”; powód błędu odczytu dokleja komunikat wyjątku po „nie można odczytać pliku: ”.

## Plan Change Log

## Review Triage Log

| # | Finding (quick) | Verdict | Route | Evidence |
|---|---|---|---|---|
| 1 | `web/package-lock.json` brak w diffie, `npm ci` padnie na czystym checkoucie | false | reject | Plik jest w drzewie, nie jest ignorowany (`git check-ignore` exit 1); pominięty tylko w pliku diff do review. Commit musi go zawierać. |
| 2 | Brak `bin/Release/net10.0-windows/shell/index.html` po weryfikacji | false | reject | Ponowny `dotnet build -c Release` po poprawkach tworzy plik i `web/contracts/generated/shell-snapshot.ts`; wcześniej folder był tylko nieobecny na dysku. |
| 3 | `UnmappedMemberHandling.Disallow` we wspólnych opcjach odrzuca kopertę z `id` | medium | patch | Architektura: koperta `{ v, type, id?, payload }` (l.151, l.361), AD-7 pola addytywne; `ShellBridge` łapał `JsonException` i cicho gubił wiadomość. Usunięte, test `Envelope_with_additional_id_field_is_accepted`. |
| 4 | „znak” w powodzie to offset bajtowy UTF-8 | low | patch | `BytePositionInLine` liczy bajty; polskie znaki w configu przesuwały kolumnę. `CharColumn` + test `Column_counts_characters_not_utf8_bytes`. |
| 5 | Ścieżka błędu wskazuje poprzednią, poprawną właściwość | low | patch | `Frame.Property` nie był czyszczony po wartości; `{"a":1,}` dawało `$.a`. Teraz `$`; `{ "a": ` → `$.a` bez zmian; nowe przypadki testowe. |
| 6 | `ShellBridge` nie sprawdza `e.Source`, brak blokady nawigacji | low | reject | Dziś jedyny efekt wiadomości to ponowne wysłanie snapshotu, a shell nie ma linków; guard należy do story wprowadzającej Intents. |
| 7 | Wyjątki po init WebView2 (np. brak `<exe>/shell`) poza try/catch w `async void` | medium | patch | `SetVirtualHostNameToFolderMapping` rzuca przy braku folderu (realne przy ręcznej kopii na UM760); proces padał zamiast MessageBox. Przeniesione do istniejącego try. |
| 8 | Nieznany `screen` daje pusty ekran bez ostrzeżenia | false | reject | Host i UI używają typów z jednego schematu (enum `placeholder|configError`); innej wartości nie da się wysłać z hosta. |
| 9 | `npm ci` pomijany przy nieaktualnym `node_modules` | low | reject | Warunek wynika z planu; zmiana zależności jest rzadka i naprawia ją `npm ci`; poprawka dodaje logikę porównania lockfile. |

## Design Notes

Kontrakt koperty mostu (oba kierunki): `{ "v": 1, "type": "snapshot", "payload": { "rev": 1, "screen": "configError", "configErrors": [{ "file": "settings.json", "path": "$.a", "reason": "…" }] } }`; UI → host: `{ "v": 1, "type": "ready", "payload": null }`. Handshake `ready` usuwa wyścig między nawigacją a pierwszym snapshotem. `ConfigPreflight` zostaje w Core jako czysta funkcja, żeby story 4 podmieniło ją na pełną walidację schematów bez zmiany ekranu ani mostu.

## Verification

**Commands:**
- `dotnet test` -- wszystkie testy Core zielone.
- `dotnet build -c Release` -- sukces; istnieją `web/contracts/generated/shell-snapshot.ts` i `src/MediaStation.App/bin/Release/net10.0-windows/shell/index.html`.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained` -- folder publish z `MediaStation.exe` i `shell/`.

**Manual checks (if no CLI):**
- UM760 (HITL, wykonuje autor): placeholder przy poprawnym configu; ekran błędu po zepsuciu `settings.json`.
