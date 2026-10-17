# Validation Report — mediastation

- **DESIGN.md:** `C:\Users\fpiec\mediastation\_bmad-output\initiative-mediastation-mvp\ux-mediastation\DESIGN.md`
- **EXPERIENCE.md:** `C:\Users\fpiec\mediastation\_bmad-output\initiative-mediastation-mvp\ux-mediastation\EXPERIENCE.md`
- **Run at:** 2026-10-17T12:52:53+02:00

## Overall verdict

Para spine'ów jest spójna i w większości gotowa do source-extract: wszystkie kolory mają hex i deklarowany kontrast (zweryfikowany), każda referencja `{path.to.token}` się rozwiązuje, J1–J4 mają protagonistkę, kroki, kulminację i ścieżkę awarii, a Handoff do architektury nazywa delty. Brak findingów critical. Przed handoffem trzeba domknąć trzy high z rubryki: brakującą sekcję Open assumptions (wymaganą przez memlog), rozjazd w sprawie Action `Keyboard` (spine ≠ najnowszy wpis memlogu ≠ brief/AD-9) oraz brak stanu „aplikacja pada na pierwszym planie”.

Dwie soczewki impeccable (critique: PASS WITH FIXES, 27/40; audit: PASS z warunkami, 13/16) potwierdzają, że shell trzyma kanon kategorii Google TV / Apple TV, ale przesuwają obraz w stronę key-screen mocków: dokładają 8 high, które dotyczą głównie jedynej funkcji ponad parytet z VIDAA i reguły jednego fokusu — podwójny ring w trybie `osk`, QR nieskanowalny z kanapy, wiersz „Do oceny” poza title-safe i bez zapasu na dłuższy tytuł, wstrzyknięty fokus znikający na jasnym artworku Netflixa. Obie soczewki niezależnie trafiają w te same miejsca (OSK, QR, title-safe), co podnosi pewność tych findingów.

Wszystkie 11 high ma rozstrzygnięcie w `.working/review-resolutions.md` (R1–R15, status: założenia przyjęte po „Dokończ”, do potwierdzenia przez użytkownika). Jedyna luka: reguła truncacji tytułu karty „Do oceny” (audit H2) jest pokryta przez R3 tylko częściowo. Otwarte pozostaje też fizyczne sprawdzenie QR na TV z 3 m (R2). Rozstrzygnięcia trzeba jeszcze wnieść do spine'ów i mocków w kolejnym Update.

Liczniki: Rubric: 0 critical · 3 high · 8 medium · 15 low | Impeccable critique: 0 critical · 3 high · 5 medium · 7 low | Impeccable audit: 0 critical · 5 high · 6 medium · 8 low

## Category verdicts

- Flow coverage — adequate
- Token completeness — adequate
- Component coverage — adequate
- State coverage — adequate
- Visual reference coverage — adequate
- Bloat & overspecification — strong
- Inheritance discipline — adequate
- Shape fit — adequate

## Findings by severity

### Critical (0)

Brak.

### High (11)

**[State coverage]** — Brak stanu „aplikacja pada na pierwszym planie” (§ EXPERIENCE.md → State Patterns → Kafelek — cykl życia)
AD-10 prowadzi do Failed przez koniec procesu / utratę CDP / timeout, ale spine opisuje Failed tylko jako wygląd na Home. Nie wiadomo, co Eliza widzi, gdy Netflix lub Moonlight zniknie w trakcie oglądania.
Fix: Dodać wiersz: Foreground → Failed ⇒ Home, fokus na kafelku Failed, chip błędu (bez dialogu) — albo inna decyzja.
Rozstrzygnięcie: R7 — MediaStation sama wraca na Home, fokus na kafelku Failed, jedno OK otwiera AppMenu z „Uruchom ponownie”. Powiązane: R8 (zerwane CDP → po limicie Failed).

