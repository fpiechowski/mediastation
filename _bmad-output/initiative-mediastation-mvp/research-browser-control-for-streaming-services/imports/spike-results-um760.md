---
title: 'Spike S1–S11: sterowanie przeglądarką na Netfliksie (wyniki)'
status: done
created: 2026-10-11
updated: 2026-10-14
source: '_bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/research-browser-control-for-streaming-services.md'
---

# Spike S1–S11: wyniki

Spike weryfikuje rekomendację z researchu: **B jako hybryda D3**. MediaStation sama uruchamia Edge lub Chrome z dedykowanym `--user-data-dir` i stałym `--remote-debugging-port`, a następnie steruje stroną przez surowe CDP bez `Runtime.enable`. Runner-up to **C (rozszerzenie)**. Kontekst produktu: [brief](../_bmad-output/initiative-mediastation-mvp/brief-mediastation/brief-mediastation.md). Instrukcja harnessu: [README](README.md).

Legenda werdyktu: ✅ zaliczony · ❌ niezaliczony · ⚠️ częściowo lub niejednoznacznie · ⏭️ pominięty.

**Konwencja komend.** Testy prowadzi agent przez `serve` (w tle) i `cmd` (README, „Dla agenta”). Zapis `cmd "key Space"` oznacza `node harness/ms.mjs cmd "key Space"`. Przed każdym krokiem agent wstawia znacznik `cmd "mark S6 player Space"`, żeby log JSONL dało się pociąć na kroki. Krok **fizyczny** wykonuje człowiek pilotem lub klawiaturą na prośbę agenta, a agent odczytuje wynik z logu (`cmd "wait key 30"` albo `cmd "events 10"`).

**Dowody.** Zrzut strony: `cmd "screenshot S1 edge overlay"`. Zrzut całego ekranu, gdy na wierzchu nie ma strony (S10, S11): `win\screenshot.ps1 -Name S10-edge-home`. Wideo z DRM na zrzutach zwykle wychodzi czarne, ale nakładka statystyk jest czytelna. Zrzuty i logi trafiają do `logs/` (w `.gitignore`, bo mogą pokazywać profil i konto). W tabelach podawaj ich ścieżki. Jeśli dowód ma trafić do repo, skopiuj wybrany plik do `spike/evidence/` i sprawdź, czy nie zawiera danych konta.

## Środowisko

| Pole | Wartość |
|---|---|
| Data sesji 1 / sesji 2 | 2026-10-12–13 / 2026-10-13–14 |
| Sprzęt | Minisforum UM760, monitor FHD z HDR (bez TV) |
| Pilot | G20S Pro przez **dongle USB 2.4G** (nie Bluetooth; Pro i Pro BT wysyłają różne kody, więc wyniki S10 dotyczą tylko dongla). Dongle w PnP: `USB\VID_1915&PID_1025` (kompozyt: MI_00 „Mic Device”, MI_02 klawiatura HID, MI_03 mysz + Consumer Control + System Control + vendor) |
| Windows (build) | Windows 11 Pro 26H2, build 26300.9457; od 2026-10-14 04:46 build **26300.9711** (KB5127648, automatyczny restart Windows Update w trakcie spike'u); PowerShell 5.1.26100 |
| Edge (wersja z logu `launch`) | 155.0.4283.45 (plik exe; wersję z `launch` uzupełnię) |
| Chrome (wersja z logu `launch`) | 155.0.8059.40 (plik exe; wersję z `launch` uzupełnię) |
| Node.js / playwright-core | Node 24.20.0 (winget OpenJS.NodeJS.LTS), npm 11.19.0 / playwright-core 1.64.0 |
| Moonlight-qt / host | Moonlight 6.2.0 (`--help` zgodny z flagami `win/moonlight.ps1`), 1 sparowany host |
| Plan Netflix | Standard (1080p, 2 ekrany) |

## Podsumowanie

| # | Test | Sesja | Werdykt | Jedno zdanie wniosku |
|---|---|---|---|---|
| S1 | Jakość i DRM w przeglądarce uruchomionej przez MediaStation | 1 | ✅ | Edge i Chrome z portem CDP i własnym profilem grają 1080p bez błędów; Edge (PlayReady SL3000, H.264 ~7 Mb/s) daje wyraźnie wyższy bitrate niż Chrome (Widevine L3, AV1 ~0,8 Mb/s), więc Edge jest domyślną przeglądarką dla Netflixa. |
| S4 | Widevine w nowym profilu | 1 | ✅ | Pusty profil gra od razu w Edge (PlayReady) i w Chrome (Widevine z katalogu instalacji, nic nie trafia do profilu), więc MediaStation może tworzyć profil per serwis bez rozgrzewania CDM. |
| S2 | Klient CDP: raw kontra connectOverCDP | 1 | ✅ | Podłączony klient CDP (raw i connectOverCDP) nie zmienia jakości, DRM ani błędów w Edge i Chrome, a `navigator.webdriver` jest `false`, więc Netflix nie reaguje na port debugowania ani na `Runtime.enable`; transport B zostaje (raw, bo i tak nie potrzebuje Playwrighta). |
| S3 | Kontrola negatywna: Playwright launch | 1 | ✅ (pomiar) | Nawet z `webdriver=true` i pełnymi flagami Playwrighta Netflix gra 1080p bez błędów, więc ryzyko G2 jest dziś niskie; B pozostaje bez Playwrighta jako margines. |
| S6 | Wejście: klawisze (A) i zamienniki C (B) | 1 | Werdykt B: ✅ · Werdykt C: ✅ z zastrzeżeniami | `key` (CDP) działa jak klawisz fizyczny, a Netflix obsługuje też większość niezaufanych klawiszy; w obu wariantach potrzebna jest mapa fokusu per ekran (modal, odcinki) i własne podświetlenie, bo geometryczny fokus i obwódka Netflixa nie wystarczą na pilota. |
| S5 | `--enable-spatial-navigation` | 1 | ❌ | Flaga działa tylko na ekranie profili; na `/browse` fokus wpada w podgląd kafelka, a w playerze OK przełącza dwa razy, więc odrzucamy ją na rzecz własnego adaptera z mapą fokusu. |
| S7 | Reguła Back „cofnij lub wyjdź” | 1 | ✅ | Reguła jest deterministyczna (watch → history.back, modal/title → closebtn, search → /browse, browse/profiles → wyjście), ale Back pilota (`BrowserBack`) Edge wykonuje sam i strona nie może go zablokować, więc MediaStation musi przechwytywać Back na poziomie Windows. |
| C1 | Wariant C: jakość i ślad automatyzacji | po sesji 1 | ✅ | Rozszerzenie bez portu CDP daje tę samą jakość i DRM co S1, `webdriver=false` i komplet zdarzeń, bez ostrzeżeń o trybie dewelopera; autoodtwarzanie wymaga flagi `--autoplay-policy=no-user-gesture-required`. |
| C2 | Wariant C: S6 w rozszerzeniu | po sesji 1 | ✅ | Wszystkie zamienniki (`jskey`, `focus`/`activate`, `jstype`, API playera, adapter z pilotem) działają jak w raw, z tymi samymi problemami UX fokusu. |
| C3 | Wariant C: Back i okno | po sesji 1 | ✅ | Macierz Back identyczna z S7, a `chrome.windows.update` obsługuje minimalizację, pełny ekran i powrót na wierzch. |
| S10 | Home z G20S Pro (dongle) i foreground | 2 | ✅ | Home przychodzi jako Raw Input HID Consumer 0x0223 (plus wstrzyknięty `VK_BROWSER_HOME` i `WM_APPCOMMAND` 7); MediaStation odzyskuje pierwszy plan nad Edge, Chrome, Moonlightem i pulpitem, jeśli połyka `VK_BROWSER_HOME` hookiem LL (osobny wątek) i robi `SetForegroundWindow` z odblokowaniem Alt lub `AttachThreadInput`. |
| S8 | Pauza i 30 min w tle | 2 | ⚠️ | Po 30 min w zminimalizowanym oknie odtwarzanie wraca od tego samego miejsca przez `video.play()`, ale po 30 min w oknie ukrytym (SW_HIDE) wznowienie kończy się błędem D7361-1254; MediaStation minimalizuje okno, wznawia przez `video.play()` i przy błędzie przeładowuje odcinek. |
| S9 | Wyciszanie drzewa procesów | 2 | ✅ | Wyciszanie sesji audio całego drzewa procesów profilu (fallback `IChannelAudioVolume`) ucisza wybraną przeglądarkę, a druga gra dalej; sam proces główny nie ma sesji audio. |
| S11 | Moonlight ukryty na 5 min | 2 | ✅ | Stream przeżywa minimalizację (5 min) i ukrycie (2 min) z powrotem obrazu, dźwięku i sterowania; MediaStation uruchamia go w borderless, przywraca z odblokowaniem pierwszego planu, a `--mute-on-focus-loss` wycisza go przy każdej utracie fokusu. |
| S12 | Prime Video, Max, Disney+ | odłożony | ⏭️ | |
| — | HDR i Atmos w Edge na TV | odłożony | ⏭️ | |

**Decyzja po spike'u:** ☐ B potwierdzone · ☑ **B z zastrzeżeniami:** Netflix nie reaguje na CDP, ale Back i Home pilota trzeba przechwytywać w Windows, a nawigacja wymaga własnej mapy fokusu (bez spatial navigation) · ☐ przejście na C · ☐ inne: … (szczegóły: „Podsumowanie po spike'u” na końcu)

---

# Sesja 1: S1, S4, S2, S3, S6, S5, S7

Przygotowanie: `cd spike; npm install`. Zamknij Edge i Chrome uruchomione z innymi profilami na portach 9223 i 9224. Przy pierwszym `launch` zaloguj się do Netfliksa w profilach `edge-netflix` i `chrome-netflix`, a przy pierwszym `serve --mode pwlaunch` w `edge-pw`. Przed testami C zainstaluj rozszerzenie w `edge-ext` i `chrome-ext` (README, „Ręczne Load unpacked”).

Testuj na jednym, stałym odcinku. Zapisz go tutaj: **1670, S3:E1 „Sarmatian Exam”** (tak tytułuje go player) (Netflix MovieId 81771640, 2187,8 s, audio pl 2.0).

## S1: jakość i DRM bez klienta CDP

**Cel:** baseline. Przeglądarka uruchomiona z portem debugowania i własnym profilem, ale bez podłączonego klienta, gra Netflixa co najmniej w 1080p.

**Kroki** (osobno dla Edge i Chrome):

1. `node harness/ms.mjs launch --browser edge --label S1-edge` (potem `--browser chrome --label S1-chrome`). Bez `serve`.
2. Odtwórz odcinek, odczekaj około 60 s, aż bitrate się ustabilizuje.
3. Fizycznie naciśnij `Ctrl+Alt+Shift+D` i odczytaj nakładkę statystyk, w tym pola związane z HDR (np. profil kodeka lub zakres dynamiki).
4. Zamknij przeglądarkę.

**Kryterium zaliczenia:** ≥1080p, brak błędu DRM (np. M7701-1003). Kolumna „HDR (orientacyjnie)” jest tylko obserwacją: monitor UM760 to FHD z HDR, więc sprawdzamy, czy Edge i Chrome w ogóle włączają HDR. Brak HDR na FHD **nie** jest porażką testu. Właściwy test HDR i Atmos na TV jest odłożony.

| Przeglądarka | Rozdzielczość (playing) | Kodek wideo | Bitrate a/v | Framerate | HDR (orientacyjnie) | Błędy | Werdykt |
|---|---|---|---|---|---|---|---|
| Edge 155.0.4283.45 | 1920x1080 | H.264 High `avc1.640028` (SDR) | 192 / 7139 kbps | 24 | brak: strumień AVC SDR (plan Standard nie daje HDR); nakładka: `HDR support: true (isTypeSupportedWithFeatures)` | brak; KeySystem `com.microsoft.playready.recommendation.3000` (PlayReady SL3000), KeyStatus 1080 `usable`; Total Dropped Frames 169/1690 przy starcie, Current 0 | ✅ |
| Chrome 155.0.8059.40 | 1920x1080 | AV1 `av01.0.04M.08` (tagi `EVEAV1,av1-hd-bitrate-capped`) | 192 / 842 kbps | 24 | brak; nakładka: `HDR support: false (media-capabilities)` | brak; KeySystem `com.widevine.alpha.SW_SECURE_DECODE` (Widevine L3), KeyStatus 1080 `usable`; Total Dropped 1/574 | ✅ |

