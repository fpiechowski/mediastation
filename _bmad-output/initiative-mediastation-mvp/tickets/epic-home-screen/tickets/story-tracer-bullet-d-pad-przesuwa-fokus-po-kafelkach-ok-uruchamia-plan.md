---
title: 'Tracer bullet: D-pad przesuwa fokus po kafelkach, OK uruchamia aplikację'
type: 'feature'
ticket: '3'
created: '2026-10-26'
status: done
baseline_revision: '00c2c1d2d36c0489e5855d45f9ec5bee6374d886'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: [blind-hunter, edge-case-hunter, verification-gap, intent-alignment]
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md'
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** Shell nie dostaje wejścia: Orchestrator obsługuje z Actions tylko Home, most przyjmuje wyłącznie `ready` (bez guardu origin), a UI nie ma kafelków ani fokusu. Nie da się wybrać aplikacji pilotem.

**Approach:** Cienka ścieżka przez wszystkie warstwy wg AD-8/AD-16: Orchestrator pamięta tryb i oddaje Actions dla Shell obok snapshotu; `ShellBridge` (logika poza WebView2) dostarcza je jedną kolejką FIFO ze snapshotem i przyjmuje z Shell tylko `ready`/Intents z origin shella; Shell renderuje jeden rząd kafelków z fokusem po `appId` (logika bez DOM, Vitest) i wysyła `launchApp`.

## Boundaries & Constraints

**Always:** Tylko Orchestrator mutuje stan i tryb (AD-3); Shell konsumuje wyłącznie Actions z mostu (nie surowe klawisze), wskaźnik nie przenosi fokusu; fokus kluczowany `appId`, nigdy indeksem; typy TS wyłącznie z `web/contracts/generated` (poza kopertą w `bridge.ts`); Core bez UI/I/O; logika walidacji i kolejki mostu bez typów WebView2/WPF/Dispatcher, żeby dała się linkować do `MediaStation.Core.Tests` (Consistency Conventions); `TreatWarningsAsErrors`; komentarze i nazwy po polsku jak w sąsiednim kodzie.

**Never:** Zakładki, chip/linia stanu, ikony, `type` w snapshocie, AppMenu, empty state, header, zegar (stories 4.4–4.10); blokada nawigacji/ramek, CSP, `ProcessFailed`/`OperationCanceled`, testy `ShellWindow` (4.11); `intentIgnored` dla `RestartApp`/`CloseApp` (4.8); ręczna edycja `web/contracts/generated`; osobny projekt testów App; nowy Intent `OpenAppMenu`; buforowanie Actions w moście.

## I/O & Edge-Case Matrix

