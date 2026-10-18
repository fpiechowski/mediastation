---
name: MediaStation
description: Ciemny 10-foot UI na TV 1920×1080 w kanonie Google TV / Apple TV — kafelki aplikacji, wiersz „Do oceny”, biały fokus widoczny z kanapy.
status: final
created: 2026-10-16
updated: 2026-10-18
sources:
  - ../brief-mediastation/brief-mediastation.md
  - ../brief-mediastation/addendum.md
  - ../architecture-mediastation/architecture-mediastation.md
  - ../forge-filmweb-rating-prompt/forge-filmweb-rating-prompt.md
  - ../research-browser-control-for-streaming-services/research-browser-control-for-streaming-services.md
  - ../change-osk-everywhere-github-update/change-osk-everywhere-github-update.md
colors:
  ground: '#0F1012'
  ground-glow: '#8AB4F812'
  surface-dim: '#1A1C20'
  surface: '#1E2024'
  surface-2: '#26292E'
  line: '#33363C'
  text: '#FFFFFF'
  text-2: '#C4C7CC'
  text-3: '#9AA0A6'
  accent: '#8AB4F8'
  accent-halo: '#8AB4F82E'
  focus: '#FFFFFF'
  focus-ink: '#0F1012'
  focus-halo: '#0F1012'
  tile-mark: '#E6E8EB'
  tile-gradient-top: '#2A2D33'
  scrim: '#0F1012B8'
  qr-bg: '#FFFFFF'
  qr-ink: '#111111'
  avatar-from: '#E8A87C'
  avatar-to: '#C77D9B'
  avatar-ink: '#1A1214'
typography:
  wordmark:
    fontFamily: Roboto Flex
    fontSize: 32px
    fontWeight: '600'
    letterSpacing: -0.01em
  clock:
    fontFamily: Roboto Flex
    fontSize: 32px
    fontWeight: '500'
    note: 'font-variant-numeric: tabular-nums'
  greeting:
    fontFamily: Roboto Flex
    fontSize: 28px
    fontWeight: '400'
  tab:
    fontFamily: Roboto Flex
    fontSize: 30px
    fontWeight: '500'
  tile-mark:
    fontFamily: Roboto Flex
    fontSize: 64px
    fontWeight: '650'
    letterSpacing: -0.02em
  tile-name:
    fontFamily: Roboto Flex
    fontSize: 30px
    fontWeight: '600'
  tile-name-unfocused:
    fontFamily: Roboto Flex
    fontSize: 30px
    fontWeight: '500'
  section-title:
    fontFamily: Roboto Flex
    fontSize: 36px
    fontWeight: '600'
    letterSpacing: -0.005em
  card-title:
    fontFamily: Roboto Flex
    fontSize: 34px
    fontWeight: '600'
    lineHeight: '1.15'
    letterSpacing: -0.01em
  body:
    fontFamily: Roboto Flex
    fontSize: 26px
    fontWeight: '400'
    lineHeight: '1.3'
  button:
    fontFamily: Roboto Flex
    fontSize: 26px
    fontWeight: '500'
  chip:
    fontFamily: Roboto Flex
    fontSize: 24px
    fontWeight: '400'
rounded:
  qr: 14px
  card: 20px
  tile: 22px
  injected-fallback: 4px
  full: 9999px
spacing:
  '1': 6px
  '2': 12px
  '3': 16px
  '4': 24px
  '5': 36px
  '6': 48px
  '7': 56px
  gutter: 112px
  title-safe-x: 96px
  title-safe-y: 54px
  bar-top: 56px
  tabs-top: 172px
  apps-top: 292px
  rate-top: 768px
  rate-top-focused: 554px
  rate-heading-above: 48px
  rate-heading-below: 20px
  tile-gap: 48px
  card-gap: 40px
  meta-gap: 30px
  meta-gap-focused: 44px