HDR w Windows (Ustawienia → Ekran → HDR) włączony podczas testu? **Tak.** · Logi: `logs/20261012-220407-S1-edge.jsonl`, `logs/20261012-220838-S1-chrome.jsonl`; zrzuty nakładki `logs/20261012-220826-S1-edge-overlay-ep.png`, `logs/20261012-221022-S1-chrome-overlay.png` (pierwszy zrzut Edge `…220542…` to zwiastun 91 s, nie liczy się). · Uwagi: oba profile świeże (`freshProfile: true`), po zalogowaniu odcinek ruszył od razu. Obie przeglądarki dają 1080p na planie Standard, ale innymi drogami: Edge gra H.264 przez PlayReady SL3000 (sprzętowe DRM) z bitrate ok. 7 Mb/s, a Chrome gra AV1 przez Widevine L3 (programowe) z bitrate ok. 0,8 Mb/s (`av1-hd-bitrate-capped`). Edge zgłasza wsparcie HDR, Chrome nie (`media-capabilities`), więc HDR na TV ma sens tylko w Edge (i wymaga planu Premium). `winwindow.ps1 -Action front` z procesu w tle przełącza okno skutecznie (`SetForegroundWindow=True`). Zrzuty całego ekranu pokazują obraz wideo (nie jest czarny) i nakładkę.

## S4: Widevine w nowym, pustym profilu

**Cel:** czy nowy profil gra od razu, a jeśli nie, ile trwa pobranie CDM. Dotyczy głównie Chrome, bo Edge może grać przez PlayReady.

**Kroki:**

1. `node harness/ms.mjs launch --browser chrome --profile chrome-fresh1 --watch-cdm --label S4-chrome-fresh` (log zapisze czas pojawienia się `WidevineCdm\<wersja>\manifest.json`; polecenie czeka maks. 15 min, agent uruchamia je w tle).
2. Zaloguj się do Netfliksa i od razu spróbuj odtworzyć odcinek. Zanotuj błąd lub czas do startu.
3. Opcjonalnie po kroku 2: `serve --mode raw --browser chrome --label S4-drm` w tle, potem `cmd drm`. Wynik pokazuje, czy `com.widevine.alpha` jest już dostępne. To podłącza klienta CDP, więc rób to dopiero po kroku 2.
4. Wariant z kopią: zamknij przeglądarki, uruchom `node harness/ms.mjs copy-cdm --from chrome-netflix --to chrome-fresh2`, potem `launch --browser chrome --profile chrome-fresh2 --watch-cdm --label S4-chrome-copy` i powtórz krok 2.
5. Edge: to samo z `--browser edge --profile edge-fresh1`. Zanotuj, czy katalog `WidevineCdm` w ogóle powstaje i czy Netflix gra bez niego.

**Kryterium zaliczenia:** profil gra przy pierwszym uruchomieniu albo znamy czas oczekiwania i działa obejście (kopia CDM).

| Wariant | CDM gotowy po [s] | Pierwsze odtworzenie OK? | Błąd | Werdykt |
|---|---|---|---|---|
| Chrome, pusty profil (`chrome-fresh1`) | nie dotyczy: `WidevineCdm` w profilu pozostał pusty; Chrome używa CDM z katalogu instalacji (`Application\155.0.8059.40\WidevineCdm`, wersja 4.10.3112.0) | ✅ tak, od razu po zalogowaniu i wyborze profilu | brak | ✅ |
| Chrome, skopiowany CDM | ⏭️ pominięty: nie ma czego kopiować (katalog pusty we wszystkich profilach), a pusty profil gra od razu | — | — | ⏭️ |
| Edge, pusty profil (`edge-fresh1`) | nie dotyczy: `WidevineCdm` w profilu pusty, Edge gra przez PlayReady | ✅ tak (pierwsza próba trafiła na limit ekranów, po „Retry” ruszyło) | „Too many people are using your account right now”: limit 2 ekranów planu Standard (TV + okno Chrome), a nie błąd DRM | ✅ |

`cmd drm` po odtworzeniu (`serve --mode raw`, `requestMediaKeySystemAccess` z `avc1.640028`): Chrome: `com.widevine.alpha` ok, `playready.recommendation` NotSupported, `playready.recommendation.3000` ok. Edge: wszystkie trzy ok. `webdriver=false` w obu. Mimo dostępnego PlayReady 3000 Netflix w Chrome wybiera Widevine L3 (S1).

Logi: `logs/*-S4-chrome-fresh.jsonl`, `logs/*-S4-edge-fresh.jsonl` (`--watch-cdm`), `logs/*-S4-drm-chrome.jsonl`, `logs/*-S4-drm-edge.jsonl`. Porty: chrome-fresh1 9226, edge-fresh1 9225, żeby nie kolidować z profilami S1. · Uwagi: w Chrome 155 i Edge 155 nowy profil nie wymaga pobierania Widevine, bo CDM jest w katalogu instalacji. Component updater nie zapisał nic do profilu przez cały test. Ograniczenie środowiska: plan Standard daje 2 równoczesne strumienie, a domowy TV zajmuje jeden, więc w spike'u gra naraz tylko jedno okno przeglądarki.

## S2: klient CDP, raw kontra connectOverCDP

**Cel:** podłączony klient CDP nie zmienia jakości ani błędów względem S1, a `navigator.webdriver === false`.

**Kroki** (najpierw Edge, potem Chrome):

1. (a) `node harness/ms.mjs launch --browser edge`, potem w tle `node harness/ms.mjs serve --mode raw --browser edge --label S2a-edge`.
2. `cmd webdriver`. Ta sama wartość jest w zdarzeniu `probe-loaded` w logu.
3. Odtwórz ten sam odcinek. Po około 60 s sprawdź nakładkę (`cmd "key Ctrl+Alt+Shift+D"` albo fizycznie) i zdarzenia `video-sample` w logu. Pooglądaj kilka minut i obserwuj `page-error` oraz `video` z `ev=error`.
4. `cmd quit`, potem **zamknij przeglądarkę**: Playwright w (b) woła `Runtime.enable`, więc pomiary nie mogą dzielić sesji.
5. (b) Ponownie `launch`, potem `serve --mode pw --browser edge --label S2b-edge` i powtórz kroki 2–4.

**Kryterium zaliczenia:** jakość i błędy takie jak w S1; `navigator.webdriver === false` w obu wariantach.

| Przeglądarka / wariant | webdriver | Rozdzielczość | Bitrate | Błędy, różnice względem S1 | Werdykt |
|---|---|---|---|---|---|
| Edge (a) raw | `false` (`cmd webdriver` i `probe-loaded`) | 1920x1080 (nakładka i `video-sample`) | 192 / 7139 kbps | brak; identycznie jak S1 (PlayReady 3000, KeyStatus 1080 usable, 0 zgubionych klatek na 5637). ~4,5 min bez `page-error` i bez `video` `error`/`waiting`/`stalled`. Nakładka otwarta zaufanym `cmd "key Ctrl+Alt+Shift+D"` | ✅ |
| Edge (b) connectOverCDP | `false` | 1920x1080 | 192 / 7139 kbps | brak; identycznie jak S1 i (a): PlayReady 3000, 0 zgubionych klatek na 5640, ~4 min bez błędów. `Runtime.enable` (Playwright) nie zmienia niczego widocznego | ✅ |
| Chrome (a) raw | `false` | 1920x1080 | 192 / 842 kbps (AV1) | brak; identycznie jak S1: AV1 `av01.0.04M.08`, Widevine `SW_SECURE_DECODE`, 1 zgubiona klatka na 6022, ~4 min bez błędów | ✅ |
| Chrome (b) connectOverCDP | `false` | 1920x1080 | 192 / 842 kbps (AV1) | brak; identycznie jak S1 i (a), 0 zgubionych klatek na 4920, ~4 min bez błędów | ✅ |

Logi: `logs/20261012-222137-S2a-edge-v2.jsonl`, `logs/20261012-222550-S2b-edge.jsonl`, `logs/20261012-223004-S2a-chrome.jsonl`, `logs/20261012-223648-S2b-chrome.jsonl`; nakładki `logs/20261012-222246-S2a-edge-overlay.png`, `logs/20261012-222656-S2b-edge-overlay.png`, `logs/20261012-223356-S2a-chrome-overlay.png`, `logs/20261012-223757-S2b-chrome-overlay.png`. Między (a) i (b) przeglądarka była zamykana. · Uwagi:
- Pierwszy przebieg Edge (a) ujawnił błąd harnessu: po nawigacji binding znikał i log nie miał zdarzeń (sekcja „Poprawki harnessu”). Pomiar powtórzony po poprawce (`S2a-edge-v2`).
- Zrzut `Page.captureScreenshot` w Edge ma czarne wideo (PlayReady SL3000 ze sprzętową ścieżką), a w Chrome widać obraz (Widevine L3, dekodowanie programowe). Nakładka jest czytelna w obu.
- **Limit ekranów:** zamknięcie przeglądarki na `/watch` (WM_CLOSE) zostawia slot strumienia zajęty przez około 1–2 min. Kolejne okno dostaje „Too many people are using your account right now” (`h1[data-uia="title"]`, przyciski `[data-uia="retryButton"]` i `[data-uia="upgradeButton"]`). `ERROR_SEL` w probe tego ekranu nie łapie (`error: null`). Przejście na `/browse` przed zamknięciem zwalnia slot od razu: kolejne okno grało bez komunikatu.

## S3: kontrola negatywna, Playwright launch

**Cel:** pomiar ryzyka G2. Czy Netflix reaguje na przeglądarkę z `webdriver=true` (pipe)?

**Kroki:**

1. W tle `node harness/ms.mjs serve --mode pwlaunch --browser edge --label S3-edge-stripped`. Przy pierwszym uruchomieniu zaloguj się w profilu `edge-pw`.
2. `cmd webdriver` powinno dać `true`. Odtwórz odcinek, sprawdź nakładkę i błędy, a potem `cmd quit`, co zamyka też przeglądarkę.
3. `serve --mode pwlaunch --browser edge --pw-defaults --label S3-edge-defaults` i powtórz krok 2. Spodziewany efekt: brak aktualizacji CDM lub gorsza jakość przez flagi.

**Kryterium:** to pomiar, a nie test zaliczany. Czy Netflix gra; czy jakość i błędy różnią się od S1.

| Wariant | webdriver | Infobar | Gra? | Rozdzielczość | Błędy, ostrzeżenia Netfliksa |
|---|---|---|---|---|---|
| ignoreDefaultArgs (stripped) | `true` | „Używasz nieobsługiwanej flagi wiersza polecenia: --no-sandbox…” (Playwright dodaje `--no-sandbox`); brak paska „kontrolowany przez zautomatyzowane oprogramowanie” | ✅ | 1920x1080, 192 / 7139 kbps, H.264, PlayReady 3000, 0 zgubionych klatek | brak; logowanie z 2FA i wybór profilu bez captchy i bez dodatkowej weryfikacji; ~5 min bez błędów |
| domyślne flagi Playwrighta | `true` | ten sam pasek `--no-sandbox` (zrzut `logs/20261012-230156-S3-edge-defaults-screen.png`); paska automatyzacji nadal brak, choć `--enable-automation` jest w domyślnych flagach | ✅ | 1920x1080, 192 / 7139 kbps, H.264, PlayReady 3000, 0 zgubionych klatek | brak; ~3 min bez błędów. `--disable-component-update` niczego nie psuje, bo CDM jest w katalogu instalacji (S4) |

Logi: `logs/20261012-224050-S3-edge-stripped.jsonl`, `logs/20261012-230045-S3-edge-defaults.jsonl`; nakładki `logs/20261012-225846-S3-edge-stripped-overlay.png`, `logs/20261012-230156-S3-edge-defaults-overlay.png`. Player Netflixa w profilu `edge-pw` ma build 6.0063.710.911 (w `edge-netflix` 6.0063.845.911) i inny ESN, co wynika z innego profilu i prawdopodobnie z testów A/B, a nie z wykrycia automatyzacji.

Wniosek dla G2: na dziś Netflix nie reaguje na `navigator.webdriver=true` ani na flagi Playwrighta: jakość i DRM są takie jak w S1. Ryzyko G2 jest niskie, ale B i tak nie potrzebuje Playwrighta (pasek `--no-sandbox` i `webdriver=true` to zbędne sygnały), więc rekomendacja „raw CDP bez flag automatyzacji” zostaje jako margines bezpieczeństwa.

## S6: zaufane i niezaufane wejście, pełna weryfikacja B kontra C

**Cel:** ustalić, czy adapter MediaStation zrealizuje całą obsługę Netflixa pilotem w B (CDP) i w C (rozszerzenie).

