---
epic: epic-foundation-contracts
date: 2026-10-20
verdict: rejected
criteria: declared
headless: false
---

# Retrospective: Foundation & contracts

## Epic summary

Epika `epic-foundation-contracts` (id 1, inicjatywa `initiative-mediastation-mvp`), covers CAP-1 i CAP-12 (tylko logi).

| Ref | Ticket | status / state | Plan baseline |
|-----|--------|----------------|---------------|
| 1.1 | Tracer bullet: MediaStation.exe → Core → WebView2 shell | done / done | `7292f5d` |
| 1.2 | Kontrakty Core dla Orchestratora i InputHub | done / done | `dfe666c` |
| 1.3 | Logger JSON Lines | done / done | `cd8b650` |
| 1.4 | Schematy i walidacja config/ z ekranem błędu | done / done | `65b99d7` |
| 1.5 | Jedna instancja i wejście CLI | done / done | `7567ec4` |
| 1.6 | Refactor sweep | — / planned | brak planu |

- **Niedokończone (`pending_tickets`): `1.6`.** Użytkownik świadomie zgodził się na retro mimo to (2026-10-20); wymusza to machine verdict **rejected**.
- Tickety na `built`: brak.

### Zakresy diffów (z `baseline_revision` planów, kolejność wg historii)

| Plan | Zakres | Commity w zakresie | Uwagi |
|------|--------|--------------------|-------|
| 1.1 | `7292f5d..cd8b650` | `0f35664`, `2d947ff`, `4efe66c` (planning change proposal, nie kod epiki), `1650ad6` build, `c228ec4` close, `cd8b650` gitignore | recorded |
| 1.3 | `cd8b650..dfe666c` | `dfe666c` (gitignore `.mcp.json`, z sesji 1.2) | build 1.3 (`65b99d7`) wylądował w zakresie 1.2: równoległe worktree, rebase po 1.2 |
| 1.2 | `dfe666c..65b99d7` | `145b3db` build 1.2, `65b99d7` build 1.3 | atrybucja z subjectów commitów (`Build story 1.x`) |
| 1.4 | `65b99d7..7567ec4` | `f299a4a`, `318f704` (close 1.2, 1.3), `bb07f76` build, `7567ec4` close | recorded |
| 1.5 | `7567ec4..HEAD` (`66aace8`) | `da900b7` build, `66aace8` close | inferred: koniec = HEAD, brak późniejszej pracy |

Merge: 0 (`merge_count` = `merges_measured` = 0), `binary_revisions` = 0. Diff kodu całej epiki (`src/ tests/ tools/ web/`, bez `package-lock.json`) ma 191 KB.

### Inventory dowodów

| Dowód | Stan |
|-------|------|
| Epic file z Done when (5 punktów) | jest, więc kryteria są **declared** |
| Requirements | `initiative-mediastation-mvp.md` nie ma sekcji Requirements; CAP-1/CAP-12 czytane z `spec-mediastation/spec-mediastation.md:30-33, 134-138` |
| Architektura | `architecture-mediastation/architecture-mediastation.md` (AD-2, AD-3, AD-11, AD-13, AD-16) |
| Story files | brak (entries nie były pullowane; intent w `description`/`verify` z `tickets.toml` i w blokach frozen planów) |
| Plany | 5/5, każdy z Review Triage Log (lens `quick`); 1.4 ma Plan Change Log (review loop 1) |
| Plan 1.6 | brak (ticket `planned`) |
| Session logs | są: 1.1 `C--Users-fpiec-mediastation/0e39a96b…`, 1.2–1.5 w `C--Users-fpiec-mediastation--worktrees-mediastation-story-1-{2..5}/`, sesja koordynująca `912d7732…` |
| Poprzednie retro | brak (pierwsza epika inicjatywy) |

## Findings

Źródła: aggregate views liczone inline (z `git_evidence.py` i odczytu plików) oraz `bmad-review` na diffie kodu `7292f5d..66aace8` z trzema lenses: adversarial (18), edge-case (20), verification-gap (6). Findings z subagentów sprawdziłem w źródle przed routingiem. **CONFIRMED** = sprawdzone w kodzie, probe albo pomiarze. **PLAUSIBLE** = zgodne z kodem, ale nie wywołane w runtime. Disposition: *fix* (proponowana poprawka), *defer* (z adresem), *accept* (świadome odstępstwo, żeby kolejne retro tego nie flagowały).

