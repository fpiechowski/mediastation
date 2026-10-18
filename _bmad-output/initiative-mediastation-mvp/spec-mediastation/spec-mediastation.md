---
id: SPEC-mediastation
companions:
  - ../architecture-mediastation/architecture-mediastation.md
  - ../ux-mediastation/EXPERIENCE.md
  - ../ux-mediastation/DESIGN.md
  - ../research-browser-control-for-streaming-services/digests/spike-um760.md
sources:
  - ../change-mediastation/change-mediastation.md
  - ../brief-mediastation/brief-mediastation.md
  - ../brief-mediastation/addendum.md
  - ../forge-filmweb-rating-prompt/forge-filmweb-rating-prompt.md
  - ../research-browser-control-for-streaming-services/research-browser-control-for-streaming-services.md
  - ../ux-mediastation/ux-mediastation.md
---

> **Canonical contract.** Ten SPEC i pliki z `companions:` to kompletny kontrakt tego, co budujemy, testujemy i walidujemy. Kontrakt komunikatów, schematy konfiguracji i Stack żyją w architecture spine. Zachowanie, flow i copy żyją w `EXPERIENCE.md`, a wygląd i tokeny w `DESIGN.md`. Zmierzone zachowanie Netflixa, Edge i Windows jest w digeście spike'a. Pliki z `sources:` służą tylko do traceability.

# MediaStation MVP

## Why

**Wizja plus ból.** Mini PC UM760 stoi pod TV jako klient Moonlight 4K@120 HDR i ma przejąć resztę roli TV boxa: Netflix, Plex i inne aplikacje. Windows jest jednak zbudowany pod mysz. Serwisy to strony WWW, klawiatura Windows nie obsługuje D-pada, nie ma „domu” pod Home, a aplikacje nie współistnieją (okna i dźwięki się nakładają). Flex Launcher, Playnite i Steam Big Picture uruchamiają przeglądarkę, ale nie sterują stroną serwisu i nie pauzują ani nie wyciszają aplikacji, którą się opuszcza. Poprzeczkę wyznacza Eliza, nietechniczna codzienna użytkowniczka VIDAA. Każde rozwiązanie, które wymaga myszy, pulpitu albo wiedzy o tym, co działa pod spodem, przegrywa u niej z pilotem od TV. Reszta MVP daje parytet z VIDAA, a „Do oceny” to jedyna funkcja, która daje jej powód, żeby wolała MediaStation.

## Capabilities

Epik może grupować kilka CAP-N (np. CAP-4 i CAP-5), a ID się przy tym nie zmieniają. Kolejność zależności: CAP-1 → CAP-2, CAP-3 → CAP-4, CAP-5, CAP-6 → CAP-7 → CAP-8 → CAP-9, CAP-11 → CAP-10, CAP-12. Pierwsza story CAP-7 idzie przed CAP-4 i CAP-9.

- **CAP-1**: Fundament i kontrakty
  - **intent:** MediaStation działa jako jedna instancja jednego procesu, ma rdzeń niezależny od Windows i jedno źródło kontraktów oraz konfiguracji, które dzielą wszystkie pozostałe capabilities.
  - **success:** Testy Core przechodzą bez referencji do Win32, WebView2, WPF i systemu plików. Typy TS powstają w buildzie z JSON Schema Core i nikt ich nie edytuje ręcznie. Druga instancja (także `--smoke`) nie startuje. Błędny `apps.json`, `input.json` albo `settings.json` (nieznane pole, zduplikowany `cdpPort`, port spoza 9220–9239) daje ekran błędu konfiguracji zamiast crasha.
  - **AD:** AD-2, AD-3, AD-11, AD-16

- **CAP-2**: Orchestrator i cykl życia aplikacji
  - **intent:** Kilka aplikacji działa naraz i przełącza się bez zamykania. Każda ma jawny stan cyklu życia, a Eliza naprawia zawieszoną aplikację z pilota.
  - **success:** Testy Orchestratora na fake'ach portów z `FakeTimeProvider` pokrywają:
    - przejścia `NotRunning → Starting → Foreground ⇄ Background → Stopping` i `Failed`;
    - Home w trakcie `Starting`, po którym aplikacja kończy start zminimalizowana;
    - adopcję żywej przeglądarki i Moonlighta po restarcie MediaStation;
    - wybudzenie: ekran główny z `focusRequest{default}`;
    - awarię aplikacji na pierwszym planie: ekran główny z fokusem na jej kafelku.

    Na UM760 jedno OK na kafelku `Failed` i jedno OK na „Uruchom ponownie” przywracają działającą aplikację (J4).
  - **AD:** AD-3, AD-4, AD-10, AD-14