**Model, który weryfikujemy.** Strzałki, OK i Back z G20S trafiają do przeglądarki na pierwszym planie jako **fizyczne, zaufane** klawisze, niezależnie od wariantu. Wejścia syntetycznego potrzebujemy tylko wtedy, gdy adapter działa sam: przesuwa fokus, aktywuje element, wpisuje tekst albo wysyła klawisz, który MediaStation przechwyciła. Zdarzenia z `dispatchEvent` (C) **nigdy** nie uruchamiają domyślnych akcji przeglądarki (Tab, spatial navigation, wpisywanie). Dlatego C musi je zastąpić innymi mechanizmami: `el.focus()`, `el.click()` i `execCommand`. Część A mierzy klawisze, a część B mierzy te zamienniki.

Wszystko mierzymy w B (`serve --mode raw --browser edge --label S6-edge`), bo raw ma oba rodzaje wejścia. C1–C3 potwierdzają wyniki w prawdziwym rozszerzeniu. W logu `key` pokazuje `trusted`, `key-after` pokazuje `prevented` (czy Netflix obsłużył zdarzenie), a `adapter` pokazuje akcję adaptera i użyty `fallback`.

### Część A1: handlery klawiszy Netfliksa

**Kroki:** dla każdego wiersza najpierw **fizyczny** klawisz (baseline z `trusted=true`, agent czyta `cmd "wait key 30"`), potem `cmd "key X"` (CDP), potem `cmd "jskey X"` (JS).

1. Player (`/watch`): `Space`, `ArrowRight`, `ArrowLeft`, `f`, `m`, `Escape`.
2. `/browse` z otwartym modalem szczegółów: `Escape`.
3. `/browse`: po ustawieniu fokusu na kafelek fizycznym Tab wykonaj `Enter`. Dla `jskey Enter` spodziewany wynik negatywny: to domyślna akcja przeglądarki, a nie handler Netfliksa.

| Ekran | Klawisz | Fizyczny | `key` (CDP) | `jskey` (JS) | `prevented` CDP / JS |
|---|---|---|---|---|---|
| player | Space | ✅ pauza/wznowienie (gdy odcinek gra); w stanie „zablokowane autoodtwarzanie” nic | ✅ przełącza | ✅ przełącza | brak `key-after` w obu (Netflix zatrzymuje propagację, czyli obsługuje) |
| player | ArrowRight | ✅ przewija do przodu | ✅ przewija, fokus przechodzi na `controls-standard` | ✅ przewija | brak `key-after` (obsłużone) |
| player | ArrowLeft | ✅ przewija do tyłu | ✅ | ✅ | brak `key-after` (obsłużone) |
| player | f | ✅ pełny ekran wł./wył. | ✅ wł./wył. | ❌ brak pełnego ekranu (`requestFullscreen` wymaga aktywacji użytkownika) | brak `key-after` |
| player | m | ✅ wycisz/odcisz | ✅ | ✅ | `true` / `true` |
| player | Escape | — (poza pełnym ekranem nie ma czego zamykać) | ✅ wychodzi z pełnego ekranu | ❌ nie wychodzi z pełnego ekranu (to domyślna akcja przeglądarki) | `false` / `false` (Netflix nie obsługuje Escape na playerze) |
| player | Enter (OK pilota) | ✅ OK pilota = `Enter` (keyCode 13): pauza/wznowienie | ✅ przełącza | ✅ przełącza | — |
| modal | Escape | ✅ zamyka modal (klawiatura) | ✅ zamyka modal | ✅ zamyka modal | `false` / `false` (zamyka bez `preventDefault`) |
| browse | Enter | ✅ Tab ustawia fokus na kafelku (biała ramka), Enter otwiera modal (`prevented=true`) | ✅ otwiera modal (`jbv`) dla kafelka z fokusem | ✅ **też otwiera modal**: Netflix ma własny handler Enter na kafelkach (wbrew oczekiwaniu) | `true` / `true` |

Klawisze fizyczne: klawiatura daje `trusted=true`. Pilot (dongle): OK → `Enter`, strzałki → `ArrowLeft/Right`, Back → `BrowserBack` (keyCode 166, `prevented=false`; na `/watch` przy `history.length=1` nic nie robi). Pomiar efektu: próbnik stanu `<video>` (`helpers/player-poll.mjs`) i `helpers/s6-seq.mjs` (stan przed i po, 6 s przerwy, żeby aktywacja użytkownika nie przechodziła między krokami). Wyniki: `logs/S6-A1-physical-poll.log`, `logs/S6-A1-physical-poll2.log`, `logs/S6-A1-synthetic.txt`, log `logs/20261012-230341-S6-edge.jsonl`.

**Autoodtwarzanie:** po `launch` i `nav` na `/watch` bez gestu użytkownika Edge blokuje autoodtwarzanie, a Netflix pokazuje duży przycisk Play. Space, OK, `key Space` ani `jskey Space` nie wychodzą z tego stanu, choć strzałki, `f` i `m` działają. Odblokowuje go `play api` (lub `play video`). Wniosek: MediaStation po starcie odcinka powinna sprawdzić `video.paused` i w razie potrzeby wywołać API playera.

Zamienniki dla playera bez klawiszy: `cmd "pause api"`, `cmd "play api"`, `cmd "pause video"`, `cmd "play video"`. Działają? ✅ Wszystkie cztery; `play api` startuje też z zablokowanego autoodtwarzania.

### Część A2: fizyczne strzałki i OK z pilota, z flagą spatial navigation i bez niej

Krótki przegląd pilotem (dongle). Pełna macierz ekranów jest w **S5** i nie trzeba jej tu powtarzać. A2 ustala tylko, czy strzałki i OK z pilota docierają jako zwykłe klawisze (log `key`: `key`, `code`, `trusted=true`) i co robią na `/browse` oraz w modalu w obu konfiguracjach przeglądarki.

1. **Bez flagi:** przeglądarka z `launch --browser edge` (bez `--spatial`), `serve --mode raw`. Na `/browse` i w modalu naciśnij pilotem ↑ ↓ ← → i OK.
2. **Z flagą:** `cmd quit`, zamknij przeglądarkę, `launch --browser edge --spatial`, ponownie `serve --mode raw`, a potem te same naciśnięcia.

| Konfiguracja | Ekran | Strzałki pilota: co się dzieje (fokus z logu `focus`) | OK pilota: co się dzieje | `key`/`code` z logu |
|---|---|---|---|---|
| bez `--enable-spatial-navigation` | `/browse` | ↑/↓ lekko przewijają stronę, ←/→ nic (`prevented=false`, cel `body`, brak zdarzeń `focus`) | nic (fokus na `body`) | `ArrowDown`/`ArrowRight`/`Enter`, keyCode 40/39/13, `trusted=true` |
| bez `--enable-spatial-navigation` | modal | ↑/↓ nic widocznego, fokus zostaje na kafelku w tle (`standard-card` „The Walking Dead”) | nie sprawdzano (OK na kafelku w tle otworzyłoby modal ponownie) | `ArrowDown`/`ArrowUp`, `trusted=true` |
| z `--enable-spatial-navigation` | `/browse` | → S5 (pełna macierz): fokus chodzi po kafelkach, ale wpada w podgląd kafelka | aktywuje element z fokusem | `ArrowDown`…, `Enter`, `trusted=true` |
| z `--enable-spatial-navigation` | modal | → S5: działa najlepiej z ekranów, ale „nie idealnie” | aktywuje | jw. |

### Część B: zamienniki, z których korzystałby C

**Kroki** (bez `--spatial`):

1. **Programowy fokus:** na każdym ekranie `cmd "focus down"`, `cmd "focus right"` itd. Czy Netflix pokazuje fokus (obwódkę lub powiększenie kafelka) i czy fizyczny Enter aktywuje element z fokusem? Na końcu wiersza zanotuj pole `fallback` z wyniku: `dom-order` (następny kafelek w DOM ze `scrollIntoView`), `row-arrow` (klik strzałki przewijania wiersza; pole `arrow` pokazuje dopasowany selektor) albo `none-found`.
2. **Niezaufany klik:** po `focus …` wykonaj `cmd activate`. Czy Netflix reaguje tak jak na fizyczny Enter lub kliknięcie myszą?
3. **Tekst:** fokus w polu wyszukiwania (`focus …` albo fizycznie), potem po kolei: `cmd "type abc"` (CDP, zaufane), `cmd "jstype exec abc"`, `cmd "jstype value abc"`. Między próbami wyczyść pole. Czy pojawia się `/search?q=` i wyniki (log `route`)?
4. **End-to-end z pilotem:** `cmd "adapter on"`, a potem nawigacja **wyłącznie pilotem**: wybór profilu, wiersze (także poza prawą krawędź), otwarcie modala, Play, wyszukiwanie (pole, wyniki). Na `/watch` adapter wyłącza się sam.

| Mechanizm | Profile | `/browse` kafelki | Koniec wiersza (`fallback`) | Modal: Play i lista odcinków | `/search` wyniki | Player: kontrolki |
|---|---|---|---|---|---|---|
| `focus …` (widoczny fokus?) | ✅ kafelki `profile-selector+tile-*`; obwódka tylko po zaufanym klawiszu (patrz niżej) | ✅ `standard-card`, `progress-card`, billboard `play-video-button`, menu `nav-*` | ✅ `row-arrow` przez `[data-uia="carousel-hawkins-right-button"]`, wiersz `[data-uia^="carousel-row-section"]`; w wierszu z jednym kafelkiem poprawnie „brak” | ✅ po zawężeniu do modala: Play, `16+`, odcinki `titleCard--container` (przewijanie przez `offscreen-geometric`) | ✅ siatka `standard-card` (góra/dół/lewo/prawo) | ❌ kontrolek nie ma w DOM, dopóki są ukryte; player obsługujemy klawiszami lub API |
| `focus` + fizyczny Enter | ✅ zaufany Enter (CDP, ta sama ścieżka wejścia co klawisz fizyczny) i OK pilota przez adapter wybierają profil | ✅ zaufany Enter po programowym fokusie otwiera modal (`progress-card` „1670”); fizyczny Enter po Tab też | — | ✅ OK na „Next Episode” startuje odcinek | ✅ OK otwiera modal | — |
| `activate` (untrusted click) | ✅ wybiera profil | ✅ otwiera modal | — | ✅ odcinek startuje (`/watch/80021960`) | ✅ otwiera modal (`/search?jbv=…`) | — |
| `adapter on` z pilotem | ✅ ↓ logo → ↓ „Filip” → OK | ✅ wiersze ↓, kafelki →, OK otwiera modal | ✅ `row-arrow` po „Arcane” (krawędź), dalej „Snowpiercer” (fallback pominął dwa kafelki ze strony docelowej) | ⚠️ działa, ale geometryczny fokus prowadzi przez linki obsady i gatunków; „Next Episode” (`play-button`) osiągalny tylko w lewo od rzędu przycisków; OK na aktorze przeniosło na `/browse/person/18820`; obwódka słabo widoczna, a miniatury odcinków pokazują tylko ikonę Play | ✅ z pola ↓ do menu i siatki; ramka dobrze widoczna; OK otwiera modal, Back zamyka; wielokrotnie bez problemu | — (na `/watch` adapter wyłączony) |

**Widoczność programowego fokusu:** Netflix rysuje obwódkę przez `:focus-visible`. Po `el.focus()` Chromium ustawia `:focus-visible` tylko wtedy, gdy ostatnim wejściem był **zaufany** klawisz. Po `jskey` (niezaufany) obwódki nie ma, a `focus({focusVisible: true})` w Edge 155 nie działa. Po `key ArrowDown` (CDP) lub fizycznym klawiszu obwódka jest widoczna (zrzuty `logs/20261012-231904-S6B-profiles-focus-Filip.png` bez obwódki i `logs/20261012-231929-S6B-profiles-focus-after-trusted-key.png` z obwódką). W C adapter reaguje na fizyczne (zaufane) klawisze pilota, więc obwódka powinna być widoczna. Potwierdza to krok `adapter on`.

**Modal:** po otwarciu modala fokus zostaje na kafelku w tle, a tło nie jest `inert`. Mapa fokusu musi mieć zakres „modal”, gdy istnieje `[role="dialog"].previewModal--container`. Bezpośrednie wejście na `/browse?jbv=ID` przekierowuje na `/title/ID` z tym samym modalem.

