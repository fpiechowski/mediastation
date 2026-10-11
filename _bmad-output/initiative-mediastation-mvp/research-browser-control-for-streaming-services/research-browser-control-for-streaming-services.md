---
title: 'Technical research: sterowanie serwisami streamingowymi w przeglądarce'
type: 'technical'
shape: 'select'
topic: 'Sterowanie serwisami streamingowymi w przeglądarce (Netflix jako wzorzec)'
decision: 'Wybór mechanizmu sterowania przeglądarką dla Netflixa i kolejnych serwisów (A Playwright, B CDP, C rozszerzenie, D inne)'
source: 'native run'
status: complete
preset: 'standard'
validation: 'normal'
created: '2026-10-11'
updated: '2026-10-11'
claims_verified: 9
claims_unverified: 3
---

# Technical research: sterowanie serwisami streamingowymi w przeglądarce

**Decyzja, której służy research:** jak MediaStation ma sterować serwisami streamingowymi w przeglądarce (nawigacja D-padem, Back, pauza, wykrywanie odtwarzania, klawiatura ekranowa). Netflix jest pierwszym serwisem i wzorcem dla kolejnych.

> Staleness: to porównanie technologii, które szybko się zmieniają (Chrome wydaje nową wersję co 4 tygodnie). Przed decyzją podjętą później niż w Q1 2027 trzeba je odświeżyć.

## Executive summary

**Rekomendacja: wariant B jako hybryda D3.** MediaStation sama uruchamia Chrome lub Edge z **konkretnym** `--remote-debugging-port`, dedykowanym `--user-data-dir` i bez flag automatyzacji. Następnie podłącza się przez **surowe CDP** albo `connectOverCDP`. Logika serwisu żyje w **adapterze JS per serwis**, który nie zależy od transportu: ten sam bundle da się wstrzyknąć przez CDP albo załadować jako content script rozszerzenia. Pewność: **średnia**. Mechanika jest potwierdzona w kodzie źródłowym, a zachowanie Netflixa wymaga spike'u.

Trzy ustalenia przesądzają o wyborze:

1. **DRM nie różnicuje kandydatów, dopóki nie psujemy flag.** Netflix oficjalnie gra w Chrome 117+ i Edge 118+ do 2160p na Windows [1][2]. Full HD SDR jest więc spełnione w obu przeglądarkach. HDR i Atmos nadal ma tylko Edge [3]. Zagrożeniem są wyłącznie domyślne flagi Playwrighta, np. `--disable-component-update` blokuje aktualizację Widevine [9][14][15].
2. **`navigator.webdriver` zależy od transportu, nie od samego CDP.** Kod Chromium włącza `AutomationControlled` dla `--enable-automation`, `--headless`, `--remote-debugging-pipe` i `--remote-debugging-port=0`. Celowo **nie** włącza go dla konkretnego numeru portu [7]. `playwright.launch()` zawsze używa pipe [10], więc zawsze daje `webdriver=true`. Przeglądarka uruchomiona przez nas z portem `9xxx` wygląda dla strony jak zwykła.
3. **Rozszerzenie (C) jest najczystsze, ale najdroższe w dystrybucji.** Chrome 137 usunął `--load-extension` z buildów branded [11]. Instalacja i aktualizacje w dedykowanym profilu stają się ręczne albo idą przez Web Store lub Edge Add-ons. Zdarzenia generowane przez rozszerzenie mają `isTrusted=false` [38], podczas gdy CDP potrafi wysłać zaufane wejście.

**Największe zastrzeżenie:** nie znaleziono **żadnego** publicznego doniesienia, że Netflix blokuje lub degraduje przeglądarki sterowane z zewnątrz, ale nie znaleziono też dowodu, że tego nie robi. Wykrywalne są subtelniejsze sygnały CDP, np. wyciek `Runtime.enable` [37]. Odpowiedź da dopiero spike (lista testów niżej).

## 1. DRM i jakość obrazu

