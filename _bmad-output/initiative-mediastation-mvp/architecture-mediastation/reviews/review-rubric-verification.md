---
title: 'Review: architecture spine MediaStation MVP (rubric + verification)'
target: ../architecture-mediastation.md
decision-log: ../.memlog.md
date: 2026-10-16
lenses: [rubric-walker, verification]
---

# Review: Architecture Spine MediaStation MVP

**Werdykt:** spine jest spójny i dobrze zakotwiczony w spike'u, ale przed slicingiem epik trzeba domknąć kilka luk: niezweryfikowany wybór TypeScript 7 (koliduje z typescript-eslint), restart po crashu przez Task Scheduler (zawodny), kontrakt wątków InputHub ↔ Orchestrator (hook LL) oraz kilka pozycji envelope operacyjnego.

## Podsumowanie findings

| # | Severity | Lens | Finding | Sugerowany fix |
| --- | --- | --- | --- | --- |
| F1 | high | verification | TypeScript 7.0.2 nie jest wspierany przez typescript-eslint (peer `typescript >=4.8.4 <6.1.0`); TS 7 nie ma stabilnego JS API | Pin TS 6.0.3 albo TS 7 tylko do `tsc --noEmit` + jawna decyzja o lincie |
| F2 | high | rubric (ops) + verification | „Restart po awarii” przez Task Scheduler nie łapie crasha po starcie procesu | Supervisor/watchdog albo reality-check w pierwszej story deploy |
| F3 | high | rubric (divergence) | Brak reguły, jak hook LL zna tryb wejścia bez blokowania na Orchestratorze; brak reguły dla wejścia wstrzykniętego (`SendInput`) | Dopisać do AD-8: lokalna kopia trybu pchana Commandem, hook nigdy nie czeka; ignorowanie własnych injected events |
| F4 | medium | rubric (divergence) | Brak reguły „Orchestrator nigdy nie awaituje wykonawcy” i właściciela timeoutów | Dopisać do AD-2/AD-3: dispatch bez await, timeouty jako self-eventy przez `TimeProvider` |
| F5 | medium | rubric (ops) | Brak single-instance; `--smoke` przy działającej instancji łamie AD-4 (drugi klient CDP) | Named mutex + reguła dla smoke |
| F6 | medium | rubric (divergence) | Kontrakt Shell ↔ Core (katalog Intentów, kształt snapshotu) i źródło prawdy typów C#/TS nie są ustalone | Jedno źródło (rekordy w Core) + generowane albo testowane kontraktowo typy TS |
| F7 | medium | rubric (ops) + verification | Envelope operacyjny niepełny: Node.js i package manager poza Stack, pipeline web → publish, lokalizacja page bundle, dev env, testy części web | Uzupełnić AD-13 i Stack (Node 24 LTS) |
| F8 | medium | rubric (Deferred) | Deferred „tryb okna przeglądarki” może rozjechać BrowserHost, WindowManager i OSK; AD-1 ma zamkniętą listę flag | Przenieść do Open questions z regułą i kolejnością story |
| F9 | low | różne | Drobne: korelacja `backResult`, kanał host → page, 26H2 jako Release Preview, SDK lokalnie 10.0.301, brak walking skeleton dla stacku A, kosmetyka mermaid | Patrz sekcja Low |

---

## Lens 1: Rubric walker

### 1. Czy spine ustala realne punkty rozjazdu epik

Dobrze pokryte: właściciel stanu (AD-3), kierunek zależności (diagram + AD-4), kolejność przełączania (AD-5), protokół strony (AD-7), Actions (AD-8), jedna OSK (AD-9), lifecycle (AD-10), `appId` i rejestr (AD-11), jeden writer „Do oceny” (AD-12). Konwencje nazewnicze i błędów są konkretne.

Brakujące punkty rozjazdu:

**F3 (high): hook LL a tryb wejścia.** AD-8 każe hookowi połykać klawisze zależnie od trybu `normal/shell/osk`, a AD-3 mówi, że tryb mutuje tylko Orchestrator (inny wątek). Callback `WH_KEYBOARD_LL` musi wrócić w `LowLevelHooksTimeout` (domyślnie ~300 ms … do 1 s); po przekroczeniach Windows od Windows 7 **po cichu usuwa hook**, co zabija Home. Epika wejścia może naiwnie zrobić synchroniczne zapytanie do Orchestratora. Dodatkowo: AD-8 `back: key:<VK>` i AD-9 `SendInput` dla OSK wstrzykują klawisze, które ten sam hook zobaczy ponownie (pętla albo podwójne Actions).
Fix (dopisać do AD-8):
- Orchestrator wysyła `SetInputMode` (Command) do InputHub; InputHub trzyma lokalną kopię (volatile), callback hooka czyta tylko ją i nigdy nie czeka na kolejkę.
- Hook przepuszcza zdarzenia z `LLKHF_INJECTED` oznaczone własnym `dwExtraInfo` MediaStation (ignoruje własne wstrzyknięcia).
- Reality-check: watchdog, który wykrywa zdjęcie hooka (np. heartbeat) i go reinstaluje — albo otwarte pytanie z miejscem rozstrzygnięcia (prototyp InputHub już jest na liście).

**F4 (medium): Orchestrator a async wykonawców.** AD-2 mówi „I/O async”, AD-3 „jedna kolejka”, ale nie ma reguły, że Orchestrator **nie awaituje** Commandów. Jeśli epika BrowserHost wystawi `Task PauseAsync()`, a Orchestrator ją awaituje w pętli, zawieszony CDP blokuje Home (to jest dokładnie dywergencja, którą AD-2 chce wykluczyć). Nie ma też właściciela timeoutów: AD-7 (~500 ms na `backResult`), AD-10 (timeout `Starting` → `Failed`), AD-5 (`pause` przed `mute` — co, jeśli strona nie odpowie?).
Fix: „Orchestrator wysyła Command fire-and-forget; wynik wraca jako Event do kolejki. Każdy timeout to self-event planowany przez Orchestrator z wstrzykniętym `TimeProvider` (.NET 8+), dzięki czemu polityki i timeouty są testowalne w xunit bez zegara.” Dla AD-5: jawnie „brak potwierdzenia `pause` w X ms → i tak `mute` + `minimize`”.

**F6 (medium): kontrakt Shell ↔ Core.** Konwencja mówi tylko „UI wysyła Intent, core pełny snapshot”. Katalog Intentów i kształt view-modelu to granica między epiką Shell UI (React) a Core; budowane przez osobnych agentów rozjadą się. To samo dotyczy protokołu strony: rekordy C# w Core vs typy TS w `web/page/runtime`.
Fix: jedna reguła „źródłem prawdy są rekordy w `MediaStation.Core`; typy TS w `web/shared/` są generowane (np. JSON Schema → TS) albo pisane ręcznie, ale z testem kontraktowym na wspólnych fixture'ach JSON”. Intent i snapshot też w kopercie `{v,type,payload}` z wersją.

**Mniejsze punkty rozjazdu (low):**
- AD-7: `backResult` nie mówi, że echo-uje `id` z `back`; przy szybkim podwójnym Back stara odpowiedź może zostać przypisana nowej prośbie. Dopisać: „odpowiedź niesie `id` żądania”.
- AD-7: kanał host → page (np. `Runtime.evaluate` wołające `bridge.receive` w głównym świecie) nie jest nazwany. AD-6 zakazuje driverowi `window.__*`, ale PageRuntime musi coś wystawić; nazwać ten jeden punkt wejścia.
- Lokalizacja page bundle (patrz F7).

### 2. Czy Rule każdego AD jest egzekwowalna i zapobiega deklarowanej dywergencji

