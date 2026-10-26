---
title: 'Host shella: blokada nawigacji, nowe okna i CSP'
type: 'feature'
ticket: '11'
created: '2026-10-26'
status: done
baseline_revision: '12e00f306f6afc96d766f5d7d9d16809f0a6b049'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: [quick]
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** `ShellWindow` nie blokuje nawigacji WebView2 poza origin shella ani ramek, loguje anulowaną nawigację jako `navigationFailed`, `index.html` nie ma CSP, a decyzje okna i `ProcessFailed` nie mają testów (AD-16, F11, R4/N3, R7/N8).

**Approach:** Decyzje nawigacji wydzielone do czystej klasy bez typów WebView2 (linkowanej do testów), `ShellWindow` tylko woła ją z `NavigationStarting`/`FrameNavigationStarting` i loguje; CSP w meta `index.html`, w Debug poszerzone przez plugin Vite tylko o to, czego wymaga dev server.

## Boundaries & Constraints

**Always:** Origin shella z `ShellOrigin` (scheme+host+port, nie prefiks napisu); wpisy logu `navigationBlocked` i `newWindowBlocked` (warn) z `uri`; `OperationCanceled` nie daje `navigationFailed`, inne błędy tak; CSP Release co najmniej `script-src 'self'; img-src 'self' https://icons.mediastation.example; frame-src 'none'; object-src 'none'`.

**Never:** Nie zmieniać mostu (`ShellBridge`, `ShellOutbox`) ani kontraktów; nie dodawać mapowania hosta ikon (story 5); nie wyłączać CSP w Release; Debug nie dostaje niczego szerszego niż potrzebuje Vite (jego origin, `ws:` HMR, inline preamble React).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Origin shella, główna ramka | URI pod origin shella | przepuszczona | — |
| Inny origin, główna ramka | `https://evil.example/` | anulowana, `navigationBlocked` | — |
| Ramka spoza origin | `FrameNavigationStarting` na obcy URI | anulowana, `navigationBlocked` | — |
| Nieparsowalny/pusty URI | `""`, `null`, `not a uri` | anulowana | brak wyjątku |
| Zbliżony origin | `https://shell.mediastation.example.evil.com/`, inny port/scheme | anulowana | — |
| Nawigacja anulowana | `OperationCanceled` | brak `navigationFailed` | — |
| Inny błąd nawigacji | np. `ConnectionReset`, `IsSuccess=false` | `navigationFailed` | — |
| Nowe okno | `NewWindowRequested` | `Handled`, `newWindowBlocked` | — |

</intent-contract>

## Code Map

- `src/MediaStation.App/Shell/ShellWindow.xaml.cs` -- jedyne miejsce z `CoreWebView2`; dziś `NavigationCompleted` loguje każdy błąd, `NewWindowRequested` już blokuje (`newWindowBlocked`), `ProcessFailed` loguje `webViewProcessFailed`; brak `NavigationStarting`/`FrameNavigationStarting`.
- `src/MediaStation.App/Shell/ShellOrigin.cs` -- origin (`#if SHELL_DEV_SERVER`: `http://localhost:5173/`, Release `https://shell.mediastation.example/index.html`); nie linkowany do testów (zależy od `#if`).
- `src/MediaStation.App/Shell/ShellMessageGate.cs` -- ma `IsShellOrigin` (porównanie scheme+host+port); wydzielić do wspólnej statycznej metody i użyć w obu miejscach.
- `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` -- linki `<Compile Include=... Link="App\...">`; dodać nowy plik; `ShellMessageGateTests.cs` jako wzór stylu.
- `web/shell/index.html`, `web/shell/vite.config.ts` -- CSP w meta; plugin `transformIndexHtml` dla `serve`.
- `_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md` (Consistency Conventions, "Testy kodu App") -- liczba linkowanych plików (21 → faktyczna).

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.App/Shell/ShellNavigationPolicy.cs` (nowy, bez WebView2/WPF) -- `ShellNavigationPolicy(Uri origin)`: `bool IsAllowed(string? uri)` (parsowanie, scheme+host+port), statyczne `SameOrigin(Uri origin, string? uri)`, `static bool ShouldLogFailure(bool isSuccess, bool operationCanceled)` -- decyzje poza typami WebView2 (R4/N3)
- [x] `src/MediaStation.App/Shell/ShellMessageGate.cs` -- `IsShellOrigin` deleguje do `ShellNavigationPolicy.SameOrigin` -- jedna definicja origin
- [x] `src/MediaStation.App/Shell/ShellWindow.xaml.cs` -- `NavigationStarting` i `FrameNavigationStarting`: gdy `!IsAllowed(e.Uri)` → `e.Cancel = true` + warn `navigationBlocked` (`uri`, `isMainFrame`); `OnNavigationCompleted` pomija log gdy `!ShouldLogFailure(e.IsSuccess, e.WebErrorStatus == OperationCanceled)`; `ProcessFailed` bez zmian -- AD-16
- [x] `web/shell/index.html` + `web/shell/vite.config.ts` -- meta CSP Release (`default`-less: `script-src 'self'; img-src 'self' https://icons.mediastation.example; frame-src 'none'; object-src 'none'; base-uri 'self'; form-action 'none'`); plugin dla `serve` podmienia na wersję dev (`script-src 'self' 'unsafe-inline'`, `connect-src 'self' ws://localhost:5173`, reszta jak Release)
- [x] `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj`, `tests/MediaStation.Core.Tests/ShellNavigationPolicyTests.cs` (nowy) -- link i testy całej macierzy (Release i dev origin), `ShouldLogFailure`; zaktualizować liczbę w architekturze
- [ ] weryfikacja na bieżącej maszynie -- patrz Verification; `ProcessFailed`: manualny check (zabicie procesu renderera) z wpisem w Implementation Notes

