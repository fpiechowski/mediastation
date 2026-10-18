---
title: 'Adversary review: aktualizacja AD-12 i AD-13 (2026-10-18)'
target: ../architecture-mediastation.md
scope: 'AD-12 (tożsamość serialu, dopasowanie, back-fill seriesId), AD-13 (wersja przeglądarki poza CDP, CdpUnavailable, smoke)'
context: [../.memlog.md (4 wpisy z 2026-10-18), review-adversary.md (H6), ../../spec-mediastation/spec-mediastation.md (CAP-7, CAP-11)]
date: '2026-10-18'
---

# Adversary review: aktualizacja AD-12 i AD-13

## Werdykt

**Nie zatwierdzać bez poprawek.** 1 critical, 4 high, 6 medium, 5 low. Najgorsze: dosłowne brzmienie reguły dopasowania w AD-12 odtwarza ten sam duplikat i fałszywą kartę `pending`, które ta zmiana miała usunąć, tylko przy odwrotnej kolejności faktów. W AD-13 `CdpUnavailable` zależy od timeoutu, który według AD-14 należy do Orchestratora, więc sygnał planu C może zginąć w wyścigu albo dać fałszywy alarm. Wszystkie poprawki mieszczą się w tekście AD-12 i AD-13. Protokół strony zostaje v1. Tam, gdzie poprawka dotyka interpretacji innego AD, jest to zaznaczone.

## Podsumowanie

| # | Tier | Para / journey | Dziura | Poprawka |
| --- | --- | --- | --- | --- |
| C1 | critical | RatingPolicy × Netflix driver | Fakt bez `seriesId` nie dopasowuje się po tytule do rekordu, który `seriesId` już zna | AD-12: strażnik tylko przy dwóch znanych i różnych `seriesId` |
| H1 | high | RatingPolicy, journey wielojęzyczny | Osierocony rekord `pending` bez `seriesId` daje fałszywą kartę w trakcie sezonu (łamie Prevents, nie tylko „duplikat”) | AD-12: dopasowanie zwraca zbiór, back-fill i reset w każdym rekordzie, jedna karta |
| H2 | high | BrowserHost × Orchestrator (AD-14) × smoke | Kto mierzy timeout `Starting`, kiedy `Starting` się kończy, wyścig `TimerElapsed` z `CdpUnavailable` | AD-13: czas mierzy Orchestrator, klasyfikację robi host na `CancelStart` |
| H3 | high | BrowserHost × smoke, journey sierota/port | Fałszywy plan C (port zajęty, handoff do sieroty) i fałszywie zielony smoke na starej wersji | AD-13: kontrole przed startem, port należy do drzewa `rootPid`, równość wersji |
| H4 | high | BrowserHost × Orchestrator × AudioRouter | Po `CdpUnavailable` żywy Edge gra zwiastun billboardu, a Orchestrator nie zna `rootPid` | AD-13: `AppWindowAttached` niezależnie od CDP, `Starting` kończy `CommandCompleted` |
| M1 | medium | Orchestrator × Store | Fakty `playback` i sprawdzenie `pending` przed `StoreLoaded` | AD-12: RatingPolicy nieaktywna do `StoreLoaded` |
| M2 | medium | RatingPolicy × Shell UI (karta, QR) | Rekord ma tylko `titleNorm`, a karta i QR potrzebują surowego tytułu | AD-12: pole `title` w rekordzie, zamrożone przy pierwszym zapisie |
| M3 | medium | RatingPolicy × driver | Niezdefiniowana normalizacja: pusty `titleNorm`, skrypty niełacińskie, zmiana funkcji | AD-12: Unicode-aware, pusty → fakt ignorowany, zmiana = migracja |
| M4 | medium | Orchestrator × Store | Kolejność wielu `Persist` w locie (back-fill zwiększa liczbę zapisów) | AD-12: Store zapisuje sekwencyjnie albo latest-wins po `rev` |
| M5 | medium | BrowserHost × Updater Edge | Wersja z pliku ≠ wersja uruchomiona (staged update), `getVersion` jako drugi writer | AD-13: jedna ścieżka exe, jeden writer, `BrowserVersionMismatch` |
| M6 | medium | BrowserHost × Config / profil / logi | „Poza `data/`” wskazuje tylko miejsca zakazane albo ulotne | AD-13: nazwać plik i katalog |
| L1–L5 | low | różne | Wiele dopasowań, zakres `service`, kolizja przestrzeni kluczy, „kolejny odcinek”, reconnect, kody wyjścia smoke, zamknięcie bez CDP | patrz niżej |

---

## Critical