| Tekst w polu wyszukiwania | Wpisuje się? | Netflix szuka (`/search?q=`)? |
|---|---|---|
| `type` (CDP `Input.insertText`, trusted) | ✅ | ✅ `/search?q=abc`, 48 kafelków |
| `jstype exec` (`execCommand`) | ✅ | ✅ `/search?q=breaking` |
| `jstype value` (setter + `input`) | ✅ | ✅ `/search?q=dexter` |

Pole wyszukiwania: ikona `[data-uia="navigation+actions+search+icon"]` (`button`; `activate` otwiera pole i ustawia na nim fokus), pole `[data-uia="navigation+actions+search+input"]`. Wyczyszczenie pola wraca na `/browse`.

Dopasowane selektory wiersza i strzałek (z pól `row` i `arrow`): wiersz `[data-uia^="carousel-row-section"]` (np. `carousel-row-section-2` = „Continue Watching for Filip”), scroller `[data-uia="carousel-scroller"]` (`overflow-x: clip`), strzałki `button[data-uia="carousel-hawkins-right-button"]` („See more titles”, `tabindex=0`) i `...-left-button` („See previous titles”). **Żaden z pierwotnych kandydatów (`.lolomoRow`, `.rowContainer`, `.slider`, `.handleNext`, `aria-label*=next`) nie pasuje do obecnego UI Netflixa.** Kafelki: `a[data-uia="standard-card"]` i `a[data-uia="progress-card"]` (`aria-label` = tytuł), odcinki w modalu `[data-uia="titleCard--container"]`. Logi: `logs/S6B-browse-focus.txt`, `logs/S6B-modal-focus.txt`, `logs/S6B-search-focus.txt`, `logs/S6B-player-focus.txt`, `logs/20261012-232338-S6-edge-e.jsonl`.

### Kryteria S6

- **B zaliczone:** w części A1 `key` działa jak klawisz fizyczny, a `type` wpisuje i wyszukuje.
- **C realne:** w części B działają `focus` (widoczny fokus, także poza ekranem przez `fallback`), `activate` oraz `jstype exec` lub `jstype value`, a adapter z pilotem przechodzi cały wieczór. Player w C da się obsłużyć przez `pause api`/`play api` albo przez `jskey`.
- Jeśli w części B `activate` nie działa, ale fizyczny Enter na elemencie z fokusem działa, C nadal jest realne: adapter przesuwa tylko fokus, a OK z pilota przechodzi do przeglądarki bez zmian.

Werdykt B: ✅ zaliczone. `key` (CDP) działa jak klawisz fizyczny we wszystkich wierszach A1, w tym `f` (pełny ekran) i Escape (wyjście z pełnego ekranu), a `type` wpisuje i wyszukuje. · Werdykt C: ✅ realne, z zastrzeżeniami. `focus` działa po dopasowaniu selektorów do nowego UI i zawężeniu do modala, także poza ekranem (`row-arrow`, `offscreen-geometric`). `activate` i oba `jstype` działają, adapter z pilotem przeszedł profil, wiersze, modal, odcinek i wyszukiwanie. Player przez `jskey` (Space, Enter, strzałki, `m`) i `pause api`/`play api`. Nie działa `jskey f` ani niezaufany Escape w pełnym ekranie, więc w C pełny ekran trzeba robić przez `chrome.windows.update({state: "fullscreen"})`. Zastrzeżenie UX dotyczy obu wariantów: geometryczna nawigacja w modalu jest uciążliwa, a obwódka Netflixa słabo widoczna, więc potrzebna jest mapa fokusu per ekran i własne podświetlenie. · Logi: `logs/20261012-230341-S6-edge.jsonl` (A1 player), `logs/20261012-232338-S6-edge-e.jsonl` (A1 modal i browse, A2, B, adapter), pliki `logs/S6-*.txt` i `logs/S6B-*.txt`, zrzuty `logs/*S6B-*.png`. Fallback `focus` poza ekranem: `row-arrow` (wiersze) i `offscreen-geometric` (pionowo, w modalu i na `/browse`); `dom-order` nie wystąpił.

## Bramka po S2 i S6: rekomendacja B czy C

Sprawdź po S6, przed S5 i S7. Bramka decyduje o **rekomendacji** i o tym, na którym transporcie robimy S5 i S7. Nie decyduje o tym, czy testujemy C: C1–C3 wykonujemy zawsze po sesji 1, jeśli starczy czasu.

| Warunek | Spełniony? |
|---|---|
| S2: z podłączonym klientem raw Netflix gra gorzej, pokazuje błąd albo `webdriver` jest `true` | ❌ nie: jakość i DRM jak w S1, `webdriver=false` |
| S2: problem występuje tylko w (b) `connectOverCDP` | ❌ nie: (b) też bez różnic |
| S6: B niezaliczone (`key` nie działa jak klawisz fizyczny albo `type` nie wpisuje) | ❌ nie: B zaliczone |
| S6: C realne (kryteria wyżej) | ✅ tak, z zastrzeżeniami (pełny ekran przez API okna, UX fokusu) |

- **Ani S2 (raw), ani S6-B nie wypadły źle:** rekomendacja B. S5 i S7 na B (raw), potem C1–C3 jako porównanie z runner-upem.
- **S2 (raw) albo S6-B wypadły źle i C jest realne:** rekomendacja C. S5 i S7 robimy od razu w wariancie C (`serve --mode ext`), a C1–C3 mają priorytet przed końcem sesji.
- **S2 albo S6-B wypadły źle i C nie jest realne:** żaden kandydat nie spełnia wymagań. Dokończ C1–C3 dla pełnego obrazu i otwórz decyzję z researchu ponownie.

Decyzja bramki: **rekomendacja B** (raw CDP). S5 i S7 na B, potem C1–C3 jako porównanie. · Uzasadnienie: ani S2 (raw i connectOverCDP), ani S6-B nie wypadły źle. Netflix nie reaguje na port debugowania, a zaufane wejście CDP zachowuje się jak fizyczne. C pozostaje realnym runner-upem, ale ma słabsze narzędzia: brak zaufanego wejścia, a pełny ekran i widoczność fokusu wymagają obejść. Niezależnie od wariantu wyszło, że Netflix sam obsługuje niezaufane klawisze (Space, Enter na kafelkach, Escape w modalu), więc różnica B/C jest mniejsza, niż zakładał research.

## S5: `--enable-spatial-navigation`

**Cel:** które ekrany Netfliksa obsługują strzałki „za darmo”, a które wymagają mapy fokusu.

**Kroki** (osobno Chrome i Edge):

1. `node harness/ms.mjs launch --browser chrome --spatial --label S5-chrome-launch`, potem w tle `serve --mode raw --browser chrome --label S5-chrome`. Serve jest tu tylko po to, żeby logować `focus`.
2. Na każdym ekranie nawiguj **strzałkami pilota** (dongle) i klawiatury, OK lub Enter wybiera. Dla porównania użyj też `cmd "key ArrowDown"` itd. Log `focus` pokaże element z `data-uia` i `aria-label`, co posłuży jako materiał do mapy fokusu. Nie włączaj `adapter on`.

**Kryterium:** dla każdego ekranu wiadomo „działa”, „częściowo” (co nie działa) albo „nie działa”.

| Ekran | Chrome | Edge | Co nie działa, przydatne selektory z logu |
|---|---|---|---|
| wybór profilu | ✅ jak Edge | ✅ działa (↓ do kafelka, OK wybiera) | `profile-selector+tile-*` |
| `/browse`: wiersze i kafelki | ❌ jak Edge: podgląd kafelka przejmuje fokus i więzi go (`previewModal--player_container`, `expand-to-detail-button`, `remove-in-bob-button`) | ❌ w praktyce nie działa: fokus na kafelku po chwili otwiera podgląd (mini‑modal z Play, My List, oceną), który przejmuje fokus (`previewModal--player_container` 120×, `add-to-my-list`, `thumbs-rate-button`, `play-button "Resume"`, `expand-to-detail-button`, `remove-in-bob-button`). Ocena rozwija kolejne przyciski (`thumbs-down/up/wayup-button`). Z podglądu nie da się wyjść strzałkami ani Back (`BrowserBack` cofa historię przeglądarki) | pułapka fokusu w podglądzie kafelka (`bob` = „billboard on browse”); potrzebny adapter, który nie zatrzymuje się na kafelku lub zamyka podgląd |
| `/browse`: menu górne | ⚠️ jak Edge | ⚠️ częściowo: ↑ dochodzi do menu (`nav-*`, `nav-audioSubtitles`), ale droga w górę prowadzi przez podglądy kafelków | `nav-home`, `nav-series`, …, `navigation+actions+search+icon` |
| modal szczegółów (`jbv`) | ⚠️ jak Edge | ⚠️ częściowo: najmniej problemów, ale fokus wchodzi w linki obsady i oceny (`thumbs-*`), a także w `previewModal--player_container` | `play-button`, `titleCard--container`, `thumbs-rate-button` |
| `/search`: pole i wyniki | nie sprawdzano (skrócony przebieg; ten sam silnik) | ⚠️ częściowo: wpisywanie i ↓ do wyników działają, ale te same pułapki podglądu i oceny (`thumbs-down-button` 45×); Back cofa historię i zamyka modal | `navigation+actions+search+submit` („Clear input”), `standard-card` |
| player: kontrolki | ❌ jak Edge: OK pauzuje i od razu wznawia (5 par `pause`→`play` < 400 ms na 10 × Enter) | ❌ ←/→ przewijają odcinek (handler Netflixa, kontrolki nie dostają fokusu). **OK przełącza dwa razy:** `pause` i po 5–300 ms `play` (31 × Enter, log `video`), bo po handlerze Netflixa spatial navigation dodatkowo „klika” element z fokusem. Bez flagi (S6) OK przełączało raz | flaga psuje OK w playerze |

Czy strzałki z pilota docierają jako zwykłe strzałki? ✅ Tak: `ArrowUp/Down/Left/Right`, `Enter`, `trusted=true`; Back to `BrowserBack`. · Logi: Edge `logs/20261012-234109-S5-edge.jsonl`, Chrome `logs/20261012-235144-S5-chrome.jsonl` (`launch --spatial`, `serve --mode raw`, bez adaptera).

**Wniosek S5:** `--enable-spatial-navigation` nie daje niczego „za darmo” poza ekranem profili. Na `/browse` i w wyszukiwarce fokus wpada w podgląd kafelka, a w playerze psuje OK (podwójne przełączenie). MediaStation **nie powinna** używać tej flagi. Potrzebny jest własny adapter z mapą fokusu per ekran (profile, wiersze, menu, wyszukiwarka, modal), który przechwytuje strzałki i OK (jak `adapter on` w S6, gdzie podgląd nie przejmował fokusu), a na `/watch` przepuszcza klawisze do Netflixa.

## S7: Back, reguła „cofnij lub wyjdź”

**Cel:** deterministyczna reguła, kiedy Back cofa w serwisie, a kiedy wychodzi do MediaStation.

**Kroki** (`serve --mode raw --browser edge --label S7-edge`): na każdym ekranie wypróbuj `cmd "back esc"`, `cmd "back history"` i `cmd "back modal"`. Wynik każdej komendy zawiera stan przed (`before`) i po (`after`). Wróć do ekranu startowego przed kolejną metodą. Sprawdź też, czy `cmd state` poprawnie rozpoznaje `profiles` i `browse` (`route.name`, `profileSel`, `modalSel`). Fizyczny Back pilota: zanotuj, jaki klawisz przychodzi (log `key`).

**Kryterium:** reguła daje ten sam wynik przy każdym powtórzeniu.

| Ekran startowy | `back esc` → | `back history` → | `back modal` → | Pożądane zachowanie |
|---|---|---|---|---|
| `/browse?jbv=…` (modal) | ✅ `/browse` (modal zamknięty) | ✅ `/browse` (jbv to wpis historii) | ✅ `/browse` (klik `[data-uia="previewModal-closebtn"]`) | zamknij modal |
| `/title/…` | ✅ `/browse` (Escape zamyka modal strony tytułu) | ⚠️ poprzedni wpis historii (tu `/browse`, bo stamtąd weszliśmy) | ✅ `/browse` | `/browse` |
| `/search?q=…` | ❌ bez zmian (fokus poza polem) | ⚠️ poprzedni wpis historii (tu `/browse`) | ❌ bez zmian (brak modala) | `/browse` |
| `/watch/…` | ❌ bez zmian (poza pełnym ekranem Escape nic nie robi, S6) | ✅ strona, z której weszliśmy (`/browse`; w rundzie adaptera S6: modal) | ❌ bez zmian | strona, z której weszliśmy |
| `/browse` | ❌ bez zmian | ❌ poprzedni wpis historii (tu `/title/80021955`), czyli nie „wyjście” | ❌ bez zmian | wyjdź do MediaStation |
| wybór profilu | ❌ bez zmian | ❌ bez zmian (bramka zostaje) | ❌ bez zmian | wyjdź do MediaStation |

