---
title: 'AppMenu: Uruchom ponownie i Zamknij'
type: 'feature'
ticket: '8'
created: '2026-10-26'
status: done
baseline_revision: 'cf1fcba68c2c5d2da86124342548436907947cbf'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: [quick]
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/ux-mediastation/mockups/key-home-failed-appmenu.html'
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** Kafelek `Failed` (i `Background`) nie ma drogi naprawy z pilota: Shell nie ma AppMenu, Orchestrator po cichu ignoruje `RestartApp`/`CloseApp` poza dozwolonymi stanami, a kontrakt wciąż niesie martwy `ActionKind.AppMenu`.

**Approach:** Shell trzyma otwarte AppMenu jako efemeryczny stan nawigacji w `focus.ts` (czysta logika, Vitest) i wysyła `restartApp`/`closeApp` przez istniejący most; Orchestrator zwraca `IgnoredIntent` dla tych dwóch Intentów; `ActionKind.AppMenu` znika z kontraktu, a `appMenu` w `input.json` jest odrzucane jak każda nieznana wartość enumu (AD-8).

## Boundaries & Constraints

**Always:** Shell konsumuje wyłącznie Actions (nie surowe klawisze); otwarcie menu nie jest Intentem; dokładnie jeden fokus (kafelek albo pozycja menu); menu zamyka się samo, nigdy pod fokusem nie zmienia się nic innego; kontrakty TS tylko generowane (`npm run contracts`), nie edytowane ręcznie; copy z `EXPERIENCE.md`: „Uruchom ponownie”, „Zamknij”; panel i scrim wg `DESIGN.md` (`appmenu`, `focus-pill`) i mockupu.

**Never:** Nowy Intent `OpenAppMenu`; zmiana logiki przejść `OnStop` poza zwracaniem `IgnoredIntent`; ruch/animacja menu; obsługa `NotRunning`/`Starting`/`Stopping`/`Foreground` w menu; ręczna edycja `web/contracts/generated`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Long OK na `Background` | fokus na kafelku `background`, `ok`/`long` | menu otwarte, fokus na pozycji 0 („Uruchom ponownie”) | — |
| Long OK na `Failed` | j.w., `failed` | menu otwarte, fokus na „Uruchom ponownie” | — |
| OK na `Failed` | `ok`/`press`, `failed` | menu otwarte, fokus na „Uruchom ponownie”, brak Intentu | — |
| OK na `Background` | `ok`/`press` | `launchApp` (bez zmian), menu zamknięte | — |
| Long OK na `NotRunning`/`Starting`/`Stopping`/`Foreground` | `ok`/`long` | bez zmian | — |
| OK na `Starting` | `ok`/`press` | bez zmian | — |
| Nawigacja w menu | menu otwarte, `up`/`down` | fokus między pozycjami, na krańcu zostaje; `left`/`right` nic | — |
| Wykonanie | `ok`/`press` na pozycji 0 / 1 | Intent `restartApp` / `closeApp` z `appId` kafelka, menu zamknięte | — |
| Back | menu otwarte, `back`/`press` | menu zamknięte, fokus na tym samym kafelku | — |
| Home | menu otwarte, `home` | menu zamknięte | — |
| Samozamknięcie | snapshot: aplikacja menu poza `background`/`failed`, albo jakakolwiek `foreground`, albo nowe `focusRequest.seq` | menu zamknięte (przy `seq` najpierw zamknięcie, potem fokus) | — |
| Orchestrator `RestartApp`/`CloseApp` | stan inny niż `Background`/`Failed`, nieznany `appId` albo inna aplikacja `Foreground`/`Starting` | brak komend i snapshotu, `IgnoredIntent` (log `intentIgnored`) | powód po polsku jak przy `LaunchApp` |
| `input.json` z `appMenu` | `"0x5D": "appMenu"` albo `gamepad.y: "appMenu"` | błąd konfiguracji jak dla nieznanej wartości enumu | ekran błędu konfiguracji |

</intent-contract>

## Code Map

