---
reviewed: ../architecture-mediastation.md
lens: adversary (pary jednostek zgodne z AD, a mimo to niekompatybilne)
date: '2026-10-16'
verdict: 'Paradygmat (single-writer Orchestrator) jest dobry, ale spine nie definiuje kontraktów wewnątrz heksagonu: routingu klawiszy per typ aplikacji, korelacji komend i supersede, tożsamości okien/procesów oraz kształtu VM/Intent/Store. Osobne epiki zbudują tu niekompatybilne kawałki.'
---

# Review adwersarza: Architecture Spine MediaStation MVP

## Metoda

Dla każdej pary epików (Input, BrowserHost+Netflix driver, Shell UI, Audio/Window, Moonlight, OSK, Do oceny, Ops/install) szukałem dwóch implementacji, które **literalnie spełniają każde AD**, a mimo to nie składają się w działający system. Każda znaleziona para to dziura; pod nią proponowana nowa lub zaostrzona reguła AD (krótka, sprawdzalna w review albo teście).

Priorytet = prawdopodobieństwo × szkoda. P1 = prawie pewne rozjechanie i blokada integracji albo złamanie „Home zawsze działa”. P2 = realne ryzyko, naprawa kosztuje refaktor dwóch epików. P3 = zauważalne, ale lokalne.

## Podsumowanie

| # | Prio | Para jednostek | Dziura | Nowa / zaostrzona reguła |
| --- | --- | --- | --- | --- |
| H1 | P1 | Input epic × BrowserHost+driver epic | D-pad w webie: passthrough (AD-8) czy `action` przez protokół (AD-7)? | AD-8a: tabela routingu per `type` |
| H2 | P1 | Input epic × Orchestrator (Audio/Window, OSK) | Tryb wejścia żyje w Orchestratorze, a decyzja o połknięciu zapada synchronicznie w hooku; long-press `AppMenu` | AD-8b: lustro trybu w InputHub, parowanie down/up, timing tylko w InputHub |
| H3 | P1 | Orchestrator × wszystkie AppHosts/WindowManager/AudioRouter | Brak korelacji komend, supersede i właściciela timerów: Home w trakcie `Starting`, wake w trakcie przełączania | AD-14: `cmdId`/`transitionId`, Orchestrator nigdy nie czeka, czas tylko jako zdarzenie |
| H4 | P1 | Audio/Window epic × BrowserHost / Moonlight / Shell epic | Dwóch właścicieli tożsamości okna/procesu (hwnd, pid, drzewo) i foregroundu shella; dryf foregroundu | AD-15: host raportuje `AppWindowAttached`, WindowManager jedynym właścicielem foregroundu i raportuje `ForegroundChanged` |
| H5 | P1 | Shell UI epic × Orchestrator epic | Kształt VM, katalog Intentów, kto trzyma fokus kafelka, typy TS vs rekordy C# | AD-16: kontrakt VM/Intent w Core + generowany TS, stan efemeryczny UI po stabilnych id |
| H6 | P2 | Do oceny epic × Store epic × Ops/Config | Schemat `rating-queue.json` (tombstone „Pomiń”), ścieżka odczytu, właściciel schematu configu i `ratingPendingDays` | AD-12a + AD-11a |
| H7 | P2 | Netflix driver × Orchestrator/RatingPolicy | Jednostki i semantyka pól `playback`, korelacja `back`↔`backResult`, `null` vs brak pola | AD-7a |
| H8 | P2 | OSK epic × Shell UI / BrowserHost / Input | Back w trybie `osk`, gdzie żyje bufor tekstu, `textFocus` z aplikacji w tle, logowanie znaków | AD-9a |
| H9 | P2 | Ops/install (smoke) × BrowserHost × Orchestrator | `--smoke` jako drugi proces = drugi klient CDP na tym samym porcie | AD-13a: single-instance |
| H10 | P3 | BrowserHost/Moonlight × Orchestrator | Adopcja sierot: kto ją inicjuje, w jakim stanie ląduje aplikacja | AD-10a |
| H11 | P3 | Wszyscy wykonawcy × Orchestrator | Kody błędów wymyślane per epik | AD-4a: enum `ErrorCode` w Core |
| H12 | P3 | Orchestrator × Shell host | Wątki: blokujący Win32 na wątku Orchestratora, zalewanie zawieszonego UI snapshotami | AD-2a |