Dwa pełne przebiegi (`logs/S7-edge.txt`, `logs/S7-edge-run2.txt`, skrypt `helpers/s7.mjs`) dały **identyczne** wyniki, o ile przed każdym ekranem wybrany jest profil. Bez tego po wizycie na `/ProfilesGate` każde `/browse` pokazuje „Who's watching?”, dopóki profil nie zostanie wybrany (zepsuty pierwszy drugi przebieg). Ten sam ekran wraca po wybudzeniu UM760 z Modern Standby.

**Reguła:**

```text
jeśli route == watch              -> history.back() (wraca do modala lub /browse, skąd weszliśmy);
                                     gdy history.length == 1: nav /browse
jeśli route == browse-modal       -> zamknij modal: klik [data-uia="previewModal-closebtn"] (albo Escape)
jeśli route == title              -> to samo co modal (closebtn/Escape -> /browse)
jeśli route == search             -> nav /browse (history.back() zależy od historii, Escape nic nie robi)
jeśli route in (browse, profiles) -> wyjdź do MediaStation
```

**Back pilota:** przychodzi jako `BrowserBack` (keyCode 166, `code` puste, `trusted=true`). Edge sam wykonuje wtedy `history.back()`. **Strona nie może tego zablokować:** z nasłuchem `keydown` w fazie capture i `preventDefault()` fizyczny Back i tak przeszedł z `/browse` na `/title/80021955` (`logs/S7-physical-back.txt`). Z kolei `key BrowserBack` przez CDP w ogóle nie cofa historii (CDP nie odtwarza skrótu przeglądarki). Wniosek: MediaStation musi przechwycić Back pilota **przed przeglądarką** (hook Windows, jak Home w S10, z połykaniem), a potem sama zastosować regułę. Inaczej Back na `/browse` cofa się po historii Netflixa zamiast wyjść. Obejście częściowe: uruchamiać serwis tak, żeby `/browse` był pierwszym wpisem historii.

Wykrywanie `/browse` i ekranu profili działa? ✅ Po poprawce `PROFILE_SEL`: `profiles` (`profileSel = [data-uia="profile-gate-screen"]`), `browse`, `browse-modal` (`modalSel = .detail-modal`), `title`, `search`, `watch`. Uwaga: modal na `/search?jbv=…` probe raportuje jako `search` z `jbv`, a nie jako `browse-modal`. · Back pilota przychodzi jako: `BrowserBack`, keyCode 166. · Logi: `logs/20261012-235603-S7-edge.jsonl`, `logs/20261013-065932-S7-edge-b.jsonl`.

## Wariant C: rozszerzenie bez CDP (C1–C3)