### C1: Fakt bez `seriesId` nie trafia do rekordu, który `seriesId` zna

**Para:** story `RatingPolicy` (CAP-11) × story drivera `netflix` (CAP-8).

AD-12: „po tytule tylko wśród rekordów z `seriesId` równym `null` albo takim samym”. „Takim samym” odnosi się do `seriesId` faktu. Dla faktu z `seriesId: null` warunek zwęża się więc do rekordów z `seriesId == null`. Człon „albo takim samym” jest przy tym martwy, bo rekord z tym samym `seriesId` dopasował już krok pierwszy. To sugeruje, że autor reguły myślał tylko o fakcie, który `seriesId` zna.

**Journey:** Eliza otwiera serial ze strony tytułu, a driver zna `seriesId = S`. Powstaje rekord `pending` `R1 (S, "dark")` z kluczem `netflix:series:S`. Następnego dnia wchodzi przez „Kontynuuj oglądanie” (`/watch/<episodeId>`), a driver `seriesId` nie ustali, bo AD-12 wymaga go tylko „gdy da się”. Fakt `(null, "dark")` nie pasuje do `R1`, bo `S ≠ null`. Powstaje `R2` z kluczem `netflix:series:dark`. Po 7 dniach `R1` daje fałszywą kartę w trakcie sezonu, a potem `R2` daje drugą kartę. To dokładnie odrzucony wariant „a” z memloga, tylko w odwrotnej kolejności.

Dwie implementacje zgodne z literą: (1) dosłowna, z duplikatem; (2) „strażnik konfliktu”, w której fakt `null` pasuje po tytule do dowolnego rekordu. Testy CAP-11 nie rozstrzygną, która jest poprawna.

**Poprawka AD-12** (zastępuje zdanie o tytule):

> `RatingPolicy` dopasowuje fakt do rekordu serialu tego samego `service`: najpierw po `seriesId`, potem po `titleNorm`. Dopasowanie po tytule jest zabronione tylko wtedy, gdy fakt i rekord mają oba znane i różne `seriesId`. Fakt bez `seriesId` dopasowuje się po tytule także do rekordu, który `seriesId` zna.

---

## High

### H1: Osierocony `pending` bez `seriesId` daje fałszywą kartę, a nie tylko duplikat

**Journey wielojęzyczny:** profil PL bez `seriesId` tworzy `R1 (null, "dom z papieru")`, `pending`. Profil EN z `seriesId` tworzy `R2 (S, "money heist")`, `pending`. Potem profil PL dostaje fakt `(S, "dom z papieru")`. Krok pierwszy trafia w `R2`, więc tytułu nie sprawdza się wcale. `R1` nie dostaje back-fillu ani resetu terminu i po 7 dniach daje kartę, choć PL ogląda dalej. Zaakceptowane ryzyko mówi tylko o „duplikacie”. Fałszywa karta w trakcie sezonu łamie natomiast wprost Prevents AD-12. Ten sam mechanizm działa przy zmianie języka profilu i przy zmianie nazwy serialu przez Netflix.

**Poprawka AD-12:**

> Dopasowanie zwraca zbiór: rekordy z `seriesId` faktu oraz rekordy z `seriesId == null` i `titleNorm` faktu. Back-fill `seriesId` i przesunięcie terminu `pending` dotyczą każdego rekordu ze zbioru, w każdym statusie. Rekordy o tym samym `seriesId` dzielą status: zmiana na `queued`, `rated` albo `skipped` przenosi się na wszystkie, a kartę pokazuje tylko najstarszy. Rekord zapamiętuje każdy zaobserwowany `titleNorm` (`titleNorms[]`).

Zaakceptowane ryzyko zawęża się wtedy do przypadku, w którym żaden fakt danego profilu nie niesie `seriesId`. Rozszerz też jego brzmienie na „różne tytuły tego samego serialu (język profilu, zmiana nazwy)”.

### H2: Timeout `Starting` ma dwóch właścicieli, a sygnał planu C ginie w wyścigu

**Para:** story BrowserHost (CAP-7) × story Orchestratora i cyklu życia × story smoke (CAP-12/CAP-8).

AD-14: „Każdy timeout (… `Starting` …) to `TimerElapsed`, zaplanowany przez Orchestrator”. AD-13: BrowserHost odpowiada `CdpUnavailable`, gdy port nie przyjmie połączenia „do końca timeoutu `Starting`”. Zgodne z literą implementacje:

- BrowserHost trzyma własny timer z tą samą wartością z `settings.json`. Oba timery strzelają w tej samej chwili. Orchestrator przechodzi w `Failed` na `TimerElapsed`, a `CommandFailed{CdpUnavailable}` przychodzi po fakcie i jest ignorowane jako wynik zamkniętej komendy. Rozróżnienie zostaje tylko w logu hosta, a smoke go nie widzi.
- BrowserHost nie ma timera, czyli czeka w nieskończoność, i `CdpUnavailable` nie powstaje nigdy.
- Home w trakcie `Starting` (najdłuższe możliwe okno, bo `CdpUnavailable` zawsze czeka do końca) zastępuje `transitionId`. Orchestrator, zgodnie z AD-14, ignoruje wynik ze starego `transitionId`. Jeśli przy tym anuluje timer przejścia, kafelek zostaje w `Starting`, a OK na `Starting` jest ignorowane (AD-8). Kafelka nie da się odzyskać.
- Smoke nie ma Orchestratora albo ma własną pętlę, więc nie wiadomo, kto planuje timeout i z jaką wartością.

**Poprawka AD-13:**

> Timeout `Starting` mierzy wyłącznie Orchestrator (AD-14). Po `TimerElapsed` wysyła `CancelStart{appId}`. BrowserHost odpowiada na `cmdId` komendy startu jednym `CommandFailed`, a kod zależy od stanu: `CdpUnavailable` (`rootPid` żyje, port nie przyjmuje), `StartTimeout` (pozostałe) albo, jeśli proces już padł, `ProcessExited` z wcześniejszego zdarzenia. Odpowiedź na komendę startu zawsze przeprowadza aplikację w `Failed`, niezależnie od `transitionId`, który ogranicza tylko skutki dla foreground. Smoke uruchamia prawdziwy Orchestrator ze skryptowanymi Intents i wartościami z `settings.json`.

Flaga: zdanie o `transitionId` doprecyzowuje interpretację AD-14, nie jego tekst. Jeśli architekt uzna je za zmianę AD-14, trzeba to zrobić świadomie.

### H3: Fałszywy plan C i fałszywie zielony smoke przy sierocie albo zajętym porcie

**Para:** BrowserHost × smoke, z adopcją z AD-10.

- **Port zajęty przez obcy proces** (stary Edge po zmianie `cdpPort` w configu, debugger IDE): Chromium nie zbinduje portu i działa bez CDP. W zależności od implementacji BrowserHost albo podepnie się do obcego endpointu (kontrola profilu istnieje tylko przy adopcji), albo zgłosi `CdpUnavailable`, a smoke wyda kod planu C, choć przeglądarka niczego nie zablokowała.
- **Handoff do sieroty:** Edge na `profiles\netflix` żyje bez CDP (np. przywrócony przez Windows po restarcie z WU), więc `ProbeExisting` go nie adoptuje. Start z tym samym `--user-data-dir` przekazuje okno istniejącej instancji, a uruchomiony proces kończy się kodem 0. Story A raportuje `ProcessExited` i każda próba „Uruchom ponownie” kończy się tak samo. Story B uznaje „proces przeglądarki żyje” per profil i zgłasza `CdpUnavailable`, czyli fałszywy plan C.
- **Fałszywie zielony smoke:** po aktualizacji, przy zatrzymanej instancji głównej (mutex), stary Edge 155 z CDP zostaje żywy, bo AD-10 nie zabija przeglądarek. Smoke czyta z pliku wersję 156, startuje, następuje handoff, smoke podpina się do portu starego 155 i przechodzi. Autor uznaje 156 za sprawdzoną.

**Poprawka AD-13:**

> Przed startem BrowserHost sprawdza, że `cdpPort` jest wolny i że profil `profiles\<appId>` nie jest używany. W przeciwnym razie odpowiada `CommandFailed{PortInUse | ProfileInUse}` bez uruchamiania. „Proces żyje” oznacza `rootPid` uruchomiony przez tę komendę. Podpięcie jest ważne tylko wtedy, gdy port należy do drzewa `rootPid`. Smoke kończy się błędem, gdy `Browser.getVersion` różni się od wersji odczytanej przed startem. Kod wyjścia planu C dotyczy wyłącznie `CdpUnavailable`.

### H4: `CdpUnavailable` zostawia żywy, głośny i niewidoczny dla Orchestratora Edge

**Para:** BrowserHost × Orchestrator × AudioRouter/WindowManager (AD-15).