---

## H1 [P1]: D-pad w aplikacji webowej ma dwie zgodne z AD ścieżki

**Para:** Input epic × BrowserHost+Netflix driver epic.

- **Input epic** czyta AD-8: „Pozostałe wejście przechodzi do aktywnej aplikacji. Wyjątek: tryb `shell` lub `osk`”. W trybie `normal` z Netflixem na wierzchu strzałki i OK idą surowo do Edge.
- **Driver epic** czyta AD-7: „host → page `action {key: up|down|left|right|ok}`”. Buduje silnik fokusu PageRuntime, który reaguje wyłącznie na `bridge.receive('action')`, a natywne `keydown` ignoruje, żeby nie dublować się z Netflixem.
- **Wynik:** strzałki trafiają do Netflixa jako natywne klawisze (Netflix częściowo na nie reaguje, częściowo nie), a silnik fokusu PageRuntime nigdy nie dostaje `action`. Albo odwrotnie: PageRuntime słucha i `keydown`, i `action`, i każdy krok wykonuje się dwa razy, jeśli Input epic też zacznie wysyłać Actions.
- Podobnie Back: AD-8 mówi, że Back jest zawsze połykany, AD-7, że idzie jako `back`. To jest spójne, ale tylko dlatego, że Back jest wymieniony z nazwy. D-pad i OK nie są.
- Do tego `Ok` w playerze (AD-5: „wznowienie robi użytkownik przez OK”) – czy OK w player to `action{ok}` (PageRuntime robi `video.play()`), czy natywny Enter/Space do Netflixa, czy Orchestrator wysyła `resume`? Trzy epiki, trzy odpowiedzi, każda zgodna z AD.

**Proponowana reguła AD-8a (routing per typ aplikacji):**

> Tryb `normal` ma tabelę routingu per `type` aplikacji, w Core:
> - `web`: Up/Down/Left/Right/Ok/Back są **połykane** i dostarczane jako Actions do Orchestratora, który wysyła `action{key}` / `back` do strony. Żaden klawisz D-pad nie dociera do przeglądarki natywnie. PageRuntime ignoruje zaufane `keydown` z D-pada.
> - `native`: D-pad i OK przechodzą natywnie, Back wg `back` z `apps.json`.
> - `moonlight`: wszystko przechodzi, poza Home (kombinacja) i Keyboard.
> - Wznowienie po pauzie to zawsze `action{ok}`; to driver decyduje, że na ekranie `watch` w stanie `paused` OK = `video.play()`. Komenda `resume` jest używana tylko przez Orchestrator (np. smoke), nigdy jako mapowanie OK.

## H2 [P1]: Tryb wejścia jest stanem Orchestratora, a decyzja o połknięciu zapada w hooku

**Para:** Input epic × Orchestrator (i pośrednio OSK oraz Shell epic).

- AD-3: tylko Orchestrator mutuje tryb wejścia. AD-8: w trybie `shell`/`osk` wejście jest połykane. Ale callback `WH_KEYBOARD_LL` musi zwrócić „połknij/przepuść” **synchronicznie** i w czasie `LowLevelHooksTimeout` (Windows po przekroczeniu cicho odpina hook – to zabija „Home zawsze działa”).
- **Wariant A (Input epic, „zgodny z AD-3”):** hook wysyła klawisz do kolejki Orchestratora i czeka na odpowiedź, bo „tylko Orchestrator decyduje”. Przy obciążonym Orchestratorze hook przekracza timeout i jest odpinany.
- **Wariant B (Input epic):** InputHub sam sprawdza, czy foreground to okno MediaStation (`GetForegroundWindow`) i na tej podstawie połyka. To jest decyzja w wykonawcy (łamie duch AD-4) i rozjeżdża się z trybem Orchestratora, gdy shell jest widoczny, ale foreground nie przeszedł (AD-5 odblokowanie foregroundu bywa zawodne).
- **Wariant C (Orchestrator epic):** wysyła `SetInputMode(osk)` jako Command; Input epic go nie przewidział, bo AD-8 mówi tylko o „trybie”, nie o komendzie.
- **Wyścig down/up:** `KeyDown(Right)` w trybie `normal` przechodzi do Edge, w międzyczasie tryb zmienia się na `shell`, `KeyUp(Right)` jest połykany → Edge ma „wciśnięty” klawisz, autorepeat w nieskończoność. Analogicznie odwrotnie.
- **`AppMenu` = długie OK (memlog):** jeśli OK przechodzi do aplikacji natywnie (H1/AD-8), InputHub nie może wykryć długiego wciśnięcia bez wstrzymania OK. Jeśli wykrywa to Orchestrator, potrzebuje surowych down/up, których AD-8 nie przewiduje (Actions to dyskretne zdarzenia). Shell epic z kolei oczekuje autorepeatu strzałek do przewijania – kto generuje repeat (urządzenie, InputHub, UI)?