- **Netflix na Windows:** Chrome 117+ i Edge 118+ grają do Ultra HD (2160p), a Firefox i Opera do 1080p [1]. 4K w Chrome na Windows 11 pojawiło się w lipcu–sierpniu 2026. Wymaga planu Premium, GTX 1050+ lub RX 400+, często HEVC Video Extensions i HDCP 2.2. Przy niespełnieniu warunków jakość spada do 1080p [2]. Dawny limit 720p dla Chrome [40] jest historią. **G1 (Full HD SDR) spełniają obie przeglądarki.** Pewność wysoka, dwa niezależne źródła.
- **HDR i Atmos:** według doniesień z 2026 r. nadal tylko Edge (PlayReady) [3]. HDR w Windows wymaga GPU ze sprzętowym PlayReady i kodeków HEVC 10-bit [4]. Pewność średnia, bo potwierdza to jedno źródło branżowe. Jedno doniesienie mówi, że Chrome dostał PlayReady zamiast Widevine. Pozostaje sprzeczne i nierozstrzygnięte (zapisane w digest).
- **Widevine w dedykowanym profilu:** Chromium pobiera Widevine przez component updater przy pierwszym kontakcie z DRM [14]. Wyłączenie aktualizacji komponentów kończy się błędem M7701-1003 [15]. Nowy profil może przez pewien czas czekać na pobranie CDM. NetflixHD kopiuje go z istniejącego profilu, żeby tego uniknąć [21].
- **Serwer licencji widzi status VMP** (PLATFORM_VERIFIED, TAMPERED lub UNVERIFIED) [16]. Branded Chrome i Edge są podpisane. Buildy niebrandowane, w tym Chrome for Testing polecany przez Google do automatyzacji [5], to ryzyko braku CDM lub weryfikacji. Pewność niska, wniosek nie został potwierdzony dla CfT.
- **Odrzucone z powodu DRM (D):**
  - WebView2 nie ma Widevine (issue otwarte od 2024-09 [17]), a Fixed Runtime nie ma PlayReady [18].
  - Electron wymaga forka castLabs i podpisu VMP przez EVS [19].
  - Netflix ze Store to od lipca 2024 aplikacja webowa na Edge [20]. Nie jest osobną ścieżką sterowania, tylko trybem uruchomienia Edge.
- **Pozostałe serwisy:** brak wiarygodnych danych z lat 2025–2026 o rozdzielczości Disney+, Prime Video i Max w przeglądarkach. Stare wątki mówią o 1080p tylko w Edge [41]. Pewność niska, do zmierzenia w spike'u per serwis.

## 2. Wykrywanie automatyzacji

| Sygnał | Co go włącza | A: Playwright launch | B: własny start + port | C: rozszerzenie |
|---|---|---|---|---|
| `navigator.webdriver=true` | `--enable-automation`, `--headless`, `--remote-debugging-pipe`, port `0` [7] | **zawsze** (pipe [10]); maskowalne `--disable-blink-features=AutomationControlled` | **nie** (konkretny port [7]) | nie |
| Infobar „sterowana przez oprogramowanie testowe” | `--enable-automation` [8] | domyślnie tak, usuwalne przez `ignoreDefaultArgs` | nie | nie (ale `chrome.debugger` pokazuje własny infobar [38]) |
| Wyciek `Runtime.enable` | klient CDP włączający domenę Runtime [37] | tak (Playwright woła go zawsze) | **do uniknięcia** w surowym CDP: `Page.addScriptToEvaluateOnNewDocument` + `Runtime.addBinding`, bez `Runtime.enable` | nie |
| Otwarty port debugowania | `--remote-debugging-port` | nie (pipe) | tak, na localhost | nie |

- **Netflix i automatyzacja:** wyszukiwania standardowe i rozszerzone (Netflix, Selenium, Playwright, webdriver, „controlled by automated test software”) nie dały żadnego doniesienia o blokadzie lub degradacji z lat 2023–2026. Netflix nie dokumentuje mechanizmów anty-automatyzacji. Materiały anty-bot dotyczą scrapingu, nie DRM. **Brak dowodu jest tu ustaleniem, a nie potwierdzeniem.** Pewność średnia.
- **Port na localhost:** Chrome 142 wprowadził Local Network Access, czyli zgodę na dostęp publicznych stron do zasobów lokalnych [39]. To ogranicza sondowanie portu przez strony. Pozostaje ryzyko lokalne: każdy proces użytkownika może przejąć profil zalogowany do Netflixa. Na domowym HTPC jednego użytkownika to ryzyko jest akceptowalne, ale trzeba je odnotować.

## 3. Mechanika sterowania a wersje Chrome i Edge

- **Chrome 136+** (zmiana z marca 2025): `--remote-debugging-port` i `--remote-debugging-pipe` są ignorowane na domyślnym `user-data-dir`. Trzeba wskazać własny katalog [5]. Potwierdzone na Chrome 151 [6]. Dedykowany profil i tak jest celem MediaStation, więc **ograniczenie nie przeszkadza**. Google kieruje jednak automatyzację do Chrome for Testing [5], co sygnalizuje, że branded Chrome może być dalej zaostrzany. To ryzyko strategiczne dla A i B. Microsoft dla Edge nadal dokumentuje `--remote-debugging-port` + `--user-data-dir` [42], a zmiana 136 najpewniej obowiązuje też Edge (wniosek).
- **Playwright (A):** domyślne przełączniki obejmują `--disable-component-update`, `--disable-extensions`, `--force-color-profile=srgb`, `--disable-backgrounding-occluded-windows` oraz wyłączenie `GlobalMediaControls` i `MediaRouter` [9]. Każdy z nich trzeba świadomie usuwać przez `ignoreDefaultArgs`. Lista zmienia się między wersjami, np. Playwright dodał wpis dla Chromium 155 [9]. Uruchomiona przeglądarka żyje i umiera razem z procesem Playwrighta (pipe).
- **Rozszerzenie (C):** Chrome 137 usunął `--load-extension` z buildów branded [11]. `Extensions.loadUnpacked` przez CDP działa tylko po pipe z `--enable-unsafe-extension-debugging` [12], czyli z `webdriver=true`. W MV3 otwarte `connectNative()` (od Chrome 105) lub aktywny WebSocket (od Chrome 116) utrzymują service worker przy życiu [13].