| Scenariusz | Wejście / stan | Oczekiwane | Błąd |
|---|---|---|---|
| Strzałka w trybie `shell` | Action Up/Down/Left/Right `press` lub `repeat` | `Actions=[action]` obok snapshotu/None | — |
| Ok w `shell` | `press` lub `long` | przekazane | — |
| Ok `repeat`, strzałka `long`, `Keyboard` | `shell` | nie przekazane | — |
| Back w `shell` | `press` | przekazane | — |
| D-pad/Ok/Back w `app` lub `osk` | tryb `app`/`osk` | `Actions` puste | — |
| Home | dowolny tryb, po zwykłej obsłudze (komendy/snapshot jak dotąd) | zawsze `Actions=[home]`, także gdy obsługa nic nie zmieniła | — |
| `LaunchApp` z `NotRunning`/`Background`, bez innej aktywnej | — | jak dotąd | — |
| `LaunchApp` w `Starting`/`Foreground`/`Stopping`/`Failed`, przy innej aktywnej, albo nieznany `appId` | — | brak komend i snapshotu, wynik niesie `IgnoredIntent` | pętla loguje `intentIgnored` (warn) |
| Snapshot i `action` tego samego kroku | `Publish(s)` potem `PublishAction(a)` | w Drain: snapshot, potem `action` | — |
| `action` przed `ready` | brak `ready` od ostatniej nawigacji | odrzucona, brak buforowania | log `shellActionDropped` (reason `notReady`) |
| `action` po nawigacji strony | `NavigationStarting` po `ready` | odrzucona do kolejnego `ready` | `shellActionDropped` (`notReady`) |
| `action` starsza niż 1000 ms | `now - tsMono > 1000` w chwili wysyłki | odrzucona | `shellActionDropped` (`stale`) |
| `ready` z origin shella | `e.Source` w origin | `Ready`: odsyła najnowszy snapshot, ustawia `ready` | — |
| Intent `launchApp`/`restartApp`/`closeApp` z origin shella | poprawny `{appId}` | trafia do kolejki Orchestratora | — |
| Wiadomość z obcego origin lub nieczytelnym `Source` | dowolny `type` | odrzucona | `bridgeMessageRejected` |
| `snapshot`/`action`/nieznany `type` od Shell, inne `v`, zły JSON, pusty `appId`, payload nie-obiekt | — | odrzucona | `bridgeMessageRejected` |
| Fokus w UI | `Left`/`Right` na kafelkach | fokus na sąsiednim `appId`; na krańcu zostaje | — |
| OK w UI | kafelek `notRunning`/`background` | Intent `launchApp{appId}`; inny stan: nic | — |
| Nowe `focusRequest.seq` | `default` / `<appId>` | fokus na pierwszym kafelku / kafelku `appId`, raz na `seq` | nieznany `appId`: fokus zostaje |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` -- `Handle`: gałąź `ActionMessage action => OnHome(action)`; `Snapshot()` buduje `AppSnapshot`; `AppState`; `OrchestratorResult(Commands, Snapshot)` (record; `WithManagedWindows` robi `result with {…}`, więc nowe pola przechodzą).
- `Orchestrator.Home.cs` -- `OnHome` (zwraca None dla Kind≠Home); tu dojdzie `OnAction` (tryb + Home zawsze).
- `Orchestrator.Launch.cs` -- `OnLaunchApp`: miejsca zwrotu `None` → `IgnoredIntent`.
- Routing `SetInputRouting` powstaje w: `Startup.cs` (2×, shell), `Home.cs`, `Failures.cs`(EnterFailed, shell), `Attach.cs` (App), `Launch.cs` (`OnResume`, App) — tryb ma być zapamiętywany w jednym helperze `Route(…)` przy każdej takiej komendzie.
- `src/MediaStation.Core/Contracts/ShellSnapshot.cs` -- `AppSnapshot` dostaje `Title` (4. pole); `Config/ShellSnapshotFactory.cs` (bootstrap) i `Orchestrator.Snapshot()` go wypełniają z `AppEntry.Title`.
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` -- ctor `(orchestrator, dispatch, publish, log)`; `Run`: dispatch komend → publish snapshotu; dochodzi publish Actions po snapshocie i log `intentIgnored`. Linkowany do testów (`OrchestratorLoopTests`, ~9 wywołań ctor).
- `src/MediaStation.App/Shell/ShellBridge.cs` -- jedyna klasa z `CoreWebView2`; `LatestSnapshotSlot` (Core) zostaje dla snapshotu. Wydzielić: `ShellMessageGate` (walidacja przychodzących) i `ShellOutbox` (kolejka FIFO + warunki odrzucenia), oba bez WebView2/WPF, linkowane do `MediaStation.Core.Tests.csproj`.
- `src/MediaStation.App/Shell/ShellWindow.xaml.cs` -- `ShellUri` pod `#if SHELL_DEV_SERVER` (Debug `http://localhost:5173/`, Release `https://shell.mediastation.example/index.html`); origin trzeba wystawić dla mostu (nowy `ShellOrigin`), reszta okna bez zmian.
- `src/MediaStation.App/CompositionRoot.cs` -- `StartRun`: `new ShellBridge(snapshot, log, app.Dispatcher)` przed `OrchestratorLoop? postsTo`; loop dostaje `bridge.Publish`. Bridge potrzebuje `postIntent` (leniwie przez `postsTo`) i `bridge.PublishAction` dla pętli. Zegar monotoniczny `tsMono`: `InputHub`/hook liczy z `GetMessageTime`-like ms (`InputClock`) — progu świeżości nie liczyć z innej skali niż `tsMono`; sprawdzić w `Platform/Input` źródło `TsMono` i użyć tego samego `Func<long>`.
- `src/MediaStation.Core/Contracts/ContractJson.cs` -- `Deserialize<T>` (ścisły), `Serialize`; `Envelope<TPayload>`; `MessageTypes`. Schematy: `ExportSchemas()` (Action, LaunchApp, … już są).
- `web/shell/src/bridge.ts` -- `connectBridge(onSnapshot)`; guard `isAppSnapshot` (dodać `title`); dodać odbiór `action` (guard z typem `Action`) i wysyłkę Intentu.
- `web/shell/src/App.tsx`, `Placeholder.tsx`, `tokens.css` -- ekran `placeholder` ma pokazać rząd kafelków (minimalny CSS z tokenów, widoczny fokus).
- `web/package.json` -- dodać devDependency `vitest` 5.0.4 (obsługuje Vite 8) i skrypt `"test": "vitest run"`; konfiguracja środowiska `node` (bez DOM).
- Testy wzorcowe: `tests/MediaStation.Core.Tests/OrchestratorHomeTests.cs`, `OrchestratorLaunchTests.cs`, `OrchestratorTestSupport.cs`, `OrchestratorLoopTests.cs`, `ShellSnapshotContractTests.cs`, `ShellSnapshotFactoryTests.cs`.
- Nie ruszać: `MissingHostPort` (daje `startWithoutHost` + `CommandFailed` → `Failed`), `CommandRouter`, InputHub, `LatestSnapshotSlot`.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Contracts/ShellSnapshot.cs`, `Config/ShellSnapshotFactory.cs`, `Orchestrator/Orchestrator.cs` -- dodać `Title` do `AppSnapshot` i wypełnić z `apps.json` (`AppState` niesie `Title`); poprawić istniejące testy snapshotu (`ShellSnapshotContractTests`, `ShellSnapshotFactoryTests`, ewentualne inne konstruktory) -- kontrakt widoku dla kafelka
- [x] `src/MediaStation.Core/Orchestrator/Orchestrator.cs` + `Orchestrator.Home.cs` + pliki z `SetInputRouting` -- rozszerzyć `OrchestratorResult` o `Actions` (`IReadOnlyList<Action>`, domyślnie puste) i `IgnoredIntent` (rekord z nazwą Intentu, `appId`, powodem); pole `_mode` (start `Shell`) ustawiane w jednym helperze przy każdym `SetInputRouting`; `Handle(Action)` → `OnAction`: Home obsłużony jak dotąd, potem zawsze dołączona do Actions; w `_mode == Shell` przekazać strzałki (press/repeat), Ok (press/long), Back (press) -- AD-8 „Actions w trybie shell"
- [x] `src/MediaStation.Core/Orchestrator/Orchestrator.Launch.cs` -- `OnLaunchApp` zwraca `IgnoredIntent` (powód: nieznany `appId`, inna aplikacja aktywna, stan inny niż `NotRunning`/`Background`) zamiast gołego `None`; `Background` bez `Hwnd` też -- `intentIgnored`
- [x] `src/MediaStation.App/Shell/ShellMessageGate.cs` (nowy, bez WebView2) -- `Evaluate(string json, string? source)` względem `Uri` origin shella → `Ready` | `IntentAccepted(Intent)` | `Rejected(v, type, reason)`; jawna lista `ready`, `launchApp`, `restartApp`, `closeApp`; origin przez parsowanie `Source` (scheme+host+port, nie prefiks napisu); pusty/null `appId` i payload nie-obiekt odrzucane
- [x] `src/MediaStation.App/Shell/ShellOutbox.cs` (nowy, bez WebView2/Dispatcher) -- `PublishSnapshot` (latest-wins), `PublishAction`, `MarkReady`, `MarkNavigated`, `Drain()` zwracające uporządkowaną listę dostaw (`Snapshot`, `Action`, `Dropped{reason}`); oczekujący snapshot idzie przed każdą `action`; `action` odrzucana, gdy brak `ready` (od startu lub od ostatniej nawigacji) albo `now - TsMono > 1000` (stała w klasie, `Func<long>` jako zegar); bez buforowania
- [x] `src/MediaStation.App/Shell/ShellBridge.cs`, `ShellOrigin.cs` (nowy), `ShellWindow.xaml.cs` -- bridge używa `ShellMessageGate` (z `e.Source`) i `ShellOutbox`; `NavigationStarting` → `MarkNavigated`; loguje `bridgeMessageRejected`, `shellActionDropped` (kind, phase, reason), `snapshotPublished` jak dotąd; Intent do `postIntent`; `PublishAction`; origin z `ShellOrigin` (jedno `#if` na Debug/Release)
- [x] `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` + `CompositionRoot.cs` -- ctor dostaje `Action<Action> publishAction`; po `_publish(snapshot)` publikuje `result.Actions` w kolejności; loguje `intentIgnored` (warn, `intent`, `appId`, `reason`); `StartRun` wiąże `postsTo` przed mostem; `tests/.../OrchestratorLoopTests.cs` dostosować do nowej sygnatury
- [x] `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` -- linki `ShellMessageGate.cs`, `ShellOutbox.cs` (i `ShellOrigin`, jeśli bez zależności od WPF); zaktualizować liczbę plików w „Consistency Conventions → Testy kodu App" w `architecture-mediastation.md` (19 → faktyczna)
- [x] `tests/MediaStation.Core.Tests/OrchestratorActionTests.cs` (nowy), `OrchestratorLaunchTests.cs`, `ShellMessageGateTests.cs`, `ShellOutboxTests.cs`, `OrchestratorLoopTests.cs` -- testy z macierzy I/O: routing Actions wg trybu (shell/app, tryb po Home i `EnterFailed`), Home zawsze, `IgnoredIntent`, kolejność snapshot→action w pętli i w outboxie, `notReady`/`stale`/po nawigacji, obcy origin, `snapshot`/`action`/nieznany typ od Shell, zły `v`, pusty `appId`, Intent dociera do `postIntent`
- [x] `web/package.json` (+ lockfile przez `npm install`) -- `vitest` 5.0.4 i `npm test`
- [x] `web/shell/src/focus.ts` (nowy, bez DOM i Reacta) + `focus.test.ts` -- czysty reduktor: stan `{focusedAppId, appliedSeq}`; `applyFocusRequest(state, snapshot)` (raz na `seq`; `default` → pierwszy kafelek, `<appId>` → ten kafelek); `applyAction(state, apps, action)` → `{state, intent?}`: Left/Right po pozycji `appId` w bieżącej liście (kraniec zostaje), OK `press` na `notRunning`/`background` → Intent `launchApp`; pozostałe Actions bez skutku; test: przesunięcie listy nie zmienia fokusu na `appId`
- [x] `web/shell/src/bridge.ts`, `App.tsx`, `Placeholder.tsx` (lub `TileRow.tsx`), `tokens.css` -- `connectBridge(onSnapshot, onAction)` z guardem `action` (typ `Action` z generated) i `sendIntent`; `App` trzyma stan z `focus.ts` (efemeryczny), renderuje rząd kafelków (tytuł + stan tekstem, jeden widoczny fokus z tokenów), zero handlerów myszy/klawiatury; typy z `contracts/generated`
- [x] weryfikacja na bieżącej maszynie (bez zmian w repo) -- patrz Verification

