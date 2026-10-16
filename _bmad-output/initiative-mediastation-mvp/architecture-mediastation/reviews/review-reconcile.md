---
title: 'Review: reconcile spine ↔ źródła'
target: ../architecture-mediastation.md
date: 2026-10-16
sources:
  - ../../brief-mediastation/brief-mediastation.md (+ addendum.md, .memlog.md)
  - ../../research-browser-control-for-streaming-services/research-browser-control-for-streaming-services.md (Wnioski międzywymiarowe, Rekomendacja, §4–6, Otwarte pytania)
  - ../../research-browser-control-for-streaming-services/imports/spike-results-um760.md (Ustalenia dla architektury, Otwarte pytania po spike'u)
  - ../../forge-filmweb-rating-prompt/forge-filmweb-rating-prompt.md
verdict: 'PASS z poprawkami: wszystkie reguły [ADOPTED] wylądowały, ale 6 cichych wymagań wypadło albo jest sprzecznych z AD'
---

# Review: reconcile spine ↔ źródła

## Werdykt

**PASS z poprawkami.** Spine wiernie przenosi wszystkie reguły **[ADOPTED]** z memlogu, researchu i spike'u: flagi i zakazy CDP (AD-1), ponawianie `addBinding` + buforowanie (AD-6), minimalizacja zamiast `SW_HIDE`, foreground z odblokowaniem, `video.play()` / reload `/watch/<id>`, wyciszanie drzewa przez `IChannelAudioVolume`, Moonlight borderless + `--mute-on-focus-loss`, `/browse` przed zamknięciem, odnowienie CDP po Modern Standby (AD-5, AD-10), Back/Home w hooku LL w osobnym wątku (AD-2, AD-8). Decyzje forge („Do oceny”) są w AD-12 z rozstrzygniętym fallbackiem.

Braki są w miejscach, gdzie struktura AD (porty, protokół, „driver nie zna CDP”, „host nie zna serwisu”) **nie przewidziała ścieżki** dla zachowania, które źródła wymagają. Kilka z nich to sprzeczność wewnątrz spine'u, nie tylko pominięcie.

## Najważniejsze braki (do poprawy przed bmad-ux / bmad-spec)

### M1. Kursor i mysz z żyroskopu: zasada produktu bez właściciela

- **Źródło:** brief, Rozwiązanie: „Użytkownik nie widzi pulpitu, okien **ani kursora**”; brief, Problem: G20S Pro BT emuluje mysz żyroskopem; Kryteria: „cały wieczór D-padem, **bez trybu myszy**”.
- **Spine:** słowo „kursor/mysz” nie występuje. AD-8 obejmuje tylko VK, HID Consumer i XInput. Ruch pilota (przypadkowy w dłoni) pokaże kursor, a hover na kafelku Netflixa otwiera podgląd/`previewModal`, który według spike'u (S5, mapa fokusu) **więzi fokus**: konflikt z silnikiem fokusu PageRuntime.
- **Gdzie wylądować:** AD-8 (InputHub: polityka dla wejścia myszy — ukrycie kursora / parkowanie poza ekranem / połykanie ruchu poza trybem, który go potrzebuje, np. Moonlight lub natywne) + wiersz w Consistency Conventions; ewentualnie odnotować w AD-6, że PageRuntime ignoruje/neutralizuje `mouseover` na kafelkach.

### M2. Dwie niezdefiniowane ścieżki D-pada w web i OK → `resume`

- **Źródło:** research §4 i spike „Mapa fokusu”: adapter **przechwytuje strzałki i OK w stronie**, a **na `/watch` przepuszcza klawisze do Netflixa**. Spike S8 / [ADOPTED]: po długiej pauzie wznawia tylko `video.play()`, **API playera nie** (a natywne OK/Space w Netflixie idzie przez jego player). Brief, krok 6: „Wznawia go przyciskiem OK”.
- **Spine:** AD-8 mówi, że poza Home/Back/Keyboard wejście **przechodzi do aktywnej aplikacji** (czyli fizyczne strzałki/OK lądują w Edge jako keydown). Jednocześnie AD-7 definiuje `host → page action{up|down|left|right|ok}`, co implikuje drogę przez InputHub → Orchestrator → BrowserHost. Nie wiadomo, która ścieżka obowiązuje (gamepad XInput może iść tylko drugą). AD-5 deklaruje „OK wznawia przez `video.play()`”, ale przy przepuszczonym OK nikt nie zamienia OK na `resume`: Orchestrator go nie widzi, a natywny handler Netflixa może nie wznowić po 30+ min.
- **Gdzie wylądować:** AD-8 + AD-7: jedna, jawna ścieżka dla web (np. „w aplikacji `web` strzałki/OK połykane i dostarczane jako `action`, driver decyduje o przepuszczeniu na `/watch`”, albo odwrotnie: fizyczne klawisze w stronie, `action` tylko dla gamepada). AD-5: kto i gdzie zamienia OK na pauzie w `video.play()` (reguła drivera na `/watch` w stanie `paused`).

### M3. Protokół strony nie ma ścieżek, których wymagają reguły [ADOPTED]

AD-6 zabrania driverowi CDP, a AD-6/AD-11 zabraniają logiki serwisu w C#. Kilka zachowań wymaga współpracy obu stron, a AD-7 nie ma na nie komunikatu:

- **Zaufany klawisz** (research §4, Rekomendacja 1; spike S6): pełny ekran w playerze tylko przez zaufane `f` z `Input.dispatchKeyEvent`; widoczny focus ring Netflixa tylko po zaufanym klawiszu. Driver wie *kiedy*, tylko BrowserHost może *wysłać*. Brak `page → host` typu `trustedKey{key}` (Deferred mówi „pełny ekran daje zaufane `f`”, ale nie ma jak go zamówić).
- **`/browse` przed zamknięciem** (AD-10 [ADOPTED]): `/browse` to URL Netflixa. BrowserHost nie powinien go znać, a host → page nie ma komunikatu typu `prepareClose` (z odpowiedzią/timeoutem), ani manifest nie ma pola „URL bezpieczny do zamknięcia”. Memlog świadomie usunął `navigate`, ale niczego w zamian nie dodał.
- **Ekran tekstowy dla OSK w natywnym trybie** jest pokryty (`textFocus`), więc tylko te dwa.
- **Gdzie wylądować:** AD-7 (tabela komunikatów: `trustedKey` page→host, `prepareClose` host→page + `closeReady`), AD-6 (manifest ewentualnie z `closeUrl`).

### M4. Klucz „Do oceny” z fallbackiem na tytuł vs język profilu i „dowolny profil”

- **Źródło:** forge, „Do sprawdzenia”: tytuł w playerze zależy od **języka profilu** (angielski u autora); forge/brief: działa na **dowolnym profilu**, wspólne seriale są na obu profilach. Addendum: `/watch/<id>` niesie id **odcinka**, id serialu tylko po przejściu przez `/title` lub `jbv` (więc start z „Continue Watching” → brak `seriesId`).
- **Spine:** AD-12 klucz serialu = `seriesId`, a gdy brak → **znormalizowany tytuł**. Najczęstsza ścieżka (Continue Watching) daje fallback, a ten sam serial na dwóch profilach daje dwa różne klucze → **duplikaty kart** (dokładnie to, czemu AD-12 ma zapobiegać) i rozjechane rekordy „serial w zawieszeniu”. Deferred twierdzi, że Wikidata „zmienia tylko cel URL, `seriesId` jest już w protokole”, a w praktyce go często nie będzie.
- **Gdzie wylądować:** AD-12 (jawna akceptacja ryzyka albo wymóg, by driver ustalał `seriesId` także przy starcie z `/watch`, np. z metadanych playera; reguła scalania przy późniejszym poznaniu `seriesId`), poprawka zdania w Deferred („Mapowanie przez Wikidata”). Kontrola języka z forge jest w otwartych pytaniach, ale bez powiązania z kluczem.

### M5. Smoke „po każdej aktualizacji przeglądarki” i warunki przejścia B→C bez właściciela

- **Źródło:** research, Rekomendacja 4–5: najtańszy hedge = smoke **po każdej aktualizacji przeglądarki**; przejście na C, gdy Google/Microsoft zablokują `--remote-debugging-port` dla niestandardowych profili albo serwis zacznie reagować na CDP; „obserwować release notes”. Brief/memlog: popupy aktualizacji Edge zamykane ręcznie, „możliwe wyłączenie auto-update”.
- **Spine:** AD-13 mówi „uruchamiany po każdej aktualizacji przeglądarki”, ale Edge jest evergreen i aktualizuje się sam; nic tego nie wykrywa ani nie uruchamia. AD-1 nazywa C planem awaryjnym bez **warunku wyzwalającego**. Smoke nie sprawdza też G1 (≥1080p / brak błędu DRM), czyli jedynego twardego warunku jakości z briefu.
- **Gdzie wylądować:** AD-13 (kto i kiedy: np. BrowserHost loguje wersję przeglądarki z `/json/version` i przy zmianie oznacza „smoke wymagany” w logu/kafelku; albo decyzja o zamrożeniu auto-update Edge po stronie autora), AD-1 (jawne triggery B→C), smoke: dodać krok jakości/DRM.

### M6. Pilot BT: wybudzanie i logger kalibracyjny sprzeczny z polityką logów

- **Źródło:** brief, krok 1 i memlog: wybudzenie przyciskiem G20S przetestowane **na dongle USB**; produkt docelowo to G20S Pro **BT**; spike: BT ma inne kody. Spike, „Poprawki harnessu” (`HomeLogger.cs`): logger kalibracyjny był keyloggerem, rozwiązanie = logować tylko klawisze „remote-like” (`RemoteLikeVk`) i HID Consumer.
- **Spine:** otwarte pytanie obejmuje tylko **kody** BT. Wybudzenie z Modern Standby przez BT (podstawowy krok wieczoru żony) nie jest nigdzie wymienione, ani decyzja „dongle czy BT jako docelowe połączenie”. Memlog: pierwsza story epiki wejścia = S10 przez BT **z loggerem kalibracyjnym**, a AD-13 mówi „InputHub loguje tylko Actions, **nigdy surowych klawiszy**”: sprzeczne, jeśli kalibracja żyje w InputHubie.
- **Gdzie wylądować:** Otwarte pytania (wybudzenie przez BT / wybór dongle vs BT), AD-13 (wyjątek: tryb kalibracji loguje tylko kody z listy remote-like + HID Consumer, nigdy liter/cyfr; wyłączony domyślnie).

## Mniejsze braki

- **Podwójne zdarzenia Home/Back** (spike S10): z jednego naciśnięcia przychodzi Raw Input HID 0x0223 **i** wstrzyknięty `VK_BROWSER_HOME` (+`WM_APPCOMMAND`). AD-8 mapuje „sygnaturą urządzenia i kodem”, ale VK z hooka LL nie niesie urządzenia. Brak reguły deduplikacji/wyboru źródła → ryzyko dwóch Actions `Home`. Miejsce: AD-8.
- **Pętla własnego wejścia:** `SendInput` (OSK natywne, `key:<VK>` w polityce Back, symulowany Alt przy foreground) przejdzie przez własny hook LL. Brak reguły ignorowania własnych wstrzyknięć (np. `dwExtraInfo` z sygnaturą). Miejsce: AD-8/AD-9.
- **Deferred z memlogu, które wypadły ze spine'u:** „Store-PWA jako tryb uruchomienia (osobna sesja SMTC)” (research, Rekomendacja 3) i „WebView2 shell prewarm aplikacji”. Miejsce: tabela Deferred.
- **Moonlight CLI (spike, poprawki harnessu):** Moonlight 6.2.0 to aplikacja GUI: `list`/`quit` dają wynik tylko na przekierowanym stdout, a `quit` nie kończy się sam (spike: czekaj maks. 30 s, potem zamknij). AD-10 mówi tylko „MediaStation sama zamyka klienta”. Miejsce: AD-10 (timeout `quit`), ewentualnie konwencja dla MoonlightHost.
- **Otwarte pytanie ze spike'u pominięte:** „Autoodtwarzanie i `play api`: dlaczego API playera nie wznawia po długiej pauzie; zależność od aktywacji użytkownika”. Wiąże się z M2. Miejsce: Otwarte pytania.
- **„MediaStation nie trzyma konta Filmwebu / żadnych wywołań Filmweb”** (forge, Odrzucone + Zakres): ograniczenie nie jest zapisane; zapobiega dryfowi w stronę nieoficjalnych API. QR URL budowany gdzie (Core vs Shell) nie jest wskazany. Miejsce: AD-12 (jedno zdanie).
- **Zegar dla `ratingPendingDays`:** `RatingPolicy` jest czysta i bez I/O, ale karta ma się pojawić po N dniach bez zdarzenia. Brak źródła czasu (np. Event `Tick`/ocena przy starcie i co dobę). Miejsce: AD-3/AD-12.
- **Shell wraca na wierzch:** reguła foreground z odblokowaniem w AD-5 dotyczy aktywacji aplikacji; brief i spike S10 wymagają tego samego dla **ekranu głównego MediaStation** po Home. Wynika z kontekstu, ale warto napisać wprost w AD-5 lub AD-8.

## Świadome decyzje, które wymagają aktualizacji źródeł

- **Autostart (memlog: decision „Autostart split”).** Brief, „Poza MVP”, nadal mówi: „Autostart z Windowsem i ukrycie pulpitu, które autor konfiguruje sam, poza produktem”. Spine (AD-13) bierze start „at logon” + restart po awarii do produktu. Dodatkowo tabela „MVP scope → architektura” podaje „Start po logowaniu i po Windows Update” jako **„Pozycja z briefu”**, a w briefie jej nie ma w „W MVP”. → Zaktualizować brief (rozdzielić: autostart/restart w produkcie; autologowanie, ukrycie pulpitu, WU poza) albo przepisać etykietę wiersza w spine.
- **OSK `[ASSUMPTION: do potwierdzenia w architekturze]`** w briefie: potwierdzone przez AD-9. → Usunąć marker w briefie, odesłać do AD-9.
- **Fallback dla ostatniego odcinka sezonu:** forge i brief mówią „Fallback do decyzji w architekturze”. AD-12 go rozstrzyga („serial w zawieszeniu”, `ratingPendingDays`). → Dopisać w forge/briefie odesłanie do AD-12.
- **Brief, „Wykrywanie automatyzacji … do sprawdzenia w pierwszym spike'u”:** zamknięte przez spike (S2/S3). → Oznaczyć jako zamknięte z odesłaniem do researchu.
- **Nazwy w researchu** (`ServiceAdapter`, `InputInterceptor`, `navigate`, `playbackState`) zastąpione w spine (`ServiceDriver`, `InputHub` + `WindowManager`, `action`, `playback`). Research jest materiałem historycznym; aktualizacja opcjonalna, ale warto w Rekomendacji 1 („Feeds”) dodać notkę o mapowaniu nazw.

## Co sprawdzono i wylądowało poprawnie

AD-1 (flagi, zakazy, runner-up C), AD-2/AD-8 (hook LL w osobnym wątku, połykanie Home/Back), AD-5 (pause → mute → minimize, nigdy `SW_HIDE`, unlock foreground, `IChannelAudioVolume`, ponawianie przy nowych sesjach, Moonlight flags), AD-6 (bundle niezależny od transportu, buforowanie, `addBinding`), AD-7 (`screen{limit|error|profiles}`, reguła Back z timeoutem), AD-9 (OSK `insertText`/`SendInput`, fallback in-page), AD-10 (lazy start, adopcja sierot, Standby, reconnect, `/browse`, zamykanie Moonlighta), AD-11 (tabela serwis → przeglądarka w danych, Edge dla Netflixa), AD-12 (progi 90%, raz na zawsze, „Pomiń”), AD-13 (hotfix drivera, loopback, brak logowania tekstu OSK, smoke), Deferred (S12, HDR/TV, toast QR, Wikidata, tryb okna), zasady briefu „Jeden dom”, „Aplikacje żyją w tle”, „Słychać tylko aktywną”, „Serwis decyduje o przeglądarce”, „Netflix jako wzorzec”.