## 4. Nawigacja D-padem i Back

- **Natywnie:** Netflix w przeglądarce ma skróty tylko w playerze: Enter/Space, F, Esc (wyjście z fullscreen), strzałki do przewijania i głośności, M, S [28]. Nie ma nawigacji strzałkami po wierszach.
- **Flaga `--enable-spatial-navigation`** nadal istnieje w kodzie Chromium [27]. NetflixHD (Linux, październik 2026) używa jej do chodzenia strzałkami po wierszach Netflixa i opisuje Esc/Backspace jako „back” serwisu [21]. To jedyne świeże prior art. Projekt jest jednoosobowy i ma 0 gwiazdek, więc pewność jest średnia. Na Windows z Netflixem trzeba to sprawdzić w spike'u.
- **Biblioteki:**
  - polyfill WICG jest **zarchiwizowany** [24];
  - js-spatial-navigation ostatnią zmianę dostał w 2024-05 [25];
  - Norigin jest aktywny (2026-09), ale służy do budowy aplikacji React, a nie do wstrzykiwania w obcy DOM [26].
- **Istniejące rozszerzenia „Netflix z pada”** są porzucone: netflix-navigator (2022-12) [22], NetflixController z wirtualną klawiaturą (2020-10) [23]. **Nie ma gotowca.** Mapy fokusu per serwis trzeba napisać samodzielnie.
- **Wzorzec:** flaga spatial navigation jako baza, a na niej cienka **mapa fokusu per serwis**: selektory wierszy, kafelków, profili, pola wyszukiwania i kontrolek playera. Dla playera najstabilniejsze jest wewnętrzne API Netflixa (`netflix.appContext.state.playerApp.getAPI().videoPlayer`), którego używa nawet Mozilla w wrapperze PiP [29]. API jest nieudokumentowane i może zniknąć. Zapasowo pozostaje `HTMLVideoElement`.
- **Wykrywanie „ekranu głównego serwisu” dla Back:** najtańszy sygnał to trasa URL SPA. W Netfliksie są to `/browse` (ekran główny), `/title/…`, `/search…` i `/watch/…`. Do tego stan DOM, np. otwarty modal szczegółów, oraz wykrywanie wyboru profilu. Adapter decyduje: „cofnij w serwisie” (Esc, `history.back()` albo zamknięcie modala) albo „wyjdź do MediaStation”. Jest to wniosek projektowy niepoparty źródłem i wymaga potwierdzenia w spike'u.
- **Zaufane wejście:** zdarzenia z JS lub rozszerzenia mają `isTrusted=false`. Zdarzenia wysłane przez CDP `Input.*` przechodzą przez pipeline wejścia przeglądarki jak zdarzenia z OS [38]. Pewność średnia, bo dla zdarzeń klawiatury brak oficjalnego potwierdzenia. Jeśli Netflix ignoruje niezaufane zdarzenia, B i A mają przewagę nad C.

## 5. Pauza, wykrywanie odtwarzania i klawiatura ekranowa

- **Pauza:** najpewniejsza droga to `video.pause()` albo API playera Netflixa, wywołane z wstrzykniętego adaptera. Działa jednakowo w A, B i C.
- **Wykrywanie odtwarzania:** zdarzenia `play`, `pause` i `ended` na `<video>` przekazywane przez binding (`Runtime.addBinding` w B, port native messaging w C).
- **SMTC:** Chrome ma **jedną** sesję SMTC dla wszystkich kart i okien, a osobną tylko dla zainstalowanych PWA [30]. SMTC nadaje się więc jako sygnał zapasowy per przeglądarka, a nie per serwis. Playwright domyślnie wyłącza `GlobalMediaControls` [9].
- **Klawiatura ekranowa:** adapter nasłuchuje `focusin` na `input`, `textarea` i `[contenteditable]`. Wtedy albo rysuje klawiaturę jako overlay w stronie, albo prosi MediaStation o natywną klawiaturę na wierzchu i wstawia tekst przez CDP `Input.insertText` (B) lub przez skrypt (C). Wszyscy kandydaci zapewniają to podobnie, więc kryterium mało ich różnicuje.

## 6. Ryzyka Windows i Moonlight

