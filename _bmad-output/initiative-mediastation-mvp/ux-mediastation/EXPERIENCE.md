---
name: MediaStation
status: final
created: 2026-10-16
updated: 2026-10-17
sources:
  - ../brief-mediastation/brief-mediastation.md
  - ../brief-mediastation/addendum.md
  - ../architecture-mediastation/architecture-mediastation.md
  - ../forge-filmweb-rating-prompt/forge-filmweb-rating-prompt.md
  - ../research-browser-control-for-streaming-services/research-browser-control-for-streaming-services.md
---

# MediaStation — Experience Spine

`DESIGN.md` = wygląd; ten plik = zachowanie. **Referencje wizualne:** szkic kierunku [`mockups/direction-canon.html`](mockups/direction-canon.html) (Home → Video, Netflix w tle, dwie karty „Do oceny”) oraz key-screen mocki linkowane inline przy stanach i journeys. Mocki powstały przed Reviewer Gate i nie zawsze mają rozstrzygnięcia R1–R16. **Przy konflikcie z mockami wygrywają spine'y.**

> **Correct-course wykonany:** override AD-8 (pointer w `osk`) i zawężenie AD-9 (R10) przyjęte w [`change-mediastation.md`](../change-mediastation/change-mediastation.md) (zatwierdzony 2026-10-17). Wszystkie R1–R16 i A1–A26 potwierdzone — patrz Rozstrzygnięcia R/A.

## Foundation

| Aspekt | Decyzja |
|---|---|
| Użytkownicy | Eliza — codzienna użytkowniczka, nietechniczna. Autor — konfiguruje i naprawia |
| Form-factor | Jedna powierzchnia: TV Hisense 1920×1080, 10-foot UI, kanapa ~3 m, wieczór, słabe ciepłe światło |
| Wejście | Pilot G20S Pro BT (strzałki, OK, Back, Home), klawiatura, gamepad. Bez myszy, kursor ukryty. Żyroskop pilota w UI MediaStation tylko w OSK |
| Model wejścia | InputHub → Actions `{kind: Up\|Down\|Left\|Right\|Ok\|Back\|Home\|Keyboard\|AppMenu, phase: press\|repeat\|long}`; Orchestrator zmienia tryby `shell\|app\|osk` (AD-8). Action `Keyboard` bez mapowania na pilocie w MVP |
| UI system | React w WebView2 (`web/shell`), brak design systemu — tokeny od zera w `DESIGN.md` |
| Okna / platforma | Stały viewport 1920×1080, bez responsive i breakpointów; title-safe 5%. Shell w WebView2; Netflix w osobnym procesie Edge; OSK jako osobne okno topmost NOACTIVATE (fallback: OSK rysowana przez PageRuntime w stronie — otwarte w architekturze). Weryfikacja wizualna tylko w 1920×1080 |
| Netflix | Osobny proces Edge (CDP); fokus rysuje PageRuntime, kontrakt ekranów od ServiceDriver (AD-7) |
| Telefon | Tylko skanuje QR; strona Filmwebu jest poza naszym UI |

**Glosariusz.** *forge* — sesja `bmad-forge-idea`, z której pochodzi karta „Do oceny” (`forge-filmweb-rating-prompt`). *memlog* — log decyzji sesji UX (`.memlog.md`). *kanon* — wybrany kierunek wizualny (kanon kategorii Google TV / Apple TV), szkic `mockups/direction-canon.html`. *spine* — `DESIGN.md` + `EXPERIENCE.md`. *Reviewer Gate* — przegląd spine'ów, z którego pochodzą rozstrzygnięcia R1–R16.

## Information Architecture

| Powierzchnia | Wejście | Zawartość |
|---|---|---|
| **Home** (Shell, tryb `shell`) | Zimny start, wybudzenie, klawisz Home, Back z korzenia aplikacji, awaria aplikacji na pierwszym planie (R7) | Header: logo + „MediaStation”, avatar + powitanie (dekoracja, bez kont), zegar. Zakładki **Video / Audio / Game** |
| Home → **Video** | Domyślna zakładka | Kafelki: Netflix, Plex. Pod nimi wiersz **„Do oceny”** (peek, znika, gdy pusty) |
| Home → **Audio** | Zakładka | W MVP pusta: Empty state; aplikacje dojdą przez `apps.json` |
| Home → **Game** | Zakładka | Jeden kafelek: Moonlight |
| **AppMenu** (nakładka, tylko `shell`) | Long-press OK na kafelku; OK na kafelku Failed | „Uruchom ponownie”, „Zamknij” (AD-10) |
| **OSK** (okno nakładki, tryb `osk`) | Otwiera się automatycznie tylko na `textFocus` w serwisie webowym (Netflix) | QWERTY, „Usuń”, „Spacja”, „123”, „Gotowe”, warianty PL |
| **Netflix** (zewnętrzny, Edge, tryb `app`) | Kafelek Netflix | Ekrany Netflixa (`profiles\|browse\|modal\|title\|search\|watch\|limit\|error`) z wstrzykniętym fokusem PageRuntime |
| **Moonlight / Plex** (natywne, tryb `app`) | Kafelek | Własne UI aplikacji; MediaStation tylko uruchamia i chowa |
| **Ekran błędu konfiguracji** (AD-11) | Błąd `apps.json` / configu przy starcie | Pełnoekranowy komunikat w głosie MediaStation, adresowany do autora |