**[Inheritance discipline]** — Action `Keyboard` poza webem — decyzja memlogu nie trafiła do spine'u (§ EXPERIENCE.md → IA → OSK; Interaction Primitives → OSK krok 1; Handoff; Deferred)
Memlog: „Action Keyboard z pilota: nie w MVP … deferred”. Spine nadal mówi „Action `Keyboard` w innych aplikacjach”, „przycisk do ustalenia”, Handoff „Nieustalony”, Deferred milczy. To zawężenie zakresu wobec briefu i AD-9.
Fix: Zapisać decyzję w IA, Interaction Primitives i Deferred; w Handoff delta „OSK poza webem tylko z klawiatury/gamepada — zawężenie AD-9/briefu → `bmad-correct-course`”.
Rozstrzygnięcie: R10 — Action `Keyboard` z pilota nie w MVP; do Deferred i do Handoff jako zawężenie AD-9 (bmad-correct-course).

**[Shape fit]** — Brak sekcji „Open assumptions” w EXPERIENCE.md (§ EXPERIENCE.md (całość); DESIGN.md `[ASSUMPTION]`)
Memlog wymaga tej sekcji; ~30 rozproszonych tagów `[ASSUMPTION]` bez zbiorczej listy — downstream nie odróżni założeń roboczych od decyzji.
Fix: Dodać sekcję z listą (założenie → lokalizacja → status), łącznie z założeniami z DESIGN.md.
Rozstrzygnięcie: R11 — sekcja „Open assumptions” w EXPERIENCE.md, tabela (id, treść, miejsce) z obu spine'ów; tagi inline zostają z odnośnikiem.