- **CAP-3**: Wejście z pilota, klawiatury i gamepada
  - **intent:** Pilot G20S Pro BT, klawiatura i gamepad sterują MediaStation jednym zestawem Actions, kierowanych według bieżącego trybu (`shell`, `app`, `osk`). Home działa zawsze.
  - **success:**
    - Jedno naciśnięcie daje jedną Action.
    - Klawisz z przypisanym `long` (OK w `shell` i `osk`) daje `press` dopiero na key-up przed progiem albo samo `long`. Ta reguła wchodzi w pierwszej story epiki.
    - Home jest połykany i obsłużony nad Edge, Moonlightem, pulpitem i przy zawieszonym UI shella. Powtórzenie S10 przez Bluetooth daje 16/16.
    - Logi zawierają tylko Actions. Surowe kody pojawiają się wyłącznie w `--calibrate-input`.
  - **AD:** AD-2, AD-8

- **CAP-4**: Okna i foreground
  - **intent:** Jeden komponent decyduje o widoczności, Z-order i foreground każdego okna, więc ekran główny wraca nad dowolne okno, także gdy fokus ukradł inny proces.
  - **success:**
    - Opuszczona przeglądarka jest zminimalizowana, nigdy `SW_HIDE`.
    - Home przywraca shell nad Edge, Chrome i Moonlightem (borderless), z odblokowaniem foreground.
    - Zmiana foreground bez udziału MediaStation trafia do Orchestratora jako `ForegroundChanged`.
    - Host nigdy sam nie wynosi okna na wierzch.
  - **AD:** AD-5, AD-14, AD-15

- **CAP-5**: Audio focus
  - **intent:** Słychać tylko aktywną aplikację. Pozostałe są wyciszone w mikserze systemowym.
  - **success:** Po przełączeniu z Netflixa na Moonlight i z powrotem słychać wyłącznie aplikację na pierwszym planie, także gdy w drzewie procesów przeglądarki powstanie nowa sesja audio, np. po zmianie odcinka.
  - **AD:** AD-5, AD-15

- **CAP-6**: Ekran główny
  - **intent:** Eliza widzi pełnoekranowy ekran główny w stylu smart TV, z zakładkami Video, Audio i Game. Przechodzi po nim D-padem, z jednym widocznym fokusem i bez kursora.
  - **success:**
    - Zakładki i kolejność kafelków wynikają z `category` i kolejności w `apps.json`. Audio pokazuje empty state.
    - Kafelek pokazuje stan aplikacji. Kropka na zakładce sygnalizuje aplikację działającą w tle.
    - Zimny start, restart i wybudzenie stawiają fokus na pierwszym kafelku Video. Powrót z aplikacji stawia go na ostatnim kafelku. Nowa karta „Do oceny” nie kradnie fokusu.
    - AppMenu otwiera long-press OK na kafelku `Background` albo `Failed` oraz OK na `Failed`.
    - Wygląd jest zgodny z `DESIGN.md` w 1920×1080, w title-safe 5%.
  - **AD:** AD-8, AD-11, AD-16

- **CAP-7**: BrowserHost
  - **intent:** MediaStation uruchamia przeglądarkę dobraną pod serwis, z osobnym profilem, i steruje nią bez śladu automatyzacji, który zmieniałby DRM, jakość albo reakcję serwisu.
  - **success:**
    - Edge startuje z profilem `profiles\<appId>` i portem CDP na loopbacku, z flagami z AD-1 i jedną flagą trybu okna.
    - `navigator.webdriver` ma wartość `false`.
    - Binding wraca po każdej nawigacji głównej ramki.
    - Zerwane CDP przy żywym procesie wraca przez reconnect, a po `cdpReconnectTimeout` (30 s) aplikacja przechodzi w `Failed`.
    - Zmiana wersji przeglądarki daje `BrowserVersionChanged` w logu.
  - **AD:** AD-1, AD-4, AD-10, AD-13

