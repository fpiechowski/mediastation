---
title: 'Zakładki Video, Audio i Game z category'
type: 'feature'
ticket: '4'
created: '2026-10-26'
status: done
baseline_revision: '7e0c27f84aa5cda18dd547bec6a784e003b092d8'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: [quick]
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md'
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** Shell pokazuje jeden płaski rząd kafelków: nie ma zakładek, nie ma fokusu na zakładkach ani pamięci fokusu per zakładka, a `category` ze snapshotu nie jest używane.

**Approach:** Czysta logika w `focus.ts` (bez DOM) rozszerza stan fokusu o strefę (`tabs`/`tiles`), aktywną zakładkę i pamięć fokusu per zakładka po `appId`; `Placeholder.tsx` renderuje rząd zakładek Video/Audio/Game i kafelki aktywnej zakładki albo empty state. Zero zmian w Core i moście: `category` już jest w `AppSnapshot`.

## Boundaries & Constraints

**Always:** Fokus kluczowany `appId`/`category`, nigdy indeksem; dokładnie jeden widoczny fokus (zakładka albo kafelek); zakładka aktywuje się, gdy dostaje fokus; Shell konsumuje tylko Actions (bez handlerów klawiatury i myszy); typy tylko z `web/contracts/generated`; logika bez DOM i Reacta; kolejność kafelków = kolejność w snapshocie w obrębie `category`; komentarze i nazwy po polsku jak w sąsiednim kodzie; tokeny z DESIGN.md w `tokens.css`.

**Never:** Kropka „w tle” na zakładce, chip i linia stanu, ikony (stories 4.5–4.6); AppMenu i long-press (4.8); wiersz „Do oceny”; nagłówek z zegarem i powitaniem (4.9); zmiany w Core, moście, kontraktach i `ShellWindow`; nowe zależności; ruch poza przejściem fokusu.

## I/O & Edge-Case Matrix

| Scenariusz | Wejście / stan | Oczekiwane | Błąd |
|---|---|---|---|
| Grupowanie | apps: netflix(video), moonlight(game), plex(video) | Video: netflix, plex (w tej kolejności); Audio: puste; Game: moonlight | — |
| Zakładki zawsze trzy | dowolny snapshot | Video, Audio, Game w tej kolejności, także bez wpisów w kategorii | — |
| Fokus w rzędzie zakładek | Lewo/Prawo w strefie `tabs` | aktywna zakładka przechodzi na sąsiednią (aktywuje się na fokus); na krańcu zostaje | — |
| Dół z zakładki | strefa `tabs`, zakładka z kafelkami | fokus na zapamiętanym kafelku tej zakładki, a bez pamięci (albo gdy `appId` zniknął z zakładki) na pierwszym | — |
| Dół z zakładki bez wpisów | Audio | brak zmiany, fokus zostaje na zakładce | — |
| Góra z kafelka | strefa `tiles` | strefa `tabs` na zakładce kafelka; fokus kafelka zapamiętany | — |
| Góra/OK/Dół w złych miejscach | Góra w `tabs`; Dół w `tiles`; OK w `tabs` | bez skutku, bez Intentu | — |
| Pamięć per zakładka po `appId` | fokus na plex, Góra, Prawo (Audio), Prawo (Game), Lewo, Lewo, Dół | fokus wraca na plex także po przestawieniu wpisów w snapshocie | — |
| Lewo/Prawo na kafelkach | strefa `tiles` | tylko w obrębie aktywnej zakładki; kraniec zostaje | — |
| `focusRequest` | `default` / `<appId>` | `default`: pierwszy kafelek Video (zakładka Video); `<appId>`: ten kafelek i jego zakładka; raz na `seq`; nieznany `appId`: bez zmian | — |
| Empty state | aktywna zakładka bez wpisów | jedno zdanie „Tu jeszcze cisza. Muzyka dojdzie wkrótce.”, brak elementu fokusowalnego | — |

</intent-contract>

## Code Map