components:
  elevation-rest:
    shadow: '0 2px 6px rgba(0,0,0,.35)'
  elevation-lift:
    shadow: '0 28px 56px rgba(0,0,0,.6), 0 10px 20px rgba(0,0,0,.45)'
  focus-ring:
    outline: '5px solid {colors.focus}'
    outlineOffset: 0
    scale: 1.08
    shadow: '{components.elevation-lift.shadow}'
    note: 'Tylko w shellu, tylko kafelki. Element w fokusie dostaje obrys + skalę + cień.'
  focus-pill:
    background: '{colors.focus}'
    textColor: '{colors.focus-ink}'
    iconColor: '{colors.focus-ink}'
    outline: none
    scale: 1
    note: 'Fokus małych kontrolek (button, klawisz OSK, pozycja AppMenu, zakładka): odwrócona pigułka. Kontrast tekstu 19.0:1; pigułka vs surface 16.3:1, vs surface-2 14.6:1.'
  focus-ring-injected:
    outline: '5px solid {colors.focus}'
    halo: '3px solid {colors.focus-halo}'
    css: 'box-shadow: 0 0 0 5px {colors.focus}, 0 0 0 8px {colors.focus-halo}'
    radius: 'border-radius elementu docelowego (getComputedStyle), fallback {rounded.injected-fallback}'
    scale: 1
    shadow: none
    note: 'PageRuntime w Netflixie. Biały obrys vs ciemne tła Netflixa ≥ 18.4:1; ciemna obwódka vs jasny artwork (#E6E6E6–#FFFFFF) 15.3–19.0:1 — na każdym tle jeden z dwóch pierścieni ma ≥ 3:1.'
  input-active:
    caret: '{colors.text}'
    underline: '2px solid {colors.text-2}'
    outline: none
    note: 'Pole docelowe w Netflixie, gdy otwarta jest OSK (tryb osk). Linia text-2 vs tło Netflixa #141414 10.9:1. Bez białego ringu.'
  logo:
    size: 48px
    mark: 'zaokrąglony ekran (obrys 3.2 / 48, rx 9) + trójkąt play, currentColor = {colors.text}'
    wordmark: '{typography.wordmark}'
    gap: 18px
    note: 'Placeholder w MVP; właściwe logo deferred.'
  avatar:
    size: 48px
    radius: '{rounded.full}'
    background: 'linear-gradient(140deg, {colors.avatar-from}, {colors.avatar-to})'
    textColor: '{colors.avatar-ink}'
    fontSize: 24px
    fontWeight: '650'
  tab:
    height: 64px
    padding: 0 32px
    radius: '{rounded.full}'
    gap: '{spacing.2}'
    textColor: '{colors.text-3}'
    typography: '{typography.tab}'
  tab-active:
    background: '{colors.surface-2}'
    textColor: '{colors.text}'
  tab-focused:
    style: '{components.focus-pill}'
  tab-running-dot:
    size: 12px
    radius: '{rounded.full}'
    background: '{colors.accent}'
    halo: '0 0 0 4px {colors.accent-halo}'
  tile:
    width: 544px
    height: 306px
    radius: '{rounded.tile}'
    background: '{colors.surface}'
    gradient: 'linear-gradient(160deg, {colors.tile-gradient-top} 0%, {colors.surface} 60%)'
    shadow: '{components.elevation-rest.shadow}'
    markTypography: '{typography.tile-mark}'
    markColor: '{colors.tile-mark}'
  tile-focused:
    ring: '{components.focus-ring}'
    nameColor: '{colors.text}'
    metaGap: '{spacing.meta-gap-focused}'
  tile-meta:
    nameTypography: '{typography.tile-name}'
    nameTypographyUnfocused: '{typography.tile-name-unfocused}'
    nameColorUnfocused: '{colors.text-3}'
    stateTypography: '{typography.body}'
    stateColor: '{colors.text-2}'
  chip:
    height: 44px
    padding: 0 18px 0 14px
    radius: '{rounded.full}'
    background: '{colors.scrim}'
    textColor: '{colors.text}'
    typography: '{typography.chip}'
    iconSize: 22px
    iconSet: 'Material Symbols Rounded'
    inset: '{spacing.4}'
  chip-background:
    icon: pause
    label: 'Wstrzymane'
  chip-background-muted:
    icon: volume_off
    label: 'Wyciszony'
    note: 'Moonlight w tle: sesja żyje, tylko wyciszona.'
  chip-starting:
    icon: progress_activity
    label: 'Uruchamiam…'
    note: 'Ikona statyczna (bez obrotu).'
  chip-stopping:
    icon: close
    label: 'Zamykam…'
  chip-failed:
    icon: error
    label: 'Nie odpowiada'
    note: 'Bez koloru błędu — ikona + tekst.'
  state-dot:
    size: 10px
    radius: '{rounded.full}'
    background: '{colors.accent}'
  rate-row:
    headingTypography: '{typography.section-title}'
    headingAbove: '{spacing.rate-heading-above}'
    headingBelow: '{spacing.rate-heading-below}'
    topRest: '{spacing.rate-top}'
    topFocused: '{spacing.rate-top-focused}'
    gap: '{spacing.card-gap}'
    note: 'Spoczynek na rate-top: widać nagłówek i górę kart (peek). Z fokusem w wierszu na rate-top-focused (przesunięcie 214px), dół kart ≥ title-safe-y od krawędzi.'
  card-do-oceny:
    width: 820px
    widthFocused: 940px
    height: 288px
    heightFocused: 408px
    radius: '{rounded.card}'
    background: '{colors.surface}'
    padding: 24px 36px 24px 24px
    gap: '{spacing.5}'
    titleTypography: '{typography.card-title}'
    titleMaxLines: 2
    hintMaxLines: 1
    subColor: '{colors.text-3}'
    hintColor: '{colors.text-2}'
    note: 'widthFocused / heightFocused: wariant karty z fokusem w wierszu „Do oceny”. Tytuł max 2 linie (ellipsis), podpowiedź 1 linia.'
  qr:
    size: 240px
    sizeFocused: 360px
    quietZone: '≥ 4 moduły, białe, wewnątrz pola'
    module: 'pełne piksele (np. wersja 3 = 29 modułów + 8 quiet: 6px → 222px / 9px → 333px, reszta pola białe), shape-rendering: crispEdges'
    ecc: L
    radius: '{rounded.qr}'
    background: '{colors.qr-bg}'
    ink: '{colors.qr-ink}'
    note: 'Wygląd: rozmiar, quiet zone, ECC L, moduły w pełnych px.'
  button:
    height: 56px
    padding: 0 28px
    radius: '{rounded.full}'
    background: '{colors.surface-2}'
    textColor: '{colors.text}'
    typography: '{typography.button}'
    iconSize: 24px
    focused: '{components.focus-pill}'
  button-ghost:
    background: '{colors.surface-2}'
    textColor: '{colors.text-2}'
    border: none
    focused: '{components.focus-pill}'
    note: '„Pomiń” w spoczynku: subtelne wypełnienie zamiast obrysu, który udawał ring. Różnica vs primary: kolor etykiety text-2 (8.6:1).'
  osk-panel:
    background: '{colors.surface}'
    radius: '{rounded.tile}'
    note: 'Panel u dołu ekranu, pełna szerokość minus {spacing.gutter}, dół ≥ {spacing.title-safe-y}.'
  osk-key:
    height: '{components.button.height}'
    radius: '{rounded.full}'
    background: '{colors.surface-2}'
    textColor: '{colors.text}'
    typography: '{typography.tab}'
    focused: '{components.focus-pill}'
    variantMark: '{colors.text-3}'
    specialKeys: 'Usuń (backspace), Spacja (space_bar), 123, Gotowe (check)'
    note: 'Klawisze z wariantami (a c e l n o s z) mają mały znacznik w rogu. Wymiary wyprowadzone z button/tab.'
  osk-variants-row:
    background: '{colors.surface-2}'
    keyStyle: '{components.osk-key}'
    note: 'Rząd wariantów wewnątrz panelu OSK, nad rzędem klawisza źródłowego; fokus na pierwszym wariancie.'
  appmenu:
    background: '{colors.surface}'
    radius: '{rounded.card}'
    itemHeight: '{components.button.height}'
    itemRadius: '{rounded.full}'
    itemTypography: '{typography.button}'
    itemFocused: '{components.focus-pill}'
    note: 'Panel nad kafelkiem; scrim {colors.scrim} pod spodem.'
  empty-state:
    typography: '{typography.body}'
    textColor: '{colors.text-2}'
    top: '{spacing.apps-top}'
    left: '{spacing.gutter}'
    note: 'W pasie kafelków. Kontrast text-2 / ground 11.2:1.'
  update-button:
    height: 48px
    padding: 0 24px 0 20px
    radius: '{rounded.full}'
    background: '{colors.surface-2}'
    textColor: '{colors.text}'
    typography: '{typography.button}'
    iconSize: 24px
    iconGap: 10px
    iconSet: 'Material Symbols Rounded'
    gapToClock: '{spacing.5}'
    focused: '{components.focus-pill}'
    note: 'Nagłówek Home, prawa grupa: [przycisk] {spacing.5} [zegar]; zegar zostaje skrajnie z prawej, na {spacing.gutter}. Wysokość 48px = logo i avatar, więc przycisk mieści się w pasie nagłówka (top {spacing.bar-top} = 56px ≥ {spacing.title-safe-y}) i nie przesuwa żadnego pasma. Szerokość wg etykiety; prawa krawędź stała, przycisk rośnie w lewo. Tekst text / surface-2 14.6:1; fokus: focus-pill vs ground 19.0:1.'
  update-button-idle:
    icon: download
    label: 'Zaktualizuj'
  update-button-downloading:
    icon: progress_activity
    label: 'Pobieram aktualizację…'
    note: 'Ikona statyczna (bez obrotu), jak chip-starting.'
  update-button-applying:
    icon: progress_activity
    label: 'Uruchamiam nową wersję…'
    note: 'Ikona statyczna.'
  update-button-failed:
    icon: error
    label: 'Zaktualizuj'
    note: 'Bez koloru błędu — ikona + linia błędu w update-status.'
  update-status:
    typography: '{typography.body}'
    textColor: '{colors.text-2}'
    top: 116px
    align: right
    right: '{spacing.gutter}'
    maxLines: 1
    note: 'Linia pod przyciskiem aktualizacji, wyrównana do prawej krawędzi nagłówka: top = {spacing.bar-top} + 48px + {spacing.2}; dół ok. 150px, 22px nad {spacing.tabs-top}. Treść: „Nowa wersja <wersja>” (idle, downloading, applying), komunikat błędu (failed) albo komunikat po rollbacku (bez przycisku). <wersja> = semver bez „v”. Kontrast text-2 / ground 11.2:1. Poziomo nie nachodzi na rząd zakładek (zakładki kończą się ok. 620px, najdłuższa linia ok. 930px od prawej).'
  config-error:
    background: '{colors.ground}'
    titleTypography: '{typography.card-title}'
    bodyTypography: '{typography.body}'
    note: 'Brak szkicu; pełnoekranowy komunikat na {colors.ground}, logo w lewym górnym rogu.'