- **Globalny Home:** `RegisterHotKey` jest prostszy, ale nie widzi zwolnienia klawisza. `WH_KEYBOARD_LL` widzi wszystko, ale wymaga pętli komunikatów [34]. G20S Pro i G20S Pro BT raportują **różne kody**, a dla Windows nie ma publicznej mapy [35]. Trzeba więc zmierzyć, co wysyła Home: zwykły VK, `VK_BROWSER_HOME` albo konsumencki HID lub `WM_APPCOMMAND`. W tym ostatnim przypadku przydaje się Raw Input. Uwaga: jeśli Home dociera jako `BROWSER_HOME`, przeglądarka sama go obsłuży, gdy MediaStation go nie przechwyci.
- **Foreground:** proces może ustawić foreground m.in. wtedy, gdy **otrzymał ostatnie zdarzenie wejściowe** [33]. Proces, który przechwycił Home hookiem lub Raw Inputem, ma więc najlepszą pozycję. Zapasowo można użyć `AllowSetForegroundWindow` i ostrożnie `AttachThreadInput` (ryzyko deadlocków [43]). Dokumentacja zastrzega, że system może odmówić mimo spełnienia warunków [33].
- **Wyciszanie per aplikacja:** wzorzec `IAudioSessionManager2` → `IAudioSessionControl2::GetProcessId` → `ISimpleAudioVolume::SetMute` [32]. Chromium gra dźwięk z **osobnego procesu audio service** [31]. Trzeba więc wyciszać sesje wszystkich PID w drzewie procesów danej przeglądarki, a nie tylko proces główny. Dwie przeglądarki tej samej marki z różnymi profilami to wciąż osobne drzewa procesów.
- **Moonlight-qt v6.2.0 (2026-10-04):**
  - CLI ma `stream <host> <app>`, `list`, `quit <host>` i `pair`;
  - opcje `--mute-on-focus-loss` i `--background-gamepad` są istotne dla przełączania;
  - `--quit-after` decyduje, czy wyjście zamyka grę na hoście [36].

  Brak doniesień o zachowaniu sesji przy minimalizacji lub ukryciu okna na Windows, więc to idzie do spike'u. Wyjście wykrywamy przez uchwyt procesu, co jest zgodne z addendum.

## Macierz kandydatów

Skala ocen 1–5. Twarde progi: G1 = co najmniej 1080p SDR, G2 = brak blokady za automatyzację, G3 = przeglądarka per serwis.

| Kryterium (waga) | A: Playwright launch | B: własny start + CDP (port) | C: rozszerzenie + NM/WS | D1: WebView2 / Electron | D2: PWA ze Store |
|---|---|---|---|---|---|
| G1 DRM 1080p | ✅ po usunięciu flag [9][15] | ✅ | ✅ | ❌ WebView2 [17], Electron wymaga castLabs [19] | ✅ (to Edge) |
| G2 automatyzacja | ⚠️ `webdriver` zawsze [7][10] + `Runtime.enable` [37] | ⚠️ tylko port i subtelne sygnały CDP | ✅ najczystszy | — | zależy od sposobu sterowania |
| G3 przeglądarka per serwis | ✅ `channel` | ✅ | ⚠️ osobna instalacja w każdej przeglądarce | ❌ | ❌ tylko Edge |
| D-pad (25%) | 4 | 4 | 4 (niezaufane zdarzenia) | — | — |
| Kruchość i utrzymanie (20%) | 3 (lista flag Playwrighta, lifecycle) | 4 | 3 (dwa artefakty, ręczna instalacja [11]) | — | — |
| Pauza i playback (15%) | 5 | 5 | 5 | — | — |
| Back zgodny z TV (15%) | 4 | 4 | 4 | — | — |
| Klawiatura ekranowa (10%) | 4 | 4 | 4 | — | — |
| Dopasowanie do stosu (10%) | 3 (Node, .NET, Python; przeglądarka zależna od procesu) | 5 (WebSocket JSON z każdego języka, reattach) | 4 (host NM = dowolny exe) | — | — |
| Jakość (bonus, 5%) | 3 (srgb i inne flagi do pilnowania) | 4 | 4 | — | — |
| **Wynik ważony** | **3,80** | **4,25** | **3,95** | odpada (G1/G3) | odpada jako mechanizm, zostaje jako tryb uruchomienia |

Wagi zostały zatwierdzone na plan gate. Wynik da się przeliczyć: przy wadze G2 jako kryterium rankingowym C zbliża się do B.

## Wnioski międzywymiarowe

- **Kryteria funkcjonalne (pauza, Back, klawiatura, D-pad) prawie nie różnicują kandydatów**, bo we wszystkich realizuje je ten sam wstrzyknięty JS. Różnią się trzy rzeczy:
  - transport i jego ślad automatyzacji,
  - koszt dystrybucji kodu do przeglądarki,
  - to, kto jest właścicielem życia procesu przeglądarki.

  Stąd hedge: **adapter serwisu jako bundle niezależny od transportu.**