### Aggregate views

**F1. Koperta czyta pobłażliwie, config ściśle (pattern divergence, styk 1.2 ↔ 1.4).** CONFIRMED (probe).
- **Problem:** `ContractJson.Options` (`src/MediaStation.Core/Contracts/ContractJson.cs:21-33`) ma `JsonSerializerDefaults.Web`, czyli `PropertyNameCaseInsensitive = true`, oraz `JsonStringEnumConverter`, który przyjmuje dowolną wielkość liter i listy po przecinku. `DeserializeEnvelope<Action>` przyjmuje `"kind":"up, down"` jako `Down`, a także `"UP"` i `"Kind"`. Schemat eksportowany z Core odrzuca wszystkie trzy formy.
- **Historia:** story 1.4 znalazło dokładnie tę pobłażliwość (plan 1.4, Code Map l.67, Review Triage #1 i #3). Poprawiło ją jednak tylko w kopii `ConfigJson` (`ConfigValidation.cs:997-1015`), a ścieżka odczytu koperty z 1.2 została bez zmian.
- **Disposition:** *fix* w 1.6. Ścisły konwerter enumów i `PropertyNameCaseInsensitive = false` w odczycie koperty. Dodatkowe pola payloadu zostają dozwolone (AD-7, triage 1.1 #3).
- **Lekcja:** P3.

**F2. Reguła `appId` istnieje w dwóch wersjach, styk 1.4 ↔ 1.5 (duplication map).** CONFIRMED.
- **Problem:** kebab-case jest prywatnym regexem w `ConfigValidation.cs:802`. `CliArgs.Parse` (`CliArgs.cs:87-105`) sprawdza tylko pustość i `-` na początku, więc `--smoke " "` albo `--smoke "a b"` dają poprawny tryb Smoke.
- **Disposition:** *defer* do epic-deploy-resilience, do story implementującej `--smoke`. Do tego czasu `appId` nigdzie nie trafia poza log.

**F3. `ConfigValidation.cs` ma 451 linii i trzy odpowiedzialności (size growth).** CONFIRMED.
- To największy plik produkcyjny epiki (`git_evidence`: +451 netto, w całości z 1.4). Mieści reguły semantyczne, klasyfikację komunikatów STJ regexami (l.723-797) oraz `ConfigJson` ze `StrictEnumConverter` (l.991-1064).
- To jeszcze nie god-class, ale F1 potrzebuje `StrictEnumConverter` poza tym plikiem.
- **Disposition:** *fix* w 1.6. Wydzielić `ConfigJson` i ścisły konwerter do osobnego pliku, wspólnego z `ContractJson`.

**F4. Cykl namespace'ów `Contracts` ↔ `Config` i niesprawdzona równoważność schematu z runtime (architecture delta).** CONFIRMED.
- **Cykl:** `ContractJson.cs:10, 100, 109` importuje `MediaStation.Core.Config`, a `Config` używa `ConfigError`, `ActionKind`, `AppType` i `PointerPolicy` z `Contracts`.
- **Schemat a runtime:** schematy configu eksportuje `ContractJson.Options`, a runtime czyta przez `ConfigJson.Options`. Doc `ConfigValidation.cs:991-996` twierdzi, że runtime przyjmuje dokładnie to, co schemat. Żaden test tego nie sprawdza, a schemat nie ma reguł semantycznych: zakresu `cdpPort`, kebab-case, VK, unikalności ani minimum > 0.
- **Disposition:** *fix* w 1.6, opcjonalnie. Eksport schematów configu przenieść do `Config`, a doc skorygować na „struktura zgodna ze schematem, reguły semantyczne tylko w runtime”.

**F5. `BannedSymbols.txt` nie pokrywa całego AD-3/AD-14 (architecture delta).** CONFIRMED.
- Lista (`src/MediaStation.Core/BannedSymbols.txt:1-33`) nie zakazuje `DateTime.Now`/`UtcNow` (AD-14: czas tylko przez `TimeProvider`), `System.Diagnostics.Process`, `System.Net.*`, nazwanych `Mutex`/`Semaphore`/`EventWaitHandle` ani `Environment.GetFolderPath`.
- Dziś Core żadnego z nich nie używa (grep po `src/MediaStation.Core`: 0 trafień), więc stan jest czysty, ale nic go nie pilnuje.
- **Disposition:** *fix* w 1.6, przez dopisanie wpisów.

**F6. Brak `id?` w rekordzie koperty (spec reconciliation).** CONFIRMED.
- Architektura (`architecture-mediastation.md:385`) definiuje kopertę `{ v, type, id?, payload }`, a `Envelope<T>(V, Type, Payload)` (`Envelope.cs:199`) nie ma `id`. Odczyt ignoruje `id`, co jest decyzją z triage 1.1 #3.
- **Disposition:** *accept* do pierwszej story z request/response, czyli protokołu strony w epic-browserhost. Tam `id` trzeba dodać.

**F7. Awaria startu WebView2 kończy proces kodem 0, styk 1.1 ↔ 1.5 (spec reconciliation).** CONFIRMED.
- `ShellWindow.xaml.cs:61-68` w `catch` loguje błąd, pokazuje MessageBox i woła `Close()`. Przy `ShutdownMode="OnMainWindowClose"` proces kończy się kodem `0`, a `stop` loguje `exitCode: 0`.
- Słownik `ExitCode` z 1.5 (`ExitCode.cs:4`: „jeden słownik dla autora, smoke i przyszłego `--supervise`”) nie ma kodu na awarię hosta.
- Zgłosiły to adversarial i edge-case, potwierdza to też moja analiza.
- **Disposition:** *fix* w 1.6. Dodać `ShellStartFailed = 5` i zamiast `Close()` wołać `Application.Shutdown((int)ExitCode.ShellStartFailed)`.

### Diff-scope review (bmad-review)

**F8. Współbieżny append do logu gubi i psuje linie, styk 1.3 ↔ 1.5.** CONFIRMED (pomiar). Zgłosiły: verification-gap, adversarial.
- **Mechanizm:** `FileLogSink.Append` (`FileLogSink.cs:29-31`) otwiera plik z `FileMode.Append` i `FileShare.ReadWrite`. To nie jest atomowy append między procesami.
- **Pomiar subagenta:** odtworzony w `scratchpad\appendtest\Program.cs` dokładnie tym wzorcem. Dwa procesy po 5000 linii dały 9125 linii, z czego 19 uszkodzonych (przeliczyłem ponownie).
- **Historia:** triage 1.3 #2 odrzucił to ryzyko z warunkiem „do rozważenia, gdy `--supervise` zacznie logować równolegle”. Story 1.5 spełniło ten warunek wcześniej: każda odrzucona instancja pisze `start`, `instanceRejected` i `stop` do tego samego pliku (`CompositionRoot.cs:45-53, 67`).
- **Realnie** kolizja wymaga nakładających się zapisów. Odrzucona instancja pisze 3 linie, więc jest rzadka, ale uszkodzona linia psuje parsowanie JSON Lines.
- **Disposition:** *fix* w 1.6 (`FileSystemRights.AppendData`) albo *defer* do epic-deploy-resilience. Decyzja autora.
- **Lekcja:** P3.

**F9. `ShellBridge` po cichu gubi wiadomości i nie ma loggera, styk 1.1 ↔ 1.3.** CONFIRMED. Zgłosiły wszystkie trzy lenses.
- Most powstał w tracerze, przed loggerem. `ShellBridge.cs:39-49` robi `return` bez logu przy `JsonException`, innym `v` i nieznanym `type`. Po stronie web jest tylko `console.warn` (`bridge.ts:55`), a DevTools są w Release wyłączone.
- Dryf handshake'u `ready` daje pusty ekran ground bez śladu. Taki cichy drop był już przyczyną błędu w triage 1.1 #3.
- **Disposition:** *fix* w 1.6. Wstrzyknąć `ILog` i dodać wpis `bridgeMessageRejected` z `v`/`type`/powodem.

**F10. WebView2 bez obsługi `NavigationCompleted`, `ProcessFailed` i `NewWindowRequested`.** CONFIRMED (odczyt `ShellWindow.xaml.cs:36-60`). Zgłosiły: adversarial, edge-case.
- Bez nich nic nie ląduje w logu, gdy Debug startuje bez `npm run dev`, gdy brakuje `shell/index.html` albo gdy padnie renderer. `window.open` otworzyłby okno Edge nad kioskiem.
- **Disposition:** *fix* w 1.6: logowanie plus `Handled = true` dla nowych okien.

**F11. `ShellBridge` nie sprawdza `e.Source`, a nawigacja nie jest ograniczona.** CONFIRMED. Zgłosiły: adversarial, edge-case.
- Triage 1.1 #6 odrzucił to z adnotacją „guard należy do story wprowadzającej Intents”, ale żaden ticket tego nie niesie.
- **Disposition:** *defer* do epic-home-screen, do pierwszej story z Intents przez most. Do zapisania w jej entry (A9).

**F12. `ShellBridge.Publish` bez marshallingu na wątek UI, a `rev` jest stałą wywołującego.** PLAUSIBLE.
- `ShellBridge.cs:28-32, 57-60`, `CompositionRoot.cs:100` (`Rev: 1`). `CoreWebView2` ma thread affinity, więc publish z wątku Orchestratora rzuci wyjątek.
- **Disposition:** *defer* do epic-orchestrator-lifecycle albo epic-home-screen, do pierwszej story publikującej snapshot z Orchestratora (A9).

**F13. `new Mutex(false, name)` może rzucić przed `WaitOne`.** PLAUSIBLE. Zgłosiły: adversarial, edge-case.
- `SingleInstance.cs:26` łapie tylko `AbandonedMutexException`. `UnauthorizedAccessException` (instancja elevated obok nie-elevated) albo `WaitHandleCannotBeOpenedException` skończy się crashem `0xE0434352` zamiast kodu 3.
- Na UM760 jest jeden użytkownik bez elevacji, więc ryzyko jest niskie.
- **Disposition:** *fix* w 1.6, tani guard.

**F14. Nieobsłużony wyjątek UI loguje się dwa razy.** PLAUSIBLE (nie wywołane).
- `DispatcherUnhandledException` bez `Handled` (`CompositionRoot.cs:139`), potem ten sam wyjątek w `AppDomain.UnhandledException` (l.127-136).
- **Disposition:** *fix* w 1.6, niski priorytet.

**F15. `JsonLinesLog.Format` wywołuje `error.Message` i `error.ToString()` przed `try`.** CONFIRMED (odczyt `JsonLinesLog.cs`, metoda `Format`). Zgłosiły: adversarial, edge-case.
- Wyjątek z rzucającym `Message`/`ToString` łamie kontrakt „implementacja nigdy nie rzuca” (`ILog.cs:17`), i to w handlerze unhandled exception.
- **Disposition:** *fix* w 1.6.

**F16. `App.tsx` nie ma sprawdzenia exhaustiveness w `switch`.** CONFIRMED.
- `web/shell/src/App.tsx:17-22`, a `web/tsconfig.json` nie ma `noImplicitReturns`. Nowy `ShellScreen` skompiluje się i da pusty ekran.
- **Disposition:** *fix* w 1.6: `default` z `never`.

**F17. Lista błędów configu ucina się bez informacji.** CONFIRMED.
- `.config-error__content { overflow: hidden }` (`web/shell/src/tokens.css:133-140`) na stałej scenie 1080 px, bez wskaźnika „i jeszcze K”.
- **Disposition:** *defer* do epic-home-screen (UX ekranu błędu).

**F18. Luki reguł semantycznych configu.** CONFIRMED (odczyt `CheckApps`/`CheckSettings`). Zgłosiły: adversarial, edge-case.
- `service` nie musi być kebab-case, mimo że to segment ścieżki `web/page/drivers/<service>`.
- `title`, `icon`, `exe`, `host` i `app` mogą być puste lub z samych spacji.
- `update.repo` przyjmuje `owner/..`.
- Czasy nie mają górnej granicy.
- **Disposition:** *defer* do konsumentów: `service` do epic-netflix-dpad lub epic-browserhost, `exe`/`host`/`app` do epic-moonlight-native, `repo` i limity do epic-deploy-resilience (A9).

**F19. Target `BuildWeb` w Debug nie robi typecheck.** CONFIRMED (`MediaStation.App.csproj:46-50`).
- Zmiana kontraktu łamiąca TS buduje się zielono w Debug i pada dopiero w Release.
- **Disposition:** *fix* w 1.6. Warunkowy `npm ci` został już odrzucony w triage 1.1 #9, więc *accept*. `SkipWebBuild=true` to świadomy flag dewelopera, też *accept*.

**F20. Luki weryfikacji z decyzji „bez App.Tests”.** CONFIRMED (verification-gap, grep `tests/`). Czysta logika żyje w App i nic jej nie uruchamia:
- dispatch startu: parse → `Invalid` przed mutexem → mutex → `Run`/`Unsupported` (`CompositionRoot.cs:55-79`);
- mapowanie `ConfigResult` → `ShellSnapshot` (`CompositionRoot.cs:98-102`);
- mapowanie wyjątków I/O na `ConfigFileState` (`ConfigFileReader.cs:24-35`);
- wartości liczbowe `ExitCode` (jawne w `ExitCode.cs`, ale żaden test ich nie przypina).

**Disposition:** *fix* w 1.6. Wydzielić czystą decyzję startu, fabrykę snapshotu i mapowanie wyjątku do Core i dodać testy. To refactor, zgodny z zakresem 1.6.

**F21. Handshake `ready` jest budowany ręcznie po stronie web, a web nie ma testów.** CONFIRMED.
- `bridge.ts:9-13, 69` ma własny interfejs `Envelope`. Test C# parsuje skopiowany literał (`ShellSnapshotContractTests.cs:52-70`).
- **Disposition:** *defer*. vitest wchodzi z driverami (architektura l.311). Do tego czasu F9 wystarcza, żeby dryf był widoczny w logu.

**F22. Klasyfikacja błędów configu po angielskich komunikatach STJ.** CONFIRMED.
- `ConfigValidation.cs:723-797`. Testy przypinają każdą klasę (plan 1.4, Design Notes), a publish self-contained przypina runtime.
- **Disposition:** *accept*.

**F23. `Action` i `InputMode` kolidują z typami BCL/WPF.** CONFIRMED w części.
- Test używa aliasu (`CoreMessageContractTests.cs:4`). App dziś nie używa `Action` (grep: 0 trafień).
- Triage 1.2 #2 odrzucił zmianę, bo nazwę narzuca zamrożony intent i Consistency Conventions.
- **Disposition:** *defer* jako pytanie do autora przed epic-input (Open questions).

**F24. Mutex `Local\` zamiast `Global\`.** Decyzja z planu 1.5 (Design Notes). **Disposition:** *accept*.

**F25. Nazwy `snapshot`/`ready` mogą kolidować z `MessageTypes`.** CONFIRMED (`MessageTypes.cs:252-262` nie wyklucza `Envelope.SnapshotType`/`ReadyType`). Dziś kolizji nie ma. **Disposition:** *defer*, niski priorytet. Guard warto dodać razem z pierwszą story, która dopisze typ do rejestru.

**F26. Ścieżka `%LOCALAPPDATA%\MediaStation\config` zakodowana na sztywno w copy ekranu błędu.** CONFIRMED (`ConfigErrorScreen.tsx:12`). Root danych jest jeden i stały (AD-13). **Disposition:** *accept*.

**Odrzucone (źródło się nie utrzymało):**
- `null` jako wartość `ConfigFileState` w słowniku: słownik buduje wyłącznie `ConfigFileReader.ReadAll` i nigdy nie wstawia null.
- Puste `Environment.GetFolderPath(LocalApplicationData)`: brak dowodu, że zdarza się na docelowym UM760 (zadanie „at logon” w sesji użytkownika).

### Lekcje procesowe (session logs i plany)

**P1. Testy HITL na UM760 nie miały spisanej procedury.**
- Autor pytał o nią dwa razy: „Jak to przetestować na UM760? Gdzie są opisane te weryfikacje?” (sesja `worktrees-…-story-1-4/3d92a356…`, 2026-10-20T10:02Z) i „Gdzie jest opisany test do wykonania zgodnie z pkt. 4?” (`912d7732…`, 08:59Z).
- Przykładowe configi testowe trafiły do nieśledzonego pliku na prośbę autora (`3d92a356…`, 10:06Z).
- Plany mają w „Manual checks” jedną linię „UM760 (HITL, autor): …” (np. plan 1.4 l.128).

**P2. Wynik HITL nie trafia do planu.**
- Commity close (`c228ec4`, `318f704`, `7567ec4`, `66aace8`) zmieniają tylko `status` (1 linia).
- Plany 1.3 (l.315), 1.4 (l.91) i 1.5 (l.206) dalej mówią „UM760 (HITL) nie sprawdzone / czeka na autora”. Jedynym dowodem testu na UM760 są subject commitu i wypowiedzi autora w sesjach.

**P3. Odłożone findings z warunkiem nie przechodzą między stories.**
- Triage 1.3 #2 (append, warunek `--supervise`) aktywowało się w 1.5 bez re-triage (F8).
- Pobłażliwość opcji znaleziona w 1.4 w bazie dziedziczonej z 1.2 nie wróciła do `ContractJson` (F1).
- Triage 1.1 #6 („guard należy do story wprowadzającej Intents”) nie ma ticketu (F11).

**P4. Głębokość review.**
- Wszystkie pięć planów ma `review: 'quick'`, `review_source: 'pinned'` i `lenses_ran: ['quick']`, także medium-risk 1.1, 1.2 i 1.4.
- Quick znalazł realne defekty w 4 z 5 stories (triage: 1.1 – 4 patche, 1.2 – 1, 1.3 – 1, 1.4 – 5).
- Trzy lenses na poziomie epiki dodały CONFIRMED defekty, których quick nie widział, przede wszystkim na stykach: F1, F7, F9, F10, F15, F20.

**P5. Wins.**
- Tracer bullet z 1.1 dał gotowy pipeline: schema → TS, most i ekran błędu. Stories 1.2 i 1.4 rozszerzyły go bez zmiany kształtu (Code Map 1.2: „bez zmian” dla `BuildWeb`; 1.4: „generują się automatycznie”).
- Lane'y 1.2 ∥ 1.3 w osobnych worktree przeszły rebase bez konfliktu (`145b3db` → `65b99d7`).
- Review loop w 1.4 złapał bad_plan, zanim trafił do mastera (Plan Change Log 1.4).
- Każda story zamknięta po HITL tego samego dnia.

## Behavior verification

Wykonane 2026-10-20 na maszynie deweloperskiej, na HEAD `66aace8`:

- `dotnet test`: 224/224 zielone.
- `dotnet build -c Release`: sukces, 0 ostrzeżeń.
- Release `MediaStation.exe` w trybach bez okna (log `%LOCALAPPDATA%\MediaStation\logs\mediastation-2026-10-20.jsonl`, 11:55:58Z–11:55:59Z):

| Argumenty | Kod wyjścia | Linie logu |
|-----------|-------------|------------|
| `--smoke netflix` | 4 | `start` → `modeUnsupported {mode: smoke, appId: netflix}` → `stop {exitCode: 4}` |
| `--calibrate-input` | 4 | `modeUnsupported {mode: calibrateInput}` → `stop {exitCode: 4}` |
| `--foo` | 2 | `invalidArguments „nieznany argument: --foo”` → `stop {exitCode: 2}` |
| `--SMOKE netflix` | 2 | `invalidArguments „nieznany argument: --SMOKE”` |
| `--smoke` | 2 | `invalidArguments „brak appId po --smoke”` |

- Probe Core (file-based app na `MediaStation.Core`):
  - `null` w polach wszystkich trzech plików configu (`apps`, `id`, `title`, `devices`, `keys`, `gamepadHome`, `update`, `update.repo`, `signature`) daje `ConfigError` z poprawną ścieżką i bez crasha. **Sprawdzone i czyste.**
  - Koperta (`ContractJson.DeserializeEnvelope<Action>`) przyjmuje `"kind":"up, down"` jako `Down`, a także `"UP"` i `"Kind"`. Patrz finding F1.
- **Nie wykonane tutaj:** tryb główny z oknem shella, druga instancja przy działającej głównej i ekran błędu configu. Nie uruchamiałem pełnoekranowego okna na maszynie dev. Te ścieżki mają tylko dowody pośrednie: notatki weryfikacji lokalnej w planach (1.4 `Implementation Notes`, 1.5 `Implementation Notes`) i deklaracje autora o teście na UM760 (sesje `912d7732` 07:08Z i 09:04Z, `3d92a356` 10:19Z, commity `c228ec4`, `318f704`, `7567ec4`, `66aace8`).

## Previous-retro follow-through

Nie było czego sprawdzać: **plik poprzedniego retro nie istnieje**. To pierwsza epika inicjatywy (`epics` order: id 1), więc brak pliku nie znaczy, że nie ma zaległych action items.

## Action items

Wszystkie są **proponowane, nie zastosowane**. Retro nie zmienia kodu, ticketów ani specu.

### Remediation (kod), zakres startowy dla 1.6 Refactor sweep

| # | Co | Findings | Owner | Uwagi |
|---|----|----------|-------|-------|
| A1 | Ścisły odczyt koperty: `StrictEnumConverter` i `PropertyNameCaseInsensitive = false` w `DeserializeEnvelope`; konwerter wydzielony z `ConfigValidation.cs` do wspólnego pliku; testy `"up, down"`, `"UP"`, `"Kind"` → `JsonException` | F1, F3 | 1.6 (`bmad-build`) | zmienia zachowanie (patrz Q1) |
| A2 | `ExitCode.ShellStartFailed = 5` i `Application.Shutdown(kod)` w `catch` `ShellWindow.OnLoaded` | F7 | 1.6 | zmienia zachowanie (Q1) |
| A3 | `ILog` w `ShellBridge` (wpis `bridgeMessageRejected`) oraz obsługa `NavigationCompleted`, `ProcessFailed`, `NewWindowRequested` z logiem | F9, F10 | 1.6 | Q1 |
| A4 | Atomowy append w `FileLogSink` (`FileSystemRights.AppendData`) i test lub skrypt z dwoma procesami | F8 | 1.6 | decyzja autora 2026-10-20 (Q2) |
| A5 | Czysta decyzja startu, fabryka `ShellSnapshot` z `ConfigResult` i mapowanie wyjątku I/O na `ConfigFileState` wydzielone do Core, z testami; test przypinający wartości `ExitCode` | F20 | 1.6 | refactor, w zakresie 1.6 |
| A6 | Drobne utwardzenia: guard konstruktora `Mutex` (F13), pojedynczy wpis unhandled (F14), `LogError` w `try` (F15), `default: never` w `App.tsx` (F16), `npm run typecheck` w Debug (F19) | F13–F16, F19 | 1.6 | |
| A7 | Dopisać do `BannedSymbols.txt`: `DateTime.Now`/`UtcNow`, `System.Diagnostics.Process`, `N:System.Net`, nazwane `Mutex`/`Semaphore`/`EventWaitHandle`, `Environment.GetFolderPath`; poprawić doc `ConfigJson` o równoważności ze schematem (opcjonalnie przenieść eksport schematów configu do `Config`) | F4, F5 | 1.6 | |

### Odłożone do kolejnych epik (do wpisania w entries przez `bmad-ticket`)

| # | Co | Findings | Owner |
|---|----|----------|-------|
| A8 | Zapisać odłożone findings z warunkiem w entries konsumentów: guard `e.Source` i blokada nawigacji (epic-home-screen, pierwsza story z Intents); marshalling `Publish` i `rev` z mostu (pierwsza story publikująca snapshot z Orchestratora); `id?` w kopercie (epic-browserhost, protokół strony); reguła `appId` wspólna z `CliArgs` (epic-deploy-resilience, `--smoke`); reguły `service`/pól pustych/`repo`/górnych granic (epic-netflix-dpad, epic-moonlight-native, epic-deploy-resilience); ucięta lista błędów (epic-home-screen); guard nazw `snapshot`/`ready` w `MessageTypes` | F2, F6, F11, F12, F17, F18, F25 | autor przez `bmad-ticket` |

### Lekcje procesowe

| # | Co | Lekcja | Owner |
|---|----|--------|-------|
| A9 | Każda story `hitl = true` ma w „Manual checks” krok po kroku skrypt dla UM760 (komendy, gdzie położyć pliki, czego oczekiwać). Przykładowe configi testowe trafiają do repo, np. `tools/samples/config/`, a nie do pliku nieśledzonego | P1 | autor (`bmad-customize` dla `bmad-build` albo zasada w AGENTS.md) |
| A10 | Zamknięcie story po HITL dopisuje do sekcji Verification planu datowaną linię z wynikiem z UM760, a nie zmienia tylko `status` | P2 | autor i `bmad-build` |
| A11 | Rejected/deferred findings z warunkiem („gdy X”) trafiają do listy w epic Notes albo do entry ticketu, który spełni warunek. Gdy review znajduje defekt w bazie dziedziczonej (np. `ContractJson.Options`), sprawdza też pozostałych konsumentów | P3 | autor (konwencja triage w `bmad-build`) |
| A12 | Dla `risk = "medium"` review w pełnym zestawie lenses (adversarial, edge-case, verification-gap) zamiast przypiętego `quick`, albo co najmniej jedno review epiki przed refactor sweep | P4 | autor (decyzja o `review_source: pinned`) |

## Acceptance verdict

**Machine verdict: rejected.** Kryteria: **declared** (Done when z epic file).

**Powód: niedokończony ticket `1.6` (Refactor sweep, `state: planned`).** Według reguły unfinished tickets ten jeden wystarcza do `rejected`, niezależnie od kryteriów.

Kryteria Done when wobec dowodów:

| # | Kryterium | Stan | Dowód |
|---|-----------|------|-------|
| 1 | Testy Core przechodzą; Core bez Win32/WebView2/WPF/systemu plików (AD-3) | spełnione | `dotnet test` 224/224 (2026-10-20); `CoreBoundaryTests.cs`, `BannedSymbols.txt` + `TreatWarningsAsErrors`; luki listy w F5 dotyczą AD-14 i obiektów OS, nie zakresu AD-3 z kryterium |
| 2 | Typy TS z JSON Schema Core w buildzie web, nikt ich nie edytuje (AD-16) | spełnione | `BuildWeb` (`MediaStation.App.csproj`), `contracts.mjs` z bannerem „nie edytuj ręcznie”, `generated/` w `.gitignore`; Release build 0 ostrzeżeń |
| 3 | Druga instancja, także `--smoke`, nie startuje (AD-2) | spełnione (dowód pośredni) | plan 1.5 Implementation Notes (kod 3, `instanceRejected` `run`/`smoke`); HITL autora (`66aace8`, sesja `912d7732` 11:48Z); w tym retro sprawdzona tylko ścieżka bez działającej instancji |
| 4 | Na UM760 błędny config daje ekran błędu zamiast crasha (AD-11) | spełnione (dowód pośredni) | testy `ConfigValidationTests`; plan 1.4 l.91; autor „Przetestowane na UM760” (`3d92a356` 10:19Z, `7567ec4`); probe null w tym retro |
| 5 | `dotnet publish` na UM760 pokazuje shell z plików lokalnych i loguje JSON Lines w UTC z rotacją dzienną (AD-13) | spełnione | HITL 1.1 i 1.3 (`c228ec4`, `318f704`, sesja `912d7732` 07:08Z i 09:04Z); rotacja przez `JsonLinesLogTests` z `FakeTimeProvider`; linie `ts` z `Z` w logu z tego retro |

Bez 1.6 epika kwalifikowałaby się na **accepted-with-open-items**: kryteria są spełnione, a otwarte findings (F1, F7–F10, F20) nie blokują Done when i mają adresy w A1–A8.

**Human decision (2026-10-20): autor zostawia `rejected`.** Epika jest **not accepted**, dopóki 1.6 się nie skończy. Po 1.6 retro można wznowić: ten plik zostaje i zostanie uzgodniony z nowymi dowodami.

## Open questions

- **Q1. Zakres 1.6. Rozstrzygnięte 2026-10-20:** autor rozszerza 1.6 o A1–A7. Entry 1.6 („tylko cleanup”) trzeba zaktualizować przy starcie przez `bmad-ticket`, bo retro nie edytuje ticketów.
- **Q2. F8. Rozstrzygnięte 2026-10-20:** naprawa w 1.6 (A4, `FileSystemRights.AppendData`).
- **Q3. F23 (`Action`, `InputMode`).** Zostawić nazwy z udokumentowanym `global using` w App, czy zmienić je teraz, zanim powstaną konsumenci? Zmiana nazwy zmienia `type` w kopercie i nazwy plików TS, więc wymaga renegocjacji zamrożonego intentu 1.2 i Consistency Conventions.
- **Q4. Override verdictu. Rozstrzygnięte 2026-10-20:** bez override, `rejected` do czasu ukończenia 1.6.
- **Niesprawdzone w tym retro:** tryb główny z oknem, druga instancja przy działającej głównej i ekran błędu na UM760. Opieram się na dowodach pośrednich (Behavior verification).
