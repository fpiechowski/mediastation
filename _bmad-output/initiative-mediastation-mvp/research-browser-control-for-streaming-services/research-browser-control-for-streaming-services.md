---
title: 'Technical research: sterowanie serwisami streamingowymi w przeglądarce'
type: 'technical'
shape: 'select'
topic: 'Sterowanie serwisami streamingowymi w przeglądarce (Netflix jako wzorzec)'
decision: 'Wybór mechanizmu sterowania przeglądarką dla Netflixa i kolejnych serwisów (A Playwright, B CDP, C rozszerzenie, D inne)'
source: 'native run (2026-10-11) + deepen: import spike S1–S11, C1–C3 na UM760 (imports/spike-results-um760.md)'
status: complete
preset: 'standard'
validation: 'normal'
created: '2026-10-11'
updated: '2026-10-15'
claims_verified: 15
claims_unverified: 3
claims_disputed: 1
claims_overturned: 2
---

# Technical research: sterowanie serwisami streamingowymi w przeglądarce

**Decyzja, której służy research:** jak MediaStation ma sterować serwisami streamingowymi w przeglądarce (nawigacja D-padem, Back, pauza, wykrywanie odtwarzania, klawiatura ekranowa). Netflix jest pierwszym serwisem i wzorcem dla kolejnych.

> Staleness: to porównanie technologii, które szybko się zmieniają (Chrome wydaje nową wersję co 4 tygodnie). Pomiary ze spike'u dotyczą Edge i Chrome 155 i UI Netflixa z października 2026. Najbliższy re-check: 2026-11-12. Przed decyzją podjętą później niż w Q1 2027 trzeba raport odświeżyć.

## Executive summary

**Rekomendacja: wariant B jako hybryda D3, potwierdzona spike'iem.** MediaStation sama uruchamia Edge (dla Netflixa) lub Chrome z **konkretnym** `--remote-debugging-port`, dedykowanym `--user-data-dir`, flagą `--autoplay-policy=no-user-gesture-required` i bez flag automatyzacji. Steruje stroną przez **surowe CDP** bez `Runtime.enable`. Logika serwisu żyje w **adapterze JS per serwis**, niezależnym od transportu. W spike'u ten sam `probe.js` działał bez zmian przez CDP i jako content script rozszerzenia [44][46]. Pewność dla Netflixa: **wysoka**, bo mechanika jest potwierdzona w kodzie źródłowym [7][10] i zmierzona na docelowym HTPC [44][45]. Dla innych serwisów pewność pozostaje **niska** do czasu S12.

Trzy ustalenia przesądzają o wyborze:

1. **Netflix nie reaguje na automatyzację.** Ani podłączony klient CDP (raw i `connectOverCDP` z `Runtime.enable`), ani `playwright.launch()` z `webdriver=true` i pełnymi flagami nie zmieniają jakości, DRM ani błędów [44]. Kryterium G2 przestaje różnicować kandydatów. B zostaje bez Playwrighta jako margines bezpieczeństwa, a nie z konieczności.
2. **DRM i jakość: Edge jest domyślną przeglądarką dla Netflixa, i to nie tylko z powodu HDR.** Obie przeglądarki grają 1080p, ale Edge używa H.264 przez PlayReady SL3000 z bitrate ok. 7 Mb/s, a Chrome AV1 przez Widevine L3 z bitrate ok. 0,8 Mb/s [44]. Obawa z researchu, że flagi Playwrighta lub nowy profil zablokują Widevine, okazała się nietrafna na Windows: CDM leży w katalogu instalacji, a pusty profil gra od razu [44].
3. **Różnica między B a C jest mniejsza, niż zakładał research.** Netflix obsługuje większość niezaufanych klawiszy, a rozszerzenie odtworzyło całą obsługę Netflixa z tą samą jakością i bez ostrzeżeń przeglądarki [45][46]. B wygrywa zaufanym wejściem (pełny ekran klawiszem, widoczny fokus po klawiszu z CDP) i brakiem ręcznej instalacji. C wygrywa brakiem otwartego portu.

**Spike zmienił kształt B:** odpada `--enable-spatial-navigation`, bo więzi fokus na `/browse` i psuje OK w playerze [45]. Back i Home z pilota trzeba przechwytywać w Windows, bo przeglądarka obsługuje je sama, zanim zobaczy je strona [45][48]. Okno przeglądarki w tle minimalizujemy, a nie ukrywamy [47].

**Największe zastrzeżenie:** pomiary pochodzą z jednego konta na planie Standard, monitora FHD i pilota podłączonego przez dongle USB. Nie wiadomo, jak zachowają się TV (HDR, Atmos, 4K), pilot przez Bluetooth i serwisy inne niż Netflix (S12). Netflix może też zmienić politykę wobec automatyzacji bez zapowiedzi.

## Co zmienił spike (deepen 2026-10-15)

Źródło: spike S1–S11 i C1–C3 na UM760, 2026-10-12–14 [44]–[48]. S12 i test na TV są odłożone.

| Twierdzenie researchu | Status po spike'u | Dowód |
|---|---|---|
| Netflix gra 1080p w Edge i Chrome [1] | ✅ potwierdzone; jakość różna między przeglądarkami | [44] S1, S2 |
| HDR i Atmos tylko w Edge [3] | ⚠️ częściowo: Edge zgłasza wsparcie HDR, Chrome nie; HDR, Atmos i TV niezmierzone | [44] S1, [46] C1 |
| Brak doniesień o blokadzie automatyzacji | ✅ potwierdzone pomiarem: brak reakcji na CDP, `Runtime.enable`, `webdriver=true` | [44] S2, S3 |
| Stały port nie ustawia `webdriver` [7] | ✅ potwierdzone: `false` przy porcie, `true` przy pipe | [44] S2, S3 |
| Widevine pobierany do profilu, flagi Playwrighta go blokują [14] | ❌ obalone na Windows: CDM w katalogu instalacji | [44] S3, S4 |
| Spatial navigation jako baza nawigacji [21][27] | ❌ obalone: działa tylko na ekranie profili | [45] S5 |
| CDP daje zaufane wejście, rozszerzenie nie [38] | ✅ potwierdzone; Netflix i tak obsługuje większość niezaufanych klawiszy | [45] S6, [46] C2 |
| Reguła Back „cofnij lub wyjdź” (wniosek projektowy) | ✅ deterministyczna; Back pilota trzeba przechwycić w Windows | [45] S7, [46] C3 |
| Wyciszanie przez `ISimpleAudioVolume` [32] | ⚠️ sporne: na build 26300 `E_NOINTERFACE`, działa `IChannelAudioVolume` | [48] S9 |
| Home z G20S Pro: kod nieznany [35] | ✅ zmierzone dla dongla USB; Bluetooth otwarty | [48] S10 |
| Moonlight po ukryciu okna: brak danych | ✅ sesja przeżywa minimalizację i ukrycie | [48] S11 |

**Rekomendacja się nie zmieniła.** Zmieniły się pewność (ze średniej na wysoką dla Netflixa), flagi uruchomienia, sposób nawigacji i warstwa Windows.

## 1. DRM i jakość obrazu