| AD | Egzekwowalność | Uwagi |
| --- | --- | --- |
| AD-1 | tak (lista flag i zakazanych metod CDP; grep/review) | Lista flag jest zamknięta, a Deferred zapowiada dodanie trybu okna (F8) |
| AD-2 | częściowo | „Zawieszony UI nie może blokować Home” to cel, nie reguła; brak „no await” (F4) |
| AD-3 | tak (referencje projektów) | Warto dopisać test architektury (np. sprawdzenie referencji `MediaStation.Core` w teście) |
| AD-4 | tak | Smoke i „jeden klient CDP” kolidują przy działającej instancji (F5) |
| AD-5 | tak | Brak reguły na nieodpowiadającą stronę przy `pause` (F4) |
| AD-6 | tak | — |
| AD-7 | tak | Korelacja `id` (low) |
| AD-8 | częściowo | Brak reguły wątkowej hooka i injected input (F3) |
| AD-9 | tak, z fallbackiem | OK, otwarte pytanie ma miejsce rozstrzygnięcia |
| AD-10 | tak | „Timeout” bez właściciela (F4) |
| AD-11 | tak (walidacja przy starcie) | — |
| AD-12 | tak | — |
| AD-13 | częściowo | Restart po awarii nie działa tak, jak zapisano (F2); brak single-instance (F5) |

### 3. Deferred: czy coś może rozjechać dwie jednostki

**F8 (medium): „Tryb okna przeglądarki (`--app` / `--start-fullscreen` / `--kiosk`)”.** To nie jest lokalna sprawa BrowserHost:
- AD-1 zamyka listę flag; dodanie flagi w story BrowserHost formalnie łamie ADOPTED rule.
- `--kiosk` zmienia zachowanie minimize/restore i fokusu (AD-5, WindowManager) oraz to, czy nakładka OSK (AD-9) w ogóle będzie nad oknem.
Fix: przenieść do „Otwartych pytań” z regułą: „tryb okna to jedyna dopuszczalna dodatkowa flaga w AD-1; rozstrzyga ją story 1 epiki BrowserHost, zanim ruszą story WindowManager i spike nakładki OSK”.

Pozostałe pozycje Deferred są bezpieczne (nowy driver, nowy wykonawca DisplayManager, rozszerzenie `apps.json`, UX). Uwagi drobne:
- Memlog (wiersz 33) defers też „Store-PWA launch mode for SMTC” i „WebView2 shell prewarm of apps”; w spine ich nie ma. Albo dopisać, albo świadomie odrzucić w memlogu.
- Otwarte pytanie „Pauza dłuższa niż kilka godzin lub przez noc” nie ma miejsca rozstrzygnięcia, choć nagłówek to obiecuje (proponuję: story wznowienia w epice BrowserHost/driver, powiązane z AD-5 `page-error` → reload).

### 4. Wymiary należące do tej altitude

| Wymiar | Status | Uwagi |
| --- | --- | --- |
| Paradygmat, granice, zależności | decided | — |
| Model procesów i wątków | decided, luki | F3, F4 |
| Kontrakty komunikatów | decided (strona), luka (shell) | F6 |
| Dane i trwałość | decided | — |
| Konfiguracja | decided | — |
| Bezpieczeństwo | decided | CDP loopback, akceptacja ryzyka — OK |
| Deployment | decided, błąd faktyczny | F2; brak rollbacku (low: zachować poprzedni katalog przy `install.ps1`) |
| Environments | **brak** | Dev vs zainstalowana instancja na tej samej maszynie, Vite dev server w WebView2 (`SetVirtualHostNameToFolderMapping` vs `http://localhost`), `global.json` — F7 |
| Operations / observability | decided | Logi OK; brak single-instance (F5) |
| Testing strategy | **częściowo** | Jest: xunit dla Core + smoke E2E. Brak: testy PageRuntime/drivera na fixture'ach DOM, testy kontraktowe protokołu C# ↔ TS, testy Shell UI, test architektury referencji — F7 |
| Build pipeline | **brak** | Kto uruchamia `vite build` i `esbuild` przed `dotnet publish`, gdzie lądują zasoby (`Shell/wwwroot`?), Node.js i package manager — F7 |