- `web/shell/src/focus.ts` -- `FocusState`, `applyFocusRequest`, `applyAction` (dziś `intent?: LaunchApp`); tu dochodzi `menu`, wyzwalacze, nawigacja i `reconcileMenu`/`applySnapshot`.
- `web/shell/src/App.tsx` -- most → reduktor; wysyła `sendIntent('launchApp', …)`; ma przejść na `applySnapshot` i generyczny Intent.
- `web/shell/src/Placeholder.tsx`, `Tile.tsx` -- renderują kafelki; menu renderuje się nad kafelkiem ze scrimem (`AppMenu.tsx`, nowy plik).
- `web/shell/src/tokens.css` -- tokeny z `DESIGN.md`; dodać `.scrim`, `.appmenu`, `.appmenu__item` (+ `--focus`), wg `mockups/key-home-failed-appmenu.html` (panel 440px, padding 16, gap 12, pozycja 56px, `focus-pill`).
- `web/shell/src/bridge.ts` -- `ACTION_KINDS` (usunąć `appMenu`), `sendIntent` już zna `restartApp`/`closeApp`.
- `web/shell/src/focus.test.ts`, `Placeholder.test.tsx` -- istniejące testy (m.in. `appMenu` w pętli „bez skutku”, `.intent` toEqual `{appId}`) do dostosowania.
- `src/MediaStation.Core/Contracts/Action.cs` -- `ActionKind.AppMenu` do usunięcia.
- `src/MediaStation.Core/Config/ConfigValidation.cs` (~l.397, ~l.522) i `InputConfig.cs` (komentarz) -- usunąć gałęzie `AppMenu`.
- `src/MediaStation.Core/Orchestrator/Orchestrator.Stop.cs` (`OnStop`) i `Orchestrator.Launch.cs` (`Ignored(LaunchApp, …)`) -- wzorzec `IgnoredIntent`; uogólnić `Ignored` na nazwę Intentu.
- Testy C#: `OrchestratorStopTests.cs`, `OrchestratorLoopTests.cs` (`Ignored_intent_is_logged_as_warning`), `ConfigValidationTests.cs` (~l.780, ~l.968), `CoreMessageContractTests.cs` (~l.14, ~l.578), `OrchestratorActionTests.cs` (~l.38), `OrchestratorHomeTests.cs` (~l.184), helpery w `OrchestratorTestSupport.cs`.
- Nie ruszać: `ShellMessageGate` (już przyjmuje `restartApp`/`closeApp`), `InputPolicy` (long OK w trybie shell daje samo `long`, bez `press`).

## Tasks & Acceptance

**Execution:**
- [ ] `src/MediaStation.Core/Orchestrator/Orchestrator.Stop.cs`, `Orchestrator.Launch.cs` -- `OnStop` zwraca `IgnoredIntent` (`RestartApp`/`CloseApp`, powody: nieznany appId, stan nie pozwala, inna aplikacja jest aktywna) -- Intent poza stanem nie znika po cichu
- [ ] `src/MediaStation.Core/Contracts/Action.cs`, `Config/ConfigValidation.cs`, `Config/InputConfig.cs` -- usunąć `AppMenu` i jego gałęzie walidacji -- `appMenu` odrzuca strict enum jak każdą nieznaną wartość (AD-8)
- [ ] `web/shell/src/focus.ts` -- `menu: { appId, item } | null` w `FocusState`; wyzwalacze, nawigacja, Back/Home, wykonanie, `applySnapshot` z samozamknięciem; wynik `intent?: { type: 'launchApp'|'restartApp'|'closeApp'; appId }` -- logika bez DOM
- [ ] `web/shell/src/AppMenu.tsx`, `Placeholder.tsx`, `App.tsx`, `tokens.css`, `bridge.ts` -- panel ze scrimem nad kafelkiem, pozycje „Uruchom ponownie”/„Zamknij”, jeden fokus (pigułka), Intent przez `sendIntent`, usunięty `appMenu` z `ACTION_KINDS`
- [ ] Testy C# i Vitest wg matrycy: `RestartApp`/`CloseApp` z `Background`/`Failed` docierają do Orchestratora (StopApp), w innych stanach `IgnoredIntent` i log `intentIgnored`; `input.json` z `appMenu` daje `ConfigError`; wyzwalacze dla każdego stanu kafelka, fokus startowy, Back, Home, samozamknięcie (zmiana stanu, nowy `seq`, `Foreground`), render menu; usunąć/dostosować testy odwołujące się do `ActionKind.AppMenu`
- [ ] `web/contracts` -- odświeżyć `npm run contracts` (pliki generated są git-ignorowane) -- `action.ts` bez `appMenu`

**Acceptance Criteria:**
- Given kafelek `Failed` w fokusie, when OK, then menu otwarte z fokusem na „Uruchom ponownie”, a OK na tej pozycji wysyła `restartApp` i zamyka menu.
- Given menu otwarte, when Back, then menu zamknięte, a fokus stoi na tym kafelku; when Home, then menu zamknięte.
- Given menu otwarte, when snapshot zmienia stan aplikacji menu poza `background`/`failed`, przynosi nowe `seq` albo pokazuje aplikację w `foreground`, then menu jest zamknięte.
- Given `input.json` z `"appMenu"`, when ładowanie configu, then błąd konfiguracji.
- Given aplikacja w `NotRunning`, when `CloseApp`, then Orchestrator nic nie zmienia i loguje `intentIgnored`.