- **CAP-8**: Netflix D-padem
  - **intent:** Cały wieczór z Netflixem obsługuje się D-padem, z wstrzykniętym fokusem i z Back zachowującym się jak na TV. Pauza, wznowienie i fakty o odtwarzaniu idą przez protokół strony.
  - **success:**
    - Vitest na fixture'ach DOM Netflixa pokrywa rozpoznanie ekranów, regułę Back (`watch`, `modal`, `title`, `search`, `root`) oraz fokus na profilu bez wyboru i na głównej akcji ekranów `limit` i `error`.
    - Smoke `--smoke netflix` przechodzi: start, `hello`, profil, odtwarzanie ≥ 1080p bez błędu DRM, pauza i reguła Back.
    - Powrót do Netflixa zastaje odcinek na pauzie, a OK go wznawia.
  - **AD:** AD-5, AD-6, AD-7

- **CAP-9**: Klawiatura ekranowa (OSK)
  - **intent:** Gdy w serwisie webowym trzeba coś wpisać, pojawia się jedna klawiatura ekranowa MediaStation, obsługiwana D-padem, a opcjonalnie także kursorem pilota. Polskie znaki są dostępne.
  - **success:**
    - OSK pojawia się automatycznie na `textFocus` w Netflixie i nie zabiera fokusu z pola.
    - Każdy znak od razu trafia do strony i wyniki `/search?q=` się aktualizują. Long OK wstawia polski wariant bez litery bazowej.
    - Na ekranie jest jeden fokus. Kursor nie wychodzi poza okno OSK.
    - Back zamyka rząd wariantów, a potem OSK. Home zamyka OSK i pokazuje ekran główny.
    - Wpisany tekst nie pojawia się w logach, Intents ani w view-modelu.
  - **AD:** AD-7, AD-8, AD-9

- **CAP-10**: Moonlight i aplikacje natywne
  - **intent:** Moonlight i Plex (oraz inne aplikacje natywne) startują z kafelka i wracają do ekranu głównego. Sesja gry przeżywa Home.
  - **success:**
    - Moonlight startuje borderless, bez przełączania trybu monitora. Back trafia do gry. Home chowa i wycisza Moonlighta, a sesja po powrocie żyje.
    - „Zamknij” kończy `quit`, a po 30 s MediaStation zamyka klienta.
    - Plex startuje z kafelka, a Back i Home wracają do ekranu głównego.
  - **AD:** AD-5, AD-8, AD-10, AD-11

- **CAP-11**: „Do oceny”
  - **intent:** Po obejrzanym filmie albo sezonie serialu, na dowolnym profilu Netflixa, na ekranie głównym czeka karta z kodem QR do wyszukiwarki Filmwebu. Kto skanuje, ten ocenia na swoim telefonie, bez szukania tytułu i bez pamiętania o nim.
  - **success:**
    - Testy `RatingPolicy` pokrywają reguły kolejki:
      - film trafia do niej przy ≥ 0.9;
      - serial trafia przy `lastInSeason == true` i ≥ 0.9;
      - przy `lastInSeason == null` powstaje rekord `pending`, który po `ratingPendingDays` (7) dniach bez kolejnego odcinka daje kartę;
      - klucz trafia do kolejki raz na zawsze, także po „Pomiń”.
    - „Ocenione” i „Pomiń” usuwają kartę bez undo.
    - Story niesie test skanu QR z 3 m na prawdziwym TV dwoma telefonami.
  - **AD:** AD-7, AD-12, AD-14

- **CAP-12**: Deploy i odporność
  - **intent:** MediaStation instaluje się skryptem, startuje po zalogowaniu i sama wraca po awarii, więc nocny restart przez Windows Update nie zostawia Elizy przed pulpitem.
  - **success:**
    - `tools/install.ps1` instaluje build i rejestruje zadanie „at logon” z `--supervise`.
    - Zabicie procesu dziecka kończy się restartem z backoffem i powrotem na ekran główny, z adopcją żywych aplikacji.
    - Logi to JSON Lines (UTC, rotacja dzienna).
    - Podmiana pliku drivera w `<install>/page/` i „Uruchom ponownie” z pilota wdrażają hotfix bez ponownej instalacji.
  - **AD:** AD-2, AD-13