- **Netflix na Windows:** Chrome 117+ i Edge 118+ grają do Ultra HD (2160p), a Firefox i Opera do 1080p [1]. 4K w Chrome na Windows 11 pojawiło się w lipcu–sierpniu 2026. Wymaga planu Premium, GTX 1050+ lub RX 400+, często HEVC Video Extensions i HDCP 2.2. Przy niespełnieniu warunków jakość spada do 1080p [2]. Dawny limit 720p dla Chrome [40] jest historią. **G1 (Full HD SDR) spełniają obie przeglądarki.** Pewność wysoka: dwa niezależne źródła i pomiar [44].
- **Pomiar na UM760 (plan Standard, Edge i Chrome 155):** obie przeglądarki uruchomione przez MediaStation z portem CDP grają 1920x1080, bez błędów i prawie bez zgubionych klatek [44]. Różnią się drogą:

  | Przeglądarka | DRM | Kodek | Bitrate wideo | HDR w nakładce |
  |---|---|---|---|---|
  | Edge 155 | PlayReady SL3000 (sprzętowe) | H.264 `avc1.640028` | ~7139 kb/s | `HDR support: true` |
  | Chrome 155 | Widevine L3 (`SW_SECURE_DECODE`) | AV1 (`av1-hd-bitrate-capped`) | ~842 kb/s | `HDR support: false` |

  Chrome ma dostęp do PlayReady 3000 (`requestMediaKeySystemAccess`), a mimo to Netflix wybiera w nim Widevine L3 [44]. **Edge jest domyślną przeglądarką dla Netflixa** z powodu bitrate, a HDR jest dodatkowym argumentem. Pewność wysoka dla 1080p na planie Standard.
- **HDR i Atmos:** według doniesień z 2026 r. nadal tylko Edge (PlayReady) [3]. HDR w Windows wymaga GPU ze sprzętowym PlayReady i kodeków HEVC 10-bit [4]. Spike daje tylko częściowe potwierdzenie: Edge zgłasza wsparcie HDR, Chrome nie, ale strumień był SDR, bo plan Standard nie daje HDR [44]. Atmos i TV nie zostały zmierzone. Pewność średnia, status `unverified`.
- **Widevine w dedykowanym profilu (skorygowane):** research zakładał, że Chromium pobiera Widevine do profilu przez component updater [14], więc `--disable-component-update` grozi błędem M7701-1003 [15], a nowy profil czeka na CDM. **Na Windows z Chrome i Edge 155 to nie zachodzi.** CDM leży w katalogu instalacji (`Application\155.0.8059.40\WidevineCdm`). Pusty profil gra od razu, katalog `WidevineCdm` w profilu zostaje pusty, a Playwright z domyślnymi flagami gra bez błędów [44]. Kopiowanie CDM jak w NetflixHD [21] jest zbędne. Edge i tak gra przez PlayReady.
- **Serwer licencji widzi status VMP** (PLATFORM_VERIFIED, TAMPERED lub UNVERIFIED) [16]. Branded Chrome i Edge są podpisane. Buildy niebrandowane, w tym Chrome for Testing polecany przez Google do automatyzacji [5], to ryzyko braku CDM lub weryfikacji. Pewność niska, spike nie testował CfT.
- **Odrzucone z powodu DRM (D):**
  - WebView2 nie ma Widevine (issue otwarte od 2024-09 [17]), a Fixed Runtime nie ma PlayReady [18].
  - Electron wymaga forka castLabs i podpisu VMP przez EVS [19].
  - Netflix ze Store to od lipca 2024 aplikacja webowa na Edge [20]. Nie jest osobną ścieżką sterowania, tylko trybem uruchomienia Edge.
- **Pozostałe serwisy:** brak wiarygodnych danych z lat 2025–2026 o rozdzielczości Disney+, Prime Video i Max w przeglądarkach. Stare wątki mówią o 1080p tylko w Edge [41]. Pewność niska. S12 odłożony.

## 2. Wykrywanie automatyzacji

| Sygnał | Co go włącza | A: Playwright launch | B: własny start + port | C: rozszerzenie |
|---|---|---|---|---|
| `navigator.webdriver=true` | `--enable-automation`, `--headless`, `--remote-debugging-pipe`, port `0` [7] | **zawsze** (pipe [10], zmierzone [44]); maskowalne `--disable-blink-features=AutomationControlled` | **nie** (konkretny port [7], zmierzone [44]) | nie (zmierzone [46]) |
| Infobar „sterowana przez oprogramowanie testowe” | `--enable-automation` [8] | w Edge 155 nie pojawił się nawet z domyślnymi flagami; pojawia się pasek o `--no-sandbox` [44] | nie | nie; po ręcznym „Load unpacked” brak ostrzeżeń o trybie dewelopera [46] |
| Wyciek `Runtime.enable` | klient CDP włączający domenę Runtime [37] | tak (Playwright woła go zawsze) | **do uniknięcia** w surowym CDP: `Page.addScriptToEvaluateOnNewDocument` + `Runtime.addBinding`, bez `Runtime.enable` | nie |
| Otwarty port debugowania | `--remote-debugging-port` | nie (pipe) | tak, na localhost | nie |

- **Netflix i automatyzacja (zmierzone):** na Edge i Chrome 155 Netflix gra identycznie bez klienta CDP, z klientem raw, z `connectOverCDP` (który woła `Runtime.enable`) i w przeglądarce uruchomionej przez `playwright.launch()` z `webdriver=true`. Logowanie z 2FA przeszło bez captchy i bez dodatkowej weryfikacji [44]. Wcześniejsze wyszukiwania nie dały żadnego doniesienia o blokadzie z lat 2023–2026. Pomiar i brak doniesień są niezależnymi liniami dowodu. Pewność wysoka na dzień 2026-10-12. To stan bieżący, a nie gwarancja: Netflix nie dokumentuje swojej polityki wobec automatyzacji.
- **Wykrywalność `Runtime.enable` przez skrypty anty-bot** [37] pozostaje `unverified`. Dla Netflixa nie ma znaczenia [44], ale może mieć dla innych serwisów (S12).
- **Port na localhost:** Chrome 142 wprowadził Local Network Access, czyli zgodę na dostęp publicznych stron do zasobów lokalnych [39]. To ogranicza sondowanie portu przez strony. Pozostaje ryzyko lokalne: każdy proces użytkownika może przejąć profil zalogowany do Netflixa. Na domowym HTPC jednego użytkownika to ryzyko jest akceptowalne, ale trzeba je odnotować.

## 3. Mechanika sterowania a wersje Chrome i Edge