---

## Brand & Style

MediaStation to kanon kategorii — Google TV / Apple TV — wykonany bez ironii i z pełną starannością. Nie szukamy własnego dziwactwa: Eliza ma poczuć, że to „normalny, ładny telewizor”, tylko bez reklam. Cztery słowa: **nowoczesny, estetyczny, ciemny, czysty**. Ciemny motyw wynika ze sceny: wieczór, za oknem ciemno, w salonie słabe ciepłe światło, kanapa ~3 m od TV.

Anty-marketing: żadnych kafelków promocyjnych, banerów ani rekomendacji. Na ekranie są tylko aplikacje domowników i to, co na nich czeka („Do oceny”). Poprzeczka jakości: Google TV i Apple TV. Głos (patrz `EXPERIENCE.md` → Voice and Tone): ciepło, na ty, z lekkim humorem — ale wizualnie nic nie mruga.

Logo w MVP to placeholder: zaokrąglony ekran z trójkątem play + wordmark „MediaStation” w lewym górnym rogu (`{components.logo}`). Właściwe logo jest deferred.

**Referencje wizualne.** Szkic kierunku: [`mockups/direction-canon.html`](mockups/direction-canon.html) — kompozycja kanonu (ekran główny, zakładka Video, Netflix w tle, dwie karty „Do oceny”); bazowe tokeny pochodzą z jego `:root` i CSS. Key-screen mocki są linkowane inline w sekcjach, których dotyczą. Mocki powstały przed Reviewer Gate i mogą nie mieć rozstrzygnięć R1–R16. **Przy konflikcie z mockami wygrywają spine'y (`DESIGN.md` + `EXPERIENCE.md`).** Rozstrzygnięcia R1–R16 i A1–A26 są potwierdzone (2026-10-17); ich lista: `EXPERIENCE.md` → Rozstrzygnięcia R/A.

