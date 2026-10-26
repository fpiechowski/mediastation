---
title: 'Nagłówek Home, title-safe i ekran błędu konfiguracji'
type: 'feature'
ticket: '9'
created: '2026-10-26'
status: done
baseline_revision: '10a8a4569051bef68afe01607a95a67febca3279'
route: 'full'
route_source: 'auto'
risk: 'low'
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

**Problem:** Home ma tylko logo (bez powitania i zegara), rząd 3 kafelków 544px i skalowany fokus wychodzą poza title-safe 5% (96px/54px), a ekran błędu konfiguracji ucina listę błędów bez informacji (F17).

**Approach:** Czyste funkcje bez DOM (`greeting`, `formatClock`, geometria rzędu, limit błędów) z testami Vitest; `Header` (logo, separator, avatar + powitanie, zegar, puste miejsce na przycisk aktualizacji) zastępuje samo `Brand` na Home; rząd kafelków przesuwa się tak, by kafelek w fokusie (po skali) mieścił się w title-safe; ekran błędu pokazuje ograniczoną liczbę błędów i wiersz „…i jeszcze K”.

## Boundaries & Constraints

**Always:** Typy TS tylko z `web/contracts/generated`; logika bez DOM i Reacta; tekst ≥ 24px; tokeny z `tokens.css` (odbicie `DESIGN.md`); komentarze i nazwy po polsku; powitanie i zegar to dekoracja bez fokusu; jedyny fokus bez zmian.

**Never:** Przycisk aktualizacji i `update-status` (epic-deploy-resilience, tu tylko miejsce w layoucie), wiersz „Do oceny”, logika kont (imię „Eliza” to stała), zmiany Core/App/mostu/kontraktów, ręczna edycja `web/contracts/generated`, nowe zależności.

## I/O & Edge-Case Matrix

| Scenariusz | Wejście / stan | Oczekiwane | Błąd |
|---|---|---|---|
| Powitanie, granice | 04:59 / 05:00 / 17:59 / 18:00 / 00:00 | „Dobry wieczór, Eliza” / „Dzień dobry, Eliza” / „Dzień dobry, Eliza” / „Dobry wieczór, Eliza” / „Dobry wieczór, Eliza” | — |
| Zegar | 09:05, 21:47 | „09:05”, „21:47” (24h, dwie cyfry) | — |
| Rząd w title-safe | 1–4 kafelki, fokus na dowolnym | widoczne granice kafelka w fokusie (skala 1.08 + obrys) w [96, 1824]; reszta rzędu przesunięta o ten sam offset | — |
| Błędów ≤ limit | 1–5 błędów | wszystkie, brak wskaźnika | — |
| Błędów > limit | 10 błędów | 5 pierwszych + „…i jeszcze 5 błędów” | — |
| Odmiana wskaźnika | K = 1 / 2–4 / 5+ / 12 / 22 | „błąd” / „błędy” / „błędów” / „błędów” / „błędy” | — |

</intent-contract>

## Code Map

- `web/shell/src/Placeholder.tsx` -- `Brand` (zostaje dla config-error), rząd `.tiles`, `AppMenu` kotwiczony po indeksie; tu podpiąć `Header` i offset rzędu.
- `web/shell/src/ConfigErrorScreen.tsx` -- lista błędów + `overflow: hidden` w `.config-error__content` (źródło F17); `tokens.css` -- `.placeholder .brand`, `.tiles`, `.appmenu` (left liczony z `--appmenu-index`), `.config-error*`.
- `web/shell/src/focus.ts` -- `tilesOf`, `FocusState`; nie zmieniać. `main.tsx` -- kursor ukryty w bezruchu już jest (`.pointer-active`, 2 s); tylko zweryfikować.
- Wzorzec testów: `tileView.ts` + `tileView.test.ts` (czyste funkcje), `Placeholder.test.tsx` (`renderToStaticMarkup`). Regex w `Placeholder.test.tsx` zależy od `<li class="tab…">`; nie psuć markupu zakładek.
- `DESIGN.md` → Layout & Spacing, `avatar`, `clock`, `greeting`, `update-button` (miejsce: prawa grupa `[przycisk] 36px [zegar]`, zegar na gutterze, rośnie w lewo).

## Tasks & Acceptance

