---
title: 'Spike S1–S11: sterowanie przeglądarką na Netfliksie (wyniki)'
status: draft
created: 2026-10-11
updated: 2026-10-11
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
| Data sesji 1 / sesji 2 | |
| Sprzęt | Minisforum UM760, monitor FHD z HDR (bez TV) |
| Pilot | G20S Pro przez **dongle USB 2.4G** (nie Bluetooth; Pro i Pro BT wysyłają różne kody, więc wyniki S10 dotyczą tylko dongla) |
| Windows (build) | |
| Edge (wersja z logu `launch`) | |
| Chrome (wersja z logu `launch`) | |
| Node.js / playwright-core | |
| Moonlight-qt / host | |
| Plan Netflix | |

## Podsumowanie

| # | Test | Sesja | Werdykt | Jedno zdanie wniosku |
|---|---|---|---|---|
| S1 | Jakość i DRM w przeglądarce uruchomionej przez MediaStation | 1 | | |
| S4 | Widevine w nowym profilu | 1 | | |
| S2 | Klient CDP: raw kontra connectOverCDP | 1 | | |
| S3 | Kontrola negatywna: Playwright launch | 1 | | |
| S6 | Wejście: klawisze (A) i zamienniki C (B) | 1 | Werdykt B: … · Werdykt C: … | |
| S5 | `--enable-spatial-navigation` | 1 | | |
| S7 | Reguła Back „cofnij lub wyjdź” | 1 | | |
| C1 | Wariant C: jakość i ślad automatyzacji | po sesji 1, jeśli starczy czasu | | |
| C2 | Wariant C: S6 w rozszerzeniu | po sesji 1, jeśli starczy czasu | | |
| C3 | Wariant C: Back i okno | po sesji 1, jeśli starczy czasu | | |
| S10 | Home z G20S Pro (dongle) i foreground | 2 | | |
| S8 | Pauza i 30 min w tle | 2 | | |
| S9 | Wyciszanie drzewa procesów | 2 | | |
| S11 | Moonlight ukryty na 5 min | 2 | | |
| S12 | Prime Video, Max, Disney+ | odłożony | ⏭️ | |
| — | HDR i Atmos w Edge na TV | odłożony | ⏭️ | |

**Decyzja po spike'u:** ☐ B potwierdzone · ☐ B z zastrzeżeniami: … · ☐ przejście na C · ☐ inne: …

---

# Sesja 1: S1, S4, S2, S3, S6, S5, S7

Przygotowanie: `cd spike; npm install`. Zamknij Edge i Chrome uruchomione z innymi profilami na portach 9223 i 9224. Przy pierwszym `launch` zaloguj się do Netfliksa w profilach `edge-netflix` i `chrome-netflix`, a przy pierwszym `serve --mode pwlaunch` w `edge-pw`. Przed testami C zainstaluj rozszerzenie w `edge-ext` i `chrome-ext` (README, „Ręczne Load unpacked”).

Testuj na jednym, stałym odcinku. Zapisz go tutaj: …

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
| Edge | | | | | | | |
| Chrome | | | | | | | |

HDR w Windows (Ustawienia → Ekran → HDR) włączony podczas testu? … · Logi: … · Uwagi: …

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
| Chrome, pusty profil | | | | |
| Chrome, skopiowany CDM | | | | |
| Edge, pusty profil | | | | |

Logi: … · Uwagi: …

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
| Edge (a) raw | | | | | |
| Edge (b) connectOverCDP | | | | | |
| Chrome (a) raw | | | | | |
| Chrome (b) connectOverCDP | | | | | |

Logi: … · Uwagi: …

## S3: kontrola negatywna, Playwright launch

**Cel:** pomiar ryzyka G2. Czy Netflix reaguje na przeglądarkę z `webdriver=true` (pipe)?

**Kroki:**

1. W tle `node harness/ms.mjs serve --mode pwlaunch --browser edge --label S3-edge-stripped`. Przy pierwszym uruchomieniu zaloguj się w profilu `edge-pw`.
2. `cmd webdriver` powinno dać `true`. Odtwórz odcinek, sprawdź nakładkę i błędy, a potem `cmd quit`, co zamyka też przeglądarkę.
3. `serve --mode pwlaunch --browser edge --pw-defaults --label S3-edge-defaults` i powtórz krok 2. Spodziewany efekt: brak aktualizacji CDM lub gorsza jakość przez flagi.

**Kryterium:** to pomiar, a nie test zaliczany. Czy Netflix gra; czy jakość i błędy różnią się od S1.