**F5 (medium): single-instance i smoke.** Task Scheduler + ręczne uruchomienie (albo dev build) dają dwa procesy z dwoma hookami LL i dwoma klientami CDP na ten sam port — łamie AD-3/AD-4. `MediaStation.exe --smoke <appId>` przy działającej instancji robi to samo (adopcja sieroty z AD-10 podepnie się do żywej przeglądarki).
Fix: AD-13 „named mutex `Local\MediaStation`; druga instancja kończy się z kodem X. `--smoke` wymaga zatrzymania instancji głównej (skrypt to robi) albo działa na osobnym `appId`/profilu/porcie”.

**F2 (high): restart po awarii.** `RestartOnFailure` w Task Scheduler restartuje zadanie, którego **uruchomienie** się nie powiodło; proces, który wystartował i potem padł, jest raportowany jako „completed” (zgłoszenia na Microsoft Q&A i forach; dodatkowo kody ujemne są czytane jako `0xFFFFFFFF` i bywają raportowane jako sukces). Dokumentacja schematu mówi ogólnie o „failure”, więc to wymaga reality-checku, nie założenia. Dla „Home zawsze działa” po crashu to główna ścieżka.
Fix (jedno z):
- mały supervisor (np. `MediaStation.exe` w trybie `--supervise` jako proces startowany przez zadanie, który restartuje instancję główną z backoffem) — to drugi proces, więc dopisać wyjątek do AD-2;
- albo story „reality-check restartu” w epice deploy (kill -9 procesu, obserwacja historii zadania) z fallbackiem na supervisor.
Dopisać też: zadanie „Run only when user is logged on” (hook LL i UI wymagają interaktywnej sesji).

### 5. Mermaid

