---
title: 'Kafelek wg DESIGN.md: ikona, tytuł, chip i linia stanu'
type: 'feature'
ticket: '5'
created: '2026-10-26'
status: done
baseline_revision: '7a8ec05b11c733358b3ab9220f13f8358c6c3fac'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: [quick]
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Kafelek pokazuje tylko tytuł i surową etykietę stanu: snapshot nie niesie `icon` ani `type`, host nie podaje ikon, `apps.json` przepuszcza puste `title`/`icon`, a wygląd odbiega od `DESIGN.md` (chip, linia stanu, ikony Material Symbols, fokus 150 ms).

**Approach:** Core wystawia `icon` (`null` przy braku pliku) i `type` w `AppSnapshot`; App sprawdza pliki ikon przy ładowaniu configu (`warn appIconMissing`) i mapuje `icons.mediastation.example` → `config/icons/`; Shell liczy chip i linię stanu czystą funkcją (Vitest) i rysuje kafelek wg `DESIGN.md`.

## Boundaries & Constraints

**Always:** Core nie dotyka dysku (AD-3); `icon` w pliku to sama nazwa pliku (bez separatorów ścieżki i `..`); mapowanie ikon `DenyCors` w Debug i Release; URL ikony `https://icons.mediastation.example/<encodeURIComponent(icon)>`; fonty (Roboto Flex, Material Symbols Rounded) bundlowane lokalnie; typy TS tylko z `web/contracts/generated`; logika stanu kafelka bez DOM; tekst ≥ 24px; komentarze i nazwy po polsku; `TreatWarningsAsErrors`.

**Never:** Kropka na zakładce (4.6), AppMenu i long-press (4.8), nagłówek/title-safe/scroll wiersza (4.9), tytuł treści w linii `Background` (epic-netflix-dpad), zmiany mostu poza guardem `apps[]`, ręczna edycja `web/contracts/generated`, nowy origin dla mostu, ikony z origin shella.

## I/O & Edge-Case Matrix