## Implementation Notes

## Plan Change Log

## Review Triage Log

### 2026-10-26 — Review pass
- verdicts: 6 findings — high 0, medium 0, low 6, false 0, maybe-false 0
- findings:
  - `[low]` `[patch]` Config tests for `appMenu` asserted only file and path substring — fixed: exact path and error equal to the error for an unknown value ("banana").
  - `[low]` `[patch]` `intentIgnored` log checked only for `LaunchApp` — fixed: added `Stop_intent_outside_background_or_failed_is_logged_as_ignored` for `CloseApp`/`RestartApp` through the loop.
  - `[low]` `[reject]` Missing Vitest edge cases (repeat/long variants, tabs zone, missing focused tile) — code ignores them by default branch; fix would only add tests, unlikely to be met in use.
  - `[low]` `[patch]` Loose `not.toContain('scrim')` — fixed: `class="scrim"`.
  - `[low]` `[reject]` Panel position magic offsets / hard-coded radius in `tokens.css` — values come from the mockup; layout-fit work belongs to later stories; more than a direct correction.
  - `[low]` `[reject]` `aria-label` falls back to `''` — `reconcileMenu` closes the menu when the app is missing, so unreachable.

## Design Notes

Menu to stan Shella, nie Orchestratora (decyzja epiki 2026-10-26): `FocusState.menu` jest efemeryczny i kluczowany `appId`. Pozycje: indeks 0 „Uruchom ponownie” (`restartApp`), 1 „Zamknij” (`closeApp`); start zawsze na 0 (przy `Failed` jawnie, w pozostałych stanach „pierwsza pozycja” to też 0). `applyFocusRequest` przy nowym `seq` najpierw zeruje `menu`, potem stosuje fokus. Reguła samozamknięcia jest w jednej funkcji `reconcileMenu(state, apps)` wołanej ze snapshotów.

## Verification

**Commands:**
- `dotnet test` (z korzenia repo) -- expected: wszystkie testy zielone
- `npm run build` w `web/` -- expected: odświeża kontrakty, typecheck i build bez błędów
- `npm test` w `web/` -- expected: Vitest zielony

**Manual checks (if no CLI):**
- Na bieżącej maszynie (UM760): kafelek `Failed` (MissingHostPort), OK na kafelku, OK na „Uruchom ponownie” -- w logu `stopWithoutHost`, kafelek „Zamykam…”, po `stoppingTimeout` wraca do `Failed`.

## Auto Run Result

Machine: UM760 (COMPUTERNAME `MEDIASTATION`, board F7BSI, model EliteMini Series).

**Summary:** AppMenu in the Shell (ephemeral `FocusState.menu`, panel + scrim, "Uruchom ponownie"/"Zamknij" sending `restartApp`/`closeApp`), self-closing on state change / new `seq` / `foreground`; Orchestrator returns `IgnoredIntent` for `RestartApp`/`CloseApp` outside `Background`/`Failed`; `ActionKind.AppMenu` removed, `appMenu` in `input.json` rejected as an unknown enum value.

**Files:** `web/shell/src/focus.ts` (menu logic), `AppMenu.tsx` (new), `Placeholder.tsx`, `App.tsx`, `bridge.ts`, `tokens.css`; `Orchestrator.Stop.cs`/`Orchestrator.Launch.cs` (IgnoredIntent), `Action.cs`, `ConfigValidation.cs`, `InputConfig.cs`; tests in `focus.test.ts`, `Placeholder.test.tsx`, `OrchestratorStopTests.cs`, `OrchestratorLoopTests.cs`, `ConfigValidationTests.cs`, `CoreMessageContractTests.cs`, `OrchestratorActionTests.cs`, `OrchestratorHomeTests.cs`.

**Review:** 6 low findings: 3 patched (config test strictness, loop log test for Close/Restart, scrim assertion), 3 rejected (see triage log). Follow-up review recommended: false.

**Verification (UM760):** `dotnet test` 1060/1060 passed; `npm run build` in `web/` passed (contracts refreshed, typecheck, build); `npm test` 79/79 passed. Manual check (OK on `Failed` tile → "Uruchom ponownie" → `stopWithoutHost`, "Zamykam…", return to `Failed` after `stoppingTimeout`) not run live: needs physical input; covered by Orchestrator/Shell tests, left for the user's whole-epic acceptance on UM760.

**Residual risks:** menu panel position is derived from the tile index in the active tab with mockup offsets; recheck once tile layout changes.