- **Chrome 136+** (zmiana z marca 2025): `--remote-debugging-port` i `--remote-debugging-pipe` są ignorowane na domyślnym `user-data-dir`. Trzeba wskazać własny katalog [5]. Potwierdzone na Chrome 151 [6]. Spike potwierdza, że port z własnym profilem działa w Chrome i Edge 155 [44]. Google kieruje automatyzację do Chrome for Testing [5], co sygnalizuje, że branded Chrome może być dalej zaostrzany. To ryzyko strategiczne dla A i B. Microsoft dla Edge nadal dokumentuje `--remote-debugging-port` + `--user-data-dir` [42].
- **Surowe CDP bez `Runtime.enable` (nowe ograniczenie):** w Edge 155 `Runtime.addBinding` znika po nawigacji dokumentu, a skrypt z `Page.addScriptToEvaluateOnNewDocument` zostaje [44]. BrowserHost musi ponawiać `addBinding` po każdym `Page.frameNavigated` głównej ramki, a adapter musi buforować komunikaty do czasu pojawienia się bindingu. Połączenie CDP zrywa się po wybudzeniu z Modern Standby i trzeba je odnawiać [47].
- **Playwright (A):** domyślne przełączniki obejmują `--disable-component-update`, `--disable-extensions`, `--force-color-profile=srgb`, `--disable-backgrounding-occluded-windows` oraz wyłączenie `GlobalMediaControls` i `MediaRouter` [9]. Lista zmienia się między wersjami [9]. W spike'u nie zaszkodziły odtwarzaniu 1080p SDR, a `--disable-component-update` jest nieszkodliwy, bo CDM jest w katalogu instalacji [44]. Wpływ `--force-color-profile=srgb` na HDR pozostaje niezmierzony. Przeglądarka żyje i umiera razem z procesem Playwrighta (pipe).
- **Rozszerzenie (C):** Chrome 137 usunął `--load-extension` z buildów branded [11]. `Extensions.loadUnpacked` przez CDP działa tylko po pipe z `--enable-unsafe-extension-debugging` [12], czyli z `webdriver=true`. Spike to potwierdza: `ext-temp` działa tylko na czas sesji i daje `webdriver=true` [46]. Ręczne „Load unpacked” w dedykowanym profilu działa bez ostrzeżeń, ale po zmianie plików rozszerzenia Chrome uruchamiał stary service worker, więc każda aktualizacja wymaga ręcznego przeładowania [46]. W MV3 otwarte `connectNative()` (od Chrome 105) lub aktywny WebSocket (od Chrome 116) utrzymują service worker przy życiu [13]. Zdarzenia sprzed połączenia trzeba kolejkować w service workerze [46].

## 4. Nawigacja D-padem i Back

- **Natywnie:** Netflix w przeglądarce ma skróty tylko w playerze: Enter/Space, F, Esc (wyjście z fullscreen), strzałki do przewijania i głośności, M, S [28]. Spike to potwierdza: bez dodatkowej logiki strzałki pilota na `/browse` tylko przewijają stronę, a fokus zostaje na `body` [45].
- **Flaga `--enable-spatial-navigation` (obalone jako baza):** research proponował ją jako bazę na wzór NetflixHD [21][27]. W Edge i Chrome 155 na Windows działa tylko na ekranie profili. Na `/browse` i w wyszukiwarce fokus na kafelku otwiera podgląd (`previewModal--player_container`), który więzi fokus i z którego nie da się wyjść strzałkami ani Back. W playerze OK przełącza pauzę dwa razy, bo flaga dodatkowo „klika” element z fokusem [45]. **MediaStation nie używa tej flagi.**
- **Biblioteki:**
  - polyfill WICG jest **zarchiwizowany** [24];
  - js-spatial-navigation ostatnią zmianę dostał w 2024-05 [25];
  - Norigin jest aktywny (2026-09), ale służy do budowy aplikacji React, a nie do wstrzykiwania w obcy DOM [26].
- **Istniejące rozszerzenia „Netflix z pada”** są porzucone: netflix-navigator (2022-12) [22], NetflixController z wirtualną klawiaturą (2020-10) [23]. **Nie ma gotowca.**
- **Wzorzec potwierdzony w spike'u:** adapter przechwytuje strzałki i OK, a sam przesuwa fokus według **mapy fokusu per ekran**. Na `/watch` przepuszcza klawisze do Netflixa [45][46]. Ustalenia dla mapy:
  - selektory obecnego UI to `[data-uia^="carousel-row-section"]`, kafelki `standard-card`, `progress-card` i `episodic-card`, strzałka wiersza `carousel-hawkins-right-button` oraz odcinki `titleCard--container`. Żaden z selektorów znanych z wcześniejszych projektów (`.lolomoRow`, `.slider`, `.handleNext`) nie pasuje [45];
  - zakres fokusu musi się zawężać do modala, gdy istnieje `[role="dialog"].previewModal--container`, bo tło nie jest `inert` [45];
  - geometryczny fokus w modalu prowadzi przez linki obsady i oceny, a „Play” bywa osiągalny tylko w bok. Mapa musi więc priorytetyzować akcje, a nie geometrię [45][46];
  - obwódka Netflixa (`:focus-visible`) pojawia się tylko po zaufanym klawiszu i jest słabo widoczna. Adapter rysuje własne podświetlenie [45].
- **Player:** wewnętrzne API Netflixa (`netflix.appContext.state.playerApp.getAPI().videoPlayer`) [29] działa przy pauzie i przy starcie z zablokowanego autoodtwarzania, ale po 30 min pauzy nie wznawia, a `video.play()` tak [45][47]. API jest nieudokumentowane. Zasada: pauza przez API lub `HTMLVideoElement`, wznowienie przez `video.play()`.
- **Reguła Back (potwierdzona, deterministyczna w dwóch przebiegach i w wariancie C)** [45][46]:

  ```text
  watch              -> history.back(); gdy history.length == 1: nav /browse
  browse-modal, title -> klik [data-uia="previewModal-closebtn"] (albo Escape)
  search             -> nav /browse
  browse, profiles   -> wyjdź do MediaStation
  ```

  Rozpoznanie ekranu: trasa URL plus `[data-uia="profile-gate-screen"]` dla profili i `.detail-modal` dla modala. Modal na `/search?jbv=…` trzeba rozpoznawać osobno [45]. Netflix pokazuje wybór profilu po każdej wizycie na `/ProfilesGate` i po wybudzeniu z Modern Standby, więc adapter musi go obsłużyć [45].
- **Back pilota trzeba przechwycić w Windows.** Pilot wysyła `BrowserBack` (keyCode 166). Edge wykonuje wtedy `history.back()` sam, a strona nie może tego zablokować nawet `preventDefault()` w fazie capture. CDP `key BrowserBack` z kolei w ogóle nie cofa historii [45]. Bez przechwycenia Back na `/browse` cofa się po historii Netflixa zamiast wyjść do MediaStation.
- **Zaufane wejście (zweryfikowane):** `Input.dispatchKeyEvent` z CDP działa jak klawisz fizyczny we wszystkich badanych akcjach [45], zgodnie z [38]. Niezaufane zdarzenia zawodzą tylko tam, gdzie przeglądarka wymaga aktywacji użytkownika (`f` → pełny ekran) albo wykonuje domyślną akcję (Escape z pełnego ekranu). Netflix ma własne handlery dla Space, Enter, strzałek, `m`, Enter na kafelkach i Escape w modalu, więc działają one także niezaufane [45]. W C pełny ekran robi `chrome.windows.update` [46].

## 5. Pauza, wykrywanie odtwarzania i klawiatura ekranowa

