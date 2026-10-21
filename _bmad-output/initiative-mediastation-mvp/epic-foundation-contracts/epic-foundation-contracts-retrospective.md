---
epic: epic-foundation-contracts
date: 2026-10-21
verdict: accepted
criteria: declared
headless: false
---

# Retrospective: Foundation & contracts

## Epic summary

Ta runda wznawia retro z 2026-10-20 po ukończeniu 1.7 (Ścisła koperta i diagnostyka hosta) i 1.6 (Refactor sweep). Poprzedni zapis został zaktualizowany; bieżące dowody wygrywają z tym, co zapisano wcześniej.

| Ref | Ticket | status / state | Plan baseline | Zakres | Uwagi |
|-----|--------|----------------|---------------|--------|-------|
| 1.1 | Tracer bullet | done / done | `7292f5d` | `7292f5d..cd8b650` | recorded |
| 1.3 | Logger JSON Lines | done / done | `cd8b650` | `cd8b650..dfe666c` | recorded; build 1.3 leży w zakresie 1.2 (równoległe worktree, rebase) |
| 1.2 | Kontrakty Core | done / done | `dfe666c` | `dfe666c..65b99d7` | atrybucja z subjectów commitów |
| 1.4 | Schematy i walidacja config/ | done / done | `65b99d7` | `65b99d7..7567ec4` | recorded |
| 1.5 | Jedna instancja i CLI | done / done | `7567ec4` | `7567ec4..006eb7a` | recorded |
| 1.7 | Ścisła koperta i diagnostyka hosta | done / done | `006eb7a` | `006eb7a..45a6d95` | recorded; 2 findings z quick review naprawione |
| 1.6 | Refactor sweep | done / done | `f68450d` (nieosiągalny z HEAD) | `45a6d95..65222c5` | plan zrebazowany do `65665bd`; baseline nie poprawiony (N6) |

