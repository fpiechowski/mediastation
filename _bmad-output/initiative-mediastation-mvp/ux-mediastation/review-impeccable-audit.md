---
title: Review — impeccable audit (a11y, kontrast, fokus, 10-foot type, jakość HTML/CSS)
reviewer: impeccable `audit` jako lens w BMad UX Reviewer Gate
date: 2026-10-17
scope:
  - .working/key-home-video.html
  - .working/key-home-game.html
  - .working/key-home-failed-appmenu.html
  - .working/key-netflix-search-osk.html
  - .working/review/*.png
contract: DESIGN.md (tokeny), EXPERIENCE.md (Accessibility Floor)
excluded: responsive / mobile / touch (jedyny viewport to TV 1920×1080)
verdict: PASS z warunkami — 0 critical, 5 high do poprawy przed implementacją
---

# Review — impeccable audit

Kontekst: ~3 m od TV, słabe ciepłe światło, tylko D-pad (kursor żyroskopu wyłącznie w OSK). Kontrasty policzone wzorem WCAG 2.x z hexów tokenów; scrim `#0F1012B8` (72%) i poświata tła (accent 7% na ground → `#181B22`) złożone alfą.

## Health score

| # | Wymiar | Wynik | Kluczowe znalezisko |
|---|---|---|---|
| 1 | Accessibility | 3/4 | Tekst i fokus w shellu z dużym zapasem; słabe punkty: wstrzyknięty fokus na jasnym artworku, podwójny fokus w OSK, kropka na zakładce bez tekstu |
| 2 | Performance | 4/4 | Statyczne mocki; ciężki cień tylko na elemencie w fokusie, animowany jest tylko `transform` |
| 3 | Theming | 3/4 | Tokeny w `:root` zgodne z DESIGN.md; kilka hardcode'ów (Plex tile, avatar, logo `#fff`) |
| 4 | Responsive | — | Pominięte zgodnie z zakresem (1920×1080 only) |
| 5 | Implementation Integrity | 3/4 | Spójny system; overflow karty „Do oceny” i brak title-safe na dole ekranu |
| | **Razem** | **13/16** | **Good** (skala /20 bez wymiaru 4) |

**Implementation Integrity verdict: PASS.** Mocki wyrażają spójny, produktowy system (jeden biały fokus, grafitowa skala, akcent tylko na wskaźnikach), tokeny 1:1 z DESIGN.md. Detektor zgłosił wyłącznie `cramped-padding` (fałszywy alarm, patrz niżej) i advisory `flat-type-hierarchy`.

## Tabela kontrastów

Próg tekstu wg kontraktu: ≥ 5.5:1 (EXPERIENCE → Accessibility Floor). Próg non-text / fokus: ≥ 3:1 względem przyległych kolorów (WCAG 1.4.11, 2.4.13).

### Tekst

| Para (tekst / tło) | Gdzie | Ratio | Wynik |
|---|---|---|---|
| `text #FFFFFF` / `ground #0F1012` | wordmark, „Do oceny”, nazwa w fokusie | 19.04 | pass |
| `text` / poświata `#181B22` | wordmark/zakładki w górnym pasie | 17.23 | pass |
| `text` / `surface #1E2024` | tytuł karty, pozycje AppMenu | 16.31 | pass |
| `text` / `surface-2 #26292E` | aktywna zakładka, „Ocenione”, klawisze OSK | 14.59 | pass |
| `text` / scrim na `surface` `#131417` | chip na kafelku | 18.42 | pass |
| `text` / scrim na `tile-gradient-top` `#17181B` | chip (górny róg gradientu) | 17.75 | pass |
| `text-2 #C4C7CC` / `ground` | zegar, linia stanu, pusta Audio | 11.23 | pass |
| `text-2` / poświata `#181B22` | powitanie, zegar | 10.17 | pass |
| `text-2` / `surface` | podpowiedź karty, „Pomiń” (ghost) | 9.62 | pass |
| `text-2` / `surface-2` | — (rezerwa) | 8.61 | pass |
| `text-3 #9AA0A6` / `ground` | nazwy poza fokusem, nieaktywne zakładki | 7.21 | pass |
| `text-3` / poświata `#181B22` | nieaktywne zakładki (najgorszy punkt glow) | 6.53 | pass |
| `text-3` / `surface` | podtytuł karty („Sezon 8 · obejrzany”) | 6.18 | pass |
| `text-3` / `surface-2` | nieużywane w mockach | 5.53 | pass (na granicy floor) |
| `tile-mark #E6E8EB` / `tile-gradient-top #2A2D33` | wordmark aplikacji | 11.24 | pass |
| `tile-mark` / `surface` | wordmark aplikacji | 13.29 | pass |
| `avatar-ink #1A1214` / `avatar-to #C77D9B` | inicjał avatara (najgorszy punkt) | 5.99 | pass |
| `qr-ink #111111` / `qr-bg #FFFFFF` | moduły QR | 18.88 | pass |
| Tekst pod scrimem AppMenu (`text-3`→ground / `text`→surface) | tło przy otwartym AppMenu | 1.62 / 2.39 | celowo nieaktywne — patrz M6 |

**Muted text: pass.** `text-3` na `#0F1012` = 7.21, na `#1E2024` = 6.18 — oba z zapasem ponad floor 5.5.

### Fokus i elementy non-text

| Para | Gdzie | Ratio | Wynik |
|---|---|---|---|
| `focus #FFFFFF` / `ground` | obrys kafelka/przycisku (zewnątrz) | 19.04 | pass |
| `focus` / `tile-gradient-top` / `surface` | obrys kafelka (wewnątrz) | 13.80 / 16.31 | pass |
| `focus` / `surface-2` | obrys przycisku, zakładki, klawisza | 14.59 | pass |
| `focus` / `surface` (panel OSK, AppMenu) | obrys klawisza / pozycji | 16.31 | pass |
| `focus` / Netflix `#141414` / `#000000` (pole) | wstrzyknięty ring | 18.42 / 21.00 | pass |
| `focus` / plakat-placeholder `#3A3A3A` | wstrzyknięty ring na kafelku Netflixa | 11.37 | pass (tylko w mocku) |
| `focus` / jasny artwork `#E6E6E6`–`#F5F5F5` | realne plakaty Netflixa | **1.25 / 1.09** | **fail** — H4 |
| `accent #8AB4F8` / `ground` / `surface-2` | kropka stanu, kropka na zakładce | 9.03 / 6.92 | pass |
| `surface-2` / `surface` | granica klawisza OSK, przycisku „Ocenione” na karcie | **1.12** | słabo — M2 |
| `line #33363C` / `surface` | obrys przycisku ghost „Pomiń” | **1.35** | słabo — M2 (dekoracyjny wg kontraktu) |
| `surface` (popup wariantów) / plakat `#3A3A3A` | rząd „ę” nad Netflixem | **1.43** | słabo — M4 |

**Fokus bez koloru:** w shellu kafelek = obrys 5px + skala 1.08 + cień (trzy kanały, czytelne z 3 m — potwierdzone na screenshotach). Przycisk/zakładka/klawisz/pozycja AppMenu = sam obrys 5px; to kształt, nie kolor, więc spełnia floor. W Netflixie tylko obrys — patrz H4.

**Rozmiary tekstu:** wszystkie role produktu ≥ 24px (chip 24, body/button 26, greeting 28, tab/tile-name 30, wordmark/clock/section 32, card-title 34, tile-mark 64). Jedyny tekst < 24px to adnotacja mocka `.nf-note` (20px) — poza produktem. **pass.**

**Copy:** całość po polsku i w tonie z EXPERIENCE („Coś się zacięło. Wciśnij OK”, „Uruchamiam…”, „Zeskanuj telefonem i oceń na Filmwebie”, „Tu jeszcze cisza. Muzyka dojdzie wkrótce.”). Etykiety zakładek Video / Audio / Game zostają po angielsku zgodnie ze spine'em. **pass.**

### Detektor (`impeccable detect --json`)

| Plik | Reguła | Ocena |
|---|---|---|
| wszystkie key-home-* | `cramped-padding` na `.tile` | **False positive** — `.mark` jest wyśrodkowany gridem w kafelku 306px; detektor mierzy komórkę grida, a nie wyrenderowany tekst. Chip ma inset 24px |
| key-netflix-search-osk | `cramped-padding` na `.nf-field` | Poza zakresem — przybliżenie UI Netflixa, nie nasz komponent |
| key-home-video | `flat-type-hierarchy` (advisory) | Częściowo zasadne — patrz L7; hierarchię niesie waga, ale „Do oceny” (32) jest mniejsze niż tytuł karty (34) |

## Findings

### Critical

Brak.

### High

**H1 — Wiersz „Do oceny” wychodzi poza title-safe na dole ekranu**
- Lokalizacja: `key-home-video.html` `.rate` (top 732) + `h2` (~38px + 20 margin) + `.card` 268px → dolna krawędź karty ≈ 1058px, margines 22px; przyciski kończą się ≈ 1034px.
- Wpływ: PC przez HDMI na wielu TV domyślnie dostaje overscan (~2.5–5%, czyli 27–54px na dole). Przy 5% przyciski „Ocenione / Pomiń” — jedyna akcja kulminacji J1 — są ucinane. Nawet bez overscanu 22px przy 112px gutterze bocznym wygląda na przypadek.
- Fix: trzymać dolny margines ≥ 54px (5% action-safe), np. `rate-top` 700 i `h2` margin 12, albo karta 248px; dodatkowo w dokumentacji instalacji wymagać trybu „Just Scan / Fit to screen”. Zaktualizować `spacing.rate-top` w DESIGN.md.

**H2 — Karta „Do oceny” bez zapasu na treść: overflow przy dłuższym tytule**
- Lokalizacja: `key-home-video.html` `.card` / `.info`. Dostępna wysokość 220px; zawartość: h3 39 + 6 + sub ~34 + 18 + hint 2 linie ~68 + 4 + przyciski 56 ≈ 221px — już teraz podpowiedź dotyka przycisków (widoczne na `review/key-home-video.png`).
- Wpływ: tytuł w 2 liniach („Pamiętnik Bridget Jones: W pogoni za rozumem”, `text-wrap: balance`) wypycha przyciski poza kartę (brak `overflow`), a w połączeniu z H1 — poza ekran. Fokus może wylądować na niewidocznym przycisku.
- Fix: `h3` → `-webkit-line-clamp: 2` + `overflow: hidden`; podpowiedź skrócić do jednej linii („Zeskanuj i oceń na Filmwebie”) albo przenieść pod QR; zarezerwować stałe sloty wysokości. Dodać do EXPERIENCE regułę truncacji tytułu.

**H3 — QR: za mała quiet zone na ciemnym tle i ułamkowy rozmiar modułu**
- Lokalizacja: `.qr` (220px, padding 14px) we wszystkich kartach; `components.qr` w DESIGN.md.
- Wpływ: przy 25 modułach moduł = 192/25 = 7.68px → quiet zone 14px ≈ 1.8 modułu (ISO/IEC 18004 wymaga 4). Za białym polem leży ciemne `surface`, które binaryzacja skanera czyta jak ciemne moduły przy finder patternach. Realny URL wyszukiwarki Filmwebu z polskim tytułem (~70 znaków, percent-encoding) to wersja 4–5 (33–37 modułów) → moduł ~5px i quiet zone ~2.8px-modułu. Ułamkowe px → rozmyte krawędzie modułów. 220px na 55" ≈ 14cm — skan z kanapy (3 m, reguła ~10:1) jest na granicy.
- Fix: rozmiar modułu całkowity (np. 6px), quiet zone ≥ 4 moduły białe wewnątrz pola (`qr` ≥ (N+8)×6px, czyli ~270px dla v5); krótszy URL (redirect `ms.local/r/<id>` lub minimalny query) żeby zejść do v2–v3; `shape-rendering: crispEdges`. W mocku użyć prawdziwego QR (patrz L5) i przetestować skan z 3 m.

**H4 — Wstrzyknięty fokus (PageRuntime) ginie na jasnym artworku Netflixa**
- Lokalizacja: `key-netflix-search-osk.html` `.inj`; `components.focus-ring-injected` w DESIGN.md.
- Wpływ: w mocku plakaty są szare (11.37:1), ale realne okładki bywają jasne/białe — biały 5px obrys bez skali i bez cienia daje 1.09–1.25:1 względem przyległego koloru (fail WCAG 2.4.13 / 1.4.11). Bez skali to jedyny sygnał fokusu, więc z 3 m Eliza traci pozycję.
- Fix: ring dwukolorowy — 5px biały + 3px ciemny halo (`box-shadow: 0 0 0 5px #fff, 0 0 0 8px rgba(15,16,18,.9)` lub `outline` + `box-shadow` wewnętrzny `#0F1012`). Zaktualizować `focus-ring-injected` (i rozważyć to samo w shellu na przyszłe jasne ikony aplikacji).

**H5 — Dwa identyczne fokusy jednocześnie w trybie OSK**
- Lokalizacja: `key-netflix-search-osk.html` ekrany 1–2: `.inj` na polu wyszukiwania + `.key.focus` (widoczne na screenshocie: dwa białe obrysy).
- Wpływ: użytkownik D-pada nie wie, który element dostanie strzałkę/OK. Narusza zasadę „jeden fokus na ekranie” i floor „fokus widoczny i jednoznaczny”.
- Fix: gdy OSK otwarta, pole w Netflixie oznaczać inaczej niż fokusem — np. ring 2px `text-3` albo tylko karetka + delikatny podkład; ring 5px biały wyłącznie w OSK. Dopisać do EXPERIENCE → State Patterns (OSK otwarta).

### Medium

**M1 — Kropka „w tle” na zakładce bez tekstu (stan tylko kolorem/kropką)**
- Lokalizacja: `.tab .run` w `key-home-video.html`, `key-home-game.html`.
- Wpływ: EXPERIENCE → Accessibility Floor: „kropka `{colors.accent}` zawsze z tekstem („W tle”)”. Na zakładce tekstu nie ma; 12px kropka z 3 m to ~7mm na 55", łatwo przeoczyć. `aria-label` na gołym `<span>` jest ignorowany (brak roli).
- Fix: albo świadomy wyjątek w EXPERIENCE (kropka na zakładce = skrót, tekst jest na kafelku), albo glif zamiast kropki (np. mała ikona pauzy w akcencie); `role="img"` + `aria-label` w implementacji.

**M2 — Granice klawiszy/przycisków niewidoczne z 3 m (1.12:1 i 1.35:1)**
- Lokalizacja: `.key` (`surface-2` na `surface`), `.btn` „Ocenione” na karcie, `.btn.ghost` (`line` 1.35:1).
- Wpływ: etykiety identyfikują elementy, więc WCAG formalnie przechodzi, ale primary vs ghost są z kanapy praktycznie nierozróżnialne, a przy kursorze żyroskopu w OSK cele do celowania nie mają czytelnych krawędzi.
- Fix: `surface-2` na karcie/panelu podnieść (np. `#30343A`, ~1.4–1.5:1) lub dać primary jaśniejsze tło; ghost obrys `text-3`-owy 2px ≥ 3:1 jeśli ma znaczyć „przycisk”.

**M3 — Linia stanu kafelka w fokusie klei się do „Do oceny”**
- Lokalizacja: `key-home-video.html`, ekran 1: meta 642 + nazwa ~35 + 6 + stan ~34 ≈ 717px vs `h2` na 732 → ~15px.
- Wpływ: „W tle · Pamiętniki wampirów, wstrzymane” wizualnie wiąże się z sekcją „Do oceny” (proximity), szczególnie że tytuł się powtarza.
- Fix: przy rewizji H1 zapewnić ≥ 36px (`spacing.5`) między meta a nagłówkiem sekcji, np. skrócić `meta` lub przesunąć wiersz — rozwiązać łącznie z H1/H2.

**M4 — Popup wariantów diakrytycznych: słaba granica i brak wskazania źródła/dostępności**
- Lokalizacja: `key-netflix-search-osk.html` ekran 2 `.variants`.
- Wpływ: popup w `surface` nad plakatami ma 1.43:1, wygląda jak samotny klawisz; klawisz „e” traci oznaczenie; żaden klawisz nie sygnalizuje, że ma warianty (jedyna droga do polskich znaków to long-press — świadomy wyjątek floor, ale bez podpowiedzi nieodkrywalny).
- Fix: popup z cieniem `--focus-shadow` i obrysem `line`, klawisz źródłowy w stanie „pressed” (`surface-2` jaśniejsze); na klawiszach a c e l n o s z mały wskaźnik wariantów (kropka/ząbek w rogu) albo stała podpowiedź w panelu „Przytrzymaj OK: ą ć ę ł ń ó ś ź ż” (≥ 24px).

**M5 — Brak wizualizacji kursora żyroskopu w OSK**
- Lokalizacja: `key-netflix-search-osk.html`; DESIGN → OSK key („ten sam obrys pod wskaźnikiem”).
- Wpływ: stan pointer-hover vs D-pad focus nie jest pokazany; nie wiadomo, czy wskaźnik i fokus mogą rozjechać się (dwa wyróżnienia naraz — ten sam problem co H5).
- Fix: dodać ekran z kursorem: widoczny wskaźnik (np. 24px kropka z ciemnym halo) + zasada „pointer przenosi fokus” (jeden ring).

**M6 — AppMenu bez widocznej nazwy aplikacji; scrim gasi kontekst Failed**
- Lokalizacja: `key-home-failed-appmenu.html` ekran 2 `.appmenu` (`aria-label` tylko w DOM); scrim zbija chip Failed do ~1.6–2.4:1.
- Wpływ: z 3 m nie widać, którego kafelka dotyczy „Zamknij”, a przyczyna („Coś się zacięło”) jest nieczytelna pod scrimem.
- Fix: nagłówek w panelu „Netflix” (`tile-name` 30/600) + opcjonalnie jedna linia stanu; albo wyłączyć kafelek-właściciela spod scrimu.

### Low

**L1 — Brak ścieżki `prefers-reduced-motion`** dla `transform: scale(1.08)` (150 ms). Fix: w reduced-motion zostawić obrys + cień, skalę włączać bez tranzycji. (`.tile` transition, wszystkie key-home-*.)

**L2 — Semantyka mocków** — przyciski, zakładki, klawisze to `<span>/<div>` bez ról; brak `role="tablist"/tab` + `aria-selected`; `aria-label` na `span.run` bez roli; `svg` w `.nf-field` bez `aria-hidden`. Dla WebView2 + Narratora w implementacji użyć `<button>`, `role="tab"`, `aria-current`. (Wszystkie pliki.)

**L3 — Hardcode poza tokenami** — `.tile.plex` `#24272C/#1B1D21` i avatar `#E8A87C/#C77D9B/#1A1214` w `key-home-failed-appmenu.html`; `stroke="#fff"` w symbolu logo (zamiast `currentColor`). Plex wygląda inaczej niż w pozostałych mockach. Fix: tokeny `var(--…)`, jeden wygląd kafelka.

**L4 — Font nie jest dołączony** — `Roboto Flex` tylko lokalnie, fallback Roboto/Segoe UI ignoruje `opsz` i wagę 650. Fix: bundlować variable font w shellu (`@font-face`, `font-display: block` — offline TV).

**L5 — QR w mocku nie jest prawdziwym kodem** (brak timing patterns/format info) — nie da się zweryfikować skanowalności H3. Fix: wygenerować realny QR dla przykładowego URL.

**L6 — Promień wstrzykniętego ringu ≠ promień elementu** — `.inj` ma 22px na polu z 4px (widać na screenshocie); DESIGN mówi jednocześnie „obrys przyjmuje promień elementu” i `radius: {rounded.tile}`. Fix: ujednolicić kontrakt — ring bierze `getComputedStyle(el).borderRadius` (min. 4px).

**L7 — Odwrócona hierarchia nagłówków** — `section-title` 32 < `card-title` 34 (detektor: krok 1.23). Fix: sekcja 36/600 albo karta 32/600.

**L8 — Kafelek Failed/Starting bez linii stanu w meta** (Background ją ma) — niespójny wzorzec. Fix: albo dodać linię stanu, albo w DESIGN zapisać, że Failed/Starting komunikuje tylko chip.

## Positive findings

- Paleta tekstu w całości ≥ 5.5:1, łącznie z poświatą tła; muted text ma realny zapas.
- Fokus w shellu trzykanałowy (obrys + skala + cień) — bardzo dobrze czytelny z kanapy, bez koloru.
- Stan Failed bez czerwieni: ikona + tekst w chipie; Background: kropka + tekst + chip z ikoną pauzy.
- Wszystkie role tekstu ≥ 24px; copy po polsku, krótkie, w tonie.
- Tokeny w `:root` 1:1 z DESIGN.md, `cursor: none`, stały viewport 1920×1080, animowany tylko `transform`.

## Recommended actions

1. **[High] `/impeccable layout`** — H1, H2, M3: przebudować pionowy budżet Home (title-safe, sloty karty, odstęp meta→sekcja).
2. **[High] `/impeccable harden`** — H3, H4, H5, L5: QR (moduł, quiet zone, długość URL), dwukolorowy ring wstrzykiwany, jeden fokus w trybie OSK.
3. **[Medium] `/impeccable clarify`** — M1, M4, M6: tekst przy stanie zakładki, podpowiedź diakrytyków, nagłówek AppMenu.
4. **[Medium] `/impeccable polish`** — M2, M5, L1–L4, L6–L8: granice komponentów, kursor żyroskopu, reduced-motion, semantyka, tokeny, font, hierarchia.