| Wariant | webdriver | Infobar | Gra? | Rozdzielczość | Błędy, ostrzeżenia Netfliksa |
|---|---|---|---|---|---|
| ignoreDefaultArgs (stripped) | | | | | |
| domyślne flagi Playwrighta | | | | | |

Wniosek dla G2: …

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
| player | Space | | | | |
| player | ArrowRight | | | | |
| player | ArrowLeft | | | | |
| player | f | | | | |
| player | m | | | | |
| player | Escape | | | | |
| modal | Escape | | | | |
| browse | Enter | | | | |

Zamienniki dla playera bez klawiszy: `cmd "pause api"`, `cmd "play api"`, `cmd "pause video"`, `cmd "play video"`. Działają? …

### Część A2: fizyczne strzałki i OK z pilota, z flagą spatial navigation i bez niej

Krótki przegląd pilotem (dongle). Pełna macierz ekranów jest w **S5** i nie trzeba jej tu powtarzać. A2 ustala tylko, czy strzałki i OK z pilota docierają jako zwykłe klawisze (log `key`: `key`, `code`, `trusted=true`) i co robią na `/browse` oraz w modalu w obu konfiguracjach przeglądarki.

1. **Bez flagi:** przeglądarka z `launch --browser edge` (bez `--spatial`), `serve --mode raw`. Na `/browse` i w modalu naciśnij pilotem ↑ ↓ ← → i OK.
2. **Z flagą:** `cmd quit`, zamknij przeglądarkę, `launch --browser edge --spatial`, ponownie `serve --mode raw`, a potem te same naciśnięcia.

| Konfiguracja | Ekran | Strzałki pilota: co się dzieje (fokus z logu `focus`) | OK pilota: co się dzieje | `key`/`code` z logu |
|---|---|---|---|---|
| bez `--enable-spatial-navigation` | `/browse` | | | |
| bez `--enable-spatial-navigation` | modal | | | |
| z `--enable-spatial-navigation` | `/browse` | → S5 (pełna macierz) | | |
| z `--enable-spatial-navigation` | modal | → S5 | | |

### Część B: zamienniki, z których korzystałby C

**Kroki** (bez `--spatial`):

1. **Programowy fokus:** na każdym ekranie `cmd "focus down"`, `cmd "focus right"` itd. Czy Netflix pokazuje fokus (obwódkę lub powiększenie kafelka) i czy fizyczny Enter aktywuje element z fokusem? Na końcu wiersza zanotuj pole `fallback` z wyniku: `dom-order` (następny kafelek w DOM ze `scrollIntoView`), `row-arrow` (klik strzałki przewijania wiersza; pole `arrow` pokazuje dopasowany selektor) albo `none-found`.
2. **Niezaufany klik:** po `focus …` wykonaj `cmd activate`. Czy Netflix reaguje tak jak na fizyczny Enter lub kliknięcie myszą?
3. **Tekst:** fokus w polu wyszukiwania (`focus …` albo fizycznie), potem po kolei: `cmd "type abc"` (CDP, zaufane), `cmd "jstype exec abc"`, `cmd "jstype value abc"`. Między próbami wyczyść pole. Czy pojawia się `/search?q=` i wyniki (log `route`)?
4. **End-to-end z pilotem:** `cmd "adapter on"`, a potem nawigacja **wyłącznie pilotem**: wybór profilu, wiersze (także poza prawą krawędź), otwarcie modala, Play, wyszukiwanie (pole, wyniki). Na `/watch` adapter wyłącza się sam.

| Mechanizm | Profile | `/browse` kafelki | Koniec wiersza (`fallback`) | Modal: Play i lista odcinków | `/search` wyniki | Player: kontrolki |
|---|---|---|---|---|---|---|
| `focus …` (widoczny fokus?) | | | | | | |
| `focus` + fizyczny Enter | | | | | | |
| `activate` (untrusted click) | | | | | | |
| `adapter on` z pilotem | | | | | | |

| Tekst w polu wyszukiwania | Wpisuje się? | Netflix szuka (`/search?q=`)? |
|---|---|---|
| `type` (CDP `Input.insertText`, trusted) | | |
| `jstype exec` (`execCommand`) | | |
| `jstype value` (setter + `input`) | | |

Dopasowane selektory wiersza i strzałek (z pól `row` i `arrow`): …

### Kryteria S6

- **B zaliczone:** w części A1 `key` działa jak klawisz fizyczny, a `type` wpisuje i wyszukuje.
- **C realne:** w części B działają `focus` (widoczny fokus, także poza ekranem przez `fallback`), `activate` oraz `jstype exec` lub `jstype value`, a adapter z pilotem przechodzi cały wieczór. Player w C da się obsłużyć przez `pause api`/`play api` albo przez `jskey`.
- Jeśli w części B `activate` nie działa, ale fizyczny Enter na elemencie z fokusem działa, C nadal jest realne: adapter przesuwa tylko fokus, a OK z pilota przechodzi do przeglądarki bez zmian.