- **`pending_tickets`: brak.** Wszystkie 7 ticketów ma `state: done` (`tickets.py status`, 2026-10-21). Brak ticketów na `built`.
- Pełny diff epiki: `7292f5d..HEAD` (22 commity, 0 merge'y). Delta od poprzedniego retro: `66aace8..HEAD` (7 commitów: retro, aktualizacja ticketów, 1.7, 1.6).
- Poprzednia runda objęła diff `7292f5d..66aace8`; ta runda czyta deltę `66aace8..HEAD` i liczy aggregate views na pełnym diffie epiki. `bmad-review` uruchomiono na delcie (trzy lenses: adversarial, edge-case-hunter, verification-gap); poprzednia runda objęła resztę.

### Inventory dowodów

| Dowód | Stan |
|-------|------|
| Epic file z Done when (5 punktów) | jest, więc kryteria są **declared** |
| Requirements | `initiative-mediastation-mvp.md` nie ma sekcji Requirements; CAP-1/CAP-12 czytane z `spec-mediastation/spec-mediastation.md:30-33, 134-138` |
| Architektura | `architecture-mediastation/architecture-mediastation.md` (AD-2, AD-3, AD-11, AD-13, AD-14, AD-16) |
| Story files | brak (entries nie były pullowane); intent w `description`/`verify` z `tickets.toml` i w blokach frozen planów |
| Plany | 7/7, każdy z Review Triage Log; 1.6 `quick` bez findings, 1.7 `quick` z 2 findings (oba `patch`) |
| Session logs | są dla 1.6 (`…story-1-6/9e00ccc4…`) i 1.7 (`…story-1-7/b204ec8e…`); 1.1–1.5 z poprzedniej rundy |
| Poprzednie retro | brak wcześniejszej epiki; poprzednia runda to ten sam plik (2026-10-20) |
| Braki | brak re-runu 1.6 na UM760 (N1); brak automatycznego pokrycia ścieżek App-only (N3); brak `tools/samples/config/` (N5) |

## Findings

### Aggregate views (runda 2026-10-20, stan po 1.7 i 1.6)

| # | Finding | Disposition po 1.7/1.6 | Dowód |
|---|---------|------------------------|-------|
| F1 | Koperta czytała pobłażliwie (`"up, down"`, `"UP"`, `"Kind"`), config ściśle | **fixed** | `ContractJson.ReadOptions` (`ContractJson.cs:25,41-52`), `StrictEnumConverter.cs`, testy `CoreMessageContractTests.cs:144-160`, `ShellSnapshotContractTests.cs:72-85` |
| F2 | `appId` ma dwie reguły (config vs `CliArgs`) | **deferred** | wpis Deferred w `epic-netflix-dpad.md` (commit `006eb7a`); warunek: story implementująca `--smoke` |
| F3 | `ConfigValidation.cs` 451 linii, trzy odpowiedzialności | **fixed (częściowo)** | konwerter wydzielony do `StrictEnumConverter.cs`; `ConfigJson` zostaje w `ConfigValidation.cs:378-398` (398 linii); brak nowego god-class; plan 1.6 świadomie nie przenosił eksportu schematów (F4) |
| F4 | Cykl `Contracts` ↔ `Config` i równoważność schematu z runtime | **accepted** | `ConfigValidation.cs:378-381` mówi „struktura zgodna ze schematem, reguły semantyczne tylko w runtime”; przeniesienie eksportu odrzucone w 1.6 (`Never` w planie) |
| F5 | `BannedSymbols.txt` nie pokrywał AD-14 i obiektów OS | **fixed** | `BannedSymbols.txt:34-45`; nowa luka ambient time w N8 |
| F6 | Brak `id?` w rekordzie koperty | **deferred** | wpis Deferred w `epic-browserhost.md`; warunek: pierwsza story request/response |
| F7 | Awaria startu WebView2 kończyła proces kodem 0 | **fixed** | `ExitCode.cs:25`, `ShellWindow.xaml.cs:66-72`; log `webView2StartFailed` i `stop {exitCode:5}` 2026-10-21T11:45:30Z/33Z |

### Diff-scope review (runda 2026-10-20)

| # | Finding | Disposition po 1.7/1.6 | Dowód |
|---|---------|------------------------|-------|
| F8 | Współbieżny append gubi i psuje linie | **fixed** | `FileLogSink.cs:32-39` (`AppendData`); `LogAppendCheck` exit 0, 10000 linii (2026-10-21) |
| F9 | `ShellBridge` cicho gubi wiadomości | **fixed** | `ShellBridge.cs:22-25,75-81`; `bridgeMessageRejected` |
| F10 | Brak `NavigationCompleted`, `ProcessFailed`, `NewWindowRequested` | **fixed** | `ShellWindow.xaml.cs:60-62,75-108` |
| F11 | Brak guard `e.Source` i blokady nawigacji | **deferred** | wpis Deferred w `epic-home-screen.md`; warunek: pierwsza story z Intents przez most |
| F12 | `Publish` bez marshallingu na wątek UI, `rev` stałe | **deferred** | wpis Deferred w `epic-orchestrator-lifecycle.md`; warunek: pierwsza story publikująca snapshot z Orchestratora |
| F13 | `new Mutex` może rzucić przed `WaitOne` | **fixed** | `SingleInstance.cs:28-47`; `mutexUnavailable`; brak testu (N3) |
| F14 | Nieobsłużony wyjątek UI loguje się dwa razy | **fixed** | `CompositionRoot.cs:26,96-118`; `DispatcherLogged`; brak testu (N3) |
| F15 | `JsonLinesLog.Format` czyta `Message`/`ToString` przed `try` | **fixed** | `JsonLinesLog.cs:57-86`; testy `JsonLinesLogTests.cs:219-246` |
| F16 | Brak exhaustiveness w `App.tsx` | **fixed** | `App.tsx:22-25` |
| F17 | Lista błędów configu ucina się bez informacji | **deferred** | wpis Deferred w `epic-home-screen.md` |
| F18 | Luki reguł semantycznych configu | **deferred** | wpisy Deferred w `epic-browserhost.md`, `epic-deploy-resilience.md`, `epic-home-screen.md`, `epic-moonlight-native.md`, `epic-orchestrator-lifecycle.md` |
| F19 | Target `BuildWeb` w Debug nie robi typecheck | **fixed** | `MediaStation.App.csproj:54-57` (`npm run typecheck` w Debug) |
| F20 | Luki weryfikacji z decyzji „bez App.Tests” | **fixed (częściowo)** | `StartDecision.cs`, `ShellSnapshotFactory.cs`, `ConfigPreflight.cs:31-41` + testy; ścieżki App-only nadal bez pokrycia (N3) |
| F21 | Handshake `ready` budowany ręcznie po stronie web | **deferred** | warunek: vitest z driverami (architektura l.311) |
| F22 | Klasyfikacja błędów configu po komunikatach STJ | **accepted** | testy `ConfigValidationTests`; publish self-contained przypina runtime |
| F23 | `Action`/`InputMode` kolidują z BCL/WPF | **fixed** | aliasy w `MediaStation.App.csproj:22-26` i `MediaStation.Core.Tests.csproj` |
| F24 | Mutex `Local\` zamiast `Global\` | **accepted** | decyzja z planu 1.5 (jeden użytkownik UM760) |
| F25 | Nazwy `snapshot`/`ready` mogą kolidować z `MessageTypes` | **fixed** | `MessageTypes.cs:36-55`; `MessageTypesTests.cs` |
| F26 | Ścieżka `%LOCALAPPDATA%\MediaStation\config` zaszyta w copy ekranu błędu | **accepted** | root danych jest jeden i stały (AD-13) |

### Nowe findings (runda 2026-10-21)

**N1. Re-run 1.6 wykonano na maszynie dev, a plan wymaga UM760; wynik nie trafił do planu.** CONFIRMED.
- Close commit `65222c5` mówi „Automated HITL run on the dev machine (self-contained Release publish)”, a plan 1.6 w `Manual checks` wymaga UM760 (`story-refactor-sweep-plan.md:104-109`). Log z 2026-10-21 (12:02–12:04Z) potwierdza `instanceRejected` (`run`, `smoke`) i trzy `configError` (`apps.json`, `input.json`, `settings.json`, `$.x`), więc zachowanie jest sprawdzone — ale nie na maszynie produkcyjnej.
- Plan 1.6 nie ma datowanej linii wyniku w `Verification`; close commit zmienia tylko `status` (`65222c5` diff: 1 linia). To ten sam wzorzec co P2/A10 z poprzedniej rundy.
- **Disposition:** *fix now* — re-run trzech checks 1.6 na UM760 albo jawne przyjęcie dowodu z dev machine przez autora; wynik dopisać do planu. Lekcja: P2/P7.

**N2. `LogAppendCheck` jest manualny i zbyt słaby, by chronić F8 przed regresją.** CONFIRMED.
- Nie jest uruchamiany przez `dotnet test` ani build; `MediaStation.Core.Tests` referuje tylko Core, a `MediaStation.LogAppendCheck` to `Exe` bez `IsTestProject` (`tools/MediaStation.LogAppendCheck/MediaStation.LogAppendCheck.csproj`). Rewert `FileLogSink` do zwykłego `FileMode.Append` przejdzie wszystkie 255 testów.
- Program startuje writery sekwencyjnie i od razu czeka; nie ma wspólnej bariery startu, więc „jednocześnie” z macierzy nie jest gwarantowane (`Program.cs:32-46`). `WaitForExit()` nie ma timeoutu (`:40`), a `finally` usuwa katalog także po błędzie (`:91-99`), więc uszkodzony log nie zostaje do diagnozy. Asercja nie sprawdza `pad` ani pełnego kształtu linii (`:55-88`).
- **Disposition:** *fix now* (R3) — bariera startu, timeout, zachowanie katalogu przy błędzie, pełny kształt linii, test live-read; uruchamiać z `release.yml` albo skryptu w repo.

**N3. Ścieżki App-only z 1.6/1.7 nie mają testów ani pełnego manualnego pokrycia.** CONFIRMED.
- `ShellBridge` (odrzucenia `bridgeMessageRejected`), `ShellWindow` (`ProcessFailed`, `NewWindowRequested`), `SingleInstance` (`mutexUnavailable`) i `CompositionRoot` (`DispatcherLogged`) nie są referowane przez żaden test. Plan 1.6 wprost zostawia Mutex i Dispatcher poza testami (`story-refactor-sweep-plan.md:82`); plan 1.7 nie ma testów App (`Never`).
- Manualne checki 1.7 pokrywają start WebView2, brak `index.html` i czysty run (`story-scisa-koperta-i-diagnostyka-hosta-plan.md:95-100`); nie pokrywają `window.open`, `ProcessFailed`, niedostępnego mutexu ani podwójnego logu UI.
- **Disposition:** *defer* (R4) — pierwsza story dotykająca danej ścieżki (browserhost/home-screen/deploy) dodaje test lub manualny check; bridge można wydzielić do Core tak jak `StartDecision`.

**N4. 1.7 (risk medium) dostał tylko `quick` review; A12 nie zadziałało.** CONFIRMED.
- `story-scisa-koperta-i-diagnostyka-hosta-plan.md:11-13`: `review: 'quick'`, `lenses_ran: ['quick']`. Quick znalazł 2 realne findings w `LogAppendCheck`, a retro z trzema lenses znalazło dalsze luki (N2, N3).
- **Disposition:** *fix now* (R5) — dla `risk = "medium"` pełne lenses albo co najmniej jedno review epiki przed refactor sweep (A12).

**N5. A9/A10 nadal częściowe; plan 1.7 zawiera nieaktualne oczekiwanie 404.** CONFIRMED.
- Manualne skrypty są w planach 1.6/1.7 (A9 spełnione w części), ale `tools/samples/config/` nie istnieje (A9 niespełnione).
- Close commity `45a6d95` i `65222c5` zmieniają tylko `status`; wyniki są w treści commitów, nie w `Verification` planu (A10 niespełnione). Plan 1.7 zakładał `httpStatusCode` 404 dla braku `index.html`, a close commit `45a6d95` odnotowuje faktyczne `0` (`webErrorStatus unknown/connectionAborted`); plan tego nie poprawia.
- **Disposition:** *fix now* (R5) — ustalić kanoniczne miejsce wyniku (plan albo commit body) i je stosować; poprawić oczekiwanie 404 → 0; sample configi do repo.

**N6. `baseline_revision` planu 1.6 wskazuje commit nieosiągalny z `HEAD`.** CONFIRMED.
- `story-refactor-sweep-plan.md:7` ma `f68450d`, którego `git merge-base --is-ancestor f68450d HEAD` nie potwierdza; to plan z worktree sprzed rebase. Zrebazowany plan to `65665bd`, a faktyczny zakres 1.6 to `45a6d95..65222c5`.
- **Disposition:** *fix now* (R2) — ustawić baseline na `65665bd` (albo `45a6d95`), żeby przyszły review/retro liczyły właściwy zakres.

**N7. Ścisły odczyt nadal przyjmuje zduplikowane klucze; konwerter enumów nie ma guardu na aliasy.** CONFIRMED / PLAUSIBLE.
- `ContractJson.CreateReadOptions` (`ContractJson.cs:41-52`) nie ustawia `AllowDuplicateProperties = false`; `ConfigJson` robi to dla plików (`ConfigValidation.cs:389-394`), a test przypina `ContractJson.Options.AllowDuplicateProperties == true` (`ConfigValidationTests.cs:828`). W kopercie podwójny `type`/`kind` nadal wygrywa ostatni.
- `StrictEnumConverter` (`StrictEnumConverter.cs:26-31`) robi `_names.Add(value, name)`; enum z aliasem (dwie nazwy, ta sama wartość) rzuci `ArgumentException`/`TargetInvocationException`, którego `ShellBridge` nie łapie (łapie tylko `JsonException`). Dziś żaden enum nie ma aliasu (grep `src/MediaStation.Core`).
- **Disposition:** *fix now* dla `AllowDuplicateProperties` (R6); *defer* dla aliasów do pierwszej story dodającej enum kontraktu.

**N8. Niskie, odłożone lub świadomie przyjęte.** 
- Modalny `MessageBox` przy awarii WebView2 nadal blokuje start w trybie bezobsługowym; `--supervise` powstaje w epic-deploy-resilience — *defer* tam.
- `NavigationCompleted` loguje `OperationCanceled` jako `navigationFailed`; normalny run 1.7 nie dał takiego wpisu — *defer* do epic-home-screen (więcej nawigacji).
- `BannedSymbols` nie zakazuje `TimeProvider.System`, `Stopwatch` ani `Environment.TickCount`; Core dziś ich nie używa — *defer* do pierwszej story z czasem w Core.
- `ShellBridge` loguje `type`/`reason` bez limitu; renderer jest nasz — *accept* (do ewentualnego hardeningu bezpieczeństwa).
- `ShellSnapshotFactoryTests` buduje `new ConfigResult(null, [])`, czyli stan sprzeczny z dokiem `ConfigResult` (`ConfigValidation.cs:14-15`); produkcja go nie tworzy — *defer* do następnego refactoru testów.
- `ContractJson.Options` pozostaje publiczny i pobłażliwy; to świadoma decyzja (zapis i schematy) — *accept*.
- `DispatcherLogged` używa domyślnej równości i nie jest czyszczony przy `Handled = true`; proces kończy się po nieobsłużonym wyjątku — *accept*.
- `App.tsx` default `never` nie chroni runtime przed nieznanym `screen`; 1.1 triage #8 odrzucił to, bo host i UI dzielą schemat — *accept*.
- `StartDecision` mapuje `mutexUnavailable` na `AlreadyRunning` (kod 3) — to wiersz macierzy 1.6 (`story-refactor-sweep-plan.md:41`), świadoma decyzja, nie finding.

## Process lessons

- **P1 (2026-10-20).** Testy HITL na UM760 nie miały spisanej procedury; autor pytał o nią dwa razy. Plany 1.6/1.7 mają już skrypty krok po kroku, więc A9 zadziałało w części.
- **P2 (2026-10-20, nadal aktualne).** Wynik HITL nie trafia do planu. Close commity 1.6/1.7 zmieniają tylko `status`, a wyniki są w treści commitów (N1, N5).
- **P3 (2026-10-20).** Odłożone findings z warunkiem nie przechodzą między stories. A8 zapisało je w epikach konsumentów (commit `006eb7a`); 1.7 naprawiło F1 w obu konsumentach (`ContractJson` i `ConfigJson`), więc tym razem zadziałało.
- **P4 (2026-10-20, nadal aktualne).** Wszystkie plany miały `quick`; 1.7 medium też (N4). Quick znajduje realne defekty, ale nie widzi luk weryfikacji (N2, N3).
- **P5 (2026-10-20).** Wins: tracer bullet dał pipeline schema → TS; 1.6 przeniósł logikę do Core z testami; 1.7 naprawił F1/F7/F8/F9/F10 i dodał 23 testy (232 → 255).
- **P6 (2026-10-21).** Rebase worktree'a zostawił `baseline_revision` na commicie spoza mastera (N6). Baseline trzeba ustawiać po rebase albo z rebased plan commit.
- **P7 (2026-10-21).** „Automated HITL” na dev machine zastąpił ręczny run autora na UM760 bez jawnej decyzji w planie (N1). Jeśli agent wykonuje HITL, plan ma to mówić i wskazywać środowisko.
- **P8 (2026-10-21).** Close commit body jest dobrym nośnikiem wyników, ale rozjeżdża się z `Verification` planu (N5). Trzeba wybrać jedno kanoniczne miejsce.

## Behavior verification

Wykonane 2026-10-21 na maszynie deweloperskiej, na HEAD `65222c5`:

- `dotnet test`: **255/255** zielone (poprzednia runda: 224/224; 1.7 dołożył 8, 1.6 dołożył 23).
- `dotnet build -c Release`: sukces, **0 ostrzeżeń**, `web/contracts/schema/*.schema.json` bez zmian (hash przed/po identyczny), `git status` czysty.
- `dotnet run --project tools/MediaStation.LogAppendCheck -c Release --no-build`: **exit 0**, 10000 linii, każda parsuje się jako JSON, każda para `(writer, seq)` dokładnie raz.
- Release `MediaStation.exe` w trybach bez okna:

| Argumenty | Kod wyjścia | Linie logu |
|-----------|-------------|------------|
| `--foo` | 2 | `start` → `invalidArguments` → `stop {exitCode: 2}` |
| `--smoke netflix` | 4 | `start` → `modeUnsupported {mode: smoke, appId: netflix}` → `stop {exitCode: 4}` |
| `--calibrate-input` | 4 | `start` → `modeUnsupported {mode: calibrateInput}` → `stop {exitCode: 4}` |

- Log z rundy HITL na dev machine (2026-10-21, `%LOCALAPPDATA%\MediaStation\logs\mediastation-2026-10-21.jsonl`):
  - `webView2StartFailed` + `stop {exitCode: 5}` (11:45:30Z/33Z) — A2.
  - `instanceRejected` (`run`, `smoke`) + `stop {exitCode: 3}` (12:02:51Z/52Z) — A5/1.6.
  - `configError` dla `apps.json`, `input.json`, `settings.json` z `$.x` i `stop {exitCode: 0}` (12:03:46Z–12:04:14Z) — 1.6.
  - `navigationFailed` z `webErrorStatus unknown`/`connectionAborted` i `httpStatusCode: 0` (2026-10-20T20:06:48Z/49Z) — 1.7 check 2; plan zakładał 404 (N5).
- **Nie wykonane tutaj:** pełnoekranowe okno shella, `ProcessFailed`, `NewWindowRequested`, niedostępny mutex i UM760. Te ścieżki mają dowody z close commitów/logów albo pozostają otwarte (N1, N3).

## Previous-retro follow-through

Punkt odniesienia: poprzednia runda tego samego retro (2026-10-20). Nie ma retro wcześniejszej epiki (to pierwsza epika inicjatywy). Wszystkie poniższe to action items z 2026-10-20; żaden nie został zastosowany przez samo retro.

| # | Item (z 2026-10-20) | Czy zadziałał | Dowód |
|---|---------------------|---------------|-------|
| A1 | Ścisły odczyt koperty: `StrictEnumConverter` + `PropertyNameCaseInsensitive = false`; testy `"up, down"`, `"UP"`, `"Kind"` | **tak** | `ContractJson.cs:25,41-52`; `StrictEnumConverter.cs`; `CoreMessageContractTests.cs:144-160`; `ShellSnapshotContractTests.cs:72-85` |
| A2 | `ExitCode.ShellStartFailed = 5` i `Application.Shutdown(kod)` w `catch` | **tak** | `ExitCode.cs:25`; `ShellWindow.xaml.cs:66-72`; log `stop {exitCode: 5}` 2026-10-21T11:45:33Z |
| A3 | `ILog` w `ShellBridge` (`bridgeMessageRejected`) + handlery WebView2 | **tak** | `ShellBridge.cs:22-25,75-81`; `ShellWindow.xaml.cs:60-62,75-108` |
| A4 | Atomowy append (`FileSystemRights.AppendData`) i test/skrypt z dwoma procesami | **tak (manualnie)** | `FileLogSink.cs:32-39`; `LogAppendCheck` exit 0 (2026-10-21); brak automatu w `dotnet test` (N2) |
| A5 | Czysta decyzja startu, fabryka snapshotu, mapowanie I/O do Core + testy `ExitCode` | **tak** | `StartDecision.cs`; `ShellSnapshotFactory.cs`; `ConfigPreflight.cs:31-41`; `StartDecisionTests.cs`, `ExitCodeTests.cs`, `ShellSnapshotFactoryTests.cs`, `ConfigFileStateTests.cs` |
| A6 | Guard `Mutex`, jeden wpis unhandled, `LogError` odporne, `never` w `App.tsx`, typecheck w Debug | **tak** | `SingleInstance.cs:28-47`; `CompositionRoot.cs:26,96-118`; `JsonLinesLog.cs:57-86`; `App.tsx:22-25`; `MediaStation.App.csproj:54-57` |
| A7 | `BannedSymbols`: AD-14 i obiekty OS; doc `ConfigJson` o schemacie | **tak** | `BannedSymbols.txt:34-45`; `ConfigValidation.cs:378-381`; eksport schematów świadomie został w `ContractJson` (plan 1.6 `Never`) |
| A8 | Odłożone findings z warunkiem w epikach konsumentów | **tak** | commit `006eb7a`: `epic-browserhost.md`, `epic-deploy-resilience.md`, `epic-home-screen.md`, `epic-moonlight-native.md`, `epic-netflix-dpad.md`, `epic-orchestrator-lifecycle.md` |
| A9 | Manualne skrypty HITL krok po kroku; sample configi w repo | **częściowo** | skrypty: plan 1.6:104-109, plan 1.7:95-100; `tools/samples/config/` nie istnieje |
| A10 | Zamknięcie story po HITL dopisuje datowany wynik do `Verification` planu | **nie** | close `45a6d95` i `65222c5` zmieniają tylko `status`; wyniki są w treści commitów (N1, N5) |
| A11 | Deferred findings z warunkiem do listy/epiki; finding w bazie dziedziczonej sprawdza innych konsumentów | **częściowo** | A8 zapisane; 1.7 naprawiło F1 w `ContractJson` i `ConfigJson`, ale konwencja nie jest spisana |
| A12 | Dla `risk = "medium"` pełne lenses zamiast `quick` | **nie** | 1.7 medium ma `review: 'quick'`, `lenses_ran: ['quick']` (plan:11-13) |

## Action items

Wszystkie są **proponowane, nie zastosowane**. Retro nie zmienia kodu, ticketów ani specu.

### Remediation i weryfikacja

| # | Co | Findings | Owner |
|---|----|----------|-------|
| R1 | Re-run trzech manualnych checks 1.6 na UM760 albo jawne przyjęcie dowodu z dev machine; datowany wynik w `Verification` planu 1.6; poprawić oczekiwanie 404 → 0 w planie 1.7 | N1, N5 | autor |
| R2 | Poprawić `baseline_revision` planu 1.6 z `f68450d` na `65665bd` (albo `45a6d95`) | N6 | autor / `bmad-ticket` |
| R3 | Utwardzić `LogAppendCheck`: wspólna bariera startu, timeout `WaitForExit`, zachowanie katalogu przy błędzie, sprawdzenie `pad` i pełnego kształtu linii, test live-read; uruchamiać z `release.yml` lub skryptu | N2 | autor |
| R4 | Pokryć ścieżki App-only: `ShellBridge` (odrzucenia), `ShellWindow` (`ProcessFailed`, `NewWindowRequested`), `SingleInstance` (`mutexUnavailable`), `CompositionRoot` (jeden wpis UI); wydzielić testowalne helpery do Core, gdzie tanie | N3 | autor, przy pierwszej story dotykającej ścieżki |
| R5 | Proces: pełne lenses dla `risk = "medium"` (A12); kanoniczne miejsce wyniku HITL (plan albo commit body) i spójność z `Verification` (A10); sample configi w `tools/samples/config/` (A9) | N4, N5 | autor |
| R6 | `AllowDuplicateProperties = false` w `ContractJson.ReadOptions` + testy podwójnego `type`/`kind`; guard/test aliasów w `StrictEnumConverter` (defer do pierwszego nowego enuma) | N7 | autor |
| R7 | Odłożone niskie: modal MessageBox → epic-deploy-resilience; `OperationCanceled` → epic-home-screen; ambient time w `BannedSymbols` → pierwsza story z czasem w Core; `ShellSnapshotFactoryTests` na poprawnym `ConfigResult` → następny refactor testów | N8 | autor / właściwe epiki |

### Spec reconciliation

| # | Co | Findings | Owner |
|---|----|----------|-------|
| R8 | Po akceptacji domknąć epikę przez `bmad-ticket`; w Notes epiki zaktualizować zapis o verdict `rejected` z 2026-10-20 (linia 52) na bieżący wynik; 1.6/1.7 są już `done` | N1, N5, N6 | autor |

## Acceptance verdict

**Machine verdict: `accepted-with-open-items`.** Kryteria: **declared** (Done when z epic file).

**Powód:** wszystkie 7 ticketów jest `done`, `pending_tickets` jest puste, a pięć punktów Done when ma dowody. Otwarte pozostają findings, które nie łamią kryteriów epiki i mają adresy w R1–R8; N1 (brak re-runu 1.6 na UM760) autor przyjął jako wystarczający dowód z dev machine (2026-10-21).

| # | Kryterium | Stan | Dowód |
|---|-----------|------|-------|
| 1 | Testy Core przechodzą; Core bez Win32/WebView2/WPF/systemu plików (AD-3) | spełnione | `dotnet test` 255/255 (2026-10-21); `CoreBoundaryTests.cs`; `BannedSymbols.txt` + `TreatWarningsAsErrors`; luki ambient time w N8 dotyczą AD-14, nie AD-3 |
| 2 | Typy TS z JSON Schema Core w buildzie web, nikt ich nie edytuje (AD-16) | spełnione | `BuildWeb` (`MediaStation.App.csproj:42-70`), `contracts.mjs`, `generated/` w `.gitignore`; Release build 0 ostrzeżeń, schema hash bez zmian |
| 3 | Druga instancja, także `--smoke`, nie startuje (AD-2) | spełnione | 1.5 HITL na UM760 (`66aace8`); 1.6 dev-machine: `instanceRejected` (`run`, `smoke`), `stop {exitCode: 3}` (log 2026-10-21T12:02:51Z/52Z) |
| 4 | Na UM760 błędny config daje ekran błędu zamiast crasha (AD-11) | spełnione | 1.4 HITL na UM760 (`7567ec4`); 1.6 dev-machine: trzy `configError` z `$.x` i `stop {exitCode: 0}` (log 2026-10-21T12:03:46Z–12:04:14Z) |
| 5 | `dotnet publish` na UM760 pokazuje shell z plików lokalnych i loguje JSON Lines UTC z rotacją dzienną (AD-13) | spełnione | 1.1/1.3 HITL na UM760; 1.6 self-contained Release publish na dev machine (placeholder, DOM + screenshot); `JsonLinesLogTests` z `FakeTimeProvider`; `ts` z `Z` w logu |

**Human decision (2026-10-21): autor podniósł verdict na `accepted`.** Autor przyjął dowód z dev machine dla 1.6 (N1) i zaakceptował R2–R8 jako otwarte/śledzone; R1 pozostaje jako uzupełnienie zapisu w planie, nie jako warunek akceptacji.

## Open questions

- **Q1. N1 — UM760 vs dev machine.** Czy dowód z dev machine (commit `65222c5`, log 2026-10-21) wystarcza dla 1.6, czy trzeba powtórzyć trzy checks na UM760? Odpowiedź zmienia, czy epika jest `accepted`, czy zostaje `accepted-with-open-items` z N1.
- **Q2. N6 — baseline 1.6.** Czy poprawić `baseline_revision` na `65665bd` (rebasowany plan) czy `45a6d95` (baza rebase)? Pierwsze obejmuje plan, drugie plan+build+close.
- **Q3. N4/A12 — review depth.** Czy od teraz każdy `risk = "medium"` dostaje pełne lenses, czy wystarczy jedno review epiki przed refactor sweep?
- **Q4. N8 — deferred low.** Czy któreś z odłożonych niskich findings (modal, `OperationCanceled`, ambient time, log limit, test fabryki) ma wejść do najbliższej story, czy zostaje w epikach konsumentów?