**Proponowana reguła AD-8b:**

> - Orchestrator publikuje tryb komendą `SetInputRouting{ mode, activeAppType, epoch }`. InputHub trzyma lokalną kopię (atomowy odczyt) i decyduje w hooku wyłącznie na jej podstawie, bez I/O i bez czekania. Hook nigdy nie blokuje się na Orchestratorze.
> - Decyzja dla `KeyUp` = decyzja zapisana dla odpowiadającego mu `KeyDown` (para po VK + urządzeniu), niezależnie od zmiany trybu w międzyczasie.
> - Cały timing wejścia (autorepeat, długie wciśnięcie, kombinacje pada) należy do InputHub i jest parametryzowany w `input.json`. Action ma kształt `{ kind, phase: press|repeat, source, tsMono }`; Orchestrator i UI nigdy nie dostają down/up.
> - `AppMenu` musi być mapowane na klawisz/kombinację **zawsze połykaną** (jak Home/Back/Keyboard), nigdy na długie wciśnięcie klawisza przepuszczanego do aplikacji.

## H3 [P1]: Komendy bez korelacji, przełączanie bez supersede, czas bez właściciela

**Para:** Orchestrator epic × każdy wykonawca (BrowserHost, MoonlightHost, WindowManager, AudioRouter); scenariusze Home w `Starting`, wake w trakcie przełączania, podwójne OK na kafelku.

- AD-5 ustala kolejność `pause → mute → minimize`, ale wykonawcy są asynchroniczni i raportują wyniki Eventami. Nie wiadomo:
  - czy Orchestrator czeka na wynik `pause` przed `mute` (i czy w tym czasie jego kolejka stoi – wtedy Home czeka na CDP z timeoutem);
  - jak dopasować Event wyniku do komendy (`ProcessExited` po `Stop` vs crash);
  - co się dzieje z odpowiedziami spóźnionymi po zmianie decyzji.
- **Home w trakcie `Starting`:** Orchestrator A (Orchestrator epic) przechodzi do shella i zostawia aplikację w `Starting`. BrowserHost (zgodnie z AD-4) kończy start i raportuje `Started`; Edge sam wychodzi na wierzch (nowo uruchomiony proces dostaje foreground). Nikt nie wydał `minimize`, bo Orchestrator epic uważał, że start „jeszcze trwa”, a BrowserHost epic uważał, że restore/foreground to jego część startu. Shell jest przykryty, tryb wejścia `shell` połyka klawisze → użytkownik widzi Netflixa, który nie reaguje na pilota.
- **Wake w trakcie przełączania:** AD-10 każe Orchestratorowi „przejść na ekran główny”, a BrowserHost „odnowić CDP i `addBinding`”. Czy BrowserHost robi to sam po `ResumeFromStandby` (fakt z SystemEvents trafia tylko do Orchestratora!), czy na komendę? Jeśli sam – to wykonawca reaguje na zdarzenie innego wykonawcy (łamie AD-4). Jeśli na komendę – nikt jej nie zdefiniował. W trakcie trwa przełączanie, którego `unmute` przychodzi po wejściu na Home → słychać aplikację w tle.
- **Timery:** 500 ms dla `backResult`, timeout `Starting`, backoff reconnectu CDP, `ratingPendingDays`. Core jest czysty i bez I/O – skąd bierze czas? Orchestrator epic doda `Task.Delay` w handlerze (łamie sekwencyjność), BrowserHost epic sam zaimplementuje 500 ms (decyzja w wykonawcy).

**Proponowana reguła AD-14 (komendy i przejścia):**