Głębokość nawigacji: Home → aplikacja; nakładki (AppMenu, OSK) zawsze jeden poziom, nigdy dwie naraz.

## Voice and Tone

Ciepło, na ty, z lekkim humorem — w komunikatach, nie w etykietach. Etykiety krótkie i neutralne (karta działa na każdym profilu — forge). Język UI: polski; tytuły treści w języku profilu Netflixa.

| Miejsce | Tekst | Status |
|---|---|---|
| Akcje karty | „Ocenione” / „Pomiń” | decyzja (zastępuje „Oceniłam”) |
| Sekcja | „Do oceny” | decyzja |
| AppMenu | „Uruchom ponownie” / „Zamknij” | decyzja (AD-10, brief) |
| OSK | „Gotowe”, „Usuń”, „Spacja”, „123” | decyzja (R12) |
| Powitanie | „Dobry wieczór, Eliza” (pora dnia + imię) | decyzja (A1) |
| Stany kafelka (chip / linia stanu) | Tabela w `DESIGN.md` → Components → Stany kafelka | decyzja (R6) |
| Podtytuł karty | „Sezon 8 · obejrzany” / „Film · obejrzany” | decyzja (A2, z kanonu) |
| Podpowiedź karty | „Zeskanuj telefonem i oceń na Filmwebie” | decyzja (A2, z kanonu) |
| Pusta Audio | „Tu jeszcze cisza. Muzyka dojdzie wkrótce.” | decyzja (A3) |
| Błąd konfiguracji | „Coś nie gra w konfiguracji” + nazwa pliku i błąd, do autora | decyzja (A4) |

| Do | Don't |
|---|---|
| Mów, co się dzieje i co zrobić jednym OK | Kody błędów, nazwy procesów, „Menedżer zadań” |
| Formy neutralne rodzajowo w etykietach | „Oceniłam”, wykrzykniki, zachęty marketingowe |

## Component Patterns

Behawioralne. Wygląd: `DESIGN.md` → Components (te same nazwy komponentów).

| Komponent | Zachowanie |
|---|---|
| **Focus ring** | Dokładnie jeden fokus na ekranie. Kafelek: `{components.focus-ring}`. Małe kontrolki (button, klawisz OSK, pozycja AppMenu, zakładka): `{components.focus-pill}`. Netflix: `{components.focus-ring-injected}` rysowany przez PageRuntime (bez skali, promień elementu). W trybie `osk` pole Netflixa dostaje `{components.input-active}`, a jedyny fokus stoi na klawiszu OSK. Nigdy nie znika podczas nawigacji |
| **Tile** | OK: NotRunning → start leniwy (Starting → Foreground); Background → przywróć na pierwszy plan; Failed → AppMenu z fokusem na „Uruchom ponownie”; Starting → brak akcji. Long-press OK → AppMenu. Plex: MediaStation tylko go uruchamia i przywraca (`back=home`) |
| **Chip** | Tylko odczyt stanu aplikacji (snapshot view-modelu, AD-16); nie fokusowalny. Treść wg `DESIGN.md` → Components → Stany kafelka |
| **State dot** | Tylko odczyt; zawsze z tekstem „W tle · …”; nie fokusowalny |
| **Tab** | Lewo/Prawo w rzędzie zakładek. Zakładka aktywuje się, gdy dostaje fokus. Dół → zapamiętany element tej zakładki (albo nic, gdy zakładka ma tylko Empty state). Kropka `{components.tab-running-dot}` = aplikacja z tej zakładki działa w tle |
| **Rate row** | Wejście fokusu w wiersz przesuwa stronę w górę i powiększa karty + QR; wyjście wraca do spoczynku. Szczegóły: Interaction Primitives → Scroll Home |
| **Card „Do oceny”** | Fokus stoi na przyciskach karty; Lewo/Prawo przechodzi między przyciskami i kartami. QR nieaktywny. „Ocenione” → `rated`, „Pomiń” → `skipped`; karta znika bez potwierdzenia i bez undo w MVP (kolejka nie usuwa kluczy, więc undo można dodać później bez utraty danych); fokus na sąsiednią kartę po kluczu (prawa, potem lewa, potem kafelki) |
| **QR** | Nigdy nie dostaje fokusu; payload: minimalny URL wyszukiwarki filmweb.pl z tytułem, bez zewnętrznego skracacza (AD-12). Otwarte: test skanu z 3 m na prawdziwym TV dwoma telefonami — w story „Do oceny” |
| **Button** | OK = akcja. Bez long-press |
| **OSK** | Patrz Interaction Primitives → OSK. Znaki trafiają od razu do aplikacji (CDP insertText / SendInput); tekst nie trafia do view-modelu (AD-9) |
| **AppMenu** | Otwiera się nad kafelkiem; Góra/Dół między pozycjami; OK wykonuje; Back zamyka i wraca na kafelek. Dostępne dla aplikacji uruchomionej lub Failed; na NotRunning long-press nic nie robi. Przy Failed otwiera się z fokusem na „Uruchom ponownie”, w pozostałych stanach z fokusem na pierwszej pozycji |
| **Empty state** | Jedno zdanie w pasie kafelków; nie fokusowalny — fokus zostaje na zakładce, Dół nic nie robi |
| **Logo** | Dekoracja, bez fokusu |
| **Avatar + powitanie** | Dekoracja, bez fokusu i bez logiki kont |
| **Config error** | Brak akcji; autor poprawia config, MediaStation startuje ponownie |