**Odrzucone kierunki wizualne:** Neon nad Wisłą, Wieczorna lampa, Tablica odjazdów.

## Colors

Grafitowa skala tonalna + jeden chłodny akcent. Kontrast liczony względem `{colors.ground}` / `{colors.surface}` / `{colors.surface-2}`.

| Token | Hex | Rola | Kontrast (ground / surface / surface-2) |
|---|---|---|---|
| `ground` | `#0F1012` | Tło całego ekranu | — |
| `ground-glow` | `#8AB4F812` | Poświata tła (accent 7%) | — |
| `surface-dim` | `#1A1C20` | Przygaszona powierzchnia (np. kafelek-tło pod scrimem AppMenu) | — (text 17.1, text-2 10.1, text-3 6.5 na surface-dim) |
| `surface` | `#1E2024` | Kafelki, karty, panele (OSK, AppMenu) | — |
| `surface-2` | `#26292E` | Aktywna zakładka, przycisk, klawisz OSK | — |
| `line` | `#33363C` | Separator powitania (dekoracyjny, nie nośny) | 1.6 / 1.3 / 1.2 |
| `text` | `#FFFFFF` | Tekst główny, nazwa w fokusie | 19.0 / 16.3 / 14.6 |
| `text-2` | `#C4C7CC` | Tekst drugorzędny, linia stanu, zegar, powitanie, empty state, „Pomiń”, linia statusu aktualizacji | 11.2 / 9.6 / 8.6 |
| `text-3` | `#9AA0A6` | Nieaktywne zakładki, nazwy poza fokusem, podtytuł karty, znacznik wariantów OSK | 7.2 / 6.2 / 5.5 |
| `accent` | `#8AB4F8` | **Tylko małe wskaźniki stanu** (kropka „działa w tle”, kropka na zakładce) | 9.0 / 7.7 / 6.9 |
| `accent-halo` | `#8AB4F82E` | Halo kropki na zakładce (accent 18%) | — |
| `focus` | `#FFFFFF` | Obrys fokusu kafelka, wstrzyknięty obrys, wypełnienie `focus-pill` | 19.0 / 16.3 / 14.6 |
| `focus-ink` | `#0F1012` | Tekst i ikona na `focus-pill` | 19.0 na `focus` |
| `focus-halo` | `#0F1012` | Ciemna obwódka 3px wstrzykniętego fokusu | 19.0 vs `focus`; 15.3–19.0 vs jasny artwork |
| `tile-mark` | `#E6E8EB` | Wordmark aplikacji na kafelku | — / 13.3 / — |
| `tile-gradient-top` | `#2A2D33` | Górny kolor delikatnego gradientu kafelka | — |
| `scrim` | `#0F1012B8` | Tło chipa na kafelku, scrim pod AppMenu (72% ground) | — |
| `qr-bg` / `qr-ink` | `#FFFFFF` / `#111111` | QR zawsze ciemny na białym — skanowalność (18.9:1) | — |
| `avatar-from` / `avatar-to` / `avatar-ink` | `#E8A87C` / `#C77D9B` / `#1A1214` | Jedyny ciepły akcent: avatar powitania (inicjał ≥ 6.0:1) | — |