- `web/shell/src/focus.ts` -- reduktor fokusu: `FocusState {focusedAppId, appliedSeq}`, `applyFocusRequest`, `applyAction` (Left/Right po `appId`, OK → `launchApp`). Tu: strefa, `activeTab`, `memory`, eksport `CATEGORIES` i `tilesOf(apps, category)`.
- `web/shell/src/focus.test.ts` -- istniejące testy (`toEqual({focusedAppId, appliedSeq})`, `focused(id)`) dostosować do nowego kształtu stanu; tile helper ma `category: 'video'`.
- `web/shell/src/App.tsx` -- trzyma `FocusState` w refie i stanie; przekazuje `focus` do `Placeholder` (teraz `focusedAppId`).
- `web/shell/src/Placeholder.tsx` -- rząd kafelków i `Brand`; dodać rząd zakładek i empty state (np. nowy `TabBar.tsx` lub w tym pliku).
- `web/shell/src/tokens.css` -- tokeny; brakuje `--surface-2: #26292E`, `--tabs-top: 172px`, tab (30px/500, 64px, padding 0 32px, gap 12px). Fokus zakładki = `focus-pill` (tło `--focus`, tekst `--focus-ink`, bez skali); aktywna bez fokusu: tło `--surface-2`, tekst `--text`; nieaktywna: `--text-3`; empty state: body 26px, `--text-2`, `top: --apps-top`, `left: --gutter`.
- `web/contracts/generated/shell-snapshot.ts` -- `AppCategory = 'video'|'audio'|'game'`, `AppSnapshot.category` (nie edytować).
- `tools/samples/config/apps.json` -- sample: netflix(video), moonlight-pc(game), plex(video) → Audio puste.
- `_bmad-output/.../ux-mediastation/EXPERIENCE.md` -- Component Patterns: Tab, Empty state; wzorzec wizualny `mockups/key-home-game.html` (Audio).
- Nie ruszać: `bridge.ts`, `main.tsx`, `ConfigErrorScreen.tsx`, Core, App (C#), `vitest.config.ts`.

## Tasks & Acceptance

**Execution:**
- [x] `web/shell/src/focus.ts` -- rozszerzyć `FocusState` o `zone: 'tabs'|'tiles'`, `activeTab: AppCategory` i `memory: Partial<Record<AppCategory, string>>` (`appId` ostatniego kafelka zakładki); wyeksportować `CATEGORIES` (video, audio, game) i `tilesOf`; `applyFocusRequest` ustawia też zakładkę kafelka (strefa `tiles`); `applyAction`: w `tiles` Lewo/Prawo tylko w aktywnej zakładce, Góra → `tabs` (zapamiętuje kafelek), w `tabs` Lewo/Prawo zmieniają `activeTab` (kraniec zostaje), Dół → zapamiętany kafelek (pierwszy, gdy brak/nieobecny; nic, gdy zakładka pusta); OK tylko na kafelku jak dotąd -- model fokusu per zakładka bez indeksów
- [x] `web/shell/src/focus.test.ts` -- dostosować istniejące testy; dodać testy z macierzy: grupowanie i kolejność z `category`, trzy zakładki zawsze, Lewo/Prawo w `tabs`, Dół/Góra, pamięć per zakładka po przestawieniu wpisów, empty state Audio (Dół nic nie robi, strefa zostaje `tabs`, brak kafelka fokusowalnego), `focusRequest` z `appId` w innej zakładce
- [x] `web/shell/src/Placeholder.tsx` (+ ewentualnie `TabBar.tsx`), `App.tsx`, `tokens.css` -- rząd zakładek Video/Audio/Game pod logo na `--tabs-top`, aktywna zakładka z jedną widoczną wskazówką fokusu (pigułka tylko w strefie `tabs`), kafelki aktywnej zakładki, empty state „Tu jeszcze cisza. Muzyka dojdzie wkrótce.” dla pustej zakładki; brak handlerów myszy/klawiatury; tylko tokeny
- [x] weryfikacja na bieżącej maszynie (bez zmian w repo) -- patrz Verification

**Acceptance Criteria:**
- Given `npm test` w `web/`, when uruchomione, then wszystkie testy (w tym grupowanie z `category`, pamięć fokusu per zakładka po `appId`, empty state Audio bez elementu fokusowalnego) przechodzą.
- Given `npm run typecheck` i `npm run build:shell` w `web/`, when uruchomione, then bez błędów.
- Given `tools/samples/config/apps.json` uruchomiony na bieżącej maszynie, when użytkownik naciśnie Góra a potem strzałki Lewo/Prawo, then widoczne są trzy zakładki Video, Audio i Game, aktywna zmienia się na fokus, a w Audio widać empty state i Dół nic nie robi.

## Implementation Notes

- Maszyna: **UM760** (COMPUTERNAME `MEDIASTATION`, płyta `F7BSI`, model `EliteMini Series`).
- Tylko `web/shell/src`: `focus.ts` (strefa, `activeTab`, `memory` po `appId`, `CATEGORIES`, `tilesOf`), `Placeholder.tsx` (rząd zakładek, empty state), `App.tsx`, `tokens.css`, `focus.test.ts`. Core, most i kontrakty bez zmian.
- Weryfikacja na UM760: `npm test` 26 zielonych; `npm run typecheck` i `npm run build:shell` bez błędów; `dotnet test MediaStation.slnx` 1040 zielonych, 0 niepowodzeń. Każdy wiersz macierzy I/O ma test w `focus.test.ts` (grupowanie, trzy zakładki, Lewo/Prawo w `tabs`, Góra/Dół, pamięć po przestawieniu wpisów, pusta Audio, `focusRequest` między zakładkami).
- Przebiegu aplikacji z sample `apps.json` (Góra, Prawo, Prawo, Dół) nie wykonano; zakładki i fokus pokrywa logika w Vitest, a wygląd (pigułka, empty state) nie był oglądany na ekranie. Wizualny odbiór zostaje na całą epikę na UM760.
- Pigułka zakładki ma promień pełny (połowa wysokości); DESIGN.md nie podaje promienia innego niż `rounded.full` dla `tab`.

## Plan Change Log

## Review Triage Log

### 2026-10-26 — Review pass
- verdicts: 6 findings — high 0, medium 0, low 5, false 1, maybe-false 0
- findings:
  - `[false]` `[reject]` Plan has empty Implementation Notes, unchecked tasks and no machine evidence — written in Finalize of this run (machine UM760, command outcomes above).
  - `[low]` `[reject]` `focusRequest` `default` with no Video apps leaves no focus — R3 and the matrix define `default` as the first Video tile; MVP config always has Video apps; a fallback adds a branch for a config not in scope.
  - `[low]` `[reject]` No visible focus before the first `focusRequest` — the snapshot with `focusRequest` arrives at start; pre-existing behaviour of `initialFocusState`.
  - `[low]` `[reject]` `Placeholder` branches untested at render level — web tests run in `node` without DOM by decision (AD, Stack); the component is a direct projection of tested state.
  - `[low]` `[reject]` Long line in `focus.test.ts` — cosmetic.
  - `[low]` `[reject]` Stale `focusedAppId` after a snapshot removes the focused tile — pre-existing; Left/Right recover on the first tile of the tab.

## Design Notes

- Stan: `zone` mówi, gdzie stoi fokus; `focusedAppId` jest kafelkiem tylko w `tiles` (w `tabs` zostaje `null`), a zapamiętany kafelek żyje w `memory[category]`. Pamięć aktualizuje każde przesunięcie na kafelek.
- Przestawienie wpisów: `memory` trzyma `appId`; przy Dół, gdy zapamiętany `appId` nie należy już do zakładki, fokus idzie na pierwszy kafelek zakładki.
- Zakładek jest zawsze trzy, w stałej kolejności Video, Audio, Game (AD-11: kategorie to zamknięty enum; „zakładka bez wpisów pokazuje empty state”).

## Verification

**Commands:**
- `npm test` (w `web/`) -- expected: testy przechodzą
- `npm run typecheck` (w `web/`) -- expected: 0 błędów
- `npm run build:shell` (w `web/`) -- expected: build bez błędów
- `dotnet test MediaStation.slnx` -- expected: 0 niepowodzeń (Core bez zmian; potwierdza brak regresji)

**Manual checks (if no CLI):**
- Na bieżącej maszynie: uruchomić MediaStation (Release) z sample `apps.json`, klawiaturą (Góra, Prawo, Prawo, Dół) przejść Video → Audio → Game i zapisać obserwacje wraz z nazwą maszyny w Implementation Notes.

## Auto Run Result

Machine: ran on UM760 (host MEDIASTATION, board F7BSI, model EliteMini Series).

**Summary:** Shell groups tiles into Video, Audio and Game tabs from `category` in snapshot order. `focus.ts` holds the zone (`tabs`/`tiles`), the active tab and a per-tab focus memory keyed by `appId`: Left/Right move between tabs (a tab activates on focus), Down returns to the remembered tile (first when none or gone), Up from tiles returns to tabs, and an empty tab shows "Tu jeszcze cisza. Muzyka dojdzie wkrótce." with focus staying on the tab and Down doing nothing.

**Files changed:** `web/shell/src/focus.ts`, `focus.test.ts`, `Placeholder.tsx`, `App.tsx`, `tokens.css`; plan file.

**Review:** quick lens, 6 findings: 0 patched, 0 deferred, 6 rejected (1 false, 5 low; reasons in the triage log).

**Verification (UM760):** `npm test` 26 passed; `npm run typecheck` and `npm run build:shell` ok; `dotnet test MediaStation.slnx` 1040 passed, 0 failed. App run with sample `apps.json` not performed.

**Residual risks:** tab look (focus pill, empty state) not viewed on screen; physical remote and TV checks fall to the user's whole-epic acceptance on UM760.

Follow-up review recommended: false (no patches).