- **Pauza:** `video.pause()` i API playera działają jednakowo w B i C [45][46]. Po 30 min pauzy w **zminimalizowanym** oknie strona, pozycja i licencja zostają, a odtwarzanie wznawia `video.play()`. Po 30 min w oknie **ukrytym** (SW_HIDE) Edge po pokazaniu okna zostawia stronę jako `hidden`, a wznowienie kończy się błędem D7361-1254. Ponowne otwarcie `/watch/<id>` odzyskuje pozycję [47]. Jedna próba na wariant, status `unverified`.
- **Autoodtwarzanie:** na zimnym starcie Edge i Chrome blokują start odcinka bez gestu użytkownika. Space i OK nie wychodzą z tego stanu, a w C nie da się go obejść ze strony. Flaga `--autoplay-policy=no-user-gesture-required` rozwiązuje problem w obu wariantach [45][46].
- **Wykrywanie odtwarzania:** zdarzenia `play`, `pause`, `ended` i `error` na `<video>` przekazywane przez binding (`Runtime.addBinding` w B, WebSocket z service workera w C). Komplet zdarzeń dotarł w obu wariantach [44][46]. Ekran „Too many people are using your account” (`[data-uia="retryButton"]`) nie jest błędem `<video>`, więc adapter musi go rozpoznawać osobno [44].
- **SMTC:** Chrome ma **jedną** sesję SMTC dla wszystkich kart i okien, a osobną tylko dla zainstalowanych PWA [30]. SMTC nadaje się jako sygnał zapasowy per przeglądarka, a nie per serwis. Playwright domyślnie wyłącza `GlobalMediaControls` [9]. Spike nie testował SMTC.
- **Klawiatura ekranowa (zweryfikowane w B i C):** wstawianie tekstu działa trzema drogami: CDP `Input.insertText` (B), `execCommand` i setter `value` ze zdarzeniem `input` (C). Każda uruchamia wyszukiwanie `/search?q=` [45][46]. Adapter nasłuchuje `focusin` na polach tekstowych i albo rysuje klawiaturę w stronie, albo prosi MediaStation o natywną klawiaturę. Kryterium nie różnicuje kandydatów.

## 6. Ryzyka Windows i Moonlight

- **Globalny Home (zmierzony dla dongla USB 2.4G):** G20S Pro przez dongle (`VID_1915&PID_1025`) wysyła Home jako **Raw Input HID Consumer, usage 0x0223** (AC Home). Windows dorabia z tego wstrzyknięty `VK_BROWSER_HOME` (0xAC, `LLKHF_INJECTED`) i `WM_APPCOMMAND` 7. Nieobsłużony `WM_APPCOMMAND` każe powłoce otworzyć domyślną przeglądarkę, a Chrome na wierzchu sam otwiera nową kartę [48]. Back to analogicznie HID 0x0224 → `VK_BROWSER_BACK` → `WM_APPCOMMAND` 1. OK i strzałki przychodzą jako zwykła klawiatura HID [48]. G20S Pro i Pro BT raportują **różne kody** [35], więc wynik nie przenosi się na Bluetooth.
- **Przechwycenie i foreground:** spike użył hooka `WH_KEYBOARD_LL`, który wymaga pętli komunikatów [34]; `RegisterHotKey` nie był testowany. Hook musi połykać `VK_BROWSER_HOME` (zmierzone) i analogicznie `VK_BROWSER_BACK` (wniosek z S7, niezmierzony), bo inaczej reaguje przeglądarka lub powłoka. Musi też działać w **osobnym wątku**, bo w wątku UI zajętym obsługą `WM_INPUT` gubi zdarzenie „down” [48]. Samo `SetForegroundWindow` zawodzi nad Chrome i Moonlightem, choć proces właśnie otrzymał wejście, zgodnie z zastrzeżeniem dokumentacji [33]. Symulowany Alt albo `AttachThreadInput` przywróciły proces na wierzch 16/16 razy nad Edge, Chrome, Moonlightem i pulpitem [48]. Ryzyko deadlocków przy `AttachThreadInput` [43] nie wystąpiło w spike'u.
- **Wyciszanie per aplikacja:** dźwięk Edge i Chrome wychodzi z procesu potomnego (audio service [31]), a proces główny nie ma sesji audio. Wyciszenie całego drzewa procesów profilu ucisza jedną przeglądarkę, a druga gra dalej [48]. Wzorzec z `ISimpleAudioVolume` [32] jest **sporny**: na Windows 11 build 26300 sesje zwracają `E_NOINTERFACE`, a działa `IChannelAudioVolume` [48]. Czy po zmianie odcinka powstaje nowa sesja audio, nie zostało sprawdzone.
- **Moonlight-qt v6.2.0 (2026-10-04):**
  - CLI ma `stream <host> <app>`, `list`, `quit <host>` i `pair` [36]. `stream` z CLI działa, a okno pojawia się po 1–2 s [48];
  - sesja przeżywa 5 min minimalizacji i 2 min ukrycia (SW_HIDE) z powrotem obrazu, dźwięku i sterowania [48];
  - `--mute-on-focus-loss` wycisza Moonlight przy każdej utracie fokusu, więc nie trzeba go wyciszać osobno [48];
  - wyłączny pełny ekran przełącza monitor między 59 a 120 Hz i gasi go na 1–2 s przy każdym przełączeniu. `--display-mode borderless` tego unika [48];
  - po `quit` host kończy sesję, a programy na hoście działają dalej. Proces `Moonlight.exe quit` nie kończy się sam, a klient może zostać z oknem błędu, więc MediaStation zamyka go sama [48].

## Macierz kandydatów

Skala ocen 1–5. Twarde progi: G1 = co najmniej 1080p SDR, G2 = brak blokady za automatyzację, G3 = przeglądarka per serwis. Oceny zaktualizowane po spike'u. Zmiany względem 2026-10-11 są oznaczone strzałką.

| Kryterium (waga) | A: Playwright launch | B: własny start + CDP (port) | C: rozszerzenie + NM/WS | D1: WebView2 / Electron | D2: PWA ze Store |
|---|---|---|---|---|---|
| G1 DRM 1080p | ✅ także z domyślnymi flagami [44] | ✅ [44] | ✅ [46] | ❌ WebView2 [17], Electron wymaga castLabs [19] | ✅ (to Edge) |
| G2 automatyzacja | ✅ na Netfliksie mimo `webdriver=true` [44], ale zbędne sygnały (`webdriver`, pasek `--no-sandbox`) | ✅ zmierzone [44] (było ⚠️) | ✅ [46] | — | zależy od sposobu sterowania |
| G3 przeglądarka per serwis | ✅ `channel` | ✅ | ⚠️ ręczna instalacja w każdym profilu [46] | ❌ | ❌ tylko Edge |
| D-pad (25%) | 4 | 4 (zaufane wejście, widoczny fokus) | 4 (Netflix obsługuje niezaufane klawisze [45]) | — | — |
| Kruchość i utrzymanie (20%) | 3 (lista flag Playwrighta, lifecycle) | 4 (ponawianie `addBinding`, reconnect po Standby [44]) | 3 (ręczne „Load unpacked” [11][46]) | — | — |
| Pauza i playback (15%) | 4 ↓ | 4 ↓ (API nie wznawia po 30 min, SW_HIDE psuje sesję [47]) | 4 ↓ | — | — |
| Back zgodny z TV (15%) | 4 | 4 (reguła deterministyczna, Back przez hook Windows [45]) | 4 (identyczna [46]) | — | — |
| Klawiatura ekranowa (10%) | 5 ↑ | 5 ↑ (`insertText` [45]) | 5 ↑ (`execCommand`, `value` [46]) | — | — |
| Dopasowanie do stosu (10%) | 3 (Node, .NET, Python; przeglądarka zależna od procesu) | 5 (WebSocket JSON z każdego języka, reattach) | 4 (host NM = dowolny exe) | — | — |
| Jakość (bonus, 5%) | 3 (`srgb` niezmierzony dla HDR) | 4 | 4 (jakość identyczna z B [46]) | — | — |
| **Wynik ważony** | **3,75** (było 3,80) | **4,20** (było 4,25) | **3,90** (było 3,95) | odpada (G1/G3) | odpada jako mechanizm, zostaje jako tryb uruchomienia |