Netflix na `/browse` sam odtwarza zwiastun billboardu z dźwiękiem (`--autoplay-policy=no-user-gesture-required`). Jeśli story BrowserHost raportuje `AppWindowAttached{hwnd, rootPid}` dopiero po CDP, to przy `CdpUnavailable` Orchestrator nie zna `rootPid` ani `hwnd`. AudioRouter i WindowManager działają tylko na id z Command, więc nie wyciszą ani nie zminimalizują okna, które Windows wyniósł przy starcie. Eliza wraca na ekran główny i słyszy zwiastun. Jeśli z kolei story BrowserHost raportuje `AppWindowAttached` od razu, a story Orchestratora traktuje je jako koniec `Starting`, aplikacja jest już `Foreground`, kiedy przychodzi `CdpUnavailable`, i przejście jest niezdefiniowane. Nie wiadomo też, czy host po `CdpUnavailable` zabija proces.

**Poprawka AD-13:**

> Dla `web` BrowserHost raportuje `AppWindowAttached` zaraz po pojawieniu się okna, niezależnie od CDP. `Starting` kończy wyłącznie `CommandCompleted` komendy startu, wysyłany po podpięciu CDP i `addBinding`. Po `CdpUnavailable` host nie zabija procesu. Orchestrator stosuje do aplikacji w `Failed` regułę opuszczania (`mute`, `minimize`), a „Zamknij” bez CDP kończy drzewo `rootPid` bez czekania na `closeReady`.

Flaga: „Failed jest wyciszony” wynika już z AD-5 („słychać tylko aktywną aplikację”), więc nie zmienia AD-5 ani AD-10, ale warto to potwierdzić przy następnej rewizji AD-10.

---

## Medium

### M1: `RatingPolicy` przed `StoreLoaded`

**Para:** Orchestrator × Store. Po restarcie przez `--supervise` adoptowany Edge może nadal odtwarzać, bo AD-5 pauzuje tylko przy opuszczaniu. `playback ≥ 0.9` przychodzi przed `StoreLoaded`, a dopasowanie do pustego zbioru tworzy nowy rekord. Potem `StoreLoaded` albo nadpisuje stan w pamięci (rekord ginie), albo jest scalany (duplikat). Sprawdzenie `pending` „przy starcie” na pustym zbiorze opóźnia karty o dobę.

**AD-12:** „Orchestrator nie stosuje `RatingPolicy`, dopóki nie przyjdzie `StoreLoaded`. Wcześniejsze fakty `playback` są buforowane. Sprawdzenie `pending` przy starcie następuje po `StoreLoaded`.”

### M2: Karta i QR nie mają z czego wziąć tytułu

**Para:** `RatingPolicy` × Shell UI (karta „Do oceny”). Reguła mówi, że rekord trzyma `seriesId` i `titleNorm`. Story karty buduje więc URL Filmwebu z `titleNorm` (małe litery, bez diakrytyków, zły wynik wyszukiwania), a story `RatingPolicy` sama dodaje inne pole. Przy H1 nie wiadomo, w którym języku jest tytuł.

**AD-12:** „Rekord trzyma `title` (surowy, z pierwszego zapisu, niezmienny) dla karty i QR. `titleNorm` służy wyłącznie dopasowaniu.”

### M3: Normalizacja tytułu bez definicji

Pusty albo niemal pusty `titleNorm` (normalizacja ASCII obcina tytuły koreańskie i japońskie, tytuły z samych symboli) skleja niezwiązane seriale pod kluczem `netflix:series:`. Driver może też przy `kind: episode` nie znać tytułu serialu i wysłać `null`, tytuł odcinka albo `"Serial: Część 3"`. Poprawka funkcji normalizacji rozjeżdża zapisane `titleNorm` z nowo liczonymi.

**AD-12:** „Normalizacja: NFKC + casefold + zwinięte białe znaki, litery wszystkich pism zachowane. Fakt `episode` bez `seriesId` i z pustym `titleNorm` nie tworzy ani nie dopasowuje rekordu. Driver, który nie zna tytułu serialu, wysyła `title: null`. Zmiana funkcji normalizacji = migracja `schemaVersion`.”

### M4: Kolejność `Persist`

Commands są fire-and-forget (AD-14), a back-fill i resety `pending` mnożą zapisy. Story Store, która wykonuje `Persist` równolegle, może pozwolić starszemu dokumentowi nadpisać nowszy. Nic w AD-12 tego nie zabrania.

**AD-12:** „Store wykonuje `Persist` sekwencyjnie w kolejności przyjęcia (albo pomija dokument starszy niż ostatnio zapisany, po `rev`). `CommandFailed` → Orchestrator ponawia z bieżącym dokumentem.”

### M5: Wersja z pliku ≠ wersja uruchomiona; dwóch writerów „poprzedniej wersji”