Werdykt B: … · Werdykt C: … · Logi: …

## Bramka po S2 i S6: rekomendacja B czy C

Sprawdź po S6, przed S5 i S7. Bramka decyduje o **rekomendacji** i o tym, na którym transporcie robimy S5 i S7. Nie decyduje o tym, czy testujemy C: C1–C3 wykonujemy zawsze po sesji 1, jeśli starczy czasu.

| Warunek | Spełniony? |
|---|---|
| S2: z podłączonym klientem raw Netflix gra gorzej, pokazuje błąd albo `webdriver` jest `true` | |
| S2: problem występuje tylko w (b) `connectOverCDP` | → wybór transportu, B zostaje (raw) |
| S6: B niezaliczone (`key` nie działa jak klawisz fizyczny albo `type` nie wpisuje) | |
| S6: C realne (kryteria wyżej) | |

- **Ani S2 (raw), ani S6-B nie wypadły źle:** rekomendacja B. S5 i S7 na B (raw), potem C1–C3 jako porównanie z runner-upem.
- **S2 (raw) albo S6-B wypadły źle i C jest realne:** rekomendacja C. S5 i S7 robimy od razu w wariancie C (`serve --mode ext`), a C1–C3 mają priorytet przed końcem sesji.
- **S2 albo S6-B wypadły źle i C nie jest realne:** żaden kandydat nie spełnia wymagań. Dokończ C1–C3 dla pełnego obrazu i otwórz decyzję z researchu ponownie.

Decyzja bramki: … · Uzasadnienie: …

## S5: `--enable-spatial-navigation`

**Cel:** które ekrany Netfliksa obsługują strzałki „za darmo”, a które wymagają mapy fokusu.

**Kroki** (osobno Chrome i Edge):

1. `node harness/ms.mjs launch --browser chrome --spatial --label S5-chrome-launch`, potem w tle `serve --mode raw --browser chrome --label S5-chrome`. Serve jest tu tylko po to, żeby logować `focus`.
2. Na każdym ekranie nawiguj **strzałkami pilota** (dongle) i klawiatury, OK lub Enter wybiera. Dla porównania użyj też `cmd "key ArrowDown"` itd. Log `focus` pokaże element z `data-uia` i `aria-label`, co posłuży jako materiał do mapy fokusu. Nie włączaj `adapter on`.

**Kryterium:** dla każdego ekranu wiadomo „działa”, „częściowo” (co nie działa) albo „nie działa”.

| Ekran | Chrome | Edge | Co nie działa, przydatne selektory z logu |
|---|---|---|---|
| wybór profilu | | | |
| `/browse`: wiersze i kafelki | | | |
| `/browse`: menu górne | | | |
| modal szczegółów (`jbv`) | | | |
| `/search`: pole i wyniki | | | |
| player: kontrolki | | | |

Czy strzałki z pilota docierają jako zwykłe strzałki? … · Logi: …

## S7: Back, reguła „cofnij lub wyjdź”

**Cel:** deterministyczna reguła, kiedy Back cofa w serwisie, a kiedy wychodzi do MediaStation.

**Kroki** (`serve --mode raw --browser edge --label S7-edge`): na każdym ekranie wypróbuj `cmd "back esc"`, `cmd "back history"` i `cmd "back modal"`. Wynik każdej komendy zawiera stan przed (`before`) i po (`after`). Wróć do ekranu startowego przed kolejną metodą. Sprawdź też, czy `cmd state` poprawnie rozpoznaje `profiles` i `browse` (`route.name`, `profileSel`, `modalSel`). Fizyczny Back pilota: zanotuj, jaki klawisz przychodzi (log `key`).

**Kryterium:** reguła daje ten sam wynik przy każdym powtórzeniu.

| Ekran startowy | `back esc` → | `back history` → | `back modal` → | Pożądane zachowanie |
|---|---|---|---|---|
| `/browse?jbv=…` (modal) | | | | zamknij modal |
| `/title/…` | | | | `/browse` |
| `/search?q=…` | | | | `/browse` |
| `/watch/…` | | | | strona, z której weszliśmy |
| `/browse` | | | | wyjdź do MediaStation |
| wybór profilu | | | | wyjdź do MediaStation |

**Reguła** (do wypełnienia):

```text
jeśli route == watch            -> …
jeśli route == browse-modal     -> …
jeśli route in (title, search)  -> …
jeśli route in (browse, profiles) -> wyjdź do MediaStation
```