Wagi zostały zatwierdzone na plan gate 2026-10-11. Kolejność kandydatów i różnica B–C (0,30) się nie zmieniły. Zmiany pauzy i klawiatury dotyczą wszystkich jednakowo. Przed spike'iem C zbliżało się do B, gdy G2 traktowało się jako kryterium rankingowe. Teraz G2 jest zmierzone i nie różnicuje kandydatów na Netfliksie, więc ten argument za C odpada. Przewaga B wynika z utrzymania i dopasowania do stosu.

## Wnioski międzywymiarowe

- **Kryteria funkcjonalne nie różnicują kandydatów.** Spike to potwierdził: ten sam `probe.js` z mapą fokusu, regułą Back i obsługą playera działał bez zmian przez CDP i w rozszerzeniu [44][46]. Kandydaci różnią się trzema rzeczami:
  - transportem i jego śladem automatyzacji, który na Netfliksie dziś nie ma znaczenia [44];
  - kosztem dystrybucji kodu do przeglądarki;
  - tym, kto jest właścicielem życia procesu przeglądarki.

  Hedge „adapter jako bundle niezależny od transportu” jest więc tani i sprawdzony.
- **Najtrudniejsza część przeniosła się z przeglądarki do Windows.** Back i Home pilota, powrót MediaStation na wierzch, wyciszanie, stan okna (minimalizacja zamiast ukrycia) i Moonlight wymagają warstwy natywnej, niezależnej od wyboru B lub C [45][47][48]. Przeglądarka nie może sama poprawnie obsłużyć Back pilota [45]. Dla architektury warstwa wejścia Windows jest więc równie ważnym komponentem co BrowserHost.
- **Dedykowany profil jest wymagany przez Chrome 136 [5] i przez separację logowania**, a nie, jak zakładał research, przez Widevine [44]. Pusty profil per serwis działa od pierwszego uruchomienia.
- **Wybór przeglądarki per serwis jest decyzją o jakości, nie o mechanizmie.** Dla Netflixa Edge daje ok. 8,5 raza wyższy bitrate i wsparcie HDR [44], a mechanizm B działa identycznie w obu przeglądarkach. Tabela „serwis → przeglądarka” czeka na S12.

## Rekomendacja

1. **Wybór: B w formie hybrydy D3.** Pewność wysoka dla Netflixa: mechanika [5][7][10] i pomiar na docelowym HTPC [44]–[48]. Dla innych serwisów pewność średnia, do potwierdzenia w S12.
   - MediaStation uruchamia `msedge.exe` (Netflix) albo `chrome.exe` z flagami `--user-data-dir=<profil-serwisu> --remote-debugging-port=<stały, np. 92xx> --no-first-run --no-default-browser-check --autoplay-policy=no-user-gesture-required`. Nie używa `--enable-spatial-navigation` [45] ani żadnej z flag Playwrighta. Tryb okna (`--app`, `--start-fullscreen` lub `--kiosk`) spike nie testował. Pełny ekran daje zaufany klawisz `f` wysłany przez CDP [45].
   - Sterowanie przez surowe CDP po WebSocket: `Page.addScriptToEvaluateOnNewDocument` (adapter), `Runtime.addBinding` ponawiany po każdej nawigacji głównej ramki (zdarzenia do MediaStation), `Input.dispatchKeyEvent` i `Input.insertText` (zaufane wejście). Bez `Runtime.enable` [44]. `connectOverCDP` jest dopuszczalny w prototypie, bo Netflix na niego nie reaguje [44].
   - Adapter Netflixa: mapa fokusu per ekran z własnym podświetleniem, przepuszczanie klawiszy na `/watch`, reguła Back, obsługa ekranu profili i ekranu limitu ekranów, wznowienie przez `video.play()`, a przy błędzie przeładowanie `/watch/<id>` [45][47].
   - Warstwa Windows: hook LL w osobnym wątku, który połyka `VK_BROWSER_HOME` i `VK_BROWSER_BACK`. Do tego `SetForegroundWindow` z odblokowaniem (Alt lub `AttachThreadInput`), minimalizacja (nie ukrywanie) okna przeglądarki w tle i wyciszanie drzewa procesów przez `IChannelAudioVolume` [47][48]. Moonlight startuje w trybie `--display-mode borderless` z `--mute-on-focus-loss` [48].
   - Higiena sesji: przed zamknięciem przeglądarki nawigacja na `/browse`, bo zamknięcie na `/watch` trzyma slot strumienia przez 1–2 min [44]. Po wybudzeniu z Modern Standby trzeba odnowić CDP i stan Netflixa [45][47].
   - **Feeds:** architecture spine. Komponenty: „BrowserHost” (lifecycle, flagi, CDP, reconnect), „ServiceAdapter” (JS per serwis), „InputInterceptor” (hook Windows dla Home i Back, foreground), „AudioRouter” (wyciszanie drzew procesów), „MoonlightHost”. Kontrakt komunikatów: `navigate`, `back`, `pause`, `resume`, `playbackState`, `textFocus`, `screen` (profile, limit ekranów, błąd).
2. **Runner-up: C (rozszerzenie).** Spike pokazał, że C jest w pełni realne: ta sama jakość i DRM, brak ostrzeżeń, cała obsługa Netflixa przez zamienniki [46]. Przejście na C ma sens, gdy Google lub Microsoft zablokują `--remote-debugging-port` dla profili niestandardowych albo gdy któryś serwis zacznie reagować na CDP. Koszt przejścia jest niski i zmierzony: zmienia się tylko most (WebSocket lub native messaging zamiast `Runtime.addBinding`) i pełny ekran (`chrome.windows.update`). Instalacja to jednorazowe „Load unpacked” w każdym dedykowanym profilu albo publikacja unlisted w Edge Add-ons lub Chrome Web Store.
3. **Odrzucone:**
   - **A jako docelowe:** Netflix na nie nie reaguje [44], ale A dodaje zbędne sygnały (`webdriver=true`, pasek `--no-sandbox`), listę flag do pilnowania i przeglądarkę zależną od procesu Node.
   - **WebView2 i Electron:** DRM.
   - **PWA ze Store jako mechanizm:** to tylko Edge z `--app`. Jako *tryb uruchomienia* B pozostaje do rozważenia, bo daje osobną sesję SMTC [30]. Spike tego nie testował.
   - **`--enable-spatial-navigation`:** więzi fokus i psuje OK w playerze [45].
4. **Najsilniejszy argument przeciw B:** otwarty port CDP to łatwy cel lokalnego przejęcia sesji i sygnał, który Google aktywnie zawęża [5]. Jeśli kolejna wersja Chrome zablokuje go także dla niestandardowych profili, B przestanie działać w Chrome z dnia na dzień. Edge może podążyć za nim. Spike nie zmienił tego ryzyka, ale potwierdził, że C jest gotowym zapasem.
5. **Najtańszy hedge:** adapter jako jeden bundle JS z interfejsem `bridge.send/receive` oraz test „smoke” po każdej aktualizacji przeglądarki (start, profil, odtworzenie, pauza, Back). Harness spike'u (`spike/`) jest gotowym punktem wyjścia dla takiego testu.