## Constraints

- Jedna maszyna i jedna powierzchnia: UM760 z Windows 11 26H2, TV 1920×1080 oglądany z ~3 m. Bez responsive. Weryfikacja wizualna tylko w 1920×1080.
- D-pad wystarcza do każdej akcji. Kursor jest ukryty w shellu i w Netflixie. Pointer jest opcją tylko w OSK oraz w Moonlight i aplikacjach natywnych, według polityki z `apps.json`.
- Home zawsze prowadzi do ekranu głównego: przy dowolnym oknie na wierzchu, przy zawieszonym UI i przy zerwanym CDP.
- Sterowanie przeglądarką to własny launch i surowy CDP na `127.0.0.1`. Zakazane są Playwright, `Runtime.enable`, `--enable-spatial-navigation` i `--remote-allow-origins`. Przejście na rozszerzenie (plan C) następuje tylko po triggerach z AD-1.
- Netflix gra co najmniej w 1080p, bez błędu DRM. Przeglądarkę serwisu wybiera wpis w `apps.json`, nie kod (Netflix: Edge).
- Przeglądarka w tle jest zminimalizowana, nigdy ukryta (`SW_HIDE`). Odtwarzanie nigdy nie wznawia się samo, tylko po OK.
- Orchestrator jest jedynym writerem stanu sesji. Wykonawcy wykonują komendy i raportują fakty, a niczego nie decydują.
- Prywatność:
  - tekst z OSK nie trafia do logów, Intents ani view-modelu;
  - MediaStation nie przechowuje konta Filmwebu;
  - QR powstaje lokalnie, bez zewnętrznego skracacza.
- Stan sesji nie jest trwały. Jedyny trwały stan to kolejka „Do oceny”: jeden dokument, w którym rekordy `pending` są zwykłym statusem, a żadnego rekordu się nie usuwa.
- Konfiguracja to ręcznie edytowany JSON, bez UI ustawień. Nowa aplikacja albo nowy serwis to wpis w `apps.json` i katalog drivera, bez zmian w kodzie hosta.
- Awarie w MVP obsługuje się ręcznie, poza „Zamknij” i „Uruchom ponownie” z pilota. Kruchość UI Netflixa jest akceptowana, a autor dostarcza hotfix drivera.
- UI jest po polsku, w ciepłym głosie na ty, z etykietami neutralnymi rodzajowo. Wartości wyglądu żyją wyłącznie w `DESIGN.md`.

## Non-goals

- Kolejne serwisy streamingowe (Prime Video, HBO Max, Disney+, S12).
- Nawigacja D-padem w Plexie i aplikacjach natywnych.
- Wywołanie klawiatury ekranowej z pilota (Action `Keyboard`, `SendInput` Unicode) w Moonlight i aplikacjach natywnych.
- Przełącznik otwartych aplikacji pod przytrzymaniem Home.
- Widgety rekomendacji i „continue watching” na ekranie głównym.
- Kafelki Moonlight per host i per gra, z własną grafiką, także Vibepollo per gra.
- Toast z QR nad playerem przy napisach końcowych.
- Strona tytułu na Filmwebie zamiast wyszukiwarki (mapowanie przez Wikidata).
- Ocena z pilota, strona Filmwebu na TV, IMDb oraz karta pokazywana tylko po seansach na jednym profilu.
- Cofnięcie „Pomiń” i „Ocenione”.
- Rodzinne zdjęcia jako tło ekranu głównego.
- Właściwe logo MediaStation. W MVP zostaje znak zastępczy.
- HDR, Atmos, 4K i przełączanie trybów obrazu na TV.
- Automatyczna obsługa awarii (popupy aktualizacji, wylogowanie, utrata sieci).
- Tryb PWA ze Store (osobna sesja SMTC) i prewarm aplikacji przy starcie.
- Autologowanie do Windows, ukrycie pulpitu i polityka Windows Update. Konfiguruje je autor, poza produktem.
- HDMI-CEC, którego UM760 nie obsługuje.
- Wydanie open source.