Wykrywanie `/browse` i ekranu profili działa? · Back pilota przychodzi jako: … · Logi: …

## Wariant C: rozszerzenie bez CDP (C1–C3)

Wykonujemy **zawsze po sesji 1, jeśli starczy czasu**, niezależnie od wyniku bramki. Bramka decyduje tylko o rekomendacji i o kolejności. Instalacja rozszerzenia: [README](README.md#ręczne-load-unpacked-do-testów-c1c3). Profile to `edge-ext` i `chrome-ext`, uruchamiane przez `launch --no-cdp`, bez portu debugowania. Serve: `serve --mode ext --label C…`.

Środowisko C: wersja rozszerzenia … · `swStats` z linii „rozszerzenie połączone” … · Czy przeglądarka pokazuje ostrzeżenie o rozszerzeniu w trybie dewelopera lub infobar? …

### C1: jakość, DRM i ślad automatyzacji z rozszerzeniem

**Kroki:** `launch --browser edge --no-cdp`, w tle `serve --mode ext --label C1-edge`, potem `cmd state`. Odtwórz ten sam odcinek i po około 60 s sprawdź nakładkę **fizycznym** `Ctrl+Alt+Shift+D`, bo w C nie ma kombinacji klawiszy. Potem to samo w Chrome.

**Kryterium:** jakość i błędy takie jak w S1; `webdriver === false`; rozszerzenie dostaje zdarzenia `video`, `route` i `focus`.

| Przeglądarka | webdriver | Rozdzielczość | Bitrate | HDR (orientacyjnie) | Błędy, różnice względem S1 | Zdarzenia w logu? | Werdykt |
|---|---|---|---|---|---|---|---|
| Edge | | | | | | | |
| Chrome | | | | | | | |

### C2: S6 w prawdziwym rozszerzeniu

**Kroki** (`serve --mode ext --label C2-edge`): powtórz kolumnę `jskey` z S6 A1 (w C `key` działa tak samo jak `jskey`) i całą część B, w tym `adapter on` z pilotem. Wyniki powinny pokrywać się z pomiarem przez raw. Zanotuj różnice.

| Element | Wynik w C | Zgodny z S6 (raw)? |
|---|---|---|
| player: `jskey Space` / `ArrowRight` / `Escape` | | |
| player: `pause api` / `play api` | | |
| `focus` + `activate` na `/browse` i w modalu, `fallback` na końcu wiersza | | |
| `jstype exec` / `jstype value` w wyszukiwarce | | |
| `adapter on` z pilotem: cały wieczór | | |

### C3: Back i okno w wariancie C

**Kroki:** tabela S7 przez `serve --mode ext` (`back esc` wysyła tu niezaufany Escape), plus `cmd "win minimized"`, `cmd "win normal"` i `cmd front` przez `chrome.windows.update`. Opcjonalnie `cmd "s8 0.5 api minimized"`.

| Ekran startowy | `back esc` (untrusted) → | `back history` → | `back modal` → |
|---|---|---|---|
| `/browse?jbv=…` | | | |
| `/title/…` | | | |
| `/search?q=…` | | | |
| `/watch/…` | | | |

`win`, `front` i `s8` działają? … · Werdykt C: … · Logi: …

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
| Urządzenie dongla (VID/PID, usage page z linii `new raw input device`) | |
| Home: LL hook (vk, scan, flags) | |
| Home: Raw Input klawiatura (VK, make) | |
| Home: Raw Input HID Consumer (bajty raportu, usage) | |
| Home: WM_APPCOMMAND / HSHELL_APPCOMMAND (cmd) | |
| Back / OK / strzałki / Menu: kody | |

| Pierwszy plan | Home przechwycony? | `plain` na wierzchu? | `attach` | `alt` | Przeglądarka sama zareagowała na Home? |
|---|---|---|---|---|---|
| Edge | | | | | |
| Chrome | | | | | |
| Moonlight (stream) | | | | | |
| Pulpit | | | | | |

`-Swallow` blokuje reakcję przeglądarki? … · Werdykt: … · Logi: …

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
| A | api | minimized | 30 min | | | | |
| B | video | hidden (SW_HIDE) | 30 min | | | | |

Werdykt: … · Logi: …

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
| Edge | | | | | | |
| Chrome | | | | | | |

Werdykt: … · Logi: …

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
| min, 5 min | | | | | |
| hide, 5 min | | | | | |
| min + `--mute-on-focus-loss` | | | | | |

Werdykt: … · Logi: …

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

# Otwarte pytania po spike'u

- …