> - Każda Command ma `cmdId`; każdy wynik wykonawcy to `CommandCompleted{cmdId, ok|error}` albo fakt z `cmdId?`. Fakty spontaniczne (`ProcessExited`, `CdpLost`) nie mają `cmdId`.
> - Orchestrator **nigdy nie czeka** w handlerze. Każde przełączanie aplikacji to jawna tranzycja z `transitionId`; kolejny krok AD-5 wysyłany jest po `CommandCompleted` kroku poprzedniego albo po timeoutcie kroku (wtedy kontynuuje, a nie zatrzymuje się).
> - Nowa decyzja (Home, inna aplikacja, wake) **zastępuje** bieżącą tranzycję: wyniki ze starym `transitionId` są ignorowane, a Orchestrator wydaje komendy kompensujące (np. aplikacja, która doszła do `Started` po Home, dostaje `pause/mute/minimize` i przechodzi do `Background`).
> - Wykonawca nie zmienia widoczności okna przy starcie: start kończy się oknem zminimalizowanym/niezmienionym, a restore+foreground wydaje wyłącznie Orchestrator.
> - Czas wchodzi do Core tylko jako port `IClock` (odczyt) i zdarzenie `TimerElapsed{timerId}` w kolejce Orchestratora (`ScheduleTimer` to Command). Timeouty produktowe (Back 500 ms, start, `ratingPendingDays`) należą do Orchestratora. Backoff reconnectu CDP należy do BrowserHost, który raportuje tylko `CdpLost`, `CdpRestored` i ostateczne `CdpGaveUp`.
> - Wake: SystemEvents → `ResumedFromStandby` → Orchestrator anuluje tranzycję, wydaje `RenewSession{appId}` każdemu żywemu hostowi webowemu i przechodzi na Home.

## H4 [P1]: Dwóch właścicieli tożsamości okna i foregroundu

**Para:** Audio/Window epic × BrowserHost / Moonlight / Shell epic.

- Komenda `MinimizeWindow` – minimalizuj **co**? AD nie mówi, kto ustala hwnd okna aplikacji ani pid/drzewo procesów dla AudioRouter.
  - **WindowManager epic** sam enumeruje okna po pid albo klasie `Chrome_WidgetWin_1`. **BrowserHost epic** też znajduje hwnd (np. przez `Browser.getWindowForTarget`) i trzyma go u siebie. Dwa źródła prawdy; przy dwóch przeglądarkach (Edge + Chrome dla przyszłych serwisów) lub dodatkowym oknie (popup logowania, okno DevTools) wybiorą różne okna.
  - **AudioRouter**: „całe drzewo procesów profilu” – po adopcji sieroty (AD-10) MediaStation nie jest rodzicem, a Edge ma usługę audio w procesie potomnym tworzonym później. Kto podaje pid korzenia i kto śledzi nowe procesy?
- **Foreground shella:** Shell epic robi `window.Activate()`/`Topmost` w WPF przy każdym snapshocie „home”, a WindowManager robi `SetForegroundWindow(shellHwnd)` z odblokowaniem. Dwa ścieżki mutacji foregroundu, wyścig z AD-5.
- **Dryf foregroundu:** okno systemowe, powiadomienie albo samo Edge kradnie fokus. Tryb wejścia mówi `shell`, InputHub połyka klawisze, a na wierzchu jest Edge – brief wymaga, żeby Home „zawsze przywracał”, ale nikt nie raportuje faktu zmiany foregroundu.

**Proponowana reguła AD-15 (okna i procesy):**

> - AppHost jest jedynym źródłem tożsamości aplikacji: raportuje `AppWindowAttached{appId, hwnd, rootPid}` (i `AppWindowDetached`). WindowManager i AudioRouter działają wyłącznie na `hwnd`/`rootPid` podanych w komendzie, nie enumerują okien ani procesów sami (AudioRouter może tylko rozwijać drzewo od `rootPid` i śledzić nowe sesje audio w nim).
> - WindowManager jest jedynym kodem, który zmienia widoczność, Z-order i foreground **każdego** okna, w tym shella i OSK. Shell host nie wywołuje `Activate`, `Topmost` ani `Focus` na oknie WPF.
> - WindowManager raportuje fakt `ForegroundChanged{hwnd, appId?}` (WinEvent hook); Orchestrator uzgadnia z nim tryb wejścia i stan aplikacji.