## Success signal

- Eliza przez 7 kolejnych seansów z Netflixem sama wybiera MediaStation zamiast VIDAA i przez tydzień ani razu nie prosi autora o pomoc.
- Warunki techniczne, demonstrowane na UM760 przy TV:
  - cały wieczór z Netflixem (profil, przeglądanie, wyszukiwanie, odtwarzanie, Back, wyjście) przechodzi D-padem, bez trybu myszy;
  - Netflix gra co najmniej w 1080p;
  - Home zawsze przywraca ekran główny, także gdy fokus ukradło inne okno.

## Open Questions

- **Tryb okna przeglądarki** (`--app`, `--start-fullscreen` czy `--kiosk`): rozstrzyga pierwsza story epiki BrowserHost (CAP-7), przed stories CAP-4 i CAP-9.
- **Wykrycie zablokowanego portu CDP po aktualizacji przeglądarki:** jak BrowserHost wykrywa zmianę wersji i niedostępność portu bez użycia CDP? `Browser.getVersion` (AD-13) idzie przez CDP, więc w tym scenariuszu nie zadziała. Autor ma dostać sygnał odróżnialny od zwykłego `Failed`, żeby wiedzieć, kiedy włączyć plan C (AD-1). Rozstrzyga story epiki BrowserHost (CAP-7). Zmiana AD-13 idzie do `bmad-architecture`.
- **G20S Pro BT:** kody przycisków i wybudzanie PC przez Bluetooth (dotąd sprawdzone tylko przez dongle). Rozstrzyga pierwsza story epiki wejścia (CAP-3): powtórzenie S10 z `--calibrate-input`.
- **Nakładka OSK nad pełnoekranowym Edge** (fallback: OSK rysowana przez PageRuntime): rozstrzyga pierwsza story epiki OSK (CAP-9).
- **Czy hook LL we własnym wątku dostaje „down” Home i Back,** także gdy MediaStation jest na wierzchu: rozstrzyga prototyp InputHub (CAP-3).
- **Nowe sesje audio po zmianie odcinka:** rozstrzyga AudioRouter (CAP-5).
- **Pauza wielogodzinna bez uśpienia PC** (S8 zmierzył 30 min): rozstrzyga story pomiarowa w epice CAP-8. Powtarza S8 dla kilku godzin, w dwóch wariantach pauzy i chowania okna, i dodaje fixture'y nowych ekranów błędu. Jeśli wynik wymaga polityki w Core, wraca do `bmad-architecture` jako change. Ścieżka po Modern Standby jest już ustalona: wraca ekran profili, a nie dokładne miejsce (AD-10).
- **Kontrole z forge i UX:** czy QR otwiera aplikację Filmweb, czy przeglądarkę; czy wyszukiwarka Filmwebu znajduje tytuł w języku profilu; skan QR (ECC L) z 3 m dwoma telefonami. Rozstrzyga story „Do oceny” (CAP-11).
- **Stabilność klucza serialu:** gdy serial trafi do „Do oceny” z kluczem z tytułu (brak `seriesId`), a później driver poda `seriesId`, powstaje drugi klucz tego samego serialu. AD-12 akceptuje duplikat tylko przy różnych językach profilu. Rozstrzyga `bmad-architecture` (AD-12), przed story `RatingPolicy` w CAP-11.
- **Konflikt źródeł: czeka na Update w `bmad-ux`, który poprawi `EXPERIENCE.md` na korzyść architektury.** Zachowanie MVP jest zgodne we wszystkich źródłach. Rozbieżności w `EXPERIENCE.md` wobec architektury po change proposalu:
  - enum Actions nadal ma `Keyboard`, który AD-8 usuwa;
  - OSK wymienia `SendInput`, a AD-9 dopuszcza tylko `insertText`;
  - reconnect CDP przypisuje driverowi, a AD-4 i AD-10 przypisują go BrowserHostowi;
  - pamięć fokusu odwołuje się do AD-12 zamiast AD-16, a minimalizacja po `Starting` do AD-15 zamiast AD-14.