**Acceptance Criteria:**
- Given `dotnet test MediaStation.slnx` przy zielonym buildzie web, when uruchomione, then wszystkie testy (w tym nowe) przechodzą bez nowych ostrzeżeń.
- Given `npm test` w `web/`, when uruchomione, then testy fokusu przechodzą (Lewo/Prawo po `appId`, OK wysyła `LaunchApp`, `focusRequest` raz na `seq`).
- Given uruchomiony MediaStation na bieżącej maszynie z `apps.json` zawierającym ≥2 aplikacje, when użytkownik naciśnie strzałkę w prawo/lewo na klawiaturze, then widoczny fokus przechodzi na sąsiedni kafelek, a w logu jest `action` i `shellActionDropped` nie występuje dla świeżych klawiszy.
- Given fokus na kafelku `notRunning`, when OK, then w logu jest `startWithoutHost`, a kolejny snapshot ma ten kafelek w `failed`.

## Implementation Notes

- Maszyna: **UM760** (COMPUTERNAME `MEDIASTATION`, płyta `F7BSI`, model `EliteMini Series`).
- Zegar mostu: `TsMono` pochodzi z `InputClock.Normalize` (ms od startu systemu, jak `time` hooka LL i `GetMessageTime`). `CompositionRoot` wstrzykuje `Environment.TickCount64` (ta sama oś), ale `ShellOutbox` liczy wiek modulo 2^32 (`(uint)(now - tsMono)`; wiek >= 2^31 to Action z przyszłości, nie stara), bo hub startuje z `high = 0` i przy uptime > 49,7 dnia jego `TsMono` miałoby inną liczbę obiegów niż `TickCount64`.
- `ShellOrigin` nie jest linkowany do testów (zależy od `SHELL_DEV_SERVER`); `ShellMessageGate` dostaje `Uri` origin w konstruktorze. Do `Core.Tests` doszły 2 linki (`ShellMessageGate.cs`, `ShellOutbox.cs`): 21 plików, zaktualizowane w architekturze.
- `NavigationStarting` (-> `MarkNavigated`) podpina `ShellBridge.Attach`, nie `ShellWindow`.
- Weryfikacja na UM760: `dotnet build MediaStation.slnx` (Debug i Release) 0 błędów / 0 ostrzeżeń; `dotnet test MediaStation.slnx` 1013 zielonych; `npm test` 13 zielonych; `npm run typecheck` i `vite build shell` bez błędów. Uruchomienie Release z klawiaturą wstrzykniętą przez `keybd_event` (Right, Right, Left, Enter) przy `apps.json` z 3 aplikacjami: w logu 4x `action` (`right`,`right`,`left`,`ok`), brak `shellActionDropped`, OK na drugim kafelku (`moonlight-pc`) dało `startWithoutHost` (`appId: moonlight-pc`), `commandFailed` i kolejny snapshot (rev 3). Widocznego obrysu fokusu nie zweryfikowano wizualnie (brak zrzutu ekranu); ścieżka fokusu potwierdzona pośrednio wyborem kafelka przez OK.