## H5 [P1]: View-model i Intenty bez jednego źródła kształtu

**Para:** Shell UI epic (React) × Orchestrator epic (C#).

- Konwencja mówi tylko: „UI wysyła Intent, core wysyła pełny snapshot”. Shell epic napisze własne typy TS (`tiles[].status: 'running'`), Orchestrator epic rekordy C# (`AppLifecycle.Foreground` serializowany jako `"Foreground"` albo `2`). Każda strona zgodna z AD, razem nie działa.
- **Fokus kafelka:** kto trzyma „który kafelek jest zaznaczony”? Shell epic: lokalny stan React (AD-8 mówi, że UI konsumuje Actions z core, więc sam przesuwa kursor). Orchestrator epic: `focusedTileIndex` w snapshocie, bo „stan jest w Orchestratorze” (AD-3). Wynik: dwa kursory albo kursor, który skacze przy każdym snapshocie. Indeksy dodatkowo przesuwają się, gdy karta „Do oceny” znika po „Oceniłam”.
- **Katalog Intentów** (`launchApp`, `rated`, `skipped`, `appMenuClose`, `appMenuRestart`, `oskKey`…) nie istnieje; każdy epik wymyśli nazwy.
- **Kolejność snapshotów:** postMessage + marshaling na wątek UI. Bez numeru wersji UI może wyrenderować starszy snapshot po nowszym.

**Proponowana reguła AD-16 (kontrakt shella):**

> - Rekordy `ViewModel` i `Intent` są zdefiniowane w `MediaStation.Core/Contracts/Shell`; TS dla `web/shell` jest **generowany** z nich w buildzie (JSON Schema → `contract.gen.ts`), ręczna edycja zabroniona. Serializacja: camelCase, enumy jako stringi camelCase, brakujące wartości jako jawne `null`.
> - Snapshot ma monotoniczny `rev`; UI odrzuca `rev` ≤ ostatniego.
> - Stan efemeryczny nawigacji (fokus, scroll, otwarte menu w obrębie widoku) należy do UI. Wszystko trwałe albo widoczne dla innych komponentów należy do core. Intenty i fokus odwołują się do **stabilnych id** (`appId`, klucz „Do oceny”), nigdy do indeksów.
> - Core przekazuje Actions do UI tylko w trybie `shell` jako `{type:'action', payload:{kind, phase}}`; UI nie dostaje Actions w trybie `normal`/`osk` (OSK patrz H8).

## H6 [P2]: Schemat Store i configu bez właściciela

**Para:** Do oceny epic × Store epic; Ops/install epic × host epics.

- **Tombstone „Pomiń”:** AD-12 – „klucz trafia do kolejki raz na zawsze, także po Pomiń”. Store epic rozsądnie usuwa kartę z `rating-queue.json` po „Oceniłam/Pomiń”. Do oceny epic deduplikuje po zawartości pliku → po „Pomiń” film wraca przy następnym `playback ≥ 0.9` (np. ponowne obejrzenie końcówki). Obie implementacje zgodne z AD.
- **Ścieżka odczytu:** „zapis wyłącznie przez Store na komendę Orchestratora”. Odczyt przy starcie? Store epic może wystawić synchroniczne `Load()` wołane przez Orchestrator (I/O w Core – łamie AD-3), albo Orchestrator epic czyta plik sam przez interfejs. Brak Eventu `StoreLoaded`.
- **Granulacja komend:** Orchestrator epic wysyła `SaveRatingQueue{caly dokument}`, Store epic implementuje `Enqueue{item}`/`Remove{key}` z logiką merge. Dwa miejsca logiki = dwóch właścicieli kolejki.
- **Normalizacja tytułu** (fallback klucza serialu) – driver normalizuje, RatingPolicy normalizuje drugi raz inaczej, albo jeden z nich nie normalizuje → duplikaty kart. Klucz serialu zmienia się, gdy driver raz poda `seriesId`, a raz nie.
- **Config:** `apps.json` ma „`…`” w schemacie. BrowserHost epic doda `startUrl` (który jest też w `manifest.json` drivera – dwa źródła), Moonlight epic doda `quitOnClose`. Gdzie żyje `ratingPendingDays` i mapowanie Home pada – `apps.json`, `input.json`, nowy plik? Kto waliduje (Config w App czy Core)? Dwa epiki wymyślą dwa pliki.

**Proponowana reguła AD-12a (Store):**

> - Store jest głupi: przy starcie emituje `StoreLoaded{doc}` dla każdego pliku `data/`, potem przyjmuje wyłącznie `Persist{file, doc}` z całym dokumentem i odpowiada `CommandCompleted`. Logika kolejki (dodanie, status, deduplikacja) jest wyłącznie w Core.
> - `rating-queue.json` = `{ schemaVersion, items: [{ key, service, kind, title, seriesId?, enqueuedAt, status: pending|rated|skipped, resolvedAt? }] }`. Rekordów nigdy się nie usuwa; zbiór kluczy to tombstone.
> - Klucz liczy i normalizuje wyłącznie `RatingPolicy` (funkcja `NormalizeTitle` w Core: NFC, lowercase, trim, zwinięte spacje, bez interpunkcji). Driver wysyła surowe fakty. Raz zapisany klucz serialu w `series-pending.json` mapuje `seriesId` ↔ tytuł, żeby późniejsze pojawienie się `seriesId` nie tworzyło drugiego klucza.

**Proponowana reguła AD-11a (Config):**

> - Schemat każdego pliku `config/` to rekordy w `MediaStation.Core/Contracts/Config` (unia dyskryminowana po `type`); podschemat `web|moonlight|native` należy do epiki danego hosta, ale zmienia się wyłącznie w Core. Nieznane pola = błąd walidacji.
> - Pliki: `apps.json` (rejestr), `input.json` (mapowanie, timing, kombinacje pada), `settings.json` (skalary polityk: `ratingPendingDays`, timeouty). Nowy parametr trafia do jednego z nich, nie do nowego pliku.
> - Dane serwisu (start URL, dopasowanie URL) żyją tylko w `manifest.json` drivera; `apps.json` ich nie powtarza.
> - Config adapter parsuje i przekazuje do Core gotowy model; walidacja semantyczna (unikalność portów, zakres 9220–9239, istnienie drivera) to czysta funkcja w Core.

## H7 [P2]: Pola protokołu strony bez jednostek i semantyki

**Para:** Netflix driver epic × Orchestrator/RatingPolicy (Do oceny epic).

- `position`, `duration`: sekundy czy milisekundy? Driver weźmie `video.currentTime` (s, float), ktoś inny metadane Netflixa (ms). Stosunek się nie zmienia, ale logi, „Do oceny” i smoke („odtworzenie” = `position` rośnie) już tak. `duration` = `NaN`/`0`/`Infinity` przed załadowaniem metadanych → `position/duration` = `NaN`/`Infinity` ≥ 0.9 → fałszywa karta.
- `season?`, `episode?`, `seriesId?`: brak pola vs `null` – C# deserializer i RatingPolicy potraktują różnie. `lastInSeason: bool|null` jest wymagane, ale co, gdy `kind: film`?
- `id?` w kopercie: AD-7 nie mówi, że `backResult` musi echować `id` z `back`. Scenariusz: `back` #1 → timeout 500 ms → Home; użytkownik wraca, `back` #2; spóźniony `backResult{handled:true}` z #1 zostaje dopasowany do #2 → Orchestrator nie idzie na Home, choć powinien.
- `screen.root` vs `backResult`: driver może zwrócić `handled:true` na ekranie root (robi `history.back()`), Orchestrator epic może idąc na skróty używać `screen.root` do decyzji Back bez wysyłania `back`. Dwie reguły Back.
- „przy zmianie stanu i co ~30 s”: czy seek to zmiana stanu? Czy `playback` przychodzi, gdy aplikacja jest w `Background` (pauza) – i czy RatingPolicy liczy zdarzenia z tła?

**Proponowana reguła AD-7a:**

> - `position`, `duration`: sekundy, liczba zmiennoprzecinkowa, `duration > 0` i skończone; inaczej driver nie wysyła `playback`. `videoId`, `seriesId`: stringi. `season`, `episode`: liczby całkowite od 1. Pola opcjonalne zawsze obecne, z `null` gdy nieznane. Dla `kind: film` `lastInSeason` = `null` i jest ignorowane.
> - Każdy komunikat host → page ma `id`; każda odpowiedź (`backResult`) echuje `id`. Odpowiedź bez pasującego, oczekującego `id` jest logowana i ignorowana.
> - Decyzja Back pochodzi wyłącznie z `backResult`/timeoutu. Driver zwraca `handled:false` wtedy i tylko wtedy, gdy bieżący ekran ma `root:true`. `screen.root` służy tylko logom i UI.
> - RatingPolicy przetwarza `playback` niezależnie od stanu aplikacji; driver wysyła `playback` przy: start, pauza, wznowienie, `ended`, seek > 10 s, oraz co 30 s ± 5 s w stanie `playing`.

## H8 [P2]: OSK – Back, bufor tekstu, `textFocus` z tła

**Para:** OSK epic × Shell UI epic × BrowserHost × Input epic.

- **Back w trybie `osk`:** AD-8 – Back zawsze połykany. Orchestrator epic wysyła `back` do strony (bo „Back w aplikacji webowej: Orchestrator wysyła `back`”), OSK epic oczekuje, że Back chowa klawiaturę. Netflix wychodzi z wyszukiwarki z otwartą OSK.
- **Bufor tekstu:** OSK epic trzyma wpisywany tekst w stanie React i wysyła całość przy „Enter” (`insertText(całość)`); BrowserHost epic zakłada wstawianie znak po znaku (wyszukiwarka Netflixa filtruje na żywo). Do tego bufor w UI to stan poza Orchestratorem (AD-3), a Intent z treścią jest logowany przez generyczny logger Intentów (łamie AD-13 „tekst z OSK nie jest logowany”).
- **Renderowanie OSK:** osobne okno nakładki (AD-9) – drugi WebView2 z osobnym bundle, czy trzecie okno WPF? Konsumuje snapshot VM czy własny kanał? Shell epic i OSK epic mogą obaj zbudować klawiaturę.
- **`textFocus` z aplikacji w tle:** driver zgłasza `textFocus{active:true}` z Edge zminimalizowanego (fokus programowy po nawigacji) albo po wybudzeniu → OSK wyskakuje nad ekranem głównym.

**Proponowana reguła AD-9a:**

> - W trybie `osk` Back = `HideOsk` (decyduje Orchestrator), nigdy nie trafia do aplikacji.
> - OSK nie ma bufora: każdy klawisz to Intent `oskKey{ char | backspace | enter }` → Command `InsertText`/`SendKey` do hosta, znak po znaku. Intent `oskKey` i odpowiadające mu Commands są oznaczone `[Redacted]` i logger zapisuje tylko typ.
> - OSK jest widokiem w kontrakcie VM (sekcja `osk` w snapshocie, H5) renderowanym przez ten sam bundle `web/shell` w drugim hoście WebView2; jedna implementacja klawiatury, także dla fallbacku w PageRuntime (ten sam layout z tego samego źródła).
> - Orchestrator honoruje `textFocus` tylko od aplikacji w stanie `Foreground` przy trybie `normal`; `textFocus{active:false}` przy otwartej OSK chowa ją.

## H9 [P2]: `--smoke` jako drugi proces łamie „jeden klient CDP”

**Para:** Ops/install epic (smoke, install.ps1) × BrowserHost × Orchestrator.

- AD-13: `MediaStation.exe --smoke netflix` używa prawdziwego BrowserHost. Task Scheduler już trzyma działający MediaStation z podpiętym CDP na 9220. Smoke uruchamia drugi proces: albo adoptuje tę samą przeglądarkę (AD-10) → dwa klienty CDP na jednej przeglądarce (łamie AD-4), dwa `addBinding`, podwójne komunikaty; albo startuje Edge na tym samym profilu → konflikt profilu.
- Smoke steruje BrowserHost bezpośrednio (bez Orchestratora) – czyli jest drugim „decydentem”, którego AD-3 nie przewiduje; Ops epic i Orchestrator epic zbudują dwa różne harnessy.
- `install.ps1` przy aktualizacji kopiuje pliki pod działający proces (zablokowane DLL) – „aktualizacja = ponowne uruchomienie skryptu” nie mówi, kto zatrzymuje instancję.

**Proponowana reguła AD-13a:**

> - Named mutex `MediaStation.Instance` – drugi proces kończy się kodem błędu, chyba że to `--smoke`, który odmawia pracy (kod wyjścia „instance running”), gdy mutex jest zajęty. Smoke przechodzi przez Orchestrator z wstrzykniętym skryptem Actions, nie przez BrowserHost bezpośrednio.
> - `install.ps1` zatrzymuje zadanie i proces przed kopiowaniem, a po nim uruchamia zadanie ponownie.

## H10 [P3]: Adopcja sierot bez inicjatora i stanu docelowego

**Para:** BrowserHost/MoonlightHost × Orchestrator.

- AD-10: „BrowserHost się podpina” – sam, przy starcie (decyzja w wykonawcy), czy na komendę? Aplikacje startują leniwie – czy zaadoptowana aplikacja jest `NotRunning` aż do wybrania kafelka (wtedy jej dźwięk gra nad ekranem głównym, bo nikt jej nie wyciszył), czy `Background`?

**Proponowana reguła AD-10a:**

> Przy starcie Orchestrator wysyła `AdoptIfRunning{appId}` do hosta każdej aplikacji; host odpowiada `AppAdopted{…AppWindowAttached}` albo `NotFound`. Zaadoptowana aplikacja przechodzi do `Background` przez pełną sekwencję opuszczenia z AD-5 (pause, mute, minimize), zanim shell pokaże ekran główny.

## H11 [P3]: Kody błędów wymyślane per epik

**Para:** dowolny wykonawca × Orchestrator.

- Konwencja: „Event z `error` (kod + komunikat); Orchestrator decyduje o `Failed`”. BrowserHost zwróci `"CDP_TIMEOUT"`, MoonlightHost `"timeout"`, NativeAppHost `ExitCode=1`. Orchestrator nie ma jak zmapować ich na `Failed` vs retry bez znajomości każdego hosta.

**Proponowana reguła AD-4a:**

> `ErrorCode` to enum w Core (`Timeout`, `ProcessExited`, `CdpUnavailable`, `WindowNotFound`, `ConfigInvalid`, `ProtocolVersion`, `Unknown`); nowe kody dodaje się w Core. Mapowanie kod → stan cyklu życia jest tabelą w Orchestratorze.

## H12 [P3]: Wątki – blokujący Win32 na Orchestratorze, zalewanie zawieszonego UI

**Para:** Orchestrator × WindowManager/AudioRouter × Shell host.

- Jeśli WindowManager wykonuje komendy synchronicznie na wątku Orchestratora, `ShowWindow`/`AttachThreadInput` na oknie zawieszonego procesu blokuje kolejkę – Home przestaje działać mimo osobnego wątku Input.
- Shell host marshaluje każdy snapshot na wątek UI; przy zawieszonym WebView2 kolejka snapshotów rośnie bez ograniczeń.

**Proponowana reguła AD-2a:**

> - Wątek Orchestratora wykonuje tylko czysty kod. Każdy wykonawca ma własną kolejkę/wątek lub async; Win32 na cudzych oknach tylko w wersjach nieblokujących (`ShowWindowAsync`, `SendMessageTimeout`).
> - Kanał snapshotów do UI to skrzynka „ostatni wygrywa” (pojemność 1); kanał do każdego wykonawcy jest ograniczony i loguje przepełnienie.

## Czego nie zgłaszam

- Wybór B/C dla przeglądarki, stos i wersje – poza zakresem tej soczewki.
- Moonlight `--mute-on-focus-loss` + AudioRouter (podwójne wyciszanie) – nadmiarowe, ale zgodne co do efektu.
- Odświeżanie page bundle z dysku „przy każdym wstrzyknięciu” vs jednorazowe `addScriptToEvaluateOnNewDocument` – rozstrzygnie się w epice BrowserHost bez wpływu na inne epiki.

## Kolejność wprowadzania

1. AD-8a + AD-8b (H1, H2) – przed startem Input i BrowserHost epiki; obie zależą od tego samego routingu.
2. AD-14 + AD-15 (H3, H4) – przed pierwszą story Orchestratora; definiują kształt każdej Command/Event.
3. AD-16 (H5) – przed pierwszą story Shell UI.
4. AD-7a, AD-12a, AD-11a, AD-9a (H6–H8) – przed odpowiednio driverem, Do oceny, Ops/Config, OSK.
5. Reszta (H9–H12) – może wejść jako zaostrzenie istniejących AD przy najbliższej aktualizacji spine'a.