Zasady:
- Cały tekst ≥ 5.5:1 (WCAG AA z zapasem). `line` nigdy nie niesie informacji samodzielnie.
- `accent` nie jest kolorem fokusu, przycisku ani tła. Fokus jest biały i rozpoznawalny bez koloru.
- Kontrast fokusu (non-text) ≥ 3:1 względem przyległych kolorów, także na jasnym artworku. Wartości per wariant: notatki `{components.focus-pill}`, `{components.focus-ring-injected}` i `{components.input-active}`.
- Tło ma subtelną poświatę: `radial-gradient(1200px 520px at 30% -10%, {colors.ground-glow}, transparent 70%)`, wysokość 520px.
- Brak koloru błędu w palecie. Stan Failed rozróżnia ikona + tekst, nie czerwień.

## Typography

Jedna rodzina: **Roboto Flex** (osie `opsz`, `wght`; polskie znaki), `font-variation-settings: "opsz" 32`, antialiasing włączony. Font bundlowany lokalnie w shellu (`@font-face`, TV bywa offline). Rozmiary 10-foot, **minimum 24px** na ekranie TV.

| Rola | Token | Rozmiar / waga | Użycie |
|---|---|---|---|
| Wordmark | `{typography.wordmark}` | 32px / 600 | „MediaStation” obok logo |
| Zegar | `{typography.clock}` | 32px / 500, tabular | Prawy górny róg |
| Powitanie | `{typography.greeting}` | 28px / 400 | „Dobry wieczór, Eliza” |
| Zakładka | `{typography.tab}` | 30px / 500 | Video / Audio / Game |
| Znak na kafelku | `{typography.tile-mark}` | 64px / 650 | Wordmark aplikacji (placeholder ikon) |
| Nazwa kafelka | `{typography.tile-name}` / `{typography.tile-name-unfocused}` | 30px / 600 (fokus), 30px / 500 (poza fokusem) | Pod kafelkiem |
| Tytuł sekcji | `{typography.section-title}` | 36px / 600 | „Do oceny” (większy niż tytuł karty i nazwa kafelka) |
| Tytuł karty | `{typography.card-title}` | 34px / 600, lh 1.15 | Tytuł filmu/serialu, max 2 linie |
| Body | `{typography.body}` | 26px / 400, lh 1.3 | Linia stanu kafelka, podtytuł i podpowiedź karty, empty state, linia statusu aktualizacji |
| Przycisk | `{typography.button}` | 26px / 500 | „Ocenione”, „Pomiń”, pozycje AppMenu, przycisk aktualizacji |
| Chip | `{typography.chip}` | 24px / 400 | Chip stanu na kafelku |

Bez all-caps, bez rozmiarów display ponad 64px. Tytuły mogą być po angielsku (język profilu Netflixa) — nie tłumaczymy ich.

## Layout & Spacing

Jedna powierzchnia: **1920×1080**, stały viewport, **bez responsive**. Title-safe 5%: `{spacing.title-safe-x}` (96px) w poziomie, `{spacing.title-safe-y}` (54px) w pionie — żaden element z fokusem ani akcją nie wychodzi poza tę ramkę. Gutter boczny `{spacing.gutter}` (112px) leży wewnątrz title-safe.

Pionowy układ Home (wartości bezwzględne, spoczynek):