**Execution:**
- [x] `web/shell/src/header.ts` + `header.test.ts` (nowe) -- `greeting(date)` (5:00–17:59 „Dzień dobry”, reszta „Dobry wieczór”, + stała `USER_NAME = 'Eliza'`), `formatClock(date)`, `msToNextMinute(date)` -- R6, granice pory dnia
- [x] `web/shell/src/layout.ts` + `layout.test.ts` (nowe) -- stałe geometrii z tokenów; `rowOffset(index, count)` liczy przesunięcie rzędu (px), by kafelek w fokusie po skali 1.08 i z obrysem 5px mieścił się w [96, 1824]; test 1–4 kafelków i każdego indeksu -- title-safe
- [x] `web/shell/src/Header.tsx` (nowy) -- logo + wordmark, separator 2px `line`, avatar (inicjał) + powitanie, zegar z tickiem co minutę (`useEffect`, sprzątanie), pusty `header__update-slot` między powitaniem a zegarem, prawa grupa zakotwiczona na guttera; `Placeholder.tsx` używa `Header` zamiast `Brand`; test renderu (powitanie, zegar, brak fokusu) -- R6
- [x] `web/shell/src/Placeholder.tsx`, `AppMenu.tsx`, `tokens.css` -- rząd i panel AppMenu dostają offset z `rowOffset` przez `--row-offset` (przejście `--focus-duration`); style nagłówka (avatar gradient, tokeny `avatar-*`, `clock`, `greeting`); bez regresji w testach zakładek
- [x] `web/shell/src/configErrors.ts` + test, `ConfigErrorScreen.tsx`, `tokens.css` -- `visibleErrors(errors, MAX=5)` → `{shown, hidden}`, `hiddenLabel(k)` z polską odmianą; ekran pokazuje `shown` i wiersz „…i jeszcze K błędów” (`body`, `text-2`); `-webkit-line-clamp` powodu (2 linie) i jedna linia plik/ścieżka, żeby 5 błędów + wskaźnik mieściło się w title-safe; test renderu z 10 błędami -- R7, F17
- [x] weryfikacja na bieżącej maszynie (bez zmian w repo) -- patrz Verification

**Acceptance Criteria:**
- Given `npm test` i `npm run build` w `web/`, when uruchomione, then przechodzą testy powitania (granice), zegara, geometrii rzędu, odmiany wskaźnika i renderu ekranu z 10 błędami, a build bez błędów.
- Given Release w 1920×1080 na bieżącej maszynie, when Home jest widoczny z fokusem na pierwszym i ostatnim kafelku, then granice nagłówka i kafelka w fokusie leżą w [96, 1824]×[54, 1026] (pomiar `getBoundingClientRect` przez CDP).
- Given kursor myszy ruszony i potem bezruch, when upływa ≥ 2 s, then `cursor` na elementach jest `none`, a po kolejnym ruchu wraca (CDP).
- Given `apps.json` z dziesięcioma błędami, when startuje App, then ekran błędu pokazuje 5 błędów i „…i jeszcze 5 błędów”, a jego treść mieści się w title-safe.

## Implementation Notes

- Maszyna: **UM760** (COMPUTERNAME `MEDIASTATION`, płyta `F7BSI`, model `EliteMini Series`; ustalone zgodnie z AGENTS.md).
- Odchylenie: logika nagłówka w `headerInfo.ts` (nie `header.ts`), bo na NTFS bez rozróżniania wielkości liter kolidowała z `Header.tsx` (TS1149); dodatkowo `initial()` dla avatara. `Brand` przeniesione z `Placeholder.tsx` do `Header.tsx` (importuje je `ConfigErrorScreen`).
- Weryfikacja na UM760: `npm test` 118 zielonych, `npm run build:shell` ok, `dotnet build MediaStation.slnx` 0 błędów / 0 ostrzeżeń.
- Pomiar w headless Edge 156 na zbudowanym shellu z atrapą mostu (1920×1080, 3 kafelki, `getBoundingClientRect`): pierwszy kafelek w fokusie left 102.2, ostatni right 1817.8 (w [96, 1824]); nagłówek x 112–1808, y 56–104; kursor `none` w bezruchu, `auto` po ruchu, `none` po 2 s; ekran z 10 błędami: 5 pozycji + „…i jeszcze 5 błędów”, dół wskaźnika y 813 (< 1026).
- Nie wykonano: przebieg Release w WebView2 z prawdziwym `apps.json` (pomiar silnikiem Chromium jak w WebView2, ścieżka hosta niesprawdzona); zostaje na odbiór epiki na UM760.

## Plan Change Log

## Review Triage Log