- **Dedykowany profil jest wymagany jednocześnie przez Chrome 136 [5], przez separację logowania i przez Widevine** [14][21]. Ta decyzja jest więc wspólna dla wszystkich kandydatów i nie zależy od wyboru.
- **Edge jako przeglądarka dla Netflixa daje HDR i Atmos [3]**, a mechanizm B działa identycznie w Edge [42]. Wybór przeglądarki per serwis nie zmienia mechanizmu.

## Rekomendacja

1. **Wybór: B w formie hybrydy D3** (pewność średnia: mechanika wysoka [5][7][10], zachowanie Netflixa niezweryfikowane).
   - MediaStation uruchamia `msedge.exe` albo `chrome.exe` z flagami `--user-data-dir=<profil-serwisu> --remote-debugging-port=<stały, np. 92xx> --app=<url> --start-fullscreen` lub `--kiosk`. Nie używa żadnej z flag Playwrighta.
   - Sterowanie przez surowe CDP po WebSocket: `Page.addScriptToEvaluateOnNewDocument` (adapter), `Runtime.addBinding` (zdarzenia do MediaStation), `Input.dispatchKeyEvent` i `Input.insertText` (zaufane wejście). Bez `Runtime.enable`. `connectOverCDP` z Playwrighta jest dopuszczalny w prototypie, ale woła `Runtime.enable` [37].
   - **Feeds:** architecture spine. Komponenty „BrowserHost” (lifecycle, CDP) i „ServiceAdapter” (JS per serwis) oraz kontrakt komunikatów (`navigate`, `back`, `pause`, `playbackState`, `textFocus`).
2. **Runner-up: C (rozszerzenie)** wygrywa, gdy spike pokaże, że Netflix lub inny serwis reaguje na port debugowania albo sygnały CDP, albo gdy Google zablokuje remote debugging w branded Chrome. Koszt przejścia jest niski, jeśli adapter od początku jest transport-agnostic: zmienia się tylko most (native messaging zamiast `Runtime.addBinding`). Instalacja to jednorazowe „Load unpacked” w dedykowanym profilu albo publikacja unlisted w Edge Add-ons lub Chrome Web Store.
3. **Odrzucone:**
   - **A jako docelowe:** `webdriver=true` jest nieusuwalne bez dodatkowego maskowania, do tego dochodzi lista szkodliwych flag i przeglądarka zależna od procesu Node.
   - **WebView2 i Electron:** DRM.
   - **PWA ze Store jako mechanizm:** to tylko Edge z `--app`. Jako *tryb uruchomienia* B pozostaje dobre, bo daje osobną sesję SMTC [30].
4. **Najsilniejszy argument przeciw B:** otwarty port CDP to łatwy cel lokalnego przejęcia sesji i sygnał, który Google aktywnie zawęża [5]. Jeśli kolejna wersja Chrome zablokuje go także dla niestandardowych profili, B przestanie działać w Chrome z dnia na dzień. Edge może podążyć za nim.
5. **Najtańszy hedge:** adapter jako jeden bundle JS z interfejsem `bridge.send/receive` oraz test „smoke” po każdej aktualizacji przeglądarki (start, profil, odtworzenie, pauza, Back).

## Spike na Netfliksie: konkretne testy

| # | Test | Kryterium zaliczenia |
|---|---|---|
| S1 | Edge i Chrome uruchomione przez MediaStation z `--remote-debugging-port=9223 --user-data-dir=…`, odtworzenie odcinka, nakładka statystyk `Ctrl+Alt+Shift+D` | ≥1080p, brak błędu DRM; zanotować rozdzielczość, kodek i HDR w Edge na TV |
| S2 | To samo z podłączonym klientem CDP: (a) surowe CDP bez `Runtime.enable`, (b) `connectOverCDP` | brak różnicy w jakości i błędach względem S1; `navigator.webdriver === false` |
| S3 | Kontrola negatywna: `playwright.launch({channel:'msedge', ignoreDefaultArgs:[…]})` | czy Netflix gra; czy jakość lub błędy różnią się od S1 (pomiar ryzyka G2) |
| S4 | Nowy pusty profil: czas do pobrania Widevine; wariant z CDM skopiowanym z innego profilu [21] | profil gra przy pierwszym uruchomieniu albo znany jest czas oczekiwania |
| S5 | `--enable-spatial-navigation` w Chrome i Edge na `/browse`, profilach, wyszukiwaniu i w playerze | które ekrany działają „za darmo”, a które wymagają mapy fokusu |
| S6 | `Input.dispatchKeyEvent` (Space, strzałki, Esc) kontra `dispatchEvent` z JS na playerze i na `/browse` | czy Netflix rozróżnia zaufane zdarzenia (przesądza B kontra C) |
| S7 | Back: Esc, `history.back()` i zamykanie modala na `/title`, `/search`, `/watch`; wykrywanie `/browse` i ekranu profili | deterministyczna reguła „cofnij lub wyjdź” |
| S8 | Pauza przy przełączeniu: `video.pause()` kontra API playera [29]; ukrycie lub minimalizacja okna na 30 min, potem wznowienie | brak utraty sesji i błędu licencji po powrocie |
| S9 | Sesje audio w mikserze: PID-y Edge i Chrome, wyciszenie całego drzewa | brak dźwięku z wyciszonej przeglądarki, pozostałe słyszalne |
| S10 | G20S Pro BT: co raportuje Home (hook LL, Raw Input, `WM_APPCOMMAND`) przy fokusie w Edge, Chrome i Moonlight; `SetForegroundWindow` po Home | Home zawsze przechwycony, MediaStation na wierzchu |
| S11 | Moonlight: `stream` z CLI, ukrycie lub minimalizacja okna na 5 min, przywrócenie; `--mute-on-focus-loss` | sesja trwa, obraz wraca |
| S12 | Prime Video, Max i Disney+ w Edge i Chrome przez S1 i S2 | rozdzielczość per serwis i przeglądarka (wejście do tabeli „serwis → przeglądarka”) |