| Pas | Top | Zawartość |
|---|---|---|
| Header | `{spacing.bar-top}` | Logo + wordmark, separator, avatar + powitanie (lewo); przycisk aktualizacji (tylko przy nowszej wersji) + zegar (prawo); pod przyciskiem linia `{components.update-status}`. Wysokość pasa bez zmian (48px) |
| Zakładki | `{spacing.tabs-top}` | Video / Audio / Game, gap `{spacing.2}` |
| Kafelki | `{spacing.apps-top}` | Kafelki 544×306 (16:9), gap `{spacing.tile-gap}`; meta pod kafelkiem `{spacing.meta-gap}`. Empty state zakładki stoi w tym pasie |
| Do oceny | `{spacing.rate-top}` | Nagłówek sekcji (odstęp nad nagłówkiem ≥ `{spacing.rate-heading-above}` od linii stanu, pod nagłówkiem `{spacing.rate-heading-below}`) + poziomy wiersz kart, gap `{spacing.card-gap}` |

**Peek i scroll wiersza „Do oceny” — geometria** (wzorzec Google TV) — ilustracja: [`mockups/key-home-video.html`](mockups/key-home-video.html). Zachowanie: `EXPERIENCE.md` → Interaction Primitives → Scroll Home.
- Spoczynek: wiersz na `{spacing.rate-top}` wystaje spod kafelków — widać nagłówek i górę kart; dolna część kart może być poza ekranem.
- Fokus w wierszu: wiersz na `{spacing.rate-top-focused}` (przesunięcie 214px; header i zakładki mogą wyjść poza ekran), karty w rozmiarze `widthFocused`/`heightFocused`, QR `{components.qr.sizeFocused}`, dół kart ≥ `{spacing.title-safe-y}` od krawędzi.

Skala: 6 / 12 / 16 / 24 / 36 / 48 / 56px (`{spacing.1}`…`{spacing.7}`). Kafelek w fokusie rośnie ×1.08 — sąsiedzi się nie przesuwają; meta pod kafelkiem w fokusie odsuwa się do `{spacing.meta-gap-focused}`, żeby skala jej nie zasłoniła. Wiersze dłuższe niż ekran przewijają się poziomo w obrębie wiersza; lewa krawędź trzyma gutter.

## Elevation & Depth

Głębia ma dwa poziomy — spoczynek i lift kafelka; pozostałe pozycje to warianty fokusu i nakładki bez liftu:

- **Spoczynek:** kafelek na `{colors.surface}`, cień `{components.elevation-rest}`. Karty bez cienia.
- **Fokus kafelka w shellu:** `{components.focus-ring}` — biały obrys 5px, `scale(1.08)`, cień `{components.elevation-lift}`. To jedyny „lift” w systemie.
- **Fokus małych kontrolek w shellu i OSK:** `{components.focus-pill}` — odwrócona pigułka (białe wypełnienie, tekst `{colors.focus-ink}`), bez skali i bez cienia.
- **Fokus w Netflixie (PageRuntime):** `{components.focus-ring-injected}` — biały obrys 5px + ciemna obwódka 3px `{colors.focus-halo}`, promień elementu Netflixa (fallback `{rounded.injected-fallback}`), **bez skali** (nie ruszamy layoutu cudzej strony) i bez cienia.
- **Pole w Netflixie przy otwartej OSK:** `{components.input-active}` — kursor tekstowy + linia 2px `{colors.text-2}`, bez ringu; jedyny fokus jest na klawiszu OSK.
- **Nakładki (OSK, AppMenu):** panel `{colors.surface}` nad treścią; scrim pod AppMenu.

Animacja fokusu: krótki transform/outline, ok. 150 ms ease-out; brak animacji wejścia ekranu. Przesunięcie strony przy wejściu do wiersza „Do oceny” używa tej samej krzywej.

## Shapes

Miękkie, zaokrąglone prostokąty — język Google TV:

| Token | Wartość | Gdzie |
|---|---|---|
| `{rounded.tile}` | 22px | Kafelki aplikacji, panel OSK |
| `{rounded.card}` | 20px | Karta „Do oceny”, AppMenu |
| `{rounded.qr}` | 14px | Białe pole QR |
| `{rounded.injected-fallback}` | 4px | Promień wstrzykniętego fokusu, gdy element Netflixa nie ma własnego |
| `{rounded.full}` | 9999px | Zakładki, przyciski, chipy, kropki stanu, avatar, klawisze OSK, `focus-pill` |

Obrys fokusu przyjmuje promień elementu (`outline-offset: 0`) — w shellu i w Netflixie.

## Components