## State Patterns

### Kafelek — cykl życia (AD-10)

Chip + linia stanu: copy, ikony i wygląd ma jedno źródło — `DESIGN.md` → Components → Stany kafelka. Ilustracje: Background — [`mockups/key-home-video.html`](mockups/key-home-video.html), [`mockups/key-home-game.html`](mockups/key-home-game.html); Failed, AppMenu i Starting — [`mockups/key-home-failed-appmenu.html`](mockups/key-home-failed-appmenu.html).

| Stan | Zachowanie |
|---|---|
| NotRunning | OK = start |
| Starting | Ikona statyczna; po timeoucie startu → Failed (AD-10). Aplikacja wychodzi na pierwszy plan po starcie (Moonlight 1–2 s). Klawisz Home w trakcie → aplikacja minimalizowana po starcie (AD-15), kafelek przechodzi w Background |
| Foreground | Home niewidoczny |
| Background | Aplikacja schowana i wyciszona (Netflix: pause → mute → minimize; Moonlight: mute → minimize, sesja żyje); kropka na zakładce. OK przywraca; Netflix stoi na pauzie, OK wznawia (nigdy autowznowienie) |
| Stopping | Fokus zostaje na kafelku |
| Failed | Ikona + tekst, bez koloru błędu. OK → AppMenu z fokusem na „Uruchom ponownie”; wyjście z Failed tylko przez AppMenu. Failed także przy niezgodnej wersji major protokołu |

### Awarie w trakcie użycia

| Stan | Zachowanie |
|---|---|
| Aplikacja pada na pierwszym planie (Foreground → Failed) | MediaStation sama wraca na Home, na zakładkę tej aplikacji; fokus staje na kafelku w stanie Failed, więc jedno OK otwiera AppMenu z fokusem na „Uruchom ponownie”. Bez dodatkowego dialogu |
| Zerwane CDP przy żywym procesie Netflixa | Brak osobnego UI; driver robi reconnect. D-pad nie jest buforowany. Klawisz Home zawsze ratuje. Jeśli reconnect nie uda się w `cdpReconnectTimeout` (30 s, `settings.json`) → Failed i zachowanie jak wyżej (AD-10, AD-11) |

### Powierzchnie

| Stan | Powierzchnia | Zachowanie |
|---|---|---|
| Zimny start / wybudzenie | Home | Zakładka Video, fokus na pierwszym kafelku (Netflix). Stan nietrwały (AD-10) |
| Restart po crashu MediaStation | Home | Żywe aplikacje adoptowane jako Background; fokus jak przy zimnym starcie |
| Powrót na Home (klawisz Home / Back) | Home | Fokus na ostatnim kafelku; nowa karta „Do oceny” widoczna (peek), ale nie przejmuje fokusu |
| Pusty „Do oceny” | Video | Wiersz znika; zakładka ma same kafelki |
| Wiele kart | Video | Poziomy wiersz, najnowsza z lewej, bez limitu, przewijany strzałkami |
| Pusta Audio | Audio | Empty state z jednym zdaniem; brak fokusowalnych elementów — fokus zostaje na zakładce. Ilustracja: [`mockups/key-home-game.html`](mockups/key-home-game.html) |
| Błąd konfiguracji (AD-11) | Shell | Pełnoekranowy komunikat do autora |
| Profile gate | Netflix | Po starcie i po wybudzeniu ServiceDriver/PageRuntime rozpoznaje ekran `profiles` i stawia fokus na ostatnio używanym profilu, ale go nie wybiera; profil wybiera Eliza jednym OK |
| Wylogowany Netflix | Netflix | Ekran logowania Netflixa as-is; klawisz Home ratuje; logowanie to sprawa autora (wariant OSK) |
| Limit („Too many people…”) | Netflix | Ekran Netflixa bez komunikatu MediaStation; PageRuntime stawia fokus na głównej akcji („Spróbuj ponownie”) |
| Błąd (`error`) | Netflix | Jak limit: ekran Netflixa as-is, fokus na głównej akcji |
| `page-error` (np. D7361 po 30 min w tle) | Netflix | Driver robi reload `/watch/<id>`; pozycja zachowana; brak UI MediaStation |
| Czarny ekran z Play po 30 min pauzy | Netflix | OK → `video.play()` |
| OSK otwarta | Netflix + OSK | Tryb `osk`; fokus systemowy w aplikacji zostaje (NOACTIVATE), ale wizualnie pole ma tylko `input-active` (kursor + linia 2px), a jedyny biały fokus stoi na klawiszu OSK. Ilustracja: [`mockups/key-netflix-search-osk.html`](mockups/key-netflix-search-osk.html) |

## Interaction Primitives