Wykonujemy **zawsze po sesji 1, jeśli starczy czasu**, niezależnie od wyniku bramki. Bramka decyduje tylko o rekomendacji i o kolejności. Instalacja rozszerzenia: [README](README.md#ręczne-load-unpacked-do-testów-c1c3). Profile to `edge-ext` i `chrome-ext`, uruchamiane przez `launch --no-cdp`, bez portu debugowania. Serve: `serve --mode ext --label C…`.

Środowisko C: wersja rozszerzenia 0.1.1 (Load unpacked z `spikeextension`, ze wszystkimi poprawkami `probe.js` z S2 i S6) · `swStats` z linii „rozszerzenie połączone” (Edge): `received 116, queued 116, flushed 0, pending 116`, czyli zdarzenia sprzed startu serve czekały w kolejce service workera · Czy przeglądarka pokazuje ostrzeżenie o rozszerzeniu w trybie dewelopera lub infobar? **Nie**, ani Edge, ani Chrome (ocena użytkownika). Na stronie rozszerzeń widać tylko błędy `ERR_CONNECTION_REFUSED` z `background.js:10`, dopóki serve nie działa; to oczekiwane ponawianie połączenia.

### C1: jakość, DRM i ślad automatyzacji z rozszerzeniem

**Kroki:** `launch --browser edge --no-cdp`, w tle `serve --mode ext --label C1-edge`, potem `cmd state`. Odtwórz ten sam odcinek i po około 60 s sprawdź nakładkę **fizycznym** `Ctrl+Alt+Shift+D`, bo w C nie ma kombinacji klawiszy. Potem to samo w Chrome.

**Kryterium:** jakość i błędy takie jak w S1; `webdriver === false`; rozszerzenie dostaje zdarzenia `video`, `route` i `focus`.

| Przeglądarka | webdriver | Rozdzielczość | Bitrate | HDR (orientacyjnie) | Błędy, różnice względem S1 | Zdarzenia w logu? | Werdykt |
|---|---|---|---|---|---|---|---|
| Edge 155.0.4283.45 (`edge-ext`, bez portu CDP) | `false` | 1920x1080 | 192 / 7139 kbps | nakładka: `HDR support: true`, strumień AVC SDR (plan Standard) | brak; identycznie jak S1: PlayReady 3000, 0 zgubionych klatek na 1746. Autoodtwarzanie po `nav` (`chrome.tabs.update`) nie zostało zablokowane | ✅ `video`, `video-sample`, `route`, `focus`, `key`, `visibility`, `probe-loaded` | ✅ |
| Chrome 155.0.8059.40 (`chrome-ext`, bez portu CDP) | `false` | 1920x1080 | 192 / 842 kbps (AV1) | nakładka: `HDR support: false (media-capabilities)` | brak; identycznie jak S1: Widevine `SW_SECURE_DECODE`, 0 zgubionych klatek na 3117. **Różnica:** po `nav` na zimnym starcie autoodtwarzanie zablokowane, a `play video` daje `NotAllowedError: play() failed because the user didn't interact with the document first`; `play api`, `jskey Space/Enter` i fizyczne OK nie startują. Dopiero `play api` **po** fizycznym OK (aktywacja użytkownika) uruchamia odtwarzanie | ✅ jak w Edge (`play-rejected` też) | ✅ (z zastrzeżeniem autoodtwarzania) |

**Autoodtwarzanie (C1, dotyczy B i C):** na zimnym starcie przeglądarka blokuje start odcinka bez gestu użytkownika. W C nie ma zaufanego wejścia, więc MediaStation nie może tego obejść z poziomu strony. Fizyczne OK też nie pomaga, bo Netflix w tym stanie ignoruje Enter i Space; dopiero następne `play api` działa. Flaga uruchomienia `--autoplay-policy=no-user-gesture-required` rozwiązuje to całkowicie: Chrome z rozszerzeniem po `nav` na `/watch` gra od razu, bez `play-rejected` (`logs/20261013-073002-C-chrome-autoplay.jsonl`). Wniosek: MediaStation dodaje tę flagę w obu wariantach.

### C2: S6 w prawdziwym rozszerzeniu

**Kroki** (`serve --mode ext --label C2-edge`): powtórz kolumnę `jskey` z S6 A1 (w C `key` działa tak samo jak `jskey`) i całą część B, w tym `adapter on` z pilotem. Wyniki powinny pokrywać się z pomiarem przez raw. Zanotuj różnice.

| Element | Wynik w C | Zgodny z S6 (raw)? |
|---|---|---|
| player: `jskey Space` / `ArrowRight` / `Escape` | Space i Enter przełączają pauzę, ←/→ przewijają, `m` obsłużone; Escape niezaufany (bez pełnego ekranu nic). `key Ctrl+Alt+Shift+D` daje błąd (kombinacje niedostępne w C) | ✅ tak |
| player: `pause api` / `play api` | ✅ oraz `pause video` / `play video` | ✅ tak |
| `focus` + `activate` na `/browse` i w modalu, `fallback` na końcu wiersza | ✅ menu, billboard, `standard-card`, `progress-card`, nowy typ `episodic-card`; `row-arrow` na krawędzi wiersza; `activate` otwiera modal; fokus w modalu zawężony (closebtn, audio, linki); `back modal` zamyka. Fokus po menu: ↑ do `notification-bell`, ← do `navigation+actions+search+icon`, `activate` otwiera pole | ✅ tak |
| `jstype exec` / `jstype value` w wyszukiwarce | ✅ `/search?q=1670`, potem `…1670x` (`value`), `…1670xabc` (`type` = `execCommand` w C) | ✅ tak |
| `adapter on` z pilotem: cały wieczór | ⚠️ krótka runda (nie cały wieczór): fizyczne strzałki i OK (`trusted=true`) przechwycone przez adapter w świecie MAIN; wiersze, `row-arrow` (4×), OK otwiera modal (2×), w modalu dojście do `play-button` „Play Episode” po szukaniu (16 × fokus na elemencie bez opisu, `dropdown-toggle`, `thumbs-rate-button`, `add-to-remind-me`); Back pilota zamyka modal (`BrowserBack` → `history.back`). Widoczność fokusu jak w raw: część elementów nie ma ramki | ✅ tak, te same zalety i te same problemy UX co w S6 B |

### C3: Back i okno w wariancie C

**Kroki:** tabela S7 przez `serve --mode ext` (`back esc` wysyła tu niezaufany Escape), plus `cmd "win minimized"`, `cmd "win normal"` i `cmd front` przez `chrome.windows.update`. Opcjonalnie `cmd "s8 0.5 api minimized"`.

| Ekran startowy | `back esc` (untrusted) → | `back history` → | `back modal` → |
|---|---|---|---|
| `/browse?jbv=…` | ✅ `/browse` | ✅ `/browse` | ✅ `/browse` (closebtn) |
| `/title/…` | ✅ `/browse` (niezaufany Escape wystarcza) | ✅ `/browse` (po pełnym przeładowaniu, ~1,5 s) | ✅ `/browse` |
| `/search?q=…` | ❌ bez zmian | ✅ `/browse` (poprzedni wpis) | ❌ bez zmian |
| `/watch/…` | ❌ bez zmian | ✅ `/browse` (poprzedni wpis) | ❌ bez zmian |

`win`, `front` i `s8` działają? ✅ `win minimized` (strona `visibility: hidden`), `win normal`, `win fullscreen`, `win maximized`, `front` (`visible`); `s8 0.5 api minimized`: pauza, minimalizacja, przywrócenie po 30 s, `play api` wznawia bez błędu. Pełny ekran w C da się więc zrobić przez `chrome.windows.update`, mimo że `jskey f` nie działa. · Werdykt C: ✅ Back i okno działają w C tak samo jak w B; Back pilota (`BrowserBack`) i tak trzeba przechwycić w Windows (S7). · Logi: `logs/20261013-070935-C1-edge.jsonl` (C1–C3 Edge w jednym serve, kroki rozdzielone znacznikami `mark`), `logs/C2-edge-player.txt`, `logs/C2-edge-browse.txt`, `logs/C3-edge.txt`, zrzuty `logs/20261013-071103-C1-edge-overlay.png`, `logs/*-C2-edge-*.png`.

---

# Sesja 2: S10, S8, S9, S11

W wariancie C używaj w S8 `serve --mode ext` zamiast `--mode raw`. Testy S9–S11 nie zależą od transportu.

## S10: Home z G20S Pro (dongle USB 2.4G) i powrót MediaStation na wierzch

**Cel:** co dokładnie wysyła Home i czy proces, który go przechwycił, zawsze wraca na pierwszy plan.

**Ważne:** pilot działa przez **dongle USB 2.4G**. G20S Pro i Pro BT wysyłają różne kody, więc wynik obowiązuje tylko dla dongla.

**Kroki:**

1. Agent startuje logger w tle: `Start-Process powershell.exe -ArgumentList '-ExecutionPolicy','Bypass','-File','win\home-logger.ps1','-DurationSec','600','-LogFile','logs\S10-home-plain.log'`. Prosi o naciśnięcie Home przy oknie loggera na wierzchu i czyta log. Log **zawsze** zawiera wszystkie źródła naraz: `LL` (vk, scan, flags), `RAW-KBD` (klawiatura z `RIDEV_INPUTSINK`), `RAW-HID` (Consumer Control 0x0C; AC Home to zwykle usage 0x0223, często bez VK), `WM_APPCOMMAND`/`HSHELL_APPCOMMAND`. Każda linia ma `fg=nazwa:PID "tytuł"`. Przy pierwszym zdarzeniu z urządzenia log zawiera jego VID/PID i usage page. Jeśli żadna linia nie jest oznaczona `<== HOME`, agent uruchamia logger ponownie z właściwym `-HomeVk` lub `-HomeHidUsage`.
2. Przy okazji: Back, OK, strzałki i Menu pilota. Ich kody przydadzą się później.
3. Na pierwszym planie po kolei: Edge (z `launch`), Chrome, Moonlight w trakcie streamu i pulpit. Naciśnij Home i odczytaj `HOME +300ms: MediaStation-on-top=`.
4. Powtórz krok 3 z `-Foreground attach` i `-Foreground alt` dla tych aplikacji, w których `plain` zawiódł.
5. `-Swallow`: czy przeglądarka nadal reaguje na Home, np. otwiera stronę domową?
6. Zatrzymanie: zamknięcie okna, upływ `-DurationSec` albo `New-Item logs\home-logger.stop`.

**Kryterium zaliczenia:** Home zawsze przechwycony, a MediaStation zawsze na wierzchu (przynajmniej jedną metodą).

| Element | Wynik |
|---|---|
| Urządzenie dongla (VID/PID, usage page z linii `new raw input device`) | `VID_1915&PID_1025` (ver 0x100). Home i Back: `MI_03&Col02`, usage page **0x0C** (Consumer), usage 0x01. OK i strzałki: `MI_02`, zwykła klawiatura HID (type 81, 264 klawisze) |
| Home: LL hook (vk, scan, flags) | `vk=0xAC` (`VK_BROWSER_HOME`), scan 0x00, down `flags=0x11`, up `flags=0x91` (0x10 = **LLKHF_INJECTED**: Windows sam generuje ten klawisz z raportu HID). Uwaga: gdy proces z hookiem jest na wierzchu i obsługuje Raw Input, „down” w LL nie dochodzi (patrz niżej) |
| Home: Raw Input klawiatura (VK, make) | `vk=0xAC make=0x00`, `dev=(injected)` (brak uchwytu urządzenia, czyli nie przychodzi z klawiatury dongla) |
| Home: Raw Input HID Consumer (bajty raportu, usage) | **`02-23-02`** = report ID 2, usage **0x0223** (AC Home); zwolnienie `02-00-00`. To pierwsze i najwcześniejsze źródło (ok. 5–15 ms przed VK) |
| Home: WM_APPCOMMAND / HSHELL_APPCOMMAND (cmd) | `cmd=7` (`APPCOMMAND_BROWSER_HOME`), `device=0x0000` (FAPPCOMMAND_KEY). Nieobsłużony przez okno na wierzchu trafia do powłoki, a ta **otwiera domyślną przeglądarkę** (Edge, profil użytkownika, nowa karta) i oddaje jej pierwszy plan |
| Back / OK / strzałki / Menu: kody | Back: HID `02-24-02` (usage 0x0224, AC Back) → wstrzyknięty `VK_BROWSER_BACK 0xA6` → `WM_APPCOMMAND cmd=1`. OK: `vk=0x0D make=0x1C` z `MI_02`. Strzałki: ↑ `0x26/0x48`, ↓ `0x28/0x50`, ← `0x25/0x4B`, → `0x27/0x4D` z `MI_02`. Menu: pilot nie ma takiego przycisku |

**Przebieg rozstrzygający (2026-10-13, 20:37–20:39 UTC).** Skrypt `helpers/s10-run.ps1` przeszedł bez udziału człowieka w oknie rozmowy: 4 tryby loggera × 4 okna. Przed każdym naciśnięciem sam wysuwał okno (Alt + `SetForegroundWindow`) i dawał sygnał dźwiękowy; człowiek naciskał tylko Home. Moonlight w trybie **borderless** (`--display-mode borderless`). Wcześniejsze przebiegi (niżej) były zaburzone przełączaniem się człowieka do okna rozmowy i tym, że logger bywał na wierzchu w chwili naciśnięcia.

| Pierwszy plan | Home przechwycony? | bez `-Swallow` | `-Swallow` + `plain` | `-Swallow` + `attach` | `-Swallow` + `alt` | Przeglądarka sama zareagowała na Home? |
|---|---|---|---|---|---|---|
| Edge (Netflix) | ✅ 4/4 RAW-HID 0x0223, hook LL „down” 4/4 | ❌ `SetForegroundWindow=True`, ale `WM_APPCOMMAND` 7 → powłoka otwiera nową kartę **domyślnego** Edge (inny proces), która przykrywa logger | ✅ logger na wierzchu | ✅ | ✅ | bez `-Swallow`: tak (przez powłokę); z `-Swallow`: nie |
| Chrome (Netflix) | ✅ | ❌ `SetForegroundWindow=False`; **Chrome sam** obsłużył `VK_BROWSER_HOME` i otworzył nową kartę we własnym oknie | ❌ `SetForegroundWindow=False`, Chrome zostaje na wierzchu | ✅ | ✅ | bez `-Swallow`: tak (sam Chrome); z `-Swallow`: nie |
| Moonlight (stream, borderless) | ✅ | ❌ `=False`, nowa karta Edge przez powłokę | ❌ `=False`, Moonlight zostaje | ✅ | ✅ | host `alder` nie dostał Home w żadnym trybie |
| Pulpit (Program Manager) | ✅ | ❌ `=True`, potem nowa karta Edge | ✅ | ✅ | ✅ | — |

`-Swallow` blokuje reakcję przeglądarki? ✅ Tak: w trybach z `-Swallow` nie było ani jednego `WM_APPCOMMAND` i żadna karta się nie otworzyła (12/12). Warunek: hook LL musi dostać „down”. W przebiegu rozstrzygającym dostał 16/16, bo okno z hookiem nie było na wierzchu w chwili naciśnięcia. We wcześniejszych przebiegach, gdy logger już był na wierzchu, „down” do hooka nie dochodził, więc `APPCOMMAND` przechodził do powłoki. Hook działa w wątku UI zajętym obsługą `WM_INPUT`, dlatego w MediaStation powinien mieć własny wątek. · **Werdykt: ✅ PASS** dla `-Swallow` + `alt` i `-Swallow` + `attach` (16/16 na wierzchu, każdy pierwszy plan), Home zawsze przechwycony. `plain` nie wystarcza (Chrome, Moonlight). Obserwacje człowieka zgodne z logiem: ekran nie gasł; w trybie bez połykania otwierały się karty (raz Chrome); host nietknięty. · Logi: `logs/S10r2-run.log` (co było na wierzchu przed i po każdym Home), `logs/S10r-plain.log`, `logs/S10r-swallow-plain.log`, `logs/S10r-swallow-alt.log`, `logs/S10r-swallow-attach.log`, zrzuty `logs/*S10r-*-before.png` i `*-after.png`. **Pomiar dotyczy dongla USB 2.4G, nie Bluetooth.**

**Moonlight w wyłącznym pełnym ekranie (domyślne ustawienie, `windowmode=0`, `fps=120`, `hdr=true`):** przy każdym wejściu i wyjściu z pełnego ekranu monitor gaśnie na 1–2 s, bo Moonlight przełącza tryb z 1920x1080@59 Hz (pulpit UM760) na 1920x1080@120 Hz (pomiar `helpers/modewatch.ps1`). Home w tym trybie (przebieg 20:13 UTC, tylko Moonlight) działał tak samo: z `-Swallow` logger na wierzchu we wszystkich trybach, ale za każdym razem z czarnym ekranem. Wniosek: MediaStation uruchamia Moonlight z `--display-mode borderless` albo ustawia pulpit na częstotliwość streamu.

**Wcześniejsze przebiegi (2026-10-13 rano, zaburzone):** `logs/S10-home-plain2.log`, `logs/S10-home-swallow.log`, `logs/S10-home-swallow-alt.log`, `logs/S10-home-moonlight.log`. Dały te same wnioski co do `alt` i połykania. Pokazały też słabość: Home przy oknie hooka na wierzchu przechodzi do powłoki.

## S8: pauza przy przełączeniu i 30 min w tle

**Cel:** brak utraty sesji i błędu licencji po powrocie.

**Kroki** (Edge, opcjonalnie Chrome; `launch`, potem w tle `serve --mode raw --browser edge --label S8-…`):

1. Próba na sucho: `cmd "s8 0.5 video minimized"`.
2. Przebieg A: odtwarzaj odcinek, potem `cmd "s8 30 api minimized"`. Koniec pojawi się w logu jako `kind=s8, phase=end`. Potem wznów przez `cmd "play api"` albo OK na pilocie i obserwuj przez około 1 min.
3. Przebieg B: `cmd "pause video"`, potem `win\window.ps1 -Action hide -ProcessName msedge`, odczekaj 30 min, potem `win\window.ps1 -Action show`, `cmd state` i wznowienie. Równolegle `cmd "s8 30 video none"` zapisze stan po czasie.
4. W logu sprawdź `visibility` (hidden/visible) i `page-error`.

**Kryterium zaliczenia:** po powrocie odtwarzanie wznawia się w tym samym miejscu, bez błędu licencji i bez przeładowania strony.

| Przebieg | Pauza | Okno | Czas | `visibility` w tle | Stan po powrocie | Wznowienie OK? | Błąd |
|---|---|---|---|---|---|---|---|
| A | api (`pause api`) | minimized (`Browser.setWindowBounds`) | 30 min (20:43–21:13 UTC) | `hidden` od 20:43:31 do przywrócenia | `/watch`, `t=482` (to samo miejsce), `readyState=4`, bez błędu i bez przeładowania; Netflix pokazuje czarny ekran z dużym przyciskiem Play | ⚠️ `play api` **nie** wznawia; `play video` wznawia od razu od `t=482`, potem 1 min bez błędów (9 × `video-sample`) | brak (`page-error` 0, bez `error`/`stalled`) |
| B (pierwsza próba) | video (`pause video`) | hidden (`winwindow.ps1 -Action hide`) | 30 min (21:15–21:45 UTC) | `hidden` | po 30 min: `/watch`, `t=518` (to samo miejsce), `readyState=4`, bez błędu | ⏳ nie sprawdzono (wznowienie odłożone; o 00:29 UM760 zasnął, o 04:45 restart przez Windows Update) | brak do chwili zerwania CDP |
| B (powtórka, 2026-10-14) | video (`pause video`) | hidden (`win\window.ps1 -Action hide`, SW_HIDE) | 30 min (06:00–06:30 UTC) | `hidden` | po 30 min: `/watch`, `t=519`, `readyState=4`, bez błędu. Po `window.ps1 -Action show` + `SetForegroundWindow` okno jest na ekranie, ale strona **nadal ma `visibility: hidden`** i wideo stoi | ❌ `play api` zwraca `paused=false`, ale `t` stoi na 519; po ok. 1 min Netflix pokazuje **„Pardon the interruption… Error Code D7361-1254-C026251E”** (`page-error` 06:32:00 UTC). Odzysk: ponowne `nav` na `/watch/81771640` gra od `t≈519` bez błędu | D7361-1254 |

Werdykt: ⚠️ PARTIAL. Minimalizacja na 30 min (A) jest bezpieczna: strona, pozycja i licencja zostają, a odtwarzanie wznawia `video.play()` (API playera nie). Ukrycie okna (SW_HIDE) na 30 min (B) kończy się błędem D7361-1254 przy wznowieniu, a do tego Edge po `SW_SHOW` nie odświeża `visibility` strony. Ponowne otwarcie odcinka odzyskuje pozycję, więc błąd da się obsłużyć, ale nie jest to „wznowienie bez przeładowania”. Wnioski: MediaStation **minimalizuje** okno przeglądarki (nie ukrywa), wznawia przez `video.play()`, a przy `page-error` (selektor `ERROR_SEL` łapie ten ekran) przeładowuje `/watch/<id>`. Jedna próba na wariant; warto powtórzyć B z minimalizacją zamiast ukrycia przy `pause video`, żeby rozdzielić wpływ metody pauzy i metody chowania okna. · Logi: `logs/20261013-224223-S8-edge-b.jsonl` (A i pierwsza próba B), `logs/20261014-075917-S8B2-edge.jsonl` (powtórka B), zrzuty `logs/20261013-231351-S8-A-after-30min.png`, `logs/20261014-083208-S8-B-screen.png`, `logs/20261014-083246-S8-B-error.png`.

## S9: sesje audio i wyciszenie drzewa procesów

**Cel:** wyciszona przeglądarka milknie, pozostałe są nadal słyszalne.

**Kroki:**

1. `launch --browser edge` (Netflix gra) i `launch --browser chrome` (dowolne audio, np. YouTube w profilu `chrome-netflix`).
2. `win\audio-mute.ps1 -Action list -ProfileDir edge-netflix`. Zapisz, które PID-y mają sesje audio i czy jest wśród nich proces główny, czy proces audio service. Zapisz też metodę (`via=mute` albo `via=channel`).
3. Kontrola negatywna: `-Action mute -ProfileDir edge-netflix -NoTree`. Czy Edge nadal gra?
4. `-Action mute -ProfileDir edge-netflix`: Edge cichy, Chrome słychać? Potem `-Action unmute`.
5. To samo dla `chrome-netflix`. Opcjonalnie w tle `-Action mute -ProfileDir edge-netflix -Watch -DurationSec 300` przez kilka zmian odcinka, albo `-Action monitor -DurationSec 120`.

**Kryterium zaliczenia:** brak dźwięku z wyciszonej przeglądarki, pozostałe słyszalne, wyciszenie widoczne w mikserze Windows.

| Przeglądarka | PID-y sesji (typ procesu) | `via` | `-NoTree` wycisza? | Drzewo wycisza? | Druga przeglądarka słyszalna? | Nowe sesje po zmianie odcinka |
|---|---|---|---|---|---|---|
| Edge 155 (`edge-fresh1`, YouTube Big Buck Bunny) | sesja tylko w procesie potomnym (audio service) PID 6012; proces główny 17608 bez sesji | `channel` (`IChannelAudioVolume`, jak w smoke teście) | ❌ nie: 0 zmienionych sesji, Edge słychać dalej (kontrola negatywna zgodna z oczekiwaniem) | ✅ słuchowo cicho, mikser `muted=True vol=0,00` | ✅ Chrome słychać | nie sprawdzano (patrz uwagi) |
| Chrome 155 (`chrome-netflix`, YouTube synthwave radio) | sesja w procesie potomnym PID 7296; proces główny 14436 bez sesji | `channel` | nie sprawdzano osobno (ta sama architektura procesów) | ✅ cicho, `muted=True` | ✅ Edge słychać | nie sprawdzano |

Przebieg: `helpers/s9-run.ps1`, 6 odcinków po 10 s z sygnałem dźwiękowym, bez udziału człowieka w oknie rozmowy. Słuchowo: 1 oba, 2 oba, 3 tylko Chrome, 4 oba, 5 tylko Edge, 6 oba. Mikser (log) zgodny w każdym odcinku. **Odstępstwo od planu:** zamiast Netflixa w Edge grał YouTube w profilu `edge-fresh1`, bo limit 2 ekranów planu Standard był zajęty (TV + Edge z S8 na pauzie). S9 sprawdza sesje audio Windows, więc treść nie ma znaczenia; `-Watch` (nowe sesje po zmianie odcinka) nie był testowany na UM760, mechanizm działa w smoke teście. Okna obu przeglądarek muszą być widoczne obok siebie: zasłonięte okno ma `visibility: hidden` i YouTube w nim nie gra (to samo dotyczy autoodtwarzania po starcie).

Werdykt: ✅ PASS. Wyciszenie całego drzewa procesów profilu ucisza tylko tę przeglądarkę, druga gra dalej, a mikser pokazuje wyciszenie. Wyciszenie samego procesu głównego nic nie daje, bo dźwięk wychodzi z procesu audio service. · Logi: `logs/S9-run.log`, `logs/S9-run-*.log`.

## S11: Moonlight z CLI, ukrycie lub minimalizacja na 5 min

**Cel:** sesja trwa, a obraz wraca po przywróceniu okna.

**Kroki:**

1. `win\moonlight.ps1 -Action list -HostName <host>`, potem `-Action start -HostName <host> -App "<app>"`.
2. `-Action hide-test -Mode min -Minutes 5` (w tle; wcześniejszy koniec: `logs\moonlight.stop`). Po przywróceniu log zawiera „SPRAWDŹ”, a człowiek ocenia obraz, dźwięk i sterowanie.
3. `-Action hide-test -Mode hide -Minutes 5`.
4. Powtórz start z `-MuteOnFocusLoss` (i `-BackgroundGamepad`). Czy dźwięk milknie po ukryciu?
5. `-Action quit -HostName <host>`. Zanotuj, czy gra na hoście została zamknięta.

**Kryterium zaliczenia:** sesja trwa, obraz, dźwięk i sterowanie wracają po przywróceniu.

| Wariant | Proces przeżył? | Obraz wrócił? | Dźwięk w tle | Gamepad w tle | Uwagi |
|---|---|---|---|---|---|
| min, 5 min (borderless) | ✅ (`logs/S11-min5-borderless.log`) | ✅ płynny; okno wróciło na wierzch (`SetForegroundWindow=True`) | ✅ Spotify z hosta słychać w trakcie minimalizacji | nie sprawdzano (brak pada) | mysz i klawiatura sterują hostem po przywróceniu |
| hide (SW_HIDE), 2 min | ✅ (`logs/S11-hide2-borderless.log`) | ✅ płynny, ale **okno nie wróciło na wierzch samo** (`SetForegroundWindow=False`, proces w tle bez odblokowania); człowiek kliknął Moonlight na pasku zadań | ✅ słychać w trakcie ukrycia | — | sterowanie działa |
| min + `--mute-on-focus-loss` (+ `--background-gamepad`), 2 min | ✅ (`logs/S11-min2-mute.log`) | ✅ płynny, na wierzchu | 🔇 Spotify cichnie w trakcie minimalizacji **i** przy samej utracie fokusu (okno rozmowy nad niezminimalizowanym Moonlightem, 15 s, `helpers/s11-focus.ps1`); po powrocie wraca | nie sprawdzano (brak pada) | sterowanie działa |

Start z CLI (`stream alder "Desktop"`) działa; okno „alder - Moonlight” pojawia się po ok. 1–2 s. Pierwotny przebieg z wyłącznym pełnym ekranem (rano, `logs/S11-min5.log`) też przeżył 5 min minimalizacji, ale każde wejście i wyjście z pełnego ekranu gasi monitor na 1–2 s (zmiana 59 → 120 Hz, S10). Z hosta streamowane było 3840x2160 AV1 10-bit HDR (nakładka Moonlighta), choć monitor UM760 ma 1080p.

**`quit`:** host kończy sesję (`/cancel` → `status_code="200"`), a programy na hoście działają dalej (Spotify gra, pulpit odblokowany). Za pierwszym razem klient streamu zamknął się sam, za drugim pokazał okno „Połączenie przerwane, Kod błędu: -1” i został otwarty. Sam proces `Moonlight.exe quit` nigdy się nie kończy (poprawka w `moonlight.ps1`: limit 30 s).

Werdykt: ✅ PASS. Sesja przeżywa 5 min minimalizacji i 2 min ukrycia, a obraz, dźwięk i sterowanie wracają. Wnioski dla architektury: (1) Moonlight w trybie `--display-mode borderless` (bez gaśnięcia monitora); (2) przywracanie okna z odblokowaniem pierwszego planu (Alt lub `AttachThreadInput`, jak w S10), bo samo `ShowWindow` + `SetForegroundWindow` z procesu w tle zawodzi; (3) `--mute-on-focus-loss` daje ciszę przy przełączeniu na MediaStation bez osobnego wyciszania procesu (S9); (4) po `quit` MediaStation zamyka klienta sama (`WM_CLOSE`), bo może zostać okno błędu. · Logi: `logs/S11-*.log`, `logs/S10r2-moonlight-start.log`, zrzuty `logs/*S11-*.png`. Host: `alder` (192.168.0.64, Sunshine/Apollo), aplikacja „Desktop”.

---

# Odłożone

- **S12:** Prime Video, Max i Disney+ w Edge i Chrome przez S1 i S2. Wejście do tabeli „serwis → przeglądarka”. Harness działa bez zmian (`--url`), ale selektory w `probe.js` są pod Netflixa, a rozszerzenie trzeba rozszerzyć o nowe domeny.
- **HDR i Atmos w Edge na TV:** wymaga UM760 podłączonego do TV. Na monitorze FHD z HDR S1 i C1 dają tylko obserwację orientacyjną.

# Smoke test harnessu (maszyna deweloperska, 2026-10-11)

Bez Netflixa, na stronie `smoke/index.html`, Windows 11 build 26300 (26H2), Edge 155.0.4283.45, Chrome 155.0.8059.39, Node 23.9.0.

| Obszar | Wynik |
|---|---|
| `launch` Edge i Chrome, profil z portem | ✅ CDP gotowe po około 300 ms, lista PID-ów procesów w logu |
| `serve` raw / pw, Edge i Chrome | ✅ `webdriver=false`; `focus`, `key`, `jskey`, `type`, `jstype`, `activate`, `pause`/`play`, `win`, `front`, `back`, `drm`, `s8`, `wait`, `events`, `quit`; kody wyjścia 0/1/2; log JSONL |
| `focus` poza ekranem | ✅ `dom-order` (wiersz z `overflow: hidden`), `row-arrow` (wiersz z `overflow: clip`, gdy okno jest widoczne), `offscreen-geometric` w pionie. Dwa błędy znalezione i poprawione: fokus wskakiwał na strzałki przewijania i do sąsiedniego wiersza |
| `adapter on` z zaufanymi klawiszami CDP | ✅ strzałki przesuwają fokus, Enter wywołuje niezaufany klik, który zmienia URL |
| `serve --mode pwlaunch` | ✅ `webdriver=true`, `quit` zamyka przeglądarkę |
| Rozszerzenie (C), `serve --mode ext` | ✅ połączenie WS, komendy probe, `win`, `front`, `nav`; kombinacje i `eval` dają błąd z kodem 1. Poprawione: kolejkowanie zdarzeń sprzed połączenia. Uwaga: po zmianie plików rozszerzenia Chrome uruchamiał stary service worker, dopóki profil nie został wyczyszczony; na UM760 klikaj „Załaduj ponownie” |
| `ext-temp` (`Extensions.loadUnpacked` przez pipe) | ⚠️ działa tylko na czas sesji i daje `webdriver=true`; do testów C służy ręczne „Load unpacked” |
| S9 `audio-mute.ps1` | ✅ po poprawce: na build 26300 sesje nie udostępniają `ISimpleAudioVolume` (`E_NOINTERFACE`), więc działa fallback `via=channel` (`IChannelAudioVolume`). Sesję audio Edge ma proces potomny, a nie korzeń, więc `-NoTree` niczego nie wycisza, a drzewo tak. `monitor`, `-Watch`, `-DurationSec` i plik stop działają |
| S10 `home-logger.ps1` | ✅ rejestracja LL, Raw Input (klawiatura, consumer, system) i shell hook; Raw Input i `SetForegroundWindow` (logger wrócił na wierzch); `-DurationSec`. ⚠️ Klawisze wstrzyknięte z powłoki agenta docierają do Raw Input, ale nie do hooka LL; przy wstrzyknięciu z procesu loggera LL działa (`flags=0x10`). Fizyczny pilot nie jest wejściem wstrzykniętym, ale na UM760 sprawdź, czy linie `LL` się pojawiają |
| `window.ps1` | ✅ list, min, restore, hide, show |
| `screenshot` (cmd) i `screenshot.ps1` | ✅ PNG w trybach raw, pw i ext (`captureVisibleTab`, rozszerzenie 0.1.1) oraz zrzut całego ekranu |
| `moonlight.ps1` | ⚠️ tylko ścieżki błędów (brak Moonlighta na tej maszynie): czytelny komunikat i kod 1 |

# Poprawki harnessu na UM760

| Plik | Problem | Poprawka |
|---|---|---|
| `win/moonlight.ps1` | Moonlight 6.2.0 to aplikacja GUI: wynik `list` i `quit` pojawia się tylko na przekierowanym stdout, więc skrypt logował samo `exit=`. | `list` i `quit` przez nową funkcję `Invoke-MoonlightCliWait` (`-RedirectStandardOutput`, `-Wait`), każda linia wyniku trafia do logu. Flagi `stream` (`--mute-on-focus-loss`, `--background-gamepad`, `--quit-after`) zgodne z `Moonlight.exe stream --help`, bez zmian. |
| `harness/drivers.mjs`, `extension/probe.js` | **Tryb raw gubił wszystkie zdarzenia probe po nawigacji.** Bez `Runtime.enable` Chromium (Edge 155) nie przenosi `Runtime.addBinding` do nowego dokumentu: po `nav` na `/watch` `window.__msBridge` był `undefined`, a `__msProbe` (z `addScriptToEvaluateOnNewDocument`) działał. Smoke test tego nie wykrył, bo strona smoke nie zmienia dokumentu. Test: `helpers/binding-test.mjs` (addBinding → `function`, reload → `undefined`, ponowne addBinding → `function`). Log sprzed poprawki bez zdarzeń: `logs/20261012-221743-S2a-edge.jsonl`. | `rawDriver` woła `Runtime.addBinding` ponownie przy każdym `Page.frameNavigated` głównej ramki. `probe.js` kolejkuje zdarzenia (maks. 200), dopóki bridge nie istnieje, i opróżnia kolejkę przy każdym `send` oraz co 300 ms. Wniosek dla architektury: BrowserHost w B musi ponawiać `addBinding` po każdej nawigacji, a adapter musi buforować komunikaty. |
| `extension/probe.js` (selektory) | Kandydaci `PROFILE_SEL`, `ROW_SEL`, `ROW_NEXT_SEL`, `ROW_PREV_SEL` nie pasowali do obecnego UI Netflixa: ekran „Who's watching?” był rozpoznawany jako `browse`, a `focus` nie znajdował wierszy ani strzałek. | Na początek list dopisane `[data-uia="profile-gate-screen"]`, `[data-uia="profile-selector"]`, `[data-uia^="carousel-row-section"]`, `[data-uia="carousel-hawkins-right-button"]`, `[data-uia="carousel-hawkins-left-button"]`. Stare kandydaty zostały. |
| `extension/probe.js` (`rendered`) | `focus` wybierał ukryty element menu powiadomień (`navigation+notifications+item`, `visibility: hidden`, `aria-hidden`). `focus()` na nim nic nie robi, więc fokus stał w miejscu. | `rendered()` odrzuca elementy z `checkVisibility({visibilityProperty: true}) === false` i wewnątrz `[inert]`. |
| `extension/probe.js` (`focusNext`) | Netflix nie oznacza tła modala jako `inert`, a fokus zostaje na kafelku w tle, więc `focus` chodził po kafelkach za modalem. Zmiana projektu testu, wprowadzona po stwierdzeniu, że bez niej pomiar „fokus w modalu” nic nie mówi. | Gdy istnieje `[role="dialog"].previewModal--container` (lub `[data-uia^="modal-motion-container"]`), `focus` szuka celów tylko w nim, a fokus spoza modala traktuje jak brak fokusu. |
| `README.md` (dokumentacja `--extra`) | `launch --extra "--flaga"` kończy się błędem `parseArgs` (wartość zaczynająca się od `--` jest traktowana jak opcja), a `launch` nie startuje przeglądarki. | Bez zmiany kodu: trzeba pisać `--extra=--flaga1 --flaga2` (w cudzysłowie dla powłoki). Odnotowane tutaj; README pokazuje tylko składnię ogólną. |
| `win/HomeLogger.cs` (prywatność) | Logger zapisywał każde naciśnięcie z każdej klawiatury, także pisanie użytkownika w innym oknie (keylogger). | Linie `LL` i `RAW-KBD` są logowane tylko dla klawiszy, które może wysłać pilot (`RemoteLikeVk`: Backspace, Enter, Esc, PgUp–strzałki, Menu, 0xA6–0xB7, Home VK); litery, cyfry i spacja nie trafiają do logu. Z pierwszego logu kalibracji usunięto linie innych klawiszy. RAW-HID i APPCOMMAND bez zmian. Plik nadal UTF-8 z BOM. |
| `win/moonlight.ps1` (`quit`) | Przy otwartym streamie `Moonlight.exe quit alder` wykonuje zadanie (host odpowiada `status_code="200"`), ale proces się nie kończy, więc `-Action quit` wisiał bez końca. | `Invoke-MoonlightCliWait` czeka maks. 30 s (`WaitForExit(30000)`), potem zamyka proces i loguje to. |

# Podsumowanie po spike'u

## Rekomendacja: B się utrzymuje

**B (surowe CDP bez `Runtime.enable`) zostaje wyborem, C (rozszerzenie) jest realnym zapasem.**

- **S2:** podłączony klient CDP, zarówno raw, jak i `connectOverCDP` z `Runtime.enable`, nie zmienia niczego w Edge ani w Chrome: ta sama rozdzielczość, bitrate i DRM, brak błędów, `navigator.webdriver=false`. S3 pokazał, że Netflix nie reaguje nawet na `webdriver=true` i pełne flagi Playwrighta. Ryzyko „Netflix wykrywa automatyzację” jest dziś niskie, a B i tak nie używa Playwrighta.
- **S6:** zaufany `Input.dispatchKeyEvent` działa jak klawisz fizyczny, także tam, gdzie niezaufane zdarzenia zawodzą: pełny ekran (`f`), wyjście z pełnego ekranu (Escape). `Input.insertText` wpisuje i wyszukuje. To przewaga B.
- **C1–C3:** rozszerzenie bez portu CDP też daje tę samą jakość i DRM, `webdriver=false` i komplet zdarzeń, bez ostrzeżeń przeglądarki. Zamienniki (`el.focus()`, `el.click()`, `execCommand`, API playera, `chrome.windows.update` dla pełnego ekranu) pokrywają całą obsługę Netflixa. Netflix obsługuje też większość niezaufanych klawiszy (Space, Enter na kafelkach, Escape w modalu), więc różnica B/C jest mniejsza, niż zakładał research. C traci tylko tam, gdzie potrzebna jest aktywacja użytkownika: pełny ekran przez klawisz i start odcinka na zimno. To drugie rozwiązuje flaga `--autoplay-policy`.
- **Kiedy przejść na C:** jeśli Google lub Microsoft zablokują `--remote-debugging-port` dla profili użytkownika albo Netflix zacznie reagować na CDP. Adapter (`probe.js`) działał bez zmian w obu transportach, więc koszt przejścia jest mały.

## Home z dongla i powrót MediaStation (S10)

- Home z G20S Pro przez **dongle USB 2.4G** (`VID_1915&PID_1025`) przychodzi jako **Raw Input HID Consumer, usage page 0x0C, usage 0x0223** (raport `02-23-02`). To najwcześniejsze i zawsze dostępne źródło. Windows dorabia z niego wstrzyknięty `VK_BROWSER_HOME` (0xAC, `LLKHF_INJECTED`) i `WM_APPCOMMAND` 7. Nieobsłużony `WM_APPCOMMAND` każe powłoce otworzyć domyślną przeglądarkę; Chrome na wierzchu reaguje też sam, otwierając nową kartę.
- **MediaStation odzyska pierwszy plan** nad Edge, Chrome, Moonlightem (borderless) i pulpitem, jeśli: (1) połyka `VK_BROWSER_HOME` hookiem LL działającym w **osobnym wątku**, bo hook w zajętym wątku UI gubi „down”; (2) robi `SetForegroundWindow` z odblokowaniem (symulowany Alt albo `AttachThreadInput`). Wynik 16/16; samo `SetForegroundWindow` zawodzi nad Chrome i Moonlightem.
- Back pilota to HID 0x0224 → `VK_BROWSER_BACK` → `WM_APPCOMMAND` 1. Edge wykonuje go sam jako `history.back()` i strona nie może tego zablokować (S7). Trzeba go przechwytywać tak samo jak Home.

## Ustalenia dla architektury

- **Flagi uruchomienia przeglądarki:** `--user-data-dir=<profil serwisu>`, `--remote-debugging-port=<stały>`, `--no-first-run`, `--no-default-browser-check`, **`--autoplay-policy=no-user-gesture-required`** (bez niej start odcinka na zimno blokuje się w obu wariantach). **Bez** `--enable-spatial-navigation` i bez flag Playwrighta. Edge jest domyślną przeglądarką dla Netflixa (PlayReady SL3000, H.264 ~7 Mb/s, wsparcie HDR); Chrome daje 1080p tylko w AV1 ~0,8 Mb/s przez Widevine L3.
- **Profil:** osobny `user-data-dir` na serwis działa od pierwszego uruchomienia (S4). Po starcie, po wizycie na `/ProfilesGate` i po wybudzeniu z Modern Standby Netflix pokazuje „Who's watching?”, więc adapter wybiera profil (`[data-uia^="profile-selector+tile-"]`).
- **Widevine/DRM:** CDM jest w katalogu instalacji Chrome i Edge, nic nie trzeba pobierać ani kopiować.
- **BrowserHost (CDP):** ponawia `Runtime.addBinding` po każdej nawigacji głównej ramki (bez `Runtime.enable` binding znika), adapter buforuje komunikaty, a połączenie CDP trzeba odnawiać po wybudzeniu z Modern Standby, bo wtedy się zrywa. Przed zamknięciem przeglądarki: nawigacja na `/browse`, inaczej slot strumienia (limit ekranów planu) zostaje zajęty 1–2 min.
- **Mapa fokusu:** potrzebna własna, per ekran. Geometryczny fokus z selektorami nowego UI (`[data-uia^="carousel-row-section"]`, `standard-card`/`progress-card`/`episodic-card`, `carousel-hawkins-right-button`, `titleCard--container`) działa, ale w modalu jest uciążliwy (linki obsady, oceny), a „Play” bywa osiągalny tylko w bok. Zakres fokusu = modal, gdy istnieje `[role="dialog"].previewModal--container`, bo tło nie jest `inert`. Obwódka Netflixa jest słabo widoczna i pojawia się tylko po zaufanym klawiszu, więc adapter rysuje własne podświetlenie. Na `/watch` adapter przepuszcza klawisze do Netflixa.
- **Reguła Back:** `watch` → `history.back()` (albo `/browse`, gdy brak historii); `browse-modal`/`title` → klik `[data-uia="previewModal-closebtn"]`; `search` → `/browse`; `browse`/`profiles` → wyjście do MediaStation. Deterministyczna w dwóch przebiegach.
- **Pauza:** `pause api` i `pause video` działają. Po 30 min pauzy w **zminimalizowanym** oknie (S8 A) strona i pozycja są zachowane, ale wznawia tylko `video.play()`; API playera Netflixa nie. Po 30 min w oknie **ukrytym** (SW_HIDE, S8 B) wznowienie kończy się błędem D7361-1254, a Edge po pokazaniu okna zostawia stronę jako `hidden`. Zasady: okno przeglądarki minimalizujemy, nie ukrywamy; wznawiamy przez `video.play()`; przy `page-error` przeładowujemy `/watch/<id>` (Netflix pamięta pozycję).
- **Audio:** dźwięk przeglądarki wychodzi z procesu audio service, nie z procesu głównego; wyciszamy całe drzewo procesów profilu (fallback `IChannelAudioVolume` na build 26300, S9). Dla Moonlighta wystarcza `--mute-on-focus-loss` (cisza przy każdej utracie fokusu).
- **Moonlight:** start z CLI (`stream <host> "Desktop"`) z **`--display-mode borderless`**; wyłączny pełny ekran przełącza monitor 59↔120 Hz i gasi go na 1–2 s przy każdym przełączeniu. Sesja przeżywa minimalizację (5 min) i ukrycie (2 min); przywracamy z odblokowaniem pierwszego planu. Po `quit` host kończy sesję, a programy na hoście działają dalej. MediaStation sama zamyka klienta, bo może zostać okno „Połączenie przerwane, kod -1”, a proces `quit` się nie kończy.
- **Inne:** po wybudzeniu z Modern Standby trzeba odtworzyć stan (CDP, profil Netflixa). Windows Update potrafi zrestartować HTPC w nocy, więc MediaStation powinna startować sama po zalogowaniu.

# Otwarte pytania po spike'u

- **TV:** HDR (wymaga planu Premium) i Atmos w Edge, 4K, a także przełączanie trybów obrazu (HDR, 120 Hz) na TV zamiast monitora FHD.
- **Bluetooth:** G20S Pro BT może wysyłać inne kody niż dongle (Home, Back, OK). S10 i reguły przechwytywania trzeba powtórzyć przez BT.
- **S12:** Prime Video, Max i Disney+ (jakość, DRM, selektory, czy reagują na CDP).
- **Limit ekranów:** jak MediaStation ma komunikować „Too many people are using your account” (`[data-uia="retryButton"]`) i czy automatycznie ponawiać.
- **Dłuższa pauza:** S8 sprawdził 30 min; co po kilku godzinach albo po nocy (wygaśnięcie sesji, Modern Standby).
- **Hook w osobnym wątku:** potwierdzić w prototypie MediaStation, że hook LL w dedykowanym wątku zawsze dostaje „down” Home i Back, także gdy MediaStation jest na wierzchu.
- **`-Watch` w S9:** czy po zmianie odcinka Netflix tworzy nową sesję audio, którą trzeba wyciszyć ponownie (nie testowane na UM760).
- **Autoodtwarzanie i `play api`:** dlaczego API playera nie wznawia po długiej pauzie i w stanie „kliknij, żeby odtworzyć”; czy to zależy od aktywacji użytkownika.