| Scenariusz | Wejście / stan | Oczekiwane | Błąd |
|---|---|---|---|
| Walidacja `title`/`icon` | `""`, `"   "` | błąd `$.apps[i].title` / `.icon` | `configError` |
| Ikona ze ścieżką | `a/b.png`, `a\b.png`, `..`, `..\x.png` | błąd `$.apps[i].icon` | `configError` |
| Brak pliku ikony | wpis OK, brak pliku w `config/icons/` | snapshot `icon: null`, log `warn appIconMissing{appId, icon}` | start trwa |
| Plik ikony jest | `netflix.png` w `config/icons/` | snapshot `icon: "netflix.png"` | — |
| `type` w snapshocie | web / moonlight / native | `web` / `moonlight` / `native` | — |
| Chip i linia | `starting` | `progress_activity` „Uruchamiam…”, „Chwilka…” | — |
| | `background` + `web` | `pause` „Wstrzymane”, kropka + „W tle · wstrzymane” | — |
| | `background` + `moonlight`/`native` | `volume_off` „Wyciszony”, kropka + „W tle · wyciszony” | — |
| | `stopping` | `close` „Zamykam…”, brak linii | — |
| | `failed` | `error` „Nie odpowiada”, „Coś się zacięło. Wciśnij OK” | — |
| | `notRunning`, `foreground` | brak chipa i linii | — |
| Ikona w UI | `icon: null` albo błąd `<img>` | sam tytuł (znak kafelka), bez `<img>` | — |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Contracts/ShellSnapshot.cs` -- `AppSnapshot(AppId, Category, State, Title)`; dochodzą `string? Icon`, `AppType Type` (enum w `SetInputRouting.cs`).
- `src/MediaStation.Core/Config/AppsConfig.cs` -- rekordy tylko z polami pliku; nic tu nie dokładać jako właściwości (STJ). `ConfigValidation.cs` `CheckApps` -- tu reguły `title`/`icon`; `LoadedConfig` (record w `ConfigValidation.cs`) dostaje `init` `MissingIcons` (zbiór `appId`, domyślnie pusty) i `IconOf(entry)`.
- `Config/ShellSnapshotFactory.cs`, `Orchestrator/Orchestrator.cs` (`Snapshot()`, `AppState`, `Describe`) -- oba budują `AppSnapshot`; wspólny `AppTypeOf(entry)` (dziś prywatnie w `Describe` i `CompositionRoot.AppTypeOf`).
- `src/MediaStation.App/CompositionRoot.cs` -- `LoadConfig(log)` (wspólne z kalibracją), `StartRun` (`new ShellWindow(bridge, WebView2UserDataDir, log)`), `ConfigDir`; dodać `IconsDir`.
- `src/MediaStation.App/Shell/ShellWindow.xaml.cs` -- mapowanie shella pod `#if !SHELL_DEV_SERVER`; ikony mapować poza `#if`. `ShellOrigin.cs` -- `Host` tylko w Release; dodać stały `IconsHost` poza `#if`.
- `tests/MediaStation.Core.Tests/` -- `ConfigValidationTests.cs` (helpery `Netflix`, `AppsJson`), `ShellSnapshotContractTests.cs`, `ShellSnapshotFactoryTests.cs`, `OrchestratorTestSupport.cs`, `.csproj` (linki App), `SchemaExportTests.cs`.
- `web/shell/src/` -- `bridge.ts` (`isAppSnapshot`), `Placeholder.tsx` (lista kafelków, `STATE_LABELS`), `tokens.css` (tile 320×200 → 544×306), `main.tsx` (import fontu), `focus.test.ts`/`bridge.test.ts` (fabryki `AppSnapshot`).
- `web/package.json` -- dodać `@fontsource/material-symbols-rounded`; `vitest.config.ts` -- `include` tylko `*.test.ts`.
- Nie ruszać: `ShellBridge`, `ShellOutbox`, `ShellMessageGate`, `ShellNavigationPolicy`, CSP (już ma `img-src https://icons.mediastation.example`), `focus.ts`.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Contracts/ShellSnapshot.cs`, `Config/AppsConfig.cs` (statyczne `AppTypeOf`), `Config/ConfigValidation.cs` (`LoadedConfig.MissingIcons`/`IconOf`), `Config/ShellSnapshotFactory.cs`, `Orchestrator/Orchestrator.cs` -- `AppSnapshot` z `Icon` i `Type`; oba źródła snapshotu biorą je z tych samych wpisów -- kontrakt widoku (AD-16)
- [x] `src/MediaStation.Core/Config/ConfigValidation.cs` -- w `CheckApps` dla każdego wpisu: `title` i `icon` niepuste i nie z samych spacji; `icon` bez `Path.GetInvalidFileNameChars()` (w tym `/`, `\`) i bez `..` -- F18, AD-11
- [x] `src/MediaStation.App/Config/AppIcons.cs` (nowy, bez WPF), `CompositionRoot.cs` -- `FindMissing(apps, iconsDir, exists)`; `LoadConfig` (tylko w trybie głównym) loguje `warn appIconMissing{appId, icon}` i ustawia `MissingIcons` w `result.Config` i `CompositionRoot.Config`; `IconsDir`; wspólne `AppTypeOf` -- AD-11
- [x] `src/MediaStation.App/Shell/ShellOrigin.cs`, `ShellWindow.xaml.cs` -- `IconsHost`; `iconsDir` w konstruktorze (`Directory.CreateDirectory`), drugie `SetVirtualHostNameToFolderMapping(IconsHost, iconsDir, DenyCors)` w Debug i Release -- AD-11
- [x] `tests/MediaStation.Core.Tests/*` -- testy: puste/spacje `title` i `icon`, `icon` z `/`, `\`, `..`; `AppIconsTests` (link w `.csproj`); snapshot z `Icon`/`Type` w fabryce i w Orchestratorze, `icon: null` przy `MissingIcons`; kontrakt JSON (`icon` nullable, `type`); liczba linków w architekturze (22 → 23)
- [x] `web/package.json`, `main.tsx`, `tokens.css` -- `@fontsource/material-symbols-rounded` (lokalnie, bez CDN); klasa `.material-symbols`
- [x] `web/shell/src/tileView.ts` (nowy, bez DOM) + `tileView.test.ts` -- `tileStatus(app)` (chip + linia + kropka, tabela stanów), `iconUrl(icon)`; testy każdego stanu i typu na snapshotach testowych
- [x] `web/shell/src/Tile.tsx` (nowy), `Placeholder.tsx`, `tokens.css`, `bridge.ts`, `bridge.test.ts`, `focus.test.ts`, `vitest.config.ts` -- kafelek 544×306 z ikoną albo znakiem-tytułem (fallback także po `onError`), chip, meta (nazwa + linia), fokus z przejściem 150 ms; guard `icon`/`type`; test fallbacku przez `renderToStaticMarkup`
- [x] weryfikacja na bieżącej maszynie (bez zmian w repo) -- patrz Verification

**Acceptance Criteria:**
- Given `dotnet test MediaStation.slnx`, when uruchomione, then przechodzą wszystkie testy (walidacja pustych/spacji `title` i `icon`, `icon` z separatorem i `..`, `icon: null` przy braku pliku) bez nowych ostrzeżeń.
- Given `npm test` i `npm run build` w `web/`, when uruchomione, then przechodzą testy chipa i linii dla każdego stanu oraz fallbacku bez ikony, a build bez błędów.
- Given aplikacja uruchomiona na bieżącej maszynie z plikiem w `config/icons/` dla jednej aplikacji, when pokazuje się ekran główny, then ten kafelek pokazuje ikonę, pozostałe sam tytuł, a log ma `warn appIconMissing` dla brakujących.
- Given kafelek w `failed`, when ekran główny jest widoczny, then kafelek pokazuje „Nie odpowiada” i „Coś się zacięło. Wciśnij OK”.

## Implementation Notes

- Maszyna: **UM760** (COMPUTERNAME `MEDIASTATION`, płyta `F7BSI`, model `EliteMini Series`; ustalone zgodnie z AGENTS.md).
- Decyzja: `icon: null` dla Core niesie `LoadedConfig.MissingIcons` (zbiór `appId`) zamiast nullable `AppEntry.Icon`, żeby schemat `apps-config` i rekordy „tylko pola pliku” zostały bez zmian.

- Odchylenie od planu: `Path` jest na liście zakazanych symboli Core (AD-3), więc walidacja `icon` używa jawnej listy znaków niedozwolonych w nazwie pliku Windows (`/ \ : * ? " < > |` i znaki sterujące 0–31) oraz `..` zamiast `Path.GetInvalidFileNameChars()`.
- `AppEntry.AppTypeOf` (Core) zastąpiło dwie prywatne kopie (`Orchestrator.Describe`, `CompositionRoot.AppTypeOf`). Link `AppIcons.cs` w `Core.Tests`: 23 pliki, zaktualizowane w architekturze.
- Weryfikacja na UM760 (MEDIASTATION / F7BSI / EliteMini Series): `dotnet build MediaStation.slnx` 0 błędów / 0 ostrzeżeń; `dotnet test` 1056 zielonych; `npm test` 48 zielonych; `npm run build` ok (fonty Roboto Flex i Material Symbols w `dist/assets`).
- Przebieg Release z tymczasowym `config/icons/netflix.png`, sterowany przez CDP: `<img>` Netflixa załadowany (`naturalWidth` 128), Plex bez `<img>` (sam tytuł jako znak), w logu `warn appIconMissing` dla `moonlight-pc` i `plex`. Snapshot wstrzyknięty przez most (Netflix `failed`, Plex `background`/`native`) i zrzut ekranu: „Nie odpowiada” + „Coś się zacięło. Wciśnij OK”, „Wyciszony” + kropka „W tle · wyciszony”, ligatury Material Symbols jako ikony. Tymczasowy folder ikon usunięto.
- Nie wykonano na żywo: OK na kafelku → `startWithoutHost` → `Failed` z hosta (bramka pierwszego planu zablokowała wstrzyknięty Enter; `failed` wymuszono przez most), oraz `onError` obrazka (pokryty kodem; `icon: null` testem `Tile.test.tsx`).

## Plan Change Log

## Review Triage Log

### 2026-10-26 — Review pass
- verdicts: 6 findings — high 0, medium 0, low 5, false 1, maybe-false 0
- findings:
  - `[low]` `[patch]` Brak dowodów weryfikacji w planie (zadania `[ ]`, puste notatki) — uzupełnione: zadania odhaczone, wyniki komend, przebieg CDP i maszyna UM760 w Implementation Notes.
  - `[low]` `[reject]` Reguła `icon` nie łapie kropki/spacji na końcu i nazw zarezerwowanych (`CON`) — taki plik zostaje zgłoszony jako brak ikony (`appIconMissing`), kafelek pokazuje tytuł; dodatkowa gałąź walidacji bez realnej szkody.
  - `[low]` `[reject]` `AppIcons.FindMissing` rzuciłoby na niezwalidowanych wpisach — wołane tylko po `ConfigValidation` (jedyne wywołanie w `LoadConfig` pod `result.Config is not null`), guard dublowałby walidację.
  - `[low]` `[reject]` `failedIcon` nie ponawia tej samej nazwy pliku — ikony to statyczne pliki z configu czytane przy starcie; ponowienie wymagałoby timera/zmiany ruchu, a komentarz opisuje tylko zmianę nazwy.
  - `[false]` `[reject]` Skalowany kafelek zachodzi na sąsiada — odstęp 48px i skala 1.08 to wartości z `DESIGN.md` (kafelek 544px rośnie o ok. 22px na stronę, w obrębie guttera/odstępu); overflow rzędu to jawnie story 4.9.
  - `[low]` `[patch]` Guard `type` w `bridge.ts` przyjmuje dowolny string — dodana tabela `APP_TYPES` po kluczach typu z generated (jak `ACTION_KINDS`) i test z `type: 'foo'`.

## Design Notes

- `tileStatus` zwraca `{chip: {icon, label} | null, line: {text, dot} | null}`; `foreground` i `notRunning` bez obu. `background` wybiera wariant po `type`: `web` → „Wstrzymane”, pozostałe → „Wyciszony”.
- Wiersz 3 kafelków (3×544 + 2×48) wychodzi poza title-safe; dopasowanie to story 4.9.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx` -- expected: 0 błędów, 0 ostrzeżeń
- `dotnet test MediaStation.slnx` -- expected: 0 niepowodzeń
- `npm test`, `npm run build` (w `web/`) -- expected: sukces

**Manual checks (if no CLI):**
- Na bieżącej maszynie: Release z `config/icons/netflix.png`; przez CDP (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`) sprawdzić `img.naturalWidth > 0` dla kafelka z ikoną i brak `<img>` dla pozostałych; log `appIconMissing`; kafelek `Failed` (OK na kafelku → `startWithoutHost`) pokazuje „Nie odpowiada” i „Coś się zacięło. Wciśnij OK”. Tymczasowe pliki ikon usunąć po teście.

## Auto Run Result

Machine: ran on UM760 (host MEDIASTATION, board F7BSI, model EliteMini Series).

**Summary:** Snapshot niesie `icon` (`null` przy braku pliku w `config/icons/`) i `type`; walidacja `apps.json` odrzuca puste/spacje `title` i `icon` oraz `icon` ze ścieżką, znakami specjalnymi i `..`; App sprawdza pliki ikon (`warn appIconMissing`) i mapuje `icons.mediastation.example` → `config/icons/` (`DenyCors`, Debug i Release); Shell rysuje kafelek 544×306 wg `DESIGN.md` (ikona albo tytuł jako znak, chip Material Symbols, linia stanu, kropka, fokus 150 ms) z fontami bundlowanymi lokalnie.

**Files changed:** Core: `ShellSnapshot.cs`, `AppsConfig.cs`, `ConfigValidation.cs`, `ShellSnapshotFactory.cs`, `Orchestrator.cs`. App: `AppIcons.cs` (nowy), `CompositionRoot.cs`, `ShellOrigin.cs`, `ShellWindow.xaml.cs`. Web: `tileView.ts`, `Tile.tsx` (nowe), `Placeholder.tsx`, `bridge.ts`, `main.tsx`, `tokens.css`, `package.json`, `vitest.config.ts`. Testy: `AppIconsTests`, `ConfigValidationTests`, `OrchestratorStartupTests`, `ShellSnapshot*Tests`, `tileView.test.ts`, `Tile.test.tsx`, `bridge.test.ts`, `focus.test.ts`. Architektura: liczba linków (23).

**Review:** quick; patches 2 (low), deferred 0, rejected 4 (3 low, 1 false; powody w triage log).

**Verification (UM760):** build 0/0; `dotnet test` 1056 zielonych; `npm test` 48; `npm run build` ok; Release + CDP: ikona załadowana, fallback bez `<img>`, `appIconMissing` w logu, wygląd `Failed` i `Background`/`native` na zrzucie.

**Residual risks:** OK na kafelku z hosta (`startWithoutHost` → `Failed`) i `onError` obrazka nie sprawdzone na żywo; trzy kafelki 544px wychodzą poza title-safe (story 4.9); fokus na pilocie i telewizorze zostaje na odbiór całej epiki na UM760.

Follow-up review recommended: false (patched: 0 high, 0 medium, 2 low).