**Model fokusu**
- Jeden fokus, nawigacja przestrzenna D-padem (Góra/Dół/Lewo/Prawo); `phase: repeat` przewija wiersze.
- Pamięć fokusu per zakładka i per wiersz, oparta na kluczach (`id` aplikacji, klucz tytułu w kolejce), nie na indeksach (AD-12).
- Powrót na Home → ostatni kafelek. Zimny start/wybudzenie → pierwszy kafelek Video. Awaria na pierwszym planie → kafelek Failed.
- W Netflixie: w modalu akcje mają pierwszeństwo przed geometrią, fokus zawężony do dialogu; na `/watch` klawisze przepuszczane (OK pauza, ←/→ ±10 s).
- Przejście fokusu ok. 150 ms ease-out.

**Scroll Home (wiersz „Do oceny”)** — geometria: `DESIGN.md` → Layout & Spacing
- Spoczynek: wiersz wystaje (peek) spod kafelków; nowa karta nie przejmuje fokusu.
- Dół z kafelka do wiersza: strona przesuwa się w górę, cały wiersz mieści się w title-safe, wszystkie karty wiersza i QR przechodzą w rozmiar „focused”. Header i zakładki mogą zjechać poza ekran.
- Góra z wiersza: strona wraca do spoczynku, fokus na zapamiętany kafelek.

**OK / long-press**
- OK = główna akcja elementu. **AppMenu = long-press OK (`phase: long`) na kafelku w trybie `shell`.**
- OK na kafelku Failed = Intent AppMenu z fokusem na „Uruchom ponownie”.
- W OSK: `press` wstawia literę, `long` otwiera rząd wariantów (patrz OSK).

**Back**

| Kontekst | Back |
|---|---|
| Netflix `watch` | `history.back` |
| Netflix `modal` / `title` | Zamknięcie |
| Netflix `search` | → `browse` |
| Netflix `browse` / `profiles` (`root:true`) | → Home (Netflix w Background na pauzie); jeśli strona nie potwierdzi obsługi Back w ~500 ms, host wraca na Home |
| Moonlight | `back=key:Escape` — Back idzie do gry; klawisz Home to jedyne wyjście |
| Plex / natywne | `back=home` |
| OSK — rząd wariantów otwarty | Zamyka rząd wariantów, fokus wraca na klawisz bazowy |
| OSK | Zamyka OSK, fokus PageRuntime na wyniki |
| AppMenu | Zamyka menu, fokus na kafelek |
| Home (shell) | Brak akcji |

**Klawisz Home** — zawsze połykany przez MediaStation, zawsze ratuje: z aplikacji chowa ją (pause, jeśli aplikacja to wspiera → mute → minimize) i pokazuje Home. Z OSK/AppMenu: zamyka nakładkę i pokazuje Home. Działa też przy zerwanym CDP.

**Pointer** — w `shell` zablokowany. W `osk` dozwolony (żyroskop; override AD-8): kursor przenosi ten sam jedyny fokus, pod wskaźnikiem jest `focus-pill`. Kursor jest ograniczony do okna OSK (`ClipCursor`), a kliknięcie poza OSK jest połykane (AD-8). W `app` wg polityki z `apps.json`: Moonlight i Plex/natywne — dozwolony; Netflix — D-pad.

**OSK** — ilustracja: [`mockups/key-netflix-search-osk.html`](mockups/key-netflix-search-osk.html)
1. Otwiera się automatycznie tylko na `textFocus{active}` w serwisie webowym (np. OK na polu wyszukiwania Netflixa). Wywołanie z pilota (Action `Keyboard`) nie jest w MVP; w Moonlight i Plex tekst tylko z klawiatury lub gamepada.
2. Inwentarz klawiszy: QWERTY; „Usuń” (backspace); „Spacja”; „123” (przełącza na panel cyfr i symboli `@ . _ -` i z powrotem); „Gotowe”. Klawisze z wariantami (a c e l n o s z) mają mały znacznik w rogu.
3. Wybór strzałkami albo kursorem żyroskopu, zatwierdzenie OK. Pole w Netflixie pokazuje kursor tekstowy i linię `input-active`, bez białego ringu.
4. **Krótki OK (`phase: press`)** wstawia literę. **Długi OK (`phase: long`)** na klawiszu z wariantami otwiera rząd wariantów (np. a → ą, z → ź ż) bez wstawiania litery bazowej, z fokusem na pierwszym wariancie; OK wstawia wariant i zamyka rząd; Back zamyka rząd bez wstawiania.
5. „Gotowe” lub Back zamyka OSK; fokus PageRuntime przechodzi do wyników.
6. Wariant logowania (autor, raz, przy konfiguracji lub po wylogowaniu): panel z `@` i cyframi pod ręką, pole hasła maskowane. Nie jest osobnym journey.

## Accessibility Floor