## Spike na Netfliksie: testy i wyniki

Wyniki z UM760 (monitor FHD z HDR, pilot G20S Pro przez dongle USB 2.4G, plan Standard), 2026-10-12–14 [44]–[48].

| # | Test | Kryterium zaliczenia | Wynik |
|---|---|---|---|
| S1 | Edge i Chrome uruchomione przez MediaStation z `--remote-debugging-port` i `--user-data-dir`, odtworzenie odcinka, nakładka statystyk | ≥1080p, brak błędu DRM | ✅ 1080p w obu; Edge H.264 ~7 Mb/s, Chrome AV1 ~0,8 Mb/s [44] |
| S2 | To samo z klientem CDP: (a) raw bez `Runtime.enable`, (b) `connectOverCDP` | bez różnic względem S1; `webdriver === false` | ✅ bez różnic, `webdriver=false` [44] |
| S3 | Kontrola negatywna: `playwright.launch()` z `ignoreDefaultArgs` i z domyślnymi flagami | pomiar ryzyka G2 | ✅ gra 1080p mimo `webdriver=true` [44] |
| S4 | Nowy pusty profil: czas do pobrania Widevine; wariant z kopią CDM | profil gra przy pierwszym uruchomieniu | ✅ gra od razu; CDM w katalogu instalacji [44] |
| S5 | `--enable-spatial-navigation` na profilach, `/browse`, wyszukiwaniu i w playerze | które ekrany działają „za darmo” | ❌ tylko profile; flaga odrzucona [45] |
| S6 | Zaufane (CDP) i niezaufane (JS) wejście, zamienniki dla C, adapter z pilotem | czy B i C obsłużą Netflixa pilotem | ✅ B; ✅ C z zastrzeżeniami (pełny ekran, UX fokusu) [45] |
| S7 | Back: Esc, `history.back()`, zamykanie modala; rozpoznanie `/browse` i profili | deterministyczna reguła | ✅ reguła deterministyczna; Back pilota do przechwycenia w Windows [45] |
| C1–C3 | Wariant C: jakość, ślad automatyzacji, S6 i S7 w rozszerzeniu, okno | jak S1, S6, S7 | ✅ identycznie jak B; potrzebna flaga `--autoplay-policy` [46] |
| S8 | Pauza i 30 min w tle (minimalizacja, SW_HIDE) | wznowienie bez błędu i przeładowania | ⚠️ minimalizacja tak; SW_HIDE kończy się D7361-1254 [47] |
| S9 | Wyciszenie drzewa procesów Edge i Chrome | wyciszona cicha, druga słyszalna | ✅ [48] |
| S10 | G20S Pro (dongle): kody Home, foreground po Home nad Edge, Chrome, Moonlight | Home zawsze przechwycony, MediaStation na wierzchu | ✅ 16/16 z połykaniem i odblokowaniem [48] |
| S11 | Moonlight: `stream` z CLI, minimalizacja i ukrycie, `--mute-on-focus-loss` | sesja trwa, obraz wraca | ✅ [48] |
| S12 | Prime Video, Max i Disney+ w Edge i Chrome przez S1 i S2 | rozdzielczość per serwis i przeglądarka | ⏭️ odłożony |
| — | HDR i Atmos w Edge na TV | HDR i Atmos działają | ⏭️ odłożony (brak TV) |

## Otwarte pytania

**Zamknięte przez spike:**

- ~~Czy Netflix reaguje na port debugowania i sygnały CDP?~~ Nie, ani na CDP, ani na Playwright [44]. Dla innych serwisów pytanie przechodzi do S12.
- ~~Czy Netflix ignoruje niezaufane zdarzenia klawiatury?~~ Nie, obsługuje większość z nich. Niezaufane zawodzą tylko przy akcjach wymagających aktywacji [45].
- ~~Kod HID przycisku Home G20S Pro na Windows~~ dla dongla USB: HID Consumer 0x0223 [48]. Bluetooth pozostaje otwarty.
- ~~Zachowanie Moonlight przy ukryciu okna.~~ Sesja przeżywa minimalizację i ukrycie [48].

**Nadal otwarte:**

- **TV: HDR, Atmos i 4K w Edge, przełączanie trybów obrazu.** Na monitorze FHD i planie Standard dało się tylko zaobserwować, że Edge zgłasza wsparcie HDR, a Chrome nie [44]. Odpowiedź da powtórzenie S1 z UM760 podłączonym do TV, na planie Premium. Trzeba też sprawdzić, jak MediaStation ma przełączać HDR i częstotliwość odświeżania (Moonlight odbierał z hosta 3840x2160 HDR, a w wyłącznym pełnym ekranie przełączał monitor na 120 Hz [48]).
- **Pilot przez Bluetooth.** G20S Pro BT wysyła inne kody niż dongle [35]. Odpowiedź da powtórzenie S10 (Home, Back, OK, strzałki) przez Bluetooth i sprawdzenie, czy reguła połykania w hooku LL nadal działa.
- **S12: Prime Video, Max i Disney+.** Rozdzielczość, DRM i kodek per przeglądarka, reakcja na CDP i `Runtime.enable` [37], selektory do mapy fokusu, reguła Back. Brak wiarygodnych źródeł [41]. Odpowiedź da S12 z harnessem spike'u (wymaga rozszerzenia selektorów `probe.js` i domen rozszerzenia).
- **Dłuższa pauza.** S8 sprawdził 30 min w jednej próbie [47]. Co się dzieje po kilku godzinach, po nocy albo po wybudzeniu z Modern Standby? Trzeba też powtórzyć wariant B (pauza przez `video.pause()` i minimalizacja), żeby rozdzielić wpływ metody pauzy i metody chowania okna.
- **Hook w osobnym wątku.** Potwierdzić w prototypie MediaStation, że hook LL w dedykowanym wątku zawsze dostaje „down” Home i Back, także gdy MediaStation jest na wierzchu [48].
- **Limit ekranów.** Jak MediaStation ma komunikować ekran „Too many people are using your account” i czy ponawiać automatycznie [44].
- **Nowe sesje audio po zmianie odcinka.** Tryb `-Watch` w S9 nie został sprawdzony na UM760 [48].
- **Kierunek Google wobec remote debugging w branded Chrome.** Obserwować release notes. Odpowiedź przyniesie Refresh tego raportu.

## Źródła