| Komponent | Anatomia i wygląd | Stany wizualne |
|---|---|---|
| **Focus ring** (`focus-ring`, `focus-pill`, `focus-ring-injected`, `input-active`) | Patrz Elevation & Depth (wszystkie warianty fokusu) | Patrz Elevation & Depth |
| **Tile** (`tile`, `tile-focused`, `tile-meta`) | 544×306, `{rounded.tile}`, gradient 160° od `{colors.tile-gradient-top}`, wordmark aplikacji `{typography.tile-mark}` na środku; pod spodem nazwa + linia stanu | Spoczynek: nazwa `{typography.tile-name-unfocused}`, `{colors.text-3}`. Fokus: ring, nazwa `{typography.tile-name}`, `{colors.text}`. Stany cyklu życia → chip + linia stanu (tabela niżej). Przykłady: [`mockups/key-home-video.html`](mockups/key-home-video.html) (Background), [`mockups/key-home-failed-appmenu.html`](mockups/key-home-failed-appmenu.html) (Failed, Starting) |
| **Chip** (`chip`, `chip-*`) | Pigułka 44px w lewym dolnym rogu kafelka (inset 24px), tło `{colors.scrim}`, ikona 22px z **Material Symbols Rounded** + krótki tekst | Tylko krótka etykieta; pełny komunikat idzie do linii stanu (tabela niżej) |
| **State dot** (`state-dot`) | Kropka 10px `{colors.accent}` przed linią stanu kafelka | Tylko Background („W tle · …”) |
| **Tab** (`tab`, `tab-active`, `tab-focused`, `tab-running-dot`) | Pigułka 64px, padding 0 32px | Nieaktywna: tekst `{colors.text-3}`. Aktywna bez fokusu: tło `{colors.surface-2}`, tekst `{colors.text}`. Fokus: `focus-pill`. Kropka 12px `{colors.accent}` z halo `{colors.accent-halo}` = w zakładce działa coś w tle |
| **Rate row** (`rate-row`) | Nagłówek „Do oceny” `{typography.section-title}` + poziomy wiersz kart | Peek i scroll: geometria w Layout & Spacing, zachowanie w `EXPERIENCE.md` → Scroll Home. Pusty: wiersz znika |
| **Card „Do oceny”** (`card-do-oceny`) | 820×288 (focused 940×408), `{rounded.card}`, `{colors.surface}`; QR po lewej; tytuł `{typography.card-title}` max 2 linie, podtytuł (`{colors.text-3}`), podpowiedź (`{colors.text-2}`, 1 linia), przyciski na dole. Ilustracja: [`mockups/key-home-video.html`](mockups/key-home-video.html) | Fokus na przyciskach karty, nie na całej karcie |
| **QR** (`qr`) | Białe pole ok. 240px (focused ok. 360px), quiet zone ≥ 4 moduły, moduły w pełnych px, ECC L, `{rounded.qr}`, moduły `{colors.qr-ink}` | Statyczny; nigdy nie dostaje fokusu |
| **Button** (`button`, `button-ghost`) | Pigułka 56px, ikona 24px + etykieta. Primary „Ocenione” (`check`) na `{colors.surface-2}`, tekst `{colors.text}`; ghost „Pomiń” (`close`) na `{colors.surface-2}`, tekst `{colors.text-2}`, bez obrysu | Fokus: `focus-pill` |
| **OSK** (`osk-panel`, `osk-key`, `osk-variants-row`) | Panel `{colors.surface}` u dołu ekranu; klawisze-pigułki: QWERTY, „Usuń” (`backspace`), „Spacja” (`space_bar`), „123” (panel cyfr i symboli `@ . _ -`), „Gotowe” (`check`); klawisze a c e l n o s z mają znacznik wariantów `{colors.text-3}` w rogu. Ilustracja: [`mockups/key-netflix-search-osk.html`](mockups/key-netflix-search-osk.html) | Fokus: `focus-pill`; kursor żyroskopu przenosi ten sam jedyny fokus. Long OK: rząd wariantów ą ć ę ł ń ó ś ź ż wewnątrz panelu, fokus na pierwszym wariancie. Pole w Netflixie: `input-active` |
| **AppMenu** (`appmenu`) | Panel `{colors.surface}`, pozycje jak button: „Uruchom ponownie”, „Zamknij”; scrim pod spodem. Ilustracja: [`mockups/key-home-failed-appmenu.html`](mockups/key-home-failed-appmenu.html) | Pozycja w fokusie: `focus-pill`; przy Failed otwiera się z fokusem na „Uruchom ponownie” |
| **Empty state** (`empty-state`) | Jedno zdanie `{typography.body}`, `{colors.text-2}`, w pasie kafelków (`{spacing.apps-top}`, od guttera). Ilustracja: [`mockups/key-home-game.html`](mockups/key-home-game.html) (pusta Audio) | Nie fokusowalny; fokus zostaje na zakładce |
| **Logo** (`logo`) | 48px: ekran rx 9 + play, `currentColor` (`{colors.text}`); wordmark obok, gap 18px | Placeholder |
| **Avatar + powitanie** (`avatar`) | Koło 48px, gradient `{colors.avatar-from}`→`{colors.avatar-to}`, inicjał; separator 2px `{colors.line}`, odstęp 56px | Dekoracja, bez fokusu |
| **Przycisk aktualizacji** (`update-button`, `update-button-*`, `update-status`) | Pigułka 48px w prawej grupie nagłówka, `{spacing.5}` na lewo od zegara; ikona 24px z **Material Symbols Rounded** + etykieta `{typography.button}`; tło `{colors.surface-2}`, tekst `{colors.text}`. Pod spodem, do prawej krawędzi nagłówka, linia `{components.update-status}`. Ilustracja: [`mockups/key-home-update.html`](mockups/key-home-update.html) | Tabela stanów niżej. Fokus: `focus-pill`. Bez przycisku nagłówek wygląda jak dotąd |
| **Config error** (`config-error`) | Pełny ekran na `{colors.ground}`, logo, tytuł + body | Brak fokusu / akcji |