**Acceptance Criteria:**
- Given build Release, when strona próbuje przejść na obcy adres (`window.location`), then strona się nie zmienia i log ma `navigationBlocked`.
- Given build Release, when `window.open`, then okno się nie otwiera i log ma `newWindowBlocked`.
- Given build Release, when shell startuje, then konsola WebView2 nie ma naruszeń CSP.

## Implementation Notes

Zweryfikowano na UM760 (`COMPUTERNAME` MEDIASTATION, płyta F7BSI, EliteMini Series; ustalone zgodnie z AGENTS.md).
- Nowy `ShellNavigationPolicy` (bez WebView2) + `ShellNavigationPolicyTests`; `ShellMessageGate` używa wspólnego `SameOrigin`; `ShellWindow` anuluje `NavigationStarting`/`FrameNavigationStarting` poza origin (`navigationBlocked`) i pomija `navigationFailed` dla `OperationCanceled`; CSP w meta `index.html`, dev wariant przez plugin Vite.
- Release z `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333` i CDP (DevTools w Release wyłączone): start bez naruszeń CSP; `window.location='https://example.com/'` nie zmienia strony, log `navigationBlocked` (isMainFrame true); `window.open` zwraca null, log `newWindowBlocked`; iframe na obcy adres: log `navigationBlocked` (isMainFrame false) oraz CSP `frame-src 'none'`; inline script zablokowany przez `script-src 'self'`. Naruszenia CSP w konsoli pojawiły się wyłącznie od celowych prób.
- `ProcessFailed` (manualny check): zabicie procesu renderera msedgewebview2 dało w logu `webViewProcessFailed` (kind `renderProcessExited`, reason `crashed`).
- `dotnet build` Debug i Release 0 błędów/0 ostrzeżeń; `dotnet test` 1040 zielonych; `npm test` 15 zielonych; `npm run build` ok.

## Plan Change Log

## Review Triage Log

### 2026-10-26 — Review pass
- verdicts: 5 findings — high 0, medium 0, low 3, false 0, maybe-false 0 (2 process notes)
- findings:
  - `[low]` `[reject]` `navigationFailed` loguje `ShellUri`, nie faktyczny URI — kod sprzed zmiany; poprawka wymaga zmiany semantyki logu, a nawigacje shella to jedna strona.
  - `[low]` `[reject]` plugin `devCsp` cicho nie działa po przeformatowaniu meta — sprawdzone: transformIndexHtml Vite podmienia CSP na wariant dev; strażnik to dodatkowa złożoność dla dev-only.
  - `[low]` `[reject]` zduplikowane łańcuchy CSP w `index.html` i `vite.config.ts` — plan wprost każe dwa warianty; maintainability bez nazwanej szkody.
  - `[low]` `[patch]` zadania w Execution nie odhaczone — odhaczone.
  - `[low]` `[patch]` brak wyniku identyfikacji maszyny w planie — dopisane `COMPUTERNAME` MEDIASTATION / F7BSI / EliteMini Series = UM760 w Implementation Notes.

## Design Notes

Polityka jest bezstanowa i testowalna; `ShellWindow` jest cienkim adapterem. Release ma DevTools wyłączone, więc `window.location`/`window.open`/konsolę sprawdzić przez `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=<port>` i CDP (`Runtime.evaluate`, `Runtime.consoleAPICalled`/`Log.entryAdded`).

## Verification

**Commands:**
- `dotnet build MediaStation.slnx` -- expected: 0 błędów, 0 ostrzeżeń
- `dotnet test MediaStation.slnx` -- expected: 0 niepowodzeń
- `npm test` i `npm run build` w `web/` -- expected: sukces

**Manual checks (if no CLI):**
- Release na bieżącej maszynie z portem CDP: brak naruszeń CSP, `window.location='https://example.com'` bez zmiany strony i `navigationBlocked` w logu, `window.open` → `newWindowBlocked`; zabicie procesu renderera daje `webViewProcessFailed`.

## Auto Run Result

**Summary:** Host shella anuluje nawigacje głównej ramki i ramek poza origin (`navigationBlocked`), nowe okna (`newWindowBlocked`), nie loguje `OperationCanceled` jako `navigationFailed`; `index.html` ma CSP, a Debug dostaje wariant dev przez plugin Vite.

**Files:** `src/MediaStation.App/Shell/ShellNavigationPolicy.cs` (nowy, decyzje bez WebView2), `ShellMessageGate.cs` (wspólne `SameOrigin`), `ShellWindow.xaml.cs` (zdarzenia nawigacji), `web/shell/index.html` (CSP), `web/shell/vite.config.ts` (CSP dev), `tests/MediaStation.Core.Tests/ShellNavigationPolicyTests.cs` + csproj (link), architecture-mediastation.md (liczba plików 22).

**Review:** quick; patches 2 (low, plan housekeeping), deferred 0, rejected 3 (low, powody w triage log).

**Follow-up review recommended:** false.

**Verification (UM760):** build Debug i Release 0/0; `dotnet test` 1040 zielonych; `npm test` 15; `npm run build` ok; live Release przez CDP: brak naruszeń CSP w starcie, `window.location` zablokowane + `navigationBlocked`, `window.open` + `newWindowBlocked`, iframe zablokowany; zabicie renderera → `webViewProcessFailed`.

**Residual risks:** brak.