- Wszystko z D-pada: każda akcja osiągalna strzałkami + OK/Back/Home; nic nie wymaga celowania.
- Dokładnie jeden fokus, widoczny z 3 m i rozpoznawalny bez koloru (obrys + skala na kafelku; odwrócona pigułka na małych kontrolkach; obrys + ciemna obwódka w Netflixie). Kontrast fokusu (non-text) ≥ 3:1 wobec przyległych kolorów, także na jasnym artworku (`DESIGN.md` → Colors).
- Akcje i treść z fokusem w title-safe 5% (96px / 54px) — overscan TV ich nie utnie.
- Tekst ≥ 24px; kontrast tekstu ≥ 5.5:1 (`DESIGN.md` → Colors).
- Stan nigdy tylko kolorem: kropka `{colors.accent}` przy kafelku zawsze z tekstem („W tle”). Świadomy wyjątek: kropka na zakładce jest skrótem bez tekstu, bo pełny stan stoi na kafelku.
- Long-press nigdy nie jest jedyną drogą do naprawy: Failed + OK otwiera AppMenu. Świadomy wyjątek: polskie znaki w OSK tylko przez long-press; klawisze z wariantami mają znacznik.
- Brak autoplay i ruchu w shellu poza przejściem fokusu i scrollem wiersza „Do oceny” (Netflix ma własne podglądy, poza naszą kontrolą); nic nie zmienia się pod fokusem samo z siebie (nowa karta nie kradnie fokusu).
- Dźwięk: jedna aplikacja słyszalna naraz (audio focus); w tle wyciszone.

## Key Flows

### J1 — „Wieczór z Pamiętnikami” (Eliza, wieczór, dziecko śpi)

Ilustracja: [`mockups/key-home-video.html`](mockups/key-home-video.html) (Home po powrocie: Netflix w tle, wiersz „Do oceny”, fokus na „Ocenione”).

1. Eliza włącza TV pilotem TV; wejście domyślne to MediaStation. Bierze G20S — PC się wybudza.
2. Home: zakładka Video, fokus na kafelku Netflix (ruch skali i biały obrys widać z kanapy).
3. OK → Netflix (Starting → Foreground), pełny ekran, bez śladu przeglądarki. Na ekranie profili fokus stoi na ostatnio używanym profilu; Eliza wybiera profil jednym OK.
4. D-padem z wstrzykniętym fokusem znajduje „Pamiętniki wampirów”, ostatni odcinek sezonu; OK — odtwarzanie.
5. Odcinek dobiega ≥ 90% → tytuł trafia do kolejki ocen (`lastInSeason`).
6. Back → Back… aż do `browse` (root) → Home. Netflix w tle, na pauzie, wyciszony.
7. **Kulminacja:** fokus stoi na Netflixie, a pod spodem wystaje wiersz „Do oceny” z kartą „Pamiętniki wampirów”. Eliza schodzi D-padem w dół: strona przesuwa się, QR rośnie. Skanuje telefonem, ocenia na Filmwebie, OK na „Ocenione”; karta znika.

Awaria: Netflix pada w trakcie → MediaStation sama wraca na Home z fokusem na kafelku Failed → J4. Limit/błąd Netflixa → ekran Netflixa, fokus na „Spróbuj ponownie”.

### J2 — „Przejęcie pilota” (Eliza → autor → Eliza)

Ilustracja: [`mockups/key-home-game.html`](mockups/key-home-game.html) (zakładka Game, kafelek Moonlight, kropka „w tle” na zakładce Video).

1. Netflix gra (J1, krok 4). Autor bierze pilota: klawisz Home → Netflix pauzuje, wycisza się, chowa.
2. Home: kafelek Netflix „W tle”. Góra → zakładki, Prawo → Game → kafelek Moonlight → OK.
3. Moonlight startuje (okno po 1–2 s, borderless); Back idzie do gry.
4. Autor idzie jeść, oddaje pilota. Eliza: klawisz Home → Moonlight chowa się i wycisza (sesja przeżywa minimalizację); kropka na zakładce Game.
5. Zakładka Video, fokus na ostatnim kafelku (Netflix) → OK.
6. **Kulminacja:** Netflix wraca dokładnie tam, gdzie był — na pauzie; OK wznawia. Nic nie zostało zamknięte.

Awaria: Netflix > 30 min w tle → `page-error` (D7361) → driver przeładowuje `/watch`, pozycja zachowana. Limit ekranów Netflixa (po zamknięciu w trakcie odtwarzania slot jest zajęty jeszcze 1–2 min) → ekran limitu Netflixa, fokus na głównej akcji.

### J3 — Wyszukiwanie przez OSK (Eliza, tytuł z rolek / Filmwebu)

Ilustracja: [`mockups/key-netflix-search-osk.html`](mockups/key-netflix-search-osk.html) (OSK nad wyszukiwarką Netflixa, rząd wariantów, fokus na pierwszym wyniku po „Gotowe”).

1. Eliza w Netflixie przechodzi D-padem do wyszukiwania; OK na polu tekstowym.
2. `textFocus` → OSK MediaStation nad Netflixem (tryb `osk`); pole przechodzi w `input-active`, fokus na klawiszu OSK.
3. Wpisuje litery strzałkami albo kursorem żyroskopu, OK; polski znak — długi OK na klawiszu ze znacznikiem, wybór wariantu, OK. Literówka — „Usuń”; kolejne słowo — „Spacja”.
4. Każdy znak idzie od razu do Netflixa → `/search?q=` aktualizuje wyniki.
5. „Gotowe” (albo Back) zamyka OSK.
6. **Kulminacja:** fokus PageRuntime ląduje na pierwszym wyniku; OK → tytuł.