**[Impeccable critique]** — F1 — Dwa ringi fokusu naraz w OSK (§ key-netflix-search-osk.html #1–#2)
Pole wyszukiwania i klawisz OSK mają identyczny biały ring 5 px; łamie „dokładnie jeden fokus”, pole wygrywa uwagą.
Fix: W trybie `osk` pole w stylu „pole aktywne” (2 px `text-2` / caret), biały ring tylko na klawiszu; token `focus-ring-injected--input-active`.
Rozstrzygnięcie: R1 — pole jako „active input” (kursor + linia 2 px `text-2`), białe ringi wyłącznie na klawiszu OSK.

**[Impeccable critique]** — F2 — QR za mały na skan z kanapy (~3 m) (§ key-home-video.html, karta „Do oceny”)
Realny URL Filmwebu da wersję 4–5 → ~5,5 px/moduł, kod ~12 cm na 55" — skan realnie z 1–1,5 m; quiet zone ≈ 2,5 modułu.
Fix: Krótszy payload + ECC L, QR rośnie do ~320–360 px przy fokusie w wierszu, quiet zone ≥ 4 moduły, test na sprzęcie.
Rozstrzygnięcie: R2 — krótki URL, ECC L, quiet zone ≥ 4 moduły, ~240 px w spoczynku / ~360 px z fokusem w wierszu; test dwoma telefonami z 3 m nadal otwarty.

**[Impeccable critique]** — F3 — Wiersz „Do oceny” upchnięty i poza title-safe (§ key-home-video.html #1–#2)
Nagłówek klei się do metadanych Netflixa (~30 px nad, 20 px pod), dół kart 22 px od krawędzi (title-safe 54 px); `flat-type-hierarchy`.
Fix: Model Google TV: peek w spoczynku, scroll strony w górę przy fokusie w wierszu, dół ≥ 54 px, odstęp 48/20.
Rozstrzygnięcie: R3 — title-safe 5% (96/54 px), peek w spoczynku, scroll w górę przy fokusie, większy odstęp nad nagłówkiem.

**[Impeccable audit]** — H1 — Wiersz „Do oceny” poza title-safe (§ key-home-video.html `.rate`; `spacing.rate-top`)
Dół karty ≈ 1058 px (margines 22 px); przy overscanie 5% przyciski „Ocenione / Pomiń” — kulminacja J1 — są ucinane.
Fix: Dolny margines ≥ 54 px; zaktualizować `spacing.rate-top`; w instalacji wymagać „Just Scan”.
Rozstrzygnięcie: R3 — title-safe 5%, peek + scroll wiersza w górę przy fokusie.

**[Impeccable audit]** — H2 — Karta „Do oceny” bez zapasu: overflow przy dłuższym tytule (§ key-home-video.html `.card` / `.info`)
Zawartość ≈ 221 px na 220 px dostępnych; dwuliniowy tytuł wypycha przyciski poza kartę, fokus może trafić na niewidoczny przycisk.
Fix: `line-clamp: 2` dla tytułu, hint w jednej linii, stałe sloty wysokości; reguła truncacji w EXPERIENCE.
Rozstrzygnięcie: Częściowo R3 (więcej miejsca po scrollu wiersza). Reguła truncacji tytułu i stałe sloty karty — nierozstrzygnięte w R1–R15.

**[Impeccable audit]** — H3 — QR: za mała quiet zone i ułamkowy moduł (§ `.qr`; `components.qr`)
Quiet zone ≈ 1,8 modułu (norma 4) na ciemnym tle; realny URL → wersja 4–5, moduł ~5 px z rozmytymi krawędziami.
Fix: Całkowity moduł, quiet zone ≥ 4 moduły, krótszy URL, `crispEdges`, realny QR w mocku i test z 3 m.
Rozstrzygnięcie: R2 — quiet zone ≥ 4 moduły, pełne piksele, krótki URL, ECC L; test na TV otwarty.

**[Impeccable audit]** — H4 — Wstrzyknięty fokus ginie na jasnym artworku Netflixa (§ `.inj`; `components.focus-ring-injected`)
Biały 5 px bez skali i cienia: 1,09–1,25:1 na jasnych plakatach (fail WCAG 2.4.13 / 1.4.11).
Fix: Ring dwukolorowy: 5 px biały + 3 px ciemny halo.
Rozstrzygnięcie: R4 — biały obrys 5 px + ciemna obwódka 3 px `ground` (#0F1012), bez skali i cienia.

**[Impeccable audit]** — H5 — Dwa identyczne fokusy w trybie OSK (§ key-netflix-search-osk.html #1–#2)
Użytkownik D-pada nie wie, który element dostanie strzałkę/OK.
Fix: Pole oznaczone inaczej niż fokusem; biały ring tylko w OSK; dopisać do State Patterns.
Rozstrzygnięcie: R1 — jeden fokus w trybie `osk`, pole jako „active input”.

### Medium (19)

**[Flow coverage]** — Kto wybiera profil Netflixa — niejednoznaczne (§ EXPERIENCE.md → Key Flows J1; State Patterns → Profile gate)
J1 krok 3 i State Patterns mówią, że profil wybiera driver, brief krok 4 — że Eliza, AD-10 tylko „Driver obsługuje wybór profilu”. Downstream nie wie, czy ekran `profiles` dostaje fokus PageRuntime, czy jest pomijany.
Fix: Zapisać decyzję (auto-wybór stałego profilu vs. Eliza wybiera D-padem z wstrzykniętym fokusem).
Rozstrzygnięcie: R9 — profil wybiera Eliza; PageRuntime stawia fokus na ostatnio używanym profilu, ale go nie wybiera (także po wybudzeniu).

**[Token completeness]** — Brak kontraktu kontrastu dla `focus-ring-injected` na treści Netflixa (§ DESIGN.md → Colors; Elevation & Depth → Fokus w Netflixie)
5 px białego obrysu na jasnych plakatach może zniknąć; kontrast liczony tylko względem `ground`/`surface`/`surface-2`.
Fix: Dodać regułę/token (ciemna obwódka zewnętrzna albo cień) i docelowy kontrast wobec dowolnego tła.
Rozstrzygnięcie: R4 — biały obrys 5 px + ciemna obwódka 3 px `ground`.

**[Component coverage]** — Empty state zakładki Audio bez komponentu (§ EXPERIENCE.md → State Patterns → Pusta Audio; DESIGN.md → Components)
Istnieje tylko jako stan + copy; mock `key-home-game.html` stawia zdanie w pasie kafelków bez tokenu typografii.
Fix: Dodać komponent „Empty state” (typografia, kolor, położenie) w obu spine'ach.
Rozstrzygnięcie: R13 — Empty state jako komponent: body, `text-2`, w pasie kafelków, fokus zostaje na zakładce.

**[Component coverage]** — Chip: niekompletne stany i nienazwane ikony (§ DESIGN.md → Components → Chip)
Brak „Zamykam…” (Stopping); ikony Starting/Failed to „[ASSUMPTION] ikona + tekst” bez glifu ani zestawu.
Fix: Tabela stanów chipa (Starting, Stopping, Background, Failed) z ikoną i tekstem; nazwać źródło ikon.
Rozstrzygnięcie: R6 — tabela chip + linia stanu dla wszystkich stanów; ikony Material Symbols Rounded.

**[Component coverage]** — Niekompletny inwentarz klawiszy OSK (§ EXPERIENCE.md → Interaction Primitives → OSK; DESIGN.md → OSK key)
Brak klawisza usuwania i spacji — J3 nie pozwala poprawić literówki ani wpisać wielowyrazowego tytułu; mock ma „Usuń”, „Spacja”, „123”.
Fix: Dopisać pełny układ (rzędy, klawisze specjalne, wymiary) do obu spine'ów.
Rozstrzygnięcie: R12 — QWERTY + „Usuń”, „Spacja”, „123”, „Gotowe”; znacznik wariantów; long OK otwiera wiersz wariantów.

**[State coverage]** — „Dokładnie jeden fokus” vs. OSK otwarta (§ EXPERIENCE.md → Component Patterns → Focus ring; mock `key-netflix-search-osk.html`)
Fokus w aplikacji zostaje (NOACTIVATE), a mock rysuje jednocześnie `.inj` na polu i fokus na klawiszu.
Fix: Reguła dla trybu `osk`: pole tylko z kursorem / przygaszonym obrysem, pełny fokus na klawiszu.
Rozstrzygnięcie: R1 — białe ringi wyłącznie na klawiszu OSK; pole w stylu „active input”.

**[State coverage]** — Brak stanu UX dla zerwanego CDP przy żywym procesie (§ AD-10; EXPERIENCE.md → State Patterns)
Netflix na pierwszym planie bez wstrzykniętego fokusu i z martwym D-padem.
Fix: Zdefiniować, co widzi użytkownik i czy D-pad jest buforowany.
Rozstrzygnięcie: R8 — bez osobnego UI, Home ratuje; po limicie reconnectu → Failed (R7); D-pad nie buforowany.

**[Visual reference coverage]** — Key-screen mocki niosą decyzje, których nie ma w spine'ach (§ `.working/key-*.html`)
Klawisze „Usuń”/„Spacja”/„123”, przygaszony Plex z hexami spoza palety (`#24272C`, `#1B1D21`), pełne zdanie Failed w chipie, dwa foci w trybie `osk`.
Fix: Przy promocji linkować każdy mock inline z opisem i przenieść decyzje do spine'ów albo usunąć z mocków.
Rozstrzygnięcie: R1, R6, R12, R14 — decyzje rozstrzygnięte; promocja mocków i linki nadal do zrobienia.

**[Impeccable critique]** — F4 — Fokus małych kontrolek słabszy niż kanon; ghost „Pomiń” udaje ring (§ przyciski, AppMenu, klawisze OSK, zakładki)
Sam obrys na ciemnej pigułce; stały obrys 2 px `line` na „Pomiń” myli z fokusem.
Fix: Token `focus-fill` (białe tło, tekst `ground`); ghost bez stałego obrysu.
Rozstrzygnięcie: R5 — odwrócona pigułka bez skali; „Pomiń” z subtelnym wypełnieniem.

**[Impeccable critique]** — F5 — Failed i Starting nieodróżnialne z kanapy (§ key-home-failed-appmenu.html #1 vs #3)
Ten sam chip, różni je ikona 22 px; „Wciśnij OK” w najmniejszym tekście; Failed bez linii stanu.
Fix: Krótki chip Failed + pełny komunikat jako linia stanu; Starting z linią stanu.
Rozstrzygnięcie: R6 — chip „Nie odpowiada” + linia „Coś się zacięło. Wciśnij OK”; Starting „Uruchamiam…” + „Chwilka…”.

**[Impeccable critique]** — F6 — Wstrzyknięty ring z promieniem 22 px na kwadratowych elementach (§ key-netflix-search-osk.html #1–#3)
Rogi elementów Netflixa wystają poza zaokrąglony ring.
Fix: Promień z `getComputedStyle` celu (fallback 4 px) + `outline-offset`.
Rozstrzygnięcie: R4 — promień elementu Netflixa (domyślnie 4 px).

**[Impeccable critique]** — F7 — Polskie znaki w OSK nieodkrywalne (§ key-netflix-search-osk.html #1)
ą ć ę ł ń ó ś ź ż tylko przez long-press OK, bez podpowiedzi.
Fix: Stała podpowiedź „Przytrzymaj OK, żeby wpisać ą, ę, ł…” + znacznik na klawiszach z wariantami.
Rozstrzygnięcie: Częściowo R12 — znacznik w rogu klawiszy z wariantami; tekstowej podpowiedzi R12 nie wprowadza.

**[Impeccable critique]** — F8 — „Pomiń” bez cofnięcia (§ key-home-video.html #2; EXPERIENCE → Card „Do oceny”)
Przypadkowe OK przy `phase: repeat` trwale usuwa kartę.
Fix: ~5 s stanu zwiniętego „Pominięte · Cofnij”.
Rozstrzygnięcie: R15 — świadomie bez undo w MVP.

**[Impeccable audit]** — M1 — Kropka „w tle” na zakładce bez tekstu (§ `.tab .run`)
Floor wymaga kropki zawsze z tekstem; 12 px z 3 m łatwo przeoczyć.
Fix: Świadomy wyjątek w EXPERIENCE albo glif zamiast kropki; `role="img"` + `aria-label`.

**[Impeccable audit]** — M2 — Granice klawiszy/przycisków niewidoczne z 3 m (§ `.key`, `.btn`, `.btn.ghost`)
`surface-2` / `surface` 1,12:1, `line` / `surface` 1,35:1 — primary vs ghost nierozróżnialne.
Fix: Podnieść `surface-2` (~1,4–1,5:1) lub jaśniejsze primary; ghost ≥ 3:1, jeśli ma znaczyć przycisk.
Rozstrzygnięcie: Częściowo R5 (ghost „Pomiń” z subtelnym wypełnieniem); granice klawiszy bez zmian.

**[Impeccable audit]** — M3 — Linia stanu kafelka klei się do „Do oceny” (§ key-home-video.html #1)
~15 px między linią stanu a nagłówkiem sekcji.
Fix: ≥ 36 px (`spacing.5`), łącznie z H1/H2.
Rozstrzygnięcie: R3 — większy odstęp nad nagłówkiem wiersza.

**[Impeccable audit]** — M4 — Popup wariantów: słaba granica, brak wskazania (§ key-netflix-search-osk.html #2 `.variants`)
1,43:1 nad plakatami; żaden klawisz nie sygnalizuje wariantów.
Fix: Cień + obrys `line`, klawisz źródłowy „pressed”, znacznik wariantów lub podpowiedź.
Rozstrzygnięcie: R12 — znacznik w rogu klawiszy z wariantami, wiersz wariantów z fokusem na pierwszym.

**[Impeccable audit]** — M5 — Brak wizualizacji kursora żyroskopu w OSK (§ key-netflix-search-osk.html; DESIGN → OSK key)
Nie wiadomo, czy wskaźnik i fokus mogą się rozjechać.
Fix: Ekran z kursorem + zasada „pointer przenosi fokus”.

**[Impeccable audit]** — M6 — AppMenu bez nazwy aplikacji; scrim gasi Failed (§ key-home-failed-appmenu.html #2)
Z 3 m nie widać, którego kafelka dotyczy menu; chip Failed pod scrimem 1,6–2,4:1.
Fix: Nagłówek „Netflix” w panelu albo wyłączenie kafelka-właściciela spod scrimu.

### Low (30)

**[Flow coverage]** — Plex bez flow (§ EXPERIENCE.md → Interaction Primitives → Back)
Brief: „tylko uruchamianie i powrót”; zachowanie da się złożyć z Tile + tabeli Back (`back=home`).
Fix: Opcjonalnie jedno zdanie w J2 lub w Component Patterns → Tile.

**[Flow coverage]** — J4: brak wskazówki po drugim Failed (§ EXPERIENCE.md → Key Flows J4)
„Brak dalszej eskalacji w MVP” zostawia Elizę bez wskazówki, co uderza w kryterium „Samodzielność”.
Fix: Dodać jedno zdanie copy na drugie Failed albo jawnie oznaczyć jako zaakceptowane ryzyko.

**[Token completeness]** — Waga 500 nazwy kafelka poza fokusem nie jest tokenem (§ DESIGN.md → Typography; `components.tile-meta`)
`typography.tile-name` ma tylko 600, tabela mówi „30 / 600 (500 poza fokusem)”.
Fix: Dodać `tile-name-unfocused` lub `fontWeight` w `tile-meta`.

**[Token completeness]** — Pochodne akcentu i cienie zapisane surowo (§ DESIGN.md → Colors → Zasady; `focus-ring.shadow`, `tile.shadow`)
`rgba(138,180,248,.18)`, poświata `rgba(138,180,248,.07)` i cienie poza tokenami.
Fix: Tokeny `accent-halo`, `ground-glow`, `shadow-rest`/`shadow-lift`.

**[Component coverage]** — Header i wiersz „Do oceny” bez wierszy komponentów (§ DESIGN.md → Layout & Spacing; EXPERIENCE.md → „Do oceny”)
Logo + powitanie + zegar oraz kontener wiersza (tytuł, scroll, znikanie) nie mają wierszy komponentów.
Fix: Dodać wiersze „Header” i „Wiersz Do oceny” albo zapisać, że to layout.

**[State coverage]** — Wylogowany Netflix bez stanu dla Elizy (§ EXPERIENCE.md → Interaction Primitives → OSK, krok 6)
Spine mówi tylko, że loguje autor.
Fix: Jeden wiersz w State Patterns: ekran Netflixa as-is, Home ratuje, sprawa dla autora.

**[State coverage]** — Starting bez limitu czasu i wskaźnika postępu (§ EXPERIENCE.md → State Patterns → Starting; Accessibility Floor)
Floor zakazuje ruchu poza fokusem — nie wiadomo, czy „Uruchamiam…” ma spinner.
Fix: Zapisać: chip statyczny, timeout z `settings.json` → Failed.
Rozstrzygnięcie: Częściowo R6 (chip „Uruchamiam…” + linia „Chwilka…”); timeout i spinner nadal do zapisania.

**[Visual reference coverage]** — Nieaktualne „[ASSUMPTION] Brak szkicu OSK/AppMenu” (§ `components.osk-key`, `osk-panel`, `appmenu`)
Szkice istnieją; wymiary OSK/AppMenu nadal nie są w tokenach; `config-error` bez szkicu.
Fix: Po promocji zaktualizować tokeny z mocków i zdjąć notatki.

**[Visual reference coverage]** — Osierocone szkice odrzuconych kierunków (§ Inspiration → „Odrzucone kierunki wizualne”)
`assigned.html`, `challenger-split-flap.html`, `model-pick.html` wymienione z nazwy bez linku i powodu.
Fix: Opcjonalnie link + jedno zdanie „dlaczego nie”.

**[Bloat & overspecification]** — „Do oceny” powtarza reguły AD-12/forge (§ EXPERIENCE.md → „Do oceny”)
Progi 0.9, `lastInSeason`, statusy `pending|queued|rated|skipped`, klucz raz na zawsze.
Fix: Zostawić decyzje UX i odesłać do AD-12.

**[Bloat & overspecification]** — „Model wejścia” przepisuje enum Actions z AD-8 (§ EXPERIENCE.md → Foundation)
Duplikat architektury.
Fix: Odwołanie do AD-8 + to, co UX z tego wyprowadza.

**[Bloat & overspecification]** — Szczegóły drivera w State Patterns / Back (§ EXPERIENCE.md → State Patterns; Back)
D7361, `video.play()`, `history.back`, „timeout ~500 ms” to wiedza architektury.
Fix: Skrócić do efektu widocznego dla użytkownika + referencja AD-7/AD-5.

**[Inheritance discipline]** — „Oceniłam” → „Ocenione” bez delty w Handoff (§ EXPERIENCE.md → Voice and Tone; Handoff)
Brief i AD nadal mówią „Oceniłam”.
Fix: Dodać wiersz delty (brief, forge).

**[Inheritance discipline]** — Rozjazd nazw komponentów między spine'ami (§ DESIGN.md / EXPERIENCE.md → Components)
„Avatar + powitanie” vs „Logo / avatar”; „Chip” + „State dot” vs „Chip / state dot”; „OSK key” obejmuje też `osk-panel`.
Fix: Jedna nazwa na komponent w obu plikach.

**[Shape fit]** — Kolejność sekcji w EXPERIENCE.md (§ EXPERIENCE.md)
„Do oceny”, Inspiration i Responsive stoją po Key Flows.
Fix: Responsive i Inspiration przed Key Flows, „Do oceny” obok Component/State Patterns.

**[Impeccable critique]** — F9 — AppMenu zasłania tożsamość kafelka (§ key-home-failed-appmenu.html #2)
Menu nie mówi, czego dotyczy.
Fix: Nagłówek panelu „Netflix” albo przesunięcie panelu.

**[Impeccable critique]** — F10 — Podwójny komunikat „wstrzymane” (§ key-home-video.html #1–#2)
Chip i linia stanu mówią to samo.
Fix: Linia „W tle · <tytuł>” albo chip tylko z ikoną.
Rozstrzygnięcie: R6 utrzymuje oba („Wstrzymane” + „…, wstrzymane”) — świadomie lub do rewizji.

**[Impeccable critique]** — F11 — Hint karty z sierotą, dotyka przycisków (§ key-home-video.html #1–#2)
„Filmwebie” w drugiej linii; ring „Ocenione” ~15 px pod hintem.
Fix: `text-wrap: balance` lub krótszy hint; odstęp ≥ 24 px.

**[Impeccable critique]** — F12 — Popup wariantu wisi nad treścią Netflixa (§ key-netflix-search-osk.html #2)
Wygląda na doklejony.
Fix: Rząd wariantów wewnątrz panelu OSK albo cień + padding.

**[Impeccable critique]** — F13 — Pusta Audio: tekst mały i przyklejony do zakładek (§ key-home-game.html #2)
Wygląda jak niedokończony stan.
Fix: Na wysokości `apps-top`, 30–34 px, `text-2`.
Rozstrzygnięcie: R13 — w pasie kafelków, body, `text-2` (rozmiar body, nie 30–34 px).

**[Impeccable critique]** — F14 — Adnotacje w mocku OSK (§ key-netflix-search-osk.html)
20 px adnotacji może przeciec do referencji.
Fix: Klasa `.doc-note`, wykluczyć z handoffu.

**[Impeccable critique]** — F15 — „Ocenione” czyta się jak stan (§ key-home-video.html)
Decyzja świadoma; tylko obserwacja.
Fix: Opcjonalnie „Już ocenione”, jeśli testy pokażą wahanie.

**[Impeccable audit]** — L1 — Brak ścieżki `prefers-reduced-motion` (§ `.tile` transition)
Skala 1.08 zawsze animowana.
Fix: Reduced-motion: obrys + cień, skala bez tranzycji.

**[Impeccable audit]** — L2 — Semantyka mocków (§ wszystkie pliki)
`<span>/<div>` bez ról, brak `tablist`/`tab`.
Fix: `<button>`, `role="tab"`, `aria-current` w implementacji.

**[Impeccable audit]** — L3 — Hardcode poza tokenami (§ key-home-failed-appmenu.html)
Plex `#24272C/#1B1D21`, avatar, `stroke="#fff"`.
Fix: Tokeny `var(--…)`, jeden wygląd kafelka.
Rozstrzygnięcie: R14 — mocki wyłącznie na tokenach (`surface-dim` / `surface`).

**[Impeccable audit]** — L4 — Font nie jest dołączony (§ wszystkie pliki)
`Roboto Flex` tylko lokalnie; fallback ignoruje `opsz` i wagę 650.
Fix: Bundlować variable font, `font-display: block`.

**[Impeccable audit]** — L5 — QR w mocku nie jest prawdziwym kodem (§ `.qr`)
Nie da się zweryfikować H3.
Fix: Wygenerować realny QR.

**[Impeccable audit]** — L6 — Promień wstrzykniętego ringu ≠ promień elementu (§ `.inj`; DESIGN.md)
Kontrakt sprzeczny (promień elementu vs `{rounded.tile}`).
Fix: Ring bierze promień elementu (min. 4 px).
Rozstrzygnięcie: R4 — promień elementu Netflixa.

**[Impeccable audit]** — L7 — Odwrócona hierarchia nagłówków (§ `section-title` 32 < `card-title` 34)
Detektor: krok 1.23.
Fix: Sekcja 36/600 albo karta 32/600.

**[Impeccable audit]** — L8 — Failed/Starting bez linii stanu (§ kafelek meta)
Background ją ma — niespójny wzorzec.
Fix: Dodać linię stanu albo zapisać wyjątek.
Rozstrzygnięcie: R6 — każdy stan ma chip + linię stanu.

## Mechanical notes

- Referencje `{path.to.token}`: wszystkie się rozwiązują; linki do `canon.html` rozwiązują się względem `ux-mediastation/`.
- Frontmatter DESIGN.md: `fontWeight`/`lineHeight` jako liczby (przykłady używają stringów) — ujednolicić; `{colors.focus}` bez cudzysłowu w `focus-ring*.outline`; `logo.mark` to proza zamiast wartości.
- Frontmatter EXPERIENCE.md bez `description` (opcjonalne). Indeks `ux-mediastation.md` i `.memlog.md` mają `updated: 2026-10-16` mimo wpisów z 2026-10-17 — odświeżyć.
- Accessibility Floor „Brak autoplay” — doprecyzować, że dotyczy shellu (Netflix ma własne autoplay, Edge startuje z `--autoplay-policy=no-user-gesture-required`).
- Detektor `impeccable detect`: `cramped-padding` na `.tile` to false positive (grid item), na `.nf-field` poza zakresem (UI Netflixa); `flat-type-hierarchy` częściowo zasadne (L7).
- Mermaid: brak diagramów, nic do walidacji.

## Reviewer files

- `review-rubric.md`
- `review-impeccable-critique.md`
- `review-impeccable-audit.md`
- `.working/review-resolutions.md` (rozstrzygnięcia R1–R15)