### 2026-10-26 — Review pass
- verdicts: 7 findings — high 0, medium 0, low 5, false 2, maybe-false 0
- findings:
  - `[low]` `[patch]` Brak dowodów weryfikacji w planie — uzupełnione: zadania odhaczone, wyniki komend i pomiarów w Implementation Notes.
  - `[low]` `[patch]` Zmiana nazwy `header.ts` → `headerInfo.ts` i `initial()` bez opisu — opisane w Implementation Notes (kolizja nazw na NTFS).
  - `[low]` `[patch]` `Brand` przeniesione do `Header.tsx` — opisane w Implementation Notes; zachowanie bez zmian.
  - `[low]` `[patch]` Brak testu okablowania `--row-offset` w `Placeholder` — dodane 3 testy (ostatni/pierwszy kafelek, strefa zakładek, otwarte menu).
  - `[false]` `[reject]` Timer zegara działa mimo wstrzykniętego `now` — efekty nie działają w `renderToStaticMarkup`, a w aplikacji `now` nie jest podawane; brak szkody.
  - `[low]` `[reject]` Zegar może się spóźnić po uśpieniu — najwyżej do następnego ticka (≤ 1 min), poprawka wymagałaby nasłuchu visibility/resync; nieopłacalna.
  - `[false]` `[reject]` Brak `overflow: hidden` w ekranie błędu grozi wylaniem — limit 5 pozycji z line-clamp ogranicza wysokość; pomiar 10 błędów: dół wskaźnika y 813 przy granicy 1026.

## Design Notes

- Granice powitania nie są w `DESIGN.md`/`EXPERIENCE.md` (tylko „pora dnia + imię”); decyzja planu: dwa warianty po polsku, granica 05:00 i 18:00. Imię to stała (brak kont, EXPERIENCE → Avatar).
- Geometria: kafelek 544 skalowany 1.08 rośnie o ~21.8px na stronę + obrys 5px (~5.4px po skali). Kafelek pierwszy w fokusie wychodzi o ok. 11px poza 96px, trzeci wychodzi poza 1824 — dlatego offset rzędu zamiast zmiany rozmiarów z `DESIGN.md` (zasada „wiersze przewijają się poziomo, lewa krawędź trzyma gutter”).
- Limit błędów jest stały (5), a nie mierzony z DOM, żeby logika dała się testować bez przeglądarki; wysokość pozycji ograniczona przez line-clamp.

## Verification

**Commands:**
- `npm test`, `npm run build` (w `web/`) -- expected: sukces
- `dotnet build MediaStation.slnx` -- expected: 0 błędów, 0 ostrzeżeń (shell dołączany do hosta)

**Manual checks (if no CLI):**
- Release z CDP (`WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`): pomiar granic nagłówka i kafelków w 1920×1080 przy fokusie na pierwszym/ostatnim kafelku; kursor po bezruchu i ruchu; tymczasowy `apps.json` z 10 błędami → ekran ze wskaźnikiem (zrzut). Przywrócić config po teście.

## Auto Run Result

Machine: ran on UM760 (host MEDIASTATION, board F7BSI, model EliteMini Series).

**Summary:** Home ma nagłówek wg `DESIGN.md` (logo, separator, avatar z powitaniem „Dzień dobry/Dobry wieczór, Eliza” wg pory dnia, zegar co minutę, puste miejsce na przycisk aktualizacji); rząd kafelków i panel AppMenu przesuwają się tak, by kafelek w fokusie mieścił się w title-safe; ekran błędu konfiguracji pokazuje 5 błędów i „…i jeszcze K błędów” (F17). Kursor ukryty w bezruchu już działał i został zweryfikowany.

**Files changed:** `web/shell/src/`: `headerInfo.ts`, `layout.ts`, `configErrors.ts`, `Header.tsx` (nowe, z testami), `Placeholder.tsx`, `ConfigErrorScreen.tsx`, `tokens.css`, `Placeholder.test.tsx`.

**Review:** quick; patches 4 (low), deferred 0, rejected 3 (2 false, 1 low; powody w triage log).

**Verification (UM760):** `npm test` 118, `npm run build:shell` ok, `dotnet build` 0/0; pomiary geometrii, kursora i ekranu z 10 błędami w headless Edge (szczegóły w Implementation Notes).

**Residual risks:** brak przebiegu Release w WebView2 z prawdziwym `apps.json`; granice powitania (05:00/18:00) to decyzja planu, nie `DESIGN.md`; fokus na TV zostaje na odbiór całej epiki na UM760.

Follow-up review recommended: false (patched: 0 high, 0 medium, 4 low).