## Plan Change Log

## Review Triage Log

### 2026-10-26 — Review pass
- verdicts: 31 findings — high 0, medium 3, low 12, false 8, maybe-false 0 (8 duplicates of other rows counted as low/false per root cause)
- findings:
  - `[medium]` `[patch]` Drain takes the snapshot before the action queue, so action N could go out before snapshot N (blind-hunter claim, edge-case-hunter ShellOutbox.cs ×2) — fixed: actions dequeued first, snapshot taken after, with a comment.
  - `[medium]` `[patch]` No test that resume from Background / SystemResumed set the mode (verification-gap) — fixed: two tests in OrchestratorActionTests (the code already used `Route`; the claim of a code bug was false, the test gap was real).
  - `[medium]` `[patch]` bridge.ts action guards hand-copied and untested; sendIntent untested (blind-hunter, verification-gap) — fixed: Record-typed tables checked by typecheck plus bridge.test.ts.
  - `[low]` `[reject]` TickCount64 vs TsMono scale (blind-hunter) — both are ms since boot; age is computed modulo 2^32 and tested; the UM760 run showed no `shellActionDropped`.
  - `[low]` `[reject]` Log flood from shellActionDropped / bridgeMessageRejected (blind-hunter, edge-case-hunter) — unlikely at the keypad rate; a limiter adds complexity.
  - `[low]` `[reject]` NavigationStarting resets ready for any navigation; an old page's queued `ready` can set it (blind-hunter, edge-case-hunter ×2) — the shell does not navigate after load; navigation blocking is story 4.11.
  - `[low]` `[reject]` Pre-navigation queued actions delivered after ready; duplicate snapshot log on ready (blind-hunter) — actions are at most 1 s old, UI drops older rev; harmless.
  - `[low]` `[reject]` restartApp/closeApp rejections are silent (blind-hunter, edge-case-hunter ×2) — the intent limits `intentIgnored` to LaunchApp; story 4.8.
  - `[low]` `[reject]` Duplicate OK launches, a11y attributes, tile overflow, ShellOrigin test, .gitattributes, CI (blind-hunter) — outside the tracer scope or cosmetic.
  - `[false]` `[reject]` Mode goes stale on ProcessExited/OnStop/other paths (blind-hunter) — every path that leaves Foreground goes through EnterFailed/Home/Resume, all using `Route`.
  - `[false]` `[reject]` Home ignored by UI / Home leaves focus (blind-hunter) — AD-8: Home only closes overlays in Shell; no overlays yet.
  - `[false]` `[reject]` Intent before `postsTo` assigned or after dispose (edge-case-hunter) — StartRun is synchronous on the UI thread before any page message; Post tolerates disposal.
  - `[low]` `[reject]` Send/Post throws inside Drain loop (edge-case-hunter) — same pattern as the previous Flush; dispatcher exception log catches it.
  - `[low]` `[reject]` Loop: publishing Actions skipped if _dispatch throws (edge-case-hunter) — snapshot is skipped too; consistent with existing behaviour.
  - `[false]` `[reject]` Actions in non-placeholder screen (edge-case-hunter) — the Orchestrator does not run on ConfigError, apps is empty.
  - `[low]` `[reject]` ShellBridge glue (Ready→MarkReady, intent → postIntent, NavigationStarting) untested (verification-gap, intent-alignment) — needs a new seam; covered end-to-end by the UM760 live run (`action` ×4, `startWithoutHost`).
  - `[low]` `[reject]` IgnoredIntent reason text asserted for one reason only; Release origin not linked into tests (verification-gap) — cosmetic.
  - `[false]` `[reject]` intent-alignment divergence notes (descriptive): live-run criterion is evidenced in Implementation Notes; remaining glue as above.