Edge aktualizuje się w tle przy działającej przeglądarce (staged `new_msedge.exe`, katalog wersji). Rejestr `EdgeUpdate\…\pv`, plik `msedge.exe` i proces, który faktycznie przyjmie start (H3: handoff), mogą wskazywać trzy różne wersje. Jeśli `Browser.getVersion` przy podpięciu też aktualizuje zapisaną wersję, `BrowserVersionChanged` miga 155→156→155 przy kolejnych startach i adopcjach. Nie wiadomo też, czy `BrowserVersionChanged` to tylko wpis w logu (CAP-7), czy Event w Core (kształt `{…}` to sugeruje).

**AD-13:** „Wersję czyta się z pliku exe pod ścieżką, którą BrowserHost uruchomi, i loguje razem ze ścieżką. Zapisaną wersję aktualizuje tylko odczyt przed startem. `Browser.getVersion` jej nie zmienia, a różnica daje osobny wpis `BrowserVersionMismatch`. `BrowserVersionChanged` i `BrowserVersionMismatch` to wyłącznie wpisy logu, nie Events Core.”

### M6: „Poza `data/`” bez adresu

Konwencja „Pliki” zna tylko `config`, `data`, `profiles\<appId>` i `logs`. W `config/` są trzy pliki, a nieznane pola są odrzucane (AD-11), więc story Config może uznać nowy plik za błąd konfiguracji. `profiles\<appId>` to `user-data-dir` Chromium, kasowany przy resecie profilu. `logs/` jest rotowany. Każda z tych lokalizacji jest zgodna z literą AD-13, a żadna nie działa dobrze.

**AD-13:** nazwać plik, np. `%LOCALAPPDATA%\MediaStation\state\browser-versions.json` (per `appId`, zapis atomowy, czyta i pisze tylko BrowserHost). Wiersz „Pliki” w Consistency Conventions trzeba dopisać. To nie jest AD.

---

## Low

- **L1: Wiele dopasowań po tytule.** Po C1 fakt `(null, "dark")` może pasować do `R1 (S1)` i `R2 (S2)`. **AD-12:** „Przy kilku kandydatach wygrywa rekord ostatnio dopasowany (`lastMatchedAt`).”
- **L2: Zakres dopasowania i przestrzeń kluczy.** Reguła nie ogranicza dopasowania do `service` ani do rekordów serialu (film o tym samym tytule). Klucze z tytułu i z id dzielą przestrzeń `netflix:series:<…>`, więc tytuł numeryczny może zderzyć się z `seriesId`, a skoro „klucz raz na zawsze”, drugi rekord nigdy nie powstanie. Trzeba też powiedzieć wprost, że wyszukanie rekordu idzie zawsze przez dopasowanie tożsamości, nigdy przez przeliczenie klucza z faktu (inaczej fakt z `S` nie znajdzie rekordu `netflix:series:dark`). **AD-12:** „Dopasowanie w obrębie `service` i `kind: series`. `id` z tytułu ma prefiks `t-`. Rekord znajduje się wyłącznie przez dopasowanie, a klucz to tylko identyfikator.”
- **L3: „Kolejny odcinek”.** Nie wiadomo, czy wymaga innego `(season, episode)`. Przy `season/episode = null` (dozwolone w AD-7) implementacja porównująca numery nigdy nie przesunie terminu. **AD-12:** „Każdy fakt ≥ 0.9 dopasowany do rekordu `pending` przesuwa jego termin, bez porównywania numerów. `lastInSeason == true` przenosi go do `queued`.”
- **L4: Reconnect po samorestarcie przeglądarki.** Edge, który zrestartuje się po aktualizacji przy działającej MediaStation, przechodzi ścieżką `CdpLost` → `cdpReconnectTimeout` → `Failed`, bez odczytu wersji i bez `CdpUnavailable`, więc trigger planu C umyka. **AD-13:** „Ten sam kod `CdpUnavailable` (z wersją w logu) dotyczy upływu `cdpReconnectTimeout` przy żywym `rootPid`.”
- **L5: Kody wyjścia smoke.** „Osobny kod wyjścia” bez wartości. Skrypty autora i story smoke mogą przyjąć różne. **AD-13:** wymienić je: `0` ok, `1` niepowodzenie scenariusza, `2` `CdpUnavailable`, `3` instancja główna działa albo `PortInUse`/`ProfileInUse`.

## Ograniczenia przeglądu

- Nie ruszano innych AD. Wyjątki oznaczone flagą: H2 (interpretacja `transitionId` z AD-14) i H4 (wyciszenie aplikacji w `Failed` jako konsekwencja AD-5, do potwierdzenia przy AD-10).
- Protokół strony pozostaje v1. Żadna poprawka nie dodaje pól do `playback`. M3 tylko doprecyzowuje semantykę `title: null`, które v1 już dopuszcza.