## Otwarte pytania

- **Czy Netflix lub inne serwisy reagują na port debugowania i sygnały CDP?** Brak publicznych danych. Odpowiedź dadzą S2, S3 i S12.
- **Czy Netflix ignoruje niezaufane zdarzenia klawiatury?** Odpowiedź da S6. Ten test przesądza między B a C.
- **Rozdzielczość Disney+, Prime Video i Max w przeglądarkach w 2026 r.** Brak wiarygodnych źródeł [41]. Odpowiedź da S12.
- **Atmos i HDR w Edge w 2026 r.** Potwierdza to jedno źródło [3]. Odpowiedź da S1 na TV.
- **Kod HID przycisku Home G20S Pro BT na Windows.** Odpowiedź da S10.
- **Zachowanie Moonlight przy ukryciu okna.** Odpowiedź da S11.
- **Kierunek Google wobec remote debugging w branded Chrome.** Obserwować release notes. Odpowiedź przyniesie Refresh tego raportu.

## Źródła

| # | Wspiera | Wydawca | Data publikacji | Dostęp | Pewność |
|---|---|---|---|---|---|
| [1] | Netflix: Chrome 117+ / Edge 118+ do 2160p na Windows | [Netflix Help Center](https://help.netflix.com/en/node/23742) | live | 2026-10-11 | wysoka |
| [2] | 4K w Chrome na Win11, wymagania, fallback 1080p | [Chrome Unboxed](https://chromeunboxed.com/netflix-finally-unlocks-4k-streaming-in-chrome-on-windows-11/) | 2026-08-05 | 2026-10-11 | wysoka |
| [3] | HDR/Atmos nadal tylko Edge | [ChannelNews](https://www.channelnews.com.au/microsoft-loses-another-edge-as-netflix-unlocks-4k-chrome-streaming/) | 2026-08 | 2026-10-11 | średnia |
| [4] | HDR w Windows: sprzętowy PlayReady, HEVC | [Microsoft Support](https://support.microsoft.com/help/4040547) | live | 2026-10-11 | wysoka |
| [5] | Chrome 136: remote debugging wymaga niestandardowego user-data-dir; CfT do automatyzacji | [Chrome for Developers](https://developer.chrome.com/blog/remote-debugging-port) | 2025-03-17 | 2026-10-11 | wysoka |
| [6] | Potwierdzenie zachowania na Chrome 151 | [GitHub trycua/cua #2916](https://github.com/trycua/cua/issues/2916) | 2026 | 2026-10-11 | średnia |
| [7] | Flagi włączające `navigator.webdriver`; konkretny port nie włącza | [Chromium runtime_features.cc](https://github.com/chromium/chromium/blob/main/content/child/runtime_features.cc) | 2026-10 | 2026-10-11 | wysoka |
| [8] | Infobar automatyzacji zależy od `--enable-automation` | [Chromium infobar_utils.cc](https://github.com/chromium/chromium/blob/main/chrome/browser/ui/startup/infobar_utils.cc) | 2026-10 | 2026-10-11 | wysoka |
| [9] | Domyślne przełączniki Playwrighta | [Playwright chromiumSwitches.ts](https://github.com/microsoft/playwright/blob/main/packages/playwright-core/src/server/chromium/chromiumSwitches.ts) | 2026-10 | 2026-10-11 | wysoka |
| [10] | Playwright launch zawsze używa `--remote-debugging-pipe` | [Playwright chromium.ts](https://github.com/microsoft/playwright/blob/main/packages/playwright-core/src/server/chromium/chromium.ts) | 2026-10 | 2026-10-11 | wysoka |
| [11] | Chrome 137 usuwa `--load-extension` w buildach branded | [Chromium Extensions group](https://groups.google.com/a/chromium.org/g/chromium-extensions/c/1-g8EFx2BBY/m/S0ET5wPjCAAJ) | 2025-04 | 2026-10-11 | wysoka |
| [12] | CDP `Extensions.loadUnpacked` tylko przez pipe + unsafe flag | [devtools-protocol Extensions.pdl](https://chromium.googlesource.com/devtools/devtools-frontend/+/19aecbbf95fa88b359123795de16fda3ee847fbd/node_modules/devtools-protocol/pdl/domains/Extensions.pdl) | 2025 | 2026-10-11 | wysoka |
| [13] | MV3: connectNative i WebSocket utrzymują service worker | [Chrome for Developers](https://developer.chrome.com/docs/extensions/develop/concepts/service-workers/lifecycle) | live | 2026-10-11 | wysoka |
| [14] | Widevine pobierany przez component updater | [nixpkgs commit](https://code.ornl.gov/nix/nixpkgs/-/commit/a701c5e8496ff433620fae2ecb13985976f48afc) | ~2024 | 2026-10-11 | średnia |
| [15] | Błąd M7701-1003 = brak odtwarzania chronionych treści | [Netflix Help Center](https://help.netflix.com/ar/node/27451) | live | 2026-10-11 | wysoka |
| [16] | Status VMP widoczny dla serwera licencji | [USENIX Security 2025](https://www.usenix.org/system/files/usenixsecurity25-roudot.pdf) | 2025 | 2026-10-11 | wysoka |
| [17] | WebView2 bez Widevine (issue otwarte) | [WebView2Feedback #4828](https://github.com/MicrosoftEdge/WebView2Feedback/issues/4828) | 2024-09 | 2026-10-11 | wysoka |
| [18] | WebView2 Fixed Runtime bez PlayReady | [WebView2Feedback #4632](https://github.com/MicrosoftEdge/WebView2Feedback/issues/4632) | 2024-06 | 2026-10-11 | wysoka |
| [19] | Electron: Widevine tylko w forku castLabs + VMP | [castLabs electron-releases](https://newreleases.io/project/github/castlabs/electron-releases/release/v18.3.13%2Bwvcus) | 2022 | 2026-10-11 | średnia |
| [20] | Netflix ze Store = aplikacja webowa na Edge | [Windows Latest](https://www.windowslatest.com/2024/07/25/windows-11s-netflix-loses-downloads-downgraded-to-microsoft-edge-based-web-app/) | 2024-07-25 | 2026-10-11 | wysoka |
| [21] | Prior art: kiosk Chromium, spatial navigation, kopiowanie Widevine | [GitHub NetflixHD](https://github.com/FromChaosComesClarity/NetflixHD) | 2026-10-08 | 2026-10-11 | średnia |
| [22] | netflix-navigator porzucony (2022-12) | [GitHub dutiyesh/netflix-navigator](https://github.com/dutiyesh/netflix-navigator) | 2022-12 | 2026-10-11 | wysoka |
| [23] | NetflixController z wirtualną klawiaturą porzucony (2020-10) | [GitHub FThompson/NetflixController](https://github.com/FThompson/NetflixController) | 2020-10 | 2026-10-11 | wysoka |
| [24] | Polyfill WICG zarchiwizowany | [GitHub WICG/spatial-navigation](https://github.com/WICG/spatial-navigation) | 2026-03 | 2026-10-11 | wysoka |
| [25] | js-spatial-navigation: ostatni push 2024-05 | [GitHub luke-chang/js-spatial-navigation](https://github.com/luke-chang/js-spatial-navigation) | 2024-05 | 2026-10-11 | wysoka |
| [26] | Norigin aktywny, dla aplikacji React | [GitHub NoriginMedia](https://github.com/NoriginMedia/Norigin-Spatial-Navigation) | 2026-09 | 2026-10-11 | wysoka |
| [27] | Switch `enable-spatial-navigation` nadal w Chromium | [Chromium content_switches.cc](https://github.com/chromium/chromium/blob/main/content/public/common/content_switches.cc) | 2026-10 | 2026-10-11 | wysoka |
| [28] | Skróty klawiszowe Netflixa w przeglądarce | [Netflix Help Center](https://help.netflix.com/en/node/24855) | live | 2026-10-11 | wysoka |
| [29] | Wewnętrzne API playera Netflixa (wrapper PiP Mozilli) | [Mozilla gecko-dev (mirror)](https://source-hub.etos-pro.ru/mozilla/gecko-dev/commit/2e684db116078bb8e0c82148cc1769ea60ee9c69) | 2022 | 2026-10-11 | średnia |
| [30] | Jedna sesja SMTC na przeglądarkę, osobna dla PWA | [Chromium system_media_controls](https://chromium.googlesource.com/chromium/src/+/lkgr/content/browser/media/system_media_controls) | live | 2026-10-11 | wysoka |
| [31] | Audio service w osobnym procesie na Windows | [Chromium services/audio README](https://chromium.googlesource.com/chromium/src/+/lkgr-android-internal/services/audio/README.md) | live | 2026-10-11 | wysoka |
| [32] | Wyciszanie per sesja audio (ISimpleAudioVolume) | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionmanager-getsimpleaudiovolume) | live | 2026-10-11 | wysoka |
| [33] | Warunki SetForegroundWindow | [Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow) | 2025-10-06 | 2026-10-11 | wysoka |
| [34] | RegisterHotKey kontra WH_KEYBOARD_LL | [CodeProject](https://api.codeproject.com/Articles/1273010/Global-Hotkeys-within-Desktop-Applications) | 2019 | 2026-10-11 | średnia |
| [35] | G20S Pro i Pro BT mają różne kody przycisków | [AlexELEC docs](https://alexelec.run.place/docs/alexelec/docs/hid-remote) | undated | 2026-10-11 | średnia |
| [36] | Moonlight-qt CLI i opcje | [GitHub moonlight-qt](https://github.com/moonlight-stream/moonlight-qt/blob/master/app/cli/commandlineparser.cpp) | 2026-10 | 2026-10-11 | wysoka |
| [37] | Wyciek `Runtime.enable` wykrywalny przez anty-bot | [apiserpent blog](https://apiserpent.com/blog/puppeteer-stealth-still-works-2026) | 2026 | 2026-10-11 | niska |
| [38] | CDP `Input` daje `isTrusted=true`, `dispatchEvent` nie; infobar `chrome.debugger` | [V2EX](https://v2ex.com/t/1236297) | 2026 | 2026-10-11 | niska |
| [39] | Chrome 142 Local Network Access | [OpenReplay blog](https://blog.openreplay.com/de/chrome-local-network-access-lna-berechtigung/) | 2025 | 2026-10-11 | średnia |
| [40] | Historyczny limit 720p dla Chrome | [PCWorld](https://www.pcworld.com/article/415654/confirmed-only-microsoft-edge-will-play-netflix-content-at-1080p-on-your-pc.html) | 2016 | 2026-10-11 | wysoka (jako historia) |
| [41] | Disney+ 1080p tylko w Edge (stare doniesienia) | [ResetEra](https://www.resetera.com/threads/why-full-hd-4k-is-not-working-on-disney-via-browser.252273/) | ~2021 | 2026-10-11 | niska |
| [42] | Edge: `--remote-debugging-port` + `--user-data-dir` | [Microsoft Learn](https://learn.microsoft.com/microsoft-edge/devtools/protocol) | live | 2026-10-11 | średnia |
| [43] | AttachThreadInput i ryzyko deadlocków | [PureBasic forum](https://www.purebasic.fr/english/viewtopic.php?p=377886) | ~2012 | 2026-10-11 | niska |

## Staleness map

Tabela pochodzi z `recon_kit.py staleness`. Okna świeżości z pakietu technicznego: wersje i kompatybilność 1 mies., ekosystem 6 mies., landscape 12 mies., wzorce 24 mies.

| Twierdzenie | Klasa | Publikacja | Re-check | Stan |
|---|---|---|---|---|
| [1][2] Netflix: Chrome i Edge do 2160p | wersje | 2026-08 | 2026-09-01 | ⚠️ przeterminowane wg daty publikacji; strona Netflix [1] sprawdzona live 2026-10-11 |
| [3] HDR i Atmos tylko w Edge | wersje | 2026-08 | 2026-09-01 | ⚠️ przeterminowane, jedno źródło; sprawdzić w S1 |
| [5] Chrome 136: user-data-dir | wersje | 2025-03 | 2025-04-01 | ⚠️ przeterminowane wg publikacji; zachowanie potwierdzone na Chrome 151 [6] |
| [11] Chrome 137: brak `--load-extension` | wersje | 2025-04 | 2025-05-01 | ⚠️ przeterminowane; sprawdzić przed wyborem C |
| [17][18] WebView2: DRM | wersje | 2024-09 | 2024-10-01 | ⚠️ przeterminowane wg publikacji; stan issue (open) sprawdzony 2026-10-11 |
| [7] reguły `navigator.webdriver` | wersje | 2026-10 | 2026-11-01 | świeże |
| [9][10] flagi Playwrighta | wersje | 2026-10 | 2026-11-01 | świeże |
| [36] Moonlight-qt CLI | wersje | 2026-10 | 2026-11-01 | świeże |
| [37] wyciek `Runtime.enable` | ekosystem | 2026-06 | 2026-12-01 | świeże |
| [22]–[26] biblioteki i rozszerzenia | ekosystem | 2026-10 | 2027-04-01 | świeże |
| brak doniesień o blokadzie automatyzacji | landscape | 2026-10 | 2027-10-01 | świeże |
| [30][31] SMTC i audio service | wzorce | 2026-10 | 2028-10-01 | świeże |

**Najwcześniejszy re-check: 2026-11-01** dla klas wersji. Twierdzenia oznaczone ⚠️ zostały potwierdzone jako nadal aktualne podczas tego runu. Mimo to warto je zweryfikować ponownie przy Refresh, najlepiej razem z wynikami spike'u.