**Stany kafelka: chip + linia stanu**. Ikony z jednego zestawu: Material Symbols Rounded.

| Stan | Chip (ikona + tekst) | Linia stanu pod nazwą (`{typography.body}`, `{colors.text-2}`) |
|---|---|---|
| NotRunning | — | — |
| Starting | `progress_activity` „Uruchamiam…” (ikona statyczna) | „Chwilka…” |
| Background | `pause` „Wstrzymane” (Netflix) / `volume_off` „Wyciszony” (Moonlight — sesja żyje, nie jest wstrzymana) | `{components.state-dot}` + „W tle · <tytuł>, wstrzymane” (Netflix) albo „W tle · wyciszony” (Moonlight) |
| Stopping | `close` „Zamykam…” | — |
| Failed | `error` „Nie odpowiada” | „Coś się zacięło. Wciśnij OK” |

**Stany przycisku aktualizacji**. Przycisk istnieje tylko przy `update.available` (EXPERIENCE.md → Przycisk aktualizacji).

| Faza | Przycisk (ikona + etykieta) | Linia `update-status` |
|---|---|---|
| `idle` | `download` „Zaktualizuj” | „Nowa wersja <wersja>” |
| `downloading` | `progress_activity` „Pobieram aktualizację…” (ikona statyczna) | „Nowa wersja <wersja>” |
| `applying` | `progress_activity` „Uruchamiam nową wersję…” (ikona statyczna) | „Nowa wersja <wersja>” |
| `failed` | `error` „Zaktualizuj” | „Nie udało się pobrać aktualizacji. Wciśnij OK, żeby spróbować jeszcze raz.” |
| po rollbacku (AD-17) | — (brak przycisku) | „Nowa wersja <wersja> nie wystartowała, działa poprzednia.” — ta sama pozycja (top 116px, do prawej), bez fokusu |

## Do's and Don'ts

| Do | Don't |
|---|---|
| Biały obrys + skala 1.08 + cień dla fokusu kafelka w shellu | Fokus kolorem akcentu, podkreśleniem albo samą zmianą tła |
| `focus-pill` (odwrócona pigułka) dla przycisków, klawiszy, pozycji menu i zakładek | Stały obrys na przycisku w spoczynku, który udaje ring |
| W Netflixie biały obrys + ciemna obwódka, promień elementu, bez skali | Skalować elementy strony Netflixa albo narzucać im promień 22px |
| Dokładnie jeden fokus; przy OSK pole Netflixa tylko jako `input-active` | Dwa białe ringi naraz |
| `{colors.accent}` tylko na małe wskaźniki stanu | Akcent jako tło przycisku, zakładki lub fokusu |
| Tekst ≥ 24px, kontrast ≥ 5.5:1; akcje w title-safe | Szary tekst `{colors.line}` niosący informację; przyciski przy krawędzi ekranu |
| QR zawsze ciemny na białym, quiet zone ≥ 4 moduły | Odwracać QR do dark mode; ułamkowe piksele modułów |
| W mockach wyłącznie tokeny; hexy spoza palety zastępuj `{colors.surface-dim}` albo `{colors.surface}` | Hexy spoza palety (np. `#24272C`, `#1B1D21`) |
| Wordmark/placeholder zamiast cudzych logotypów w szkicach | Kafelki promocyjne, banery, rekomendacje, reklamy |
| Jedna powierzchnia 1920×1080 | Breakpointy, kursor myszy, okna, pulpit |
| Przycisk aktualizacji neutralny (`surface-2`), stan ikoną i tekstem | Akcent, czerwień, pulsowanie albo badge, żeby przyciągnąć uwagę |