Wyjątki: wyczyszczone pole → Netflix wraca do `/browse`. Brak wyników → ekran Netflixa as-is.

### J4 — „Netflix nie odpowiada” (Eliza, sama, bez autora)

Ilustracja: [`mockups/key-home-failed-appmenu.html`](mockups/key-home-failed-appmenu.html) (kafelek Failed → AppMenu z fokusem na „Uruchom ponownie” → Starting).

1. Eliza jest na Home (wróciła sama albo MediaStation wróciła za nią po awarii na pierwszym planie); kafelek Netflix w stanie Failed: chip „Nie odpowiada”, linia „Coś się zacięło. Wciśnij OK”.
2. OK na kafelku → AppMenu z fokusem na „Uruchom ponownie”.
3. OK → Stopping → Starting („Uruchamiam…” / „Chwilka…”).
4. **Kulminacja:** Netflix startuje na pierwszy plan — naprawa jednym OK, bez Menedżera zadań.

Awaria: restart znów kończy się Failed → kafelek wraca do Failed; „Zamknij” w AppMenu zostaje jako druga opcja (brak dalszej eskalacji w MVP).

## „Do oceny”

| Reguła | Wartość |
|---|---|
| Film | Do kolejki po ≥ 90% (0.9) |
| Serial | Po odcinku `lastInSeason` obejrzanym ≥ 90% |
| Fallback (`lastInSeason == null`) | Rekord `pending`; karta po **`ratingPendingDays = 7`** dniach bez kolejnego odcinka obejrzanego ≥ 90% |
| Kolejka | Oparta na kluczach: każdy klucz trafia do kolejki tylko raz; statusy `pending\|queued\|rated\|skipped` (AD-12) |

Odrzucone (forge): Filmweb na TV, ocena z pilota, karta tylko dla profilu Elizy, IMDb.

## Inspiration & Anti-patterns

- **Z Google TV / Apple TV:** zakładki + rzędy kafelków, biały fokus z liftem, odwrócona pigułka na małych kontrolkach, peek i scroll wierszy, ciemne tło, zero chromu systemowego.
- **Kontekst gustu Elizy:** YouTube, rodzinne zdjęcia (zdjęcia jako tło — deferred).
- **Odrzucone:** reklamy i kafelki marketingowe, widgety rekomendacji; kursor, okna, pulpit, celowanie, wyjście na pulpit, nakładające się dźwięki.

## Deferred / poza MVP

- Przełącznik aplikacji pod przytrzymaniem Home.
- Toast z QR nad playerem (w MVP kulminacja na Home).
- Widgety rekomendacji / continue watching.
- Kafelki Moonlight per host / per gra z customową grafiką; Vibepollo per gra.
- Rodzinne zdjęcia jako tło Home.
- Właściwe logo (w MVP placeholder).
- Action `Keyboard` z pilota (wywołanie OSK poza serwisami webowymi).
- Undo po „Pomiń” / „Ocenione”.

## Handoff do architektury

| Delta / decyzja | Wartość | Dotyczy |
|---|---|---|
| Zakładki Home | Pole kategorii w `apps.json` (`video\|audio\|game`) — brief mówi tylko o kafelkach | AD-16, `apps.json` |
| Pointer w OSK | `pointer: allowed` w trybie `osk` (żyroskop), kursor ograniczony do okna OSK (`ClipCursor`) — **override AD-8** przyjęty w `change-mediastation` | AD-8, AD-9 |
| Action `Keyboard` z pilota | Nie w MVP: OSK tylko auto na `textFocus` w serwisach webowych; w Moonlight/Plex tekst z klawiatury lub gamepada. **Zawężenie AD-9 (i briefu) dla pilota** przyjęte w `change-mediastation` (R10) | AD-8, AD-9, brief |
| OK na kafelku Failed | Intent AppMenu z fokusem na „Uruchom ponownie” | AD-8, AD-10 |
| Foreground → Failed | Orchestrator sam pokazuje Home, fokus na kafelku Failed przez `focusRequest{appId, seq}` (R7) | AD-10, AD-15, AD-16 |
| Fokus startowy | Zimny start, restart, wybudzenie → `focusRequest{default, seq}` (Video, pierwszy kafelek); UI stosuje każde nowe `seq` raz (R7, L31) | AD-10, AD-16 |
| Zerwane CDP | Bez UI; D-pad niebuforowany; po `cdpReconnectTimeout` (30 s) → Failed (R8) | AD-7, AD-10, AD-11 |
| Profil Netflixa | ServiceDriver/PageRuntime rozpoznaje `profiles` i fokusuje ostatnio używany profil, ale go nie wybiera (R9) | AD-7, AD-10 |
| Fokus w trybie `osk` | Host ogłasza stronie `osk{active}`; PageRuntime przełącza ring pola na `input-active`; jedyny fokus na klawiszu OSK (R1) | AD-7, AD-9 |
| Fokus wstrzykiwany | Biały obrys + ciemna obwódka, promień elementu (`getComputedStyle`, fallback 4 px), bez skali (R4) | AD-7 |
| Mapowanie `AppMenu` | Long-press OK (`phase: long`) na kafelku w `shell`; OK ma `press` dopiero na key-up przed progiem long | AD-8 |
| OSK: `press` / `long` | `press` (na key-up) wstawia literę; `long` otwiera rząd wariantów bez litery bazowej (R12) | AD-8, AD-9 |
| Etykieta akcji karty | „Ocenione” zamiast „Oceniłam” (brief, forge) | brief, AD-12 |
| QR payload | Minimalny URL wyszukiwarki filmweb.pl z tytułem, bez zewnętrznego skracacza, ECC L (R2); test 3 m w story „Do oceny” | AD-12 |
| `ratingPendingDays` | 7 | AD-12, `settings.json` |
| Moonlight | `back=key:Escape`, `pointer` dozwolony; Home jedynym wyjściem | AD-8, `apps.json` |
| Plex / natywne | `back=home`, `pointer` dozwolony | AD-8, `apps.json` |
| UX ekranu `limit` (i `error`) | Ekran Netflixa as-is; PageRuntime fokus na głównej akcji | AD-7 |
| Fokus po wybudzeniu | Video, pierwszy kafelek (zgodne z nietrwałym stanem AD-10) | AD-10, AD-16 |