Oba bloki sparsowane przez `mermaid@11` (`mermaid.parse`, jsdom, kontrola negatywna zwraca błąd): **OK** (`flowchart-v2`). Kosmetyka: w drugim diagramie `profiles\\netflix` i `%LOCALAPPDATA%\\MediaStation` wyrenderują się z podwójnym backslashem (mermaid nie escapuje `\`); użyć pojedynczego `\` albo `/`.

---

## Lens 2: Verification

Sprawdzone 2026-10-16 w rejestrach (npm, NuGet flat container + registration, dotnet release-metadata, GitHub releases) i w sieci.

| Pozycja Stack | Wartość w spine | Stan faktyczny | Werdykt |
| --- | --- | --- | --- |
| .NET | 10.0.13 / SDK 10.0.402 | latest-release 10.0.13 (2026-10-13), SDK 10.0.402, `support-phase: active`, EOL 2028-11-14 | ✔ aktualne. Uwaga: lokalnie zainstalowane SDK to 10.0.301 i 8.0.412; brak `global.json` (low) |
| WPF | z .NET 10 | — | ✔ |
| Microsoft.Web.WebView2 | 1.0.4258.31 | najnowsze stable 1.0.4258.31 (nowsze 1.0.4312 to prerelease); pakiet deklaruje WPF/WinForms dla dotnet | ✔ |
| Microsoft.Windows.CsWin32 | 0.3.363 | najnowsze, opublikowane 2026-10-14 (2 dni); source generator na netstandard2.0, działa z net10 | ✔, ale świeży 0.x z częstymi zmianami — pin exact, bez floating |
| xunit.v3 | 4.0.2 | najnowsze (2026-10-09); runner niezdecydowany (`xunit.runner.visualstudio` 4.0.1 + `Microsoft.NET.Test.Sdk` 18.11.0 albo Microsoft Testing Platform) | ✔, low: nazwać runner |
| React | 19.3.1 | realne, npm latest, 2026-10-14 (równolegle backport 19.2.9); `@types/react` 19.3.0 | ✔ |
| Vite | 8.3.4 | npm latest (2026-10-08); peer `esbuild ^0.27 \|\| ^0.28`; engines `node ^20.19.0 \|\| >=22.12.0`; `@vitejs/plugin-react` 6.1.2 wymaga `vite ^8` | ✔ |
| TypeScript | 7.0.2 | npm latest (GA 2026-07-08, Go-native). **Pakiet eksportuje tylko `./lib/version.cjs` i `./unstable/*`** — brak stabilnego JS API. `typescript-eslint` 8.71.2 (latest): peer `typescript >=4.8.4 <6.1.0`; wsparcie TS 7 czeka na API w TS 7.1 (PR #12803 „EXPERIMENTAL_backend”, otwarty; issue #12720 zamknięte jako not planned → #10940). Zgłoszony realny przypadek Vite 8 + TS 7.0.2: build OK, `npm ci` → ERESOLVE, lint crash | ✘ **F1** |
| esbuild | 0.28.2 | npm latest (2026-08-08), w zakresie peer Vite | ✔ |
| Edge/Chrome | evergreen, 155 w spike'u | potwierdzone pomiarami spike'u (S1–S11) | ✔ (re-check 2026-11-12 wg research) |
| Moonlight-qt | 6.2.0 | GitHub release v6.2.0, 2026-10-04; flagi `--display-mode borderless` i `--mute-on-focus-loss` zweryfikowane w spike'u S11 | ✔ |
| Windows 11 | 26H2, build 26300 | 26H2 = build 26300.x, Release Preview od 2026-08-27; GA w źródłach niepotwierdzone (sprzeczne doniesienia). Maszyna raportuje 10.0.26300 | ⚠ low: oznaczyć „Release Preview / Insider” albo potwierdzić GA; 24H2 Home/Pro kończy wsparcie 2026-10-13 |
| Node.js | **brak w Stack** | Vite 8 wymaga ^20.19 / >=22.12. Lokalnie `node v23.9.0` (linia nieparzysta, EOL). Aktualny LTS: 24.22.0 (2026-10-14); Node 26 wejdzie w LTS pod koniec października | ✘ **F7** |

**F1 (high): TypeScript 7.0.2.** Memlog pokazuje, że decyzja przeszła z „TS 6” (wiersz 31) na „TS 7” (wiersz 32) wyłącznie dlatego, że 7.0.2 jest `latest` — bez sprawdzenia toolchainu. Fakty:
- Vite 8 (Oxc/Rolldown) i esbuild tylko usuwają typy, więc **build i dev działają** z TS 7; `tsc --noEmit` z TS 7 też.
- Każde narzędzie importujące kompilator jako bibliotekę (typescript-eslint, `vite-plugin-checker` w trybie TS, część pluginów IDE/test) nie działa; `npm ci` z typescript-eslint padnie na peer range.
- Microsoft rekomenduje dla takich narzędzi `@typescript/typescript6` (istnieje na npm, 6.0.2, bin `tsc6`).
Fix (wybrać i zapisać w Stack + memlog):
- (a) TS 6.0.3 dla `web/*` do czasu TS 7.1 + wsparcia w typescript-eslint; albo
- (b) TS 7.0.2 do typecheck (`tsc --noEmit`) + jawna reguła „bez type-aware typescript-eslint do TS 7.1; lint składniowy albo `@typescript/typescript6` dla narzędzi bibliotecznych”.
Dla hobby projektu z agentami budującymi (którzy domyślnie postawią `npm create vite` + eslint) bezpieczniejsze jest (a).

**F7 (medium): envelope buildu i środowisk.** Dopisać do Stack: Node.js 24 LTS (24.22.0) i package manager (npm + lockfile). Dopisać do AD-13:
- pipeline: `npm ci && vite build` (shell) + `esbuild` (page bundle) przed `dotnet publish`, np. target MSBuild w `MediaStation.App.csproj`; zasoby shell w katalogu mapowanym przez `SetVirtualHostNameToFolderMapping`;
- lokalizacja page bundle: install dir (`%LOCALAPPDATA%\Programs\MediaStation`) czy `%LOCALAPPDATA%\MediaStation`? Hotfix „podmiana pliku” i `install.ps1` (nadpisuje?) muszą wskazywać to samo miejsce;
- `global.json` z SDK 10.0.402 (`rollForward: latestFeature`); lokalnie trzeba podnieść SDK z 10.0.301;
- dev environment: shell z Vite dev server w WebView2 (flaga `--dev`), dev build vs zainstalowana instancja (F5);
- testing strategy dla web: fixture'y DOM Netflixa dla drivera (np. Vitest + jsdom/happy-dom), testy kontraktowe protokołu (F6).

**Reality-check decyzji niezwiązanych z wersjami:**
- **Stack A (WPF + WebView2 + CsWin32 w .NET 10, jeden proces)** — technologia mainstream (WebView2 WPF control wspiera .NET Core/5+), ale spike był w Node + PowerShell/C# `Add-Type` (`spike/win/*.cs`). Współistnienie wątku hooka LL, WebView2 i nakładki `WS_EX_NOACTIVATE` w jednym procesie .NET 10 nie było mierzone. Low/medium: nazwać pierwszą story „walking skeleton” (WPF+WebView2 shell, InputHub w osobnym wątku, Home przełącza Edge) jako reality-check AD-2.
- **AD-1/AD-5/AD-10 [ADOPTED]** — pokryte pomiarami spike'u (S1–S11, C1–C3); `Runtime.addBinding` bez `Runtime.enable` z ponawianiem po nawigacji potwierdzone w spike (Edge 155).
- **Task Scheduler restart-on-failure** — nie zweryfikowane, sprzeczne z doniesieniami (F2).
- **`dotnet publish` self-contained win-x64 dla WPF** — wspierane; single-file niezdecydowane (low, nie wpływa na epiki).

## Low (zbiorczo, F9)

- AD-7: `backResult` echo `id`; nazwać kanał host → page.
- Open question „pauza przez noc” bez miejsca rozstrzygnięcia.
- Memlog-only Deferred (SMTC/Store-PWA, prewarm) brak w spine.
- Windows 11 26H2 jako Release Preview — oznaczyć kanał.
- `global.json` / lokalne SDK 10.0.301.
- Runner xunit.v3 niezdecydowany.
- Mermaid: podwójne backslashe w etykietach.
- Rollback w `install.ps1` (zachować poprzednią wersję).
- Test architektury: `MediaStation.Core` bez referencji Windows (AD-3) jako test, nie tylko konwencja.

## Źródła weryfikacji

- npm registry: `typescript`, `typescript-eslint`, `@typescript-eslint/typescript-estree`, `vite`, `@vitejs/plugin-react`, `react`, `@types/react`, `esbuild`, `@typescript/typescript6` (2026-10-16)
- NuGet: `microsoft.web.webview2`, `microsoft.windows.cswin32`, `xunit.v3`, `xunit.runner.visualstudio`, `microsoft.net.test.sdk`
- https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json
- https://nodejs.org/dist/index.json
- https://github.com/moonlight-stream/moonlight-qt/releases/tag/v6.2.0
- https://github.com/typescript-eslint/typescript-eslint/issues/10940 , /issues/12720 , /pull/12803 , /issues/12518
- https://dev.to/the-modern-web/why-angular-vue-and-eslint-cant-upgrade-to-typescript-70-yet-and-why-ts-71-changes-441g
- https://learn.microsoft.com/en-us/windows/win32/TaskSchd/taskschedulerschema-restartonfailure-settingstype-element
- https://learn.microsoft.com/en-us/answers/questions/2197094/task-scheduler-completed-with-with-return-code-214
- https://pureinfotech.com/?p=191272 , https://www.tweaktown.com/news/113334/windows-11-version-26h2-reaches-release-preview-ahead-of-general-availability-later-this-year/index.html
- Mermaid: lokalny parse `mermaid@11` + jsdom, oba bloki OK