## Design Notes

- Dostarczanie: `ShellOutbox.Drain()` wywoływany z jednego `Dispatcher.BeginInvoke` (jak dotąd `Flush`); `Publish*` z wątku Orchestratora tylko pod lockiem i zwraca „zaplanuj flush". Snapshot oczekujący jest wysyłany bezpośrednio przed każdą `action` (albo raz na początku Drain), więc Action jest oceniana na stanie z kroku lub nowszym.
- Świeżość: próg 1000 ms liczony względem tej samej skali co `Action.TsMono`; zegar do outboxa wstrzykuje CompositionRoot (nie `Environment.TickCount64`, jeśli `TsMono` ma inne źródło) — implementator potwierdza źródło w `Platform/Input` i opisuje w Implementation Notes.
- `intentIgnored` loguje pętla (Core nie ma loga): `OrchestratorResult.IgnoredIntent`.
- Tryb domyślny `Shell`: `OrchestratorStarted` i tak wysyła routing `shell`.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx` -- expected: 0 błędów, 0 ostrzeżeń (buduje też web)
- `dotnet test MediaStation.slnx` -- expected: 0 niepowodzeń
- `npm test` (w `web/`) -- expected: testy przechodzą
- `npm run build` (w `web/`) -- expected: typecheck i build bez błędów

**Manual checks (if no CLI):**
- Na bieżącej maszynie: uruchomić MediaStation (Release z `shell/`), strzałki klawiatury przesuwają fokus, OK na kafelku → w `%LOCALAPPDATA%\MediaStation\logs` wpis `startWithoutHost`, kafelek w `failed`; zapisać w Implementation Notes razem z nazwą maszyny.

## Auto Run Result

Machine: ran on UM760 (host MEDIASTATION, board F7BSI, model EliteMini Series).

**Summary:** Tracer bullet through all layers. Orchestrator remembers the input mode and returns Actions for Shell next to the snapshot (Home always); `LaunchApp` rejections return `IgnoredIntent` (logged `intentIgnored` by the loop). `ShellOutbox` (FIFO with snapshot-first, notReady/stale drops) and `ShellMessageGate` (explicit list, origin from `e.Source`) are WebView2-free and linked into tests. Shell renders a row of tiles with appId-keyed focus (`focus.ts`, Vitest).

**Files changed:** Core: `ShellSnapshot.cs`, `ShellSnapshotFactory.cs`, `Orchestrator*.cs` (title, mode, Actions, IgnoredIntent). App: `ShellOutbox.cs`, `ShellMessageGate.cs`, `ShellOrigin.cs` (new), `ShellBridge.cs`, `ShellWindow.xaml.cs`, `OrchestratorLoop.cs`, `CompositionRoot.cs`. Web: `focus.ts`, `bridge.ts`, `App.tsx`, `Placeholder.tsx`, `tokens.css`, `vitest.config.ts`, `package.json`. Tests: new `OrchestratorActionTests`, `ShellMessageGateTests`, `ShellOutboxTests`, `focus.test.ts`, `bridge.test.ts`; updated loop/launch/snapshot tests. Architecture doc: linked-file count.

**Review:** 3 patches applied (all medium): Drain ordering, resume/wake mode tests, bridge guard types and tests. Deferred: none. Rejected findings and reasons are in the triage log.

**Verification (UM760):** `dotnet build MediaStation.slnx` 0 errors / 0 warnings; `dotnet test` 1015 passed, 0 failed; `npm test` 15 passed; `npm run build` ok; live Release run with injected keys: 4× `action`, no `shellActionDropped`, OK → `startWithoutHost` and tile `failed`.

**Residual risks:** focus ring not checked visually (no screenshot); `ShellBridge` glue covered only by the live run; physical remote and TV checks fall to the user's whole-epic acceptance on UM760.

Follow-up review recommended: false (no high patched; 3 medium patched, each with a test).