## Rozstrzygnięcia R/A

Założenia z rundy triage „Dokończ” (A1–A26) i rozstrzygnięcia Reviewer Gate (R1–R16) użytkownik potwierdził 2026-10-17 w ramach [`change-mediastation`](../change-mediastation/change-mediastation.md). Wszystkie są decyzjami; treść żyje w spine'ach, a id zostają dla odwołań z architektury, memloga i `validation-report`.

| Id | Rozstrzygnięcie | Gdzie |
|---|---|---|
| A1 | Powitanie „Dobry wieczór, Eliza” (pora dnia + imię) | EXPERIENCE → Voice and Tone; DESIGN → Typography |
| A2 | Copy karty z kanonu: podtytuł „Sezon 8 · obejrzany” / „Film · obejrzany”, podpowiedź „Zeskanuj telefonem i oceń na Filmwebie” | EXPERIENCE → Voice and Tone |
| A3 | Copy pustej Audio: „Tu jeszcze cisza. Muzyka dojdzie wkrótce.” | EXPERIENCE → Voice and Tone, State Patterns |
| A4 | Copy błędu konfiguracji: „Coś nie gra w konfiguracji” + plik i błąd | EXPERIENCE → Voice and Tone |
| A5 | Scrim `{colors.scrim}` pod AppMenu | DESIGN → Elevation & Depth, Components (`appmenu`) |
| A6 | Przejście fokusu ok. 150 ms ease-out | DESIGN → Elevation & Depth; EXPERIENCE → Interaction Primitives |
| A7 | Karta znika bez potwierdzenia; fokus na sąsiednią kartę (prawa, lewa, potem kafelki) | EXPERIENCE → Component Patterns (Card) |
| A8 | Back na Home nic nie robi | EXPERIENCE → Interaction Primitives → Back |
| A9 | Klawisz Home zamyka OSK/AppMenu i pokazuje Home | EXPERIENCE → Interaction Primitives → Klawisz Home |
| A10 | Zakładka aktywuje się na fokus | EXPERIENCE → Component Patterns (Tab) |
| A11 | OK na kafelku Starting jest ignorowane | EXPERIENCE → Component Patterns (Tile) |
| A12 | Fokus po restarcie MediaStation jak przy zimnym starcie | EXPERIENCE → State Patterns → Powierzchnie |
| A13 | Brak dalszej eskalacji przy powtórnej awarii (J4) | EXPERIENCE → Key Flows J4 |
| A14 | Failed: ikona + tekst, bez koloru błędu (memlog L63, potwierdzone 2026-10-17) | DESIGN → Colors, `chip-failed`; EXPERIENCE → State Patterns |
| A15 | AppMenu: Back zamyka i wraca na kafelek; long-press na NotRunning nic nie robi; przy Failed z fokusem na „Uruchom ponownie”, inaczej na pierwszej pozycji | EXPERIENCE → Component Patterns, Back |
| A16 | Wiersze dłuższe niż ekran przewijają się poziomo; lewa krawędź trzyma gutter | DESIGN → Layout & Spacing |
| A17 | Fokus na przyciskach karty, nie na całej karcie | DESIGN → Components; EXPERIENCE → Component Patterns |
| A18 | OSK: panel u dołu, pełna szerokość minus gutter, `{rounded.tile}`; wymiary klawiszy z button/tab | DESIGN → `osk-panel`, `osk-key`, Shapes, Components |
| A19 | AppMenu: panel nad kafelkiem, `{rounded.card}` | DESIGN → `appmenu`, Shapes, Components |
| A20 | Config error: pełny ekran na `ground`, logo, bez akcji; po poprawce configu restart | DESIGN → `config-error`; EXPERIENCE → Component Patterns |
| A21 | W Netflixie brak pointera, tylko D-pad | EXPERIENCE → Interaction Primitives → Pointer |
| A22 | Kursor żyroskopu w OSK przenosi jedyny fokus (`focus-pill` pod wskaźnikiem); kursor ograniczony do okna OSK (`ClipCursor`, AD-8) | DESIGN → Components (OSK); EXPERIENCE → Pointer |
| A23 | Wylogowany Netflix: ekran as-is, klawisz Home ratuje, sprawa autora | EXPERIENCE → State Patterns → Powierzchnie |
| A24 | Tytuł karty max 2 linie (ellipsis), podpowiedź 1 linia | DESIGN → `card-do-oceny`, Typography, Components |
| A25 | Kropka na zakładce bez tekstu = świadomy wyjątek floor (tekst stoi na kafelku) | DESIGN → Components (Tab); EXPERIENCE → Tab, Accessibility Floor |
| A26 | Starting: ikona statyczna; po timeoucie startu → Failed | DESIGN → `chip-starting`, stany kafelka; EXPERIENCE → State Patterns |
| R1 | Jeden fokus w trybie `osk`: biały fokus tylko na klawiszu; pole Netflixa jako `input-active` (kursor + linia 2px `text-2`) | DESIGN → `input-active`, Colors, Elevation, Components; EXPERIENCE → Focus ring, State Patterns, OSK, J3, Handoff |
| R2 | QR: minimalny URL wyszukiwarki filmweb.pl z tytułem, bez zewnętrznego skracacza, ECC L, quiet zone ≥ 4 moduły, moduły w pełnych px, ok. 240px → 360px z fokusem w wierszu; otwarte: test 3 m, dwa telefony — story „Do oceny” (AD-12) | DESIGN → `qr`, `card-do-oceny`, Components; EXPERIENCE → Component Patterns (QR), Handoff |
| R3 | Title-safe 5% (96px / 54px); wiersz „Do oceny” w peek, scroll w górę z fokusem; większy odstęp nad nagłówkiem | DESIGN → spacing, `rate-row`, Layout & Spacing, Components; EXPERIENCE → Scroll Home, Rate row, Accessibility Floor |
| R4 | Fokus wstrzykiwany: obrys 5px + ciemna obwódka 3px `focus-halo`, promień elementu (fallback 4px), bez skali i cienia | DESIGN → `focus-ring-injected`, Colors, Elevation, Shapes; EXPERIENCE → Focus ring, Handoff |
| R5 | Fokus małych kontrolek = odwrócona pigułka; „Pomiń” w spoczynku z wypełnieniem, bez obrysu | DESIGN → `focus-pill`, `button-ghost`, Components; EXPERIENCE → Focus ring |
| R6 | Stany kafelka: chip + linia stanu (tabela); ikony Material Symbols Rounded; copy „Chwilka…” | DESIGN → `chip-*`, Stany kafelka; EXPERIENCE → State Patterns, Voice and Tone, Chip |
| R7 | Aplikacja pada na pierwszym planie → MediaStation wraca na Home, fokus na kafelku Failed | EXPERIENCE → Awarie w trakcie użycia, Model fokusu, J1, J4, Handoff |
| R8 | Zerwane CDP przy żywym procesie: bez UI, klawisz Home ratuje, D-pad niebuforowany, po `cdpReconnectTimeout` = 30 s (`settings.json`) → Failed (AD-10, AD-11) | EXPERIENCE → Awarie w trakcie użycia, Klawisz Home, Handoff |
| R9 | Profil Netflixa wybiera Eliza; driver tylko fokusuje ostatnio używany profil (też po wybudzeniu) | EXPERIENCE → Profile gate, J1 krok 3, Handoff |
| R10 | Action `Keyboard` z pilota nie w MVP (memlog L62, potwierdzone 2026-10-17); OSK tylko auto w webie, Moonlight/Plex z klawiatury lub gamepada; zawężenie AD-9 przyjęte w `change-mediastation` | EXPERIENCE → Foundation, IA, OSK, Deferred, Handoff |
| R11 | Meta: wszystkie założenia obu spine'ów zebrane w jednej tabeli z id i miejscem (memlog L66); po potwierdzeniu 2026-10-17 tabela to ta sekcja, tagi inline usunięte | EXPERIENCE → Rozstrzygnięcia R/A |
| R12 | OSK: QWERTY, „Usuń”, „Spacja”, „123”, „Gotowe”; znacznik wariantów na a c e l n o s z; `press` wstawia, `long` otwiera rząd wariantów bez litery bazowej, Back go zamyka | DESIGN → `osk-key`, `osk-variants-row`, Components; EXPERIENCE → OSK, Back, J3, Handoff |
| R13 | Empty state jako komponent: body, `text-2`, w pasie kafelków; fokus zostaje na zakładce | DESIGN → `empty-state`, Components; EXPERIENCE → Component Patterns, State Patterns |
| R14 | Mocki wyłącznie na tokenach; hexy spoza palety → `surface-dim` (#1A1C20) albo `surface` | DESIGN → Do's and Don'ts |
| R15 | „Pomiń” bez undo w MVP | EXPERIENCE → Card, Deferred |
| R16 | Długi tytuł karty „Do oceny”: tytuł max 2 linie z wielokropkiem, podpowiedź 1 linia (A24); przyciski zakotwiczone na dole karty, nigdy poza kartą (memlog L67). Test QR z 3 m — R2 | DESIGN → `card-do-oceny`, Typography, Components |