| # | Wspiera | Wydawca | Data publikacji | Dostęp | Pewność |
|---|---|---|---|---|---|
| [1] | Netflix: Chrome 117+ / Edge 118+ do 2160p na Windows | [Netflix Help Center](https://help.netflix.com/en/node/23742) | live | 2026-10-11 | wysoka |
| [2] | 4K w Chrome na Win11, wymagania, fallback 1080p | [Chrome Unboxed](https://chromeunboxed.com/netflix-finally-unlocks-4k-streaming-in-chrome-on-windows-11/) | 2026-08-05 | 2026-10-11 | wysoka |
| [3] | HDR/Atmos nadal tylko Edge | [ChannelNews](https://www.channelnews.com.au/microsoft-loses-another-edge-as-netflix-unlocks-4k-chrome-streaming/) | 2026-08 | 2026-10-11 | średnia (unverified; częściowo zgodne z [44]) |
| [4] | HDR w Windows: sprzętowy PlayReady, HEVC | [Microsoft Support](https://support.microsoft.com/help/4040547) | live | 2026-10-11 | wysoka |
| [5] | Chrome 136: remote debugging wymaga niestandardowego user-data-dir; CfT do automatyzacji | [Chrome for Developers](https://developer.chrome.com/blog/remote-debugging-port) | 2025-03-17 | 2026-10-11 | wysoka |
| [6] | Potwierdzenie zachowania na Chrome 151 | [GitHub trycua/cua #2916](https://github.com/trycua/cua/issues/2916) | 2026 | 2026-10-11 | średnia |
| [7] | Flagi włączające `navigator.webdriver`; konkretny port nie włącza | [Chromium runtime_features.cc](https://github.com/chromium/chromium/blob/main/content/child/runtime_features.cc) | 2026-10 | 2026-10-11 | wysoka (potwierdzone [44]) |
| [8] | Infobar automatyzacji zależy od `--enable-automation` | [Chromium infobar_utils.cc](https://github.com/chromium/chromium/blob/main/chrome/browser/ui/startup/infobar_utils.cc) | 2026-10 | 2026-10-11 | wysoka |
| [9] | Domyślne przełączniki Playwrighta | [Playwright chromiumSwitches.ts](https://github.com/microsoft/playwright/blob/main/packages/playwright-core/src/server/chromium/chromiumSwitches.ts) | 2026-10 | 2026-10-11 | wysoka |
| [10] | Playwright launch zawsze używa `--remote-debugging-pipe` | [Playwright chromium.ts](https://github.com/microsoft/playwright/blob/main/packages/playwright-core/src/server/chromium/chromium.ts) | 2026-10 | 2026-10-11 | wysoka |
| [11] | Chrome 137 usuwa `--load-extension` w buildach branded | [Chromium Extensions group](https://groups.google.com/a/chromium.org/g/chromium-extensions/c/1-g8EFx2BBY/m/S0ET5wPjCAAJ) | 2025-04 | 2026-10-11 | wysoka |
| [12] | CDP `Extensions.loadUnpacked` tylko przez pipe + unsafe flag | [devtools-protocol Extensions.pdl](https://chromium.googlesource.com/devtools/devtools-frontend/+/19aecbbf95fa88b359123795de16fda3ee847fbd/node_modules/devtools-protocol/pdl/domains/Extensions.pdl) | 2025 | 2026-10-11 | wysoka |
| [13] | MV3: connectNative i WebSocket utrzymują service worker | [Chrome for Developers](https://developer.chrome.com/docs/extensions/develop/concepts/service-workers/lifecycle) | live | 2026-10-11 | wysoka |
| [14] | Widevine pobierany przez component updater | [nixpkgs commit](https://code.ornl.gov/nix/nixpkgs/-/commit/a701c5e8496ff433620fae2ecb13985976f48afc) | ~2024 | 2026-10-11 | obalone dla Windows (Chrome/Edge 155) przez [44] |
| [15] | Błąd M7701-1003 = brak odtwarzania chronionych treści | [Netflix Help Center](https://help.netflix.com/ar/node/27451) | live | 2026-10-11 | wysoka |
| [16] | Status VMP widoczny dla serwera licencji | [USENIX Security 2025](https://www.usenix.org/system/files/usenixsecurity25-roudot.pdf) | 2025 | 2026-10-11 | wysoka |
| [17] | WebView2 bez Widevine (issue otwarte) | [WebView2Feedback #4828](https://github.com/MicrosoftEdge/WebView2Feedback/issues/4828) | 2024-09 | 2026-10-11 | wysoka |
| [18] | WebView2 Fixed Runtime bez PlayReady | [WebView2Feedback #4632](https://github.com/MicrosoftEdge/WebView2Feedback/issues/4632) | 2024-06 | 2026-10-11 | wysoka |
| [19] | Electron: Widevine tylko w forku castLabs + VMP | [castLabs electron-releases](https://newreleases.io/project/github/castlabs/electron-releases/release/v18.3.13%2Bwvcus) | 2022 | 2026-10-11 | średnia |
| [20] | Netflix ze Store = aplikacja webowa na Edge | [Windows Latest](https://www.windowslatest.com/2024/07/25/windows-11s-netflix-loses-downloads-downgraded-to-microsoft-edge-based-web-app/) | 2024-07-25 | 2026-10-11 | wysoka |
| [21] | Prior art: kiosk Chromium, spatial navigation, kopiowanie Widevine | [GitHub NetflixHD](https://github.com/FromChaosComesClarity/NetflixHD) | 2026-10-08 | 2026-10-11 | spatial navigation i kopia CDM obalone dla Windows przez [44][45] |
| [22] | netflix-navigator porzucony (2022-12) | [GitHub dutiyesh/netflix-navigator](https://github.com/dutiyesh/netflix-navigator) | 2022-12 | 2026-10-11 | wysoka |
| [23] | NetflixController z wirtualną klawiaturą porzucony (2020-10) | [GitHub FThompson/NetflixController](https://github.com/FThompson/NetflixController) | 2020-10 | 2026-10-11 | wysoka |
| [24] | Polyfill WICG zarchiwizowany | [GitHub WICG/spatial-navigation](https://github.com/WICG/spatial-navigation) | 2026-03 | 2026-10-11 | wysoka |
| [25] | js-spatial-navigation: ostatni push 2024-05 | [GitHub luke-chang/js-spatial-navigation](https://github.com/luke-chang/js-spatial-navigation) | 2024-05 | 2026-10-11 | wysoka |
| [26] | Norigin aktywny, dla aplikacji React | [GitHub NoriginMedia](https://github.com/NoriginMedia/Norigin-Spatial-Navigation) | 2026-09 | 2026-10-11 | wysoka |
| [27] | Switch `enable-spatial-navigation` nadal w Chromium | [Chromium content_switches.cc](https://github.com/chromium/chromium/blob/main/content/public/common/content_switches.cc) | 2026-10 | 2026-10-11 | wysoka |
| [28] | Skróty klawiszowe Netflixa w przeglądarce | [Netflix Help Center](https://help.netflix.com/en/node/24855) | live | 2026-10-11 | wysoka |
| [29] | Wewnętrzne API playera Netflixa (wrapper PiP Mozilli) | [Mozilla gecko-dev (mirror)](https://source-hub.etos-pro.ru/mozilla/gecko-dev/commit/2e684db116078bb8e0c82148cc1769ea60ee9c69) | 2022 | 2026-10-11 | średnia |
| [30] | Jedna sesja SMTC na przeglądarkę, osobna dla PWA | [Chromium system_media_controls](https://chromium.googlesource.com/chromium/src/+/lkgr/content/browser/media/system_media_controls) | live | 2026-10-11 | wysoka |
| [31] | Audio service w osobnym procesie na Windows | [Chromium services/audio README](https://chromium.googlesource.com/chromium/src/+/lkgr-android-internal/services/audio/README.md) | live | 2026-10-11 | wysoka (potwierdzone [48]) |
| [32] | Wyciszanie per sesja audio (ISimpleAudioVolume) | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionmanager-getsimpleaudiovolume) | live | 2026-10-11 | sporne: `E_NOINTERFACE` na build 26300 [48] |
| [33] | Warunki SetForegroundWindow | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow) | 2025-10-06 | 2026-10-11 | wysoka |
| [34] | RegisterHotKey kontra WH_KEYBOARD_LL | [CodeProject](https://api.codeproject.com/Articles/1273010/Global-Hotkeys-within-Desktop-Applications) | 2019 | 2026-10-11 | średnia |
| [35] | G20S Pro i Pro BT mają różne kody przycisków | [AlexELEC docs](https://alexelec.run.place/docs/alexelec/docs/hid-remote) | undated | 2026-10-11 | średnia |
| [36] | Moonlight-qt CLI i opcje | [GitHub moonlight-qt](https://github.com/moonlight-stream/moonlight-qt/blob/master/app/cli/commandlineparser.cpp) | 2026-10 | 2026-10-11 | wysoka (potwierdzone [48]) |
| [37] | Wyciek `Runtime.enable` wykrywalny przez anty-bot | [apiserpent blog](https://apiserpent.com/blog/puppeteer-stealth-still-works-2026) | 2026 | 2026-10-11 | niska (unverified; Netflix nie reaguje [44]) |
| [38] | CDP `Input` daje `isTrusted=true`, `dispatchEvent` nie; infobar `chrome.debugger` | [V2EX](https://v2ex.com/t/1236297) | 2026 | 2026-10-11 | wysoka (potwierdzone [45]) |
| [39] | Chrome 142 Local Network Access | [OpenReplay blog](https://blog.openreplay.com/de/chrome-local-network-access-lna-berechtigung/) | 2025 | 2026-10-11 | średnia |
| [40] | Historyczny limit 720p dla Chrome | [PCWorld](https://www.pcworld.com/article/415654/confirmed-only-microsoft-edge-will-play-netflix-content-at-1080p-on-your-pc.html) | 2016 | 2026-10-11 | wysoka (jako historia) |
| [41] | Disney+ 1080p tylko w Edge (stare doniesienia) | [ResetEra](https://www.resetera.com/threads/why-full-hd-4k-is-not-working-on-disney-via-browser.252273/) | ~2021 | 2026-10-11 | niska |
| [42] | Edge: `--remote-debugging-port` + `--user-data-dir` | [Microsoft Learn](https://learn.microsoft.com/microsoft-edge/devtools/protocol) | live | 2026-10-11 | średnia |
| [43] | AttachThreadInput i ryzyko deadlocków | [PureBasic forum](https://www.purebasic.fr/english/viewtopic.php?p=377886) | ~2012 | 2026-10-11 | niska |
| [44] | Spike S1–S4 na UM760: jakość, DRM, CDP, Playwright, nowy profil (Edge i Chrome 155) | [Spike MediaStation, pomiar własny](imports/spike-results-um760.md) | 2026-10-12 | 2026-10-15 | wysoka (2 przeglądarki, 4 warianty S2) |
| [45] | Spike S5–S7: spatial navigation, zaufane i niezaufane wejście, mapa fokusu, reguła Back | [Spike MediaStation, pomiar własny](imports/spike-results-um760.md) | 2026-10-13 | 2026-10-15 | wysoka (Edge i Chrome, 2 przebiegi S7) |
| [46] | Spike C1–C3: wariant C (rozszerzenie bez portu CDP) | [Spike MediaStation, pomiar własny](imports/spike-results-um760.md) | 2026-10-13 | 2026-10-15 | wysoka |
| [47] | Spike S8: pauza 30 min w oknie zminimalizowanym i ukrytym | [Spike MediaStation, pomiar własny](imports/spike-results-um760.md) | 2026-10-14 | 2026-10-15 | średnia (unverified; jedna próba na wariant) |
| [48] | Spike S9–S11: wyciszanie, Home z pilota (dongle USB), foreground, Moonlight | [Spike MediaStation, pomiar własny](imports/spike-results-um760.md) | 2026-10-14 | 2026-10-15 | wysoka (S10 16/16; tylko dongle) |

## Staleness map

Tabela pochodzi z `recon_kit.py staleness` (2026-10-15). Okna świeżości z pakietu technicznego: wersje i kompatybilność 1 mies., ekosystem 6 mies., landscape 12 mies., wzorce 24 mies. Pomiary spike'u są w klasie wersji, bo zależą od Edge i Chrome 155 oraz bieżącego UI Netflixa.

| Twierdzenie | Klasa | Publikacja | Re-check | Stan |
|---|---|---|---|---|
| [1][44] Netflix 1080p w Edge i Chrome 155 | wersje | 2026-10-12 | 2026-11-12 | świeże (pomiar) |
| [7][44] reguły `navigator.webdriver` | wersje | 2026-10-12 | 2026-11-12 | świeże |
| [44] Netflix nie reaguje na CDP ani Playwright | wersje | 2026-10-12 | 2026-11-12 | świeże |
| [44] Widevine CDM w katalogu instalacji | wersje | 2026-10-12 | 2026-11-12 | świeże |
| [45] spatial navigation odrzucona; selektory UI Netflixa | wersje | 2026-10-13 | 2026-11-13 | świeże; selektory starzeją się z każdym wdrożeniem UI Netflixa |
| [46] wariant C bez ostrzeżeń, `--autoplay-policy` | wersje | 2026-10-13 | 2026-11-13 | świeże |
| [47] pauza 30 min: minimalizacja tak, SW_HIDE nie | wersje | 2026-10-14 | 2026-11-14 | świeże, jedna próba |
| [36][48] Moonlight-qt 6.2.0 CLI i zachowanie okna | wersje | 2026-10-14 | 2026-11-14 | świeże |
| [3] HDR i Atmos tylko w Edge | wersje | 2026-08 | 2026-09-15 | ⚠️ przeterminowane, jedno źródło; sprawdzić na TV |
| [5] Chrome 136: user-data-dir | wersje | 2025-03 | 2025-04-17 | ⚠️ przeterminowane wg publikacji; port z własnym profilem działa na 155 [44] |
| [11] Chrome 137: brak `--load-extension` | wersje | 2025-04 | 2025-05-15 | ⚠️ przeterminowane; sprawdzić przed wyborem C |
| [17][18] WebView2: DRM | wersje | 2024-09 | 2024-10-15 | ⚠️ przeterminowane wg publikacji; stan issue (open) sprawdzony 2026-10-11 |
| [37] wyciek `Runtime.enable` | ekosystem | 2026-06 | 2026-12-15 | świeże |
| [22]–[26] biblioteki i rozszerzenia | ekosystem | 2026-10 | 2027-04-11 | świeże |
| [44] brak blokady automatyzacji (doniesienia + pomiar) | landscape | 2026-10-12 | 2027-10-12 | świeże |
| [48] kody HID pilota G20S Pro (dongle) | wzorce | 2026-10-13 | 2028-10-13 | świeże |
| [30][31] SMTC i audio service | wzorce | 2026-10 | 2028-10-11 | świeże |

**Najwcześniejszy re-check dla świeżych twierdzeń: 2026-11-12** (pomiary spike'u na Edge i Chrome 155). Przy następnym wydaniu Edge lub Chrome warto powtórzyć S1, S2 i S7 harnessem spike'u, a twierdzenia ⚠️ zweryfikować przy Refresh.
