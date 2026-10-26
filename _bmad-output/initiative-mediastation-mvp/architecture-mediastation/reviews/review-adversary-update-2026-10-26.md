---
title: 'Adversary review: aktualizacja AD-8, AD-11, AD-16 i Consistency Conventions (2026-10-26)'
target: ../architecture-mediastation.md
scope: 'git diff z 2026-10-26: AD-8 (Actions w trybie shell, AppMenu w Shell, usunięcie ActionKind.AppMenu), AD-11 (ikony przez drugi virtual host), AD-16 (tabela komunikatów mostu, guard origin, blokada nawigacji, pola view-modelu, stan efemeryczny UI, eksport schematów w przebiegu testów), Conventions (linkowanie źródeł App do MediaStation.Core.Tests)'
context: [../../tickets/epic-home-screen/tickets/tickets.toml (4.1–4.10), ../../tickets/epic-home-screen/epic-home-screen.md (Notes), ../../ux-mediastation/EXPERIENCE.md, src/MediaStation.App/Shell/ShellBridge.cs, src/MediaStation.Core/Orchestrator/Orchestrator.cs (OnHome), src/MediaStation.Core/Contracts/{Action,Envelope,MessageTypes,ShellSnapshot}.cs, src/MediaStation.Core/Config/ShellSnapshotFactory.cs, src/MediaStation.App/MediaStation.App.csproj (BuildWeb)]
date: '2026-10-26'
---

# Adversary review: aktualizacja AD-8, AD-11, AD-16 i Conventions

## Werdykt

**Nie puszczać pętli bez poprawek.** 0 critical, 4 high, 6 medium, 5 low. Zmiana dobrze rozstrzyga *kto* jest właścicielem AppMenu, ale nie mówi *jak długo* ono żyje ani *w jakiej kolejności* Shell widzi `action` względem `snapshot`. Pętla zbuduje bridge (4.3) i AppMenu (4.8) osobno, każda zgodnie z literą AD, a złożone dadzą: Action rozstrzyganą na nieaktualnym stanie kafelka, menu wiszące nad zmienionym kafelkiem, `LaunchApp` wychodzący z `Failed` z pominięciem AppMenu i most, który z kierunkowej tabeli robi listę dozwolonych nazw bez kierunku. Wszystkie poprawki to dopiski do AD-8, AD-11 i AD-16; kontrakt koperty zostaje `v: 1`.

## Podsumowanie

| # | Tier | Para jednostek | Dziura | Poprawka (skrót) |
| --- | --- | --- | --- | --- |
| H1 | high | 4.3 ShellBridge (wysyłka `action`) × 4.8/4.7 Shell | Kolejność `action` względem `snapshot`, „dokładnie raz” bez gotowej strony, kolejka bez granic przy zawieszonym UI | AD-16: jedna kolejka FIFO na wątku UI, flush snapshotu przed `action`, drop przed `ready`, odrzut przeterminowanych |
| H2 | high | 4.3/4.8 Shell (wyzwalacze ze snapshotu) × Orchestrator (handlery Intents) | Intent zbudowany na nieaktualnym stanie: `LaunchApp` na `Failed` wychodzi z `Failed` bez AppMenu, podwójny `RestartApp`, `CloseApp` na `NotRunning` | AD-8/AD-3: Orchestrator ponownie waliduje każdy Intent względem własnego stanu; tabela stan × Intent |
| H3 | high | 4.3 ShellBridge (whitelist) × `MessageTypes` / 4.8 | Tabela mostu nie jest kierunkowa w regule odrzutu; generyczny dispatch po `MessageTypes` przyjmie od strony `action`, Commands i Events | AD-16: lista przyjmowanych od Shell = dokładnie `ready` + Intents z tabeli; `action` i `snapshot` od Shell odrzucane |
| H4 | high | 4.7 `focusRequest` × 4.8 AppMenu × Orchestrator (tryb) | Brak reguły zamknięcia AppMenu: zmiana stanu kafelka, nowy `focusRequest`, wyjście z trybu `shell` (Starting → Foreground, powrót Back) | AD-8: zamknięcie AppMenu na trzech wyzwalaczach ze snapshotu |
| M1 | medium | Orchestrator `OnHome` × 4.8 (Home zamyka menu) | Home ma dwa znaczenia: „zamknij nakładkę” i „nowa decyzja o pierwszym planie” (AD-14) — zamknięcie menu anuluje trwający start lub restart innej aplikacji; tryb „przed” czy „po” Home nieokreślony | AD-8: przekazanie Home do Shell wg trybu *po* obsłudze, niezależnie od wyniku `OnHome`; jawna decyzja o anulowaniu |
| M2 | medium | 4.3 (snapshot z `title`/`icon`) × 4.5 (`icon: null` z App) | Core nie ma systemu plików, a `apps.json.icon` jest wymagane; brak kanału, którym `icon: null` trafia do Core; dwa buildery `AppSnapshot` | AD-11/AD-16: rekord deskryptora aplikacji z rozwiązanym `icon: string?` budowany w App; jeden builder `AppSnapshot` |
| M3 | medium | 4.1 (eksport w `dotnet test`) × target `BuildWeb` × `dotnet build` rozwiązania | Zagnieżdżony `dotnet test` z `BeforeBuild` App buduje Core równolegle z zewnętrznym buildem; `npm test` na świeżym klonie bez `contracts` | AD-16: `BuildWeb` nie startuje zagnieżdżonego `dotnet test` w buildzie rozwiązania; `npm test` zależy od `contracts` |
| M4 | medium | 4.1/4.3 (generowane typy) × 4.8 (parser wiadomości w TS) | Kształt koperty mostu (`type: 'snapshot' \| 'action'`) nie jest rekordem Core, więc TS pisze go ręcznie | AD-16: eksport schematu wiadomości mostu (unia z `const type`) |
| M5 | medium | 4.3 (Shell konsumuje wyłącznie Actions) × AD-8 (mysz/żyroskop „przechodzi” w `shell`) | Klik myszą/żyroskopem w kafelek to druga ścieżka do `LaunchApp` i AppMenu, poza Actions | AD-8: w Shell wskaźnik nie wyzwala Intents ani AppMenu (albo: klik = fokus + `Ok press`) |
| M6 | medium | 4.3 (wyzwalacze OK) × 4.8 (wyzwalacze AppMenu) | Tabela stan × faza OK niepełna: `Ok press` na `Stopping` i `Foreground` nieokreślone | AD-8: pełna tabela stan × (`press`, `long`) |
| L1 | low | 4.3 ShellWindow (URL startowy) × ShellBridge (guard) | Origin Debug („origin Vite dev servera”) z dwóch źródeł; Vite zmienia port, gdy zajęty; `#if SHELL_DEV_SERVER` w pliku linkowanym do testów | AD-16: jedno źródło origin, `strictPort`, origin wstrzykiwany, bez `#if` w plikach linkowanych |
| L2 | low | 4.3 (kontrakt `Action`) × `ConfigValidation` × EXPERIENCE.md | Usunięcia `ActionKind.AppMenu` nie ma w żadnej story; EXPERIENCE.md nadal wymienia `AppMenu` w kontrakcie | przypisać 4.3; poprawić EXPERIENCE.md |
| L3 | low | 4.7 × nowa instancja UI po `ready` | `focusRequest` może być `null` (dziś `ShellSnapshotFactory`) albo stary `<appId>` | AD-16: nowa instancja: `null` → `default` |
| L4 | low | 4.5 (walidacja `icon`) × mapowanie virtual host | „Bez separatorów i bez `..`” przepuszcza `:` (ADS na NTFS), `%`, `?`, `#`; katalog `config/` liczony w dwóch miejscach | AD-11: zbiór znaków i jedno źródło ścieżki `config/` |
| L5 | low | Conventions × każda story dodająca plik linkowany | „19 plików na 2026-10-26” — liczba, którą każda story unieważnia | usunąć liczbę |

---

## High

### H1: Kolejność i dostarczanie `action` względem `snapshot`

**Para:** story 4.3 (ShellBridge: nowy typ `action` Core → Shell) × story 4.8 (AppMenu: wyzwalacze „z otrzymanej Action i `state` kafelka ze snapshotu”) i 4.7 (`focusRequest`).

AD-16 mówi o `action`: „poza snapshotem, w kolejności i każda dokładnie raz”, a o `snapshot`: latest-wins. Nie mówi nic o kolejności *między* nimi, a AD-8 każe Shell rozstrzygać wyzwalacz AppMenu na „stanie kafelka ze snapshotu” — czyli na tym snapshotcie, który akurat dotarł.

Konstrukcje zgodne z literą, a niezgodne ze sobą:

1. **Action przed snapshotem, który ją poprzedzał w Core.** Dzisiejszy `ShellBridge.Publish` tylko podmienia slot i planuje `BeginInvoke(Flush)`; 4.3 może dodać `action` jako osobne `BeginInvoke` z innym `DispatcherPriority`, albo jako Command (pętla dispatchuje Commands *przed* publikacją snapshotu, `OrchestratorLoop`: „Snapshot publikujemy dopiero po wysłaniu komend”). Scenariusz: aplikacja X przechodzi `Background → Failed` (snapshot rev 41 w slocie), zaraz potem przychodzi `Ok press`. Jeśli `action` wyprzedzi flush rev 41, Shell widzi X jako `Background` i wysyła `LaunchApp` zamiast otworzyć AppMenu z fokusem na „Uruchom ponownie”. Przy odwrotnej kolejności: AppMenu. Obie implementacje przestrzegają „w kolejności” (kolejność między `action`), żadna nie łamie latest-wins.
2. **„Dokładnie raz” bez gotowej strony.** Action przed `ready` (zimny start, przeładowanie Vite HMR, restart procesu renderera WebView2) trafia do `PostWebMessageAsJson` strony bez listenera albo do nikogo. 4.3 może więc buforować do `ready` (żeby spełnić „dokładnie raz”), a wtedy po zawieszeniu/przeładowaniu UI odtworzy 20 starych strzałek i `Ok` — fokus „ucieka”, a stary `Ok` uruchamia aplikację. Albo nie buforuje i łamie literę.
3. **Kolejka bez granic.** AD-14 chroni core przed zawieszonym UI tylko dla snapshotu (latest-wins). Kolejka `action` z „dokładnie raz” rośnie bez ograniczeń, gdy wątek UI stoi (trzymana strzałka daje `repeat` co `repeatIntervalMs`), i po odwieszeniu odtwarza całą serię.

**Poprawka (AD-16, wiersz `action` i nowy punkt):**

> Snapshot i `action` idą do strony jedną kolejką FIFO na wątku UI. Przed wysłaniem `action` most wysyła oczekujący snapshot (jeśli jest), więc Shell zawsze rozstrzyga Action na stanie co najmniej tak nowym jak w chwili jej przekazania przez Orchestrator. Do Shell nie trafiają Commands: przekazanie Action to osobne wyjście Orchestratora, publikowane po snapshotcie z tego samego kroku. „Dokładnie raz” dotyczy strony, która wysłała `ready`: Actions przed pierwszym `ready` po każdej nawigacji są odrzucane bez buforowania. Most odrzuca Action starszą niż `bridge.actionMaxAgeMs` (np. 1000 ms, liczone z `tsMono`) w chwili wysyłki i nigdy nie trzyma więcej niż jednej niewysłanej `repeat`. Logika kolejki leży poza typami WPF/WebView2 (plik linkowany do testów, Conventions).

### H2: Intent zbudowany na nieaktualnym stanie kafelka

**Para:** Shell (4.3 `LaunchApp` z OK, 4.8 `RestartApp`/`CloseApp` z menu) × Orchestrator (handlery Intents z 4.3 i 4.8).

Zmiana przenosi decyzję „co znaczy OK na tym kafelku” do Shell, który zna stan z opóźnieniem (snapshot latest-wins, H1). Nawet z poprawką H1 między emisją Action a przetworzeniem Intentu stan może się zmienić (Intent wraca przez kolejkę Orchestratora, za Eventami, które przyszły w międzyczasie). AD-8 nie mówi, że Orchestrator waliduje Intent ponownie; AD-10 mówi tylko „wyjście z `Failed` tylko przez AppMenu”.

- `LaunchApp` na aplikacji, która właśnie przeszła w `Failed`: handler 4.3 zgodny z literą („OK na `NotRunning` albo `Background` wysyła `LaunchApp`” — to reguła *Shell*) może potraktować go jak start i wyprowadzić aplikację z `Failed` z pominięciem AppMenu, łamiąc AD-10.
- AppMenu zamyka się po OK na „Uruchom ponownie”, ale snapshot ze `Stopping` jeszcze nie dotarł; kolejne OK na kafelku nadal pokazanym jako `Failed` otwiera menu i daje drugi `RestartApp`.
- Menu otwarte dla X w `Background`, proces X się kończy (`NotRunning`), użytkownik wybiera „Zamknij” → `CloseApp` na `NotRunning`.

**Poprawka (AD-8, punkt AppMenu, i AD-3):**

> Intents są prośbami, nie decyzjami. Orchestrator waliduje każdy Intent względem własnego stanu w chwili przetworzenia: `LaunchApp` tylko z `NotRunning` i `Background`; `RestartApp` tylko z `Background` i `Failed`; `CloseApp` tylko z `Background`, `Failed` i `Foreground`. W innym stanie Intent jest ignorowany bez zmiany stanu i logowany `intentIgnored{ type, appId, state }`. Wyjście z `Failed` daje wyłącznie `RestartApp` albo `CloseApp`.

Testy: `dotnet test` w 4.3 (`LaunchApp` na `Failed`, `Starting`, `Stopping` = brak zmiany) i 4.8 (`RestartApp` drugi raz w `Stopping`, `CloseApp` na `NotRunning`).

### H3: Tabela mostu bez kierunku w regule odrzutu

**Para:** 4.3 ShellBridge (whitelist) × `MessageTypes` (rejestr Core) / 4.8 (dopisuje `restartApp`, `closeApp`) / kolejne epiki.

AD-16: „Każdy inny `type` … most odrzuca”. „Inny” niż typy w tabeli — a w tabeli jest `action` (Core → Shell) i `snapshot`. Literalnie przychodzące od strony `{ type: "action", payload: { kind: "home", … } }` nie jest „innym typem”. Do tego `MessageTypes` już rejestruje `action` (rekord `Action`) obok `stopApp`, `processExited`, `systemResumed` itd. Najkrótsza implementacja 4.3 — `MessageTypes.TryGet(type)` + deserializacja + wrzucenie do kolejki Orchestratora — przechodzi testy z 4.3 (`launchApp` dociera, obcy origin odrzucony) i jednocześnie pozwala stronie shella wstrzyknąć Action, Command albo Event do kolejki single-writera (AD-3). Guard origin nie pomaga: to ta sama strona.

**Poprawka (AD-16, pod tabelą):**

> Most przyjmuje od Shell wyłącznie wiersze „Shell → Core” tej tabeli (dziś `ready`, `launchApp`, `restartApp`, `closeApp`), po jawnej liście, nie po `MessageTypes`. `snapshot` i `action` przychodzące od Shell są odrzucane z `bridgeMessageRejected`. Do kolejki Orchestratora trafia z mostu tylko rekord dziedziczący po `Intent`.

### H4: Brak reguły końca życia AppMenu

**Para:** 4.7 (`focusRequest`, powrót na ostatni kafelek) × 4.8 (AppMenu w stanie efemerycznym Shell) × Orchestrator (tryb).

AD-8 wymienia, co *otwiera* AppMenu i że zamykają je Back, Home i wybór pozycji. Nic więcej. Shell nie zna trybu (snapshot go nie niesie), a Actions w trybach `app`/`osk` do Shell nie trafiają. Konstrukcje:

1. **Starting → Foreground z otwartym menu.** OK na A (`NotRunning`) → `Starting`; tryb nadal `shell`, więc Actions płyną. Long-press na B (`Background`) → menu B. Okno A się podpina → `Foreground`, routing `app`. Menu B zostaje otwarte pod aplikacją. Powrót przez Back z korzenia (`app` + `web` → `backResult handled:false`) albo przez awarię A nie przekazuje Home do Shell — użytkownik wraca na ekran z menu B nad kafelkiem, którego stan mógł się zmienić. Sprzeczne z EXPERIENCE („Powrót na Home → fokus na ostatnim kafelku”).
2. **`focusRequest` przy otwartym menu.** Wybudzenie z Modern Standby (AD-10) wysyła `focusRequest{default}`. 4.7 przenosi fokus kafelków na pierwszy kafelek Video; 4.8 trzyma menu otwarte i dalej przechwytuje D-pad. Dwa fokusy; Back „zamyka menu z fokusem na kafelku” — którym?
3. **Stan kafelka pod menu się zmienia** (`Background → NotRunning`, `Failed → Stopping`): menu oferuje pozycje bez sensu (patrz H2).

**Poprawka (AD-8, punkt AppMenu):**

> Shell zamyka AppMenu bez wysyłania Intentu, gdy: (a) snapshot ma aplikację menu w stanie innym niż `Background` i `Failed` albo jej nie ma; (b) snapshot niesie nowe `focusRequest.seq` — najpierw zamknięcie, potem zastosowanie fokusu; (c) snapshot ma którąkolwiek aplikację w `Foreground` (wyjście z trybu `shell`). Zmiana `Background ↔ Failed` menu nie zamyka i nie zmienia jego fokusu.

Testy w 4.8 (`npm test`): trzy wyzwalacze zamknięcia na snapshotach testowych.

---

## Medium

### M1: Home — tryb „przed” czy „po”, i Home jako decyzja o pierwszym planie

**Para:** Orchestrator `OnHome` (4.2 wydziela handler, 4.3 dodaje przekazanie) × Shell 4.8 (Home zamyka menu) i 4.4/4.7 (fokus po powrocie).

1. „W trybie `shell` dodatkowo przekazuje go do Shell” — tryb sprzed obsługi Home czy po? Home z `app` przełącza na `shell`. Wariant „po” przekazuje Home przy każdym powrocie z aplikacji; jeśli 4.4 zaimplementuje Home w Shell jak typowy TV launcher („wróć na zakładkę Video”), łamie „powrót na ostatni kafelek” (EXPERIENCE, AD-16). Wariant „przed” nie zamyka menu, które przeżyło wyjście do aplikacji (H4.1).
2. Dzisiejszy `OnHome` zwraca `OrchestratorResult.None`, gdy nie ma aplikacji `Foreground` ani `Starting`. Najprostsze dopisanie przekazania w gałęzi z wynikiem pomija je w najczęstszym przypadku: Home na ekranie głównym z otwartym menu.
3. AD-14 i komentarz w `OnHome`: Home „zawsze podbija `transitionId`: restart podjęty wcześniej nie wróci na wierzch”, a Home w trakcie `Starting` kończy start kompensacją do `Background`. Po zmianie Home jest też zwykłym klawiszem „zamknij nakładkę”. Sekwencja: OK na A → `Starting`; long-press na B → menu; Home, żeby zamknąć menu → A ląduje w `Background` zamiast na wierzchu. Analogicznie „Uruchom ponownie”, potem Home z przyzwyczajenia → restart nie wraca na pierwszy plan. Obie jednostki trzymają się litery.

**Poprawka (AD-8, punkt „Actions w trybie `shell`”):**

> Orchestrator przekazuje Home do Shell, gdy po obsłudze Home tryb to `shell`, niezależnie od tego, czy obsługa dała komendy. W Shell Home tylko zamyka nakładki; nie zmienia fokusu kafelka ani zakładki. Home w trybie `shell` pozostaje decyzją o pierwszym planie (AD-14): anuluje trwający `Starting` i przywrócenie po restarcie także wtedy, gdy zamyka AppMenu. Do zamknięcia menu bez skutków dla sesji służy Back.

(Alternatywa, jeśli produkt tego nie chce: Home w trybie `shell` bez aplikacji `Foreground` nie podbija `transitionId`. Wtedy trzeba zaktualizować wiersz `Starting` w EXPERIENCE.md. Decyzja należy do użytkownika; ważne, żeby była zapisana, bo dziś każda z dwóch interpretacji przechodzi review.)

### M2: Skąd `icon: null` i kto buduje `AppSnapshot`

**Para:** 4.3 (snapshot dostaje `title` i `icon` z `apps.json`) × 4.5 (brak pliku → `appIconMissing` i `icon: null`).

AD-11: „App sprawdza [plik] przy ładowaniu konfiguracji … a snapshot niesie `icon: null`”. Snapshot buduje Core (`Orchestrator.Snapshot()` i `ShellSnapshotFactory.FromConfig`), Core nie ma systemu plików (AD-3), a rekord `AppEntry.Icon` jest wymagany i niepusty (ta sama zmiana). Implementacje zgodne z literą:

- 4.5-a: App podmienia `Icon` na `null` w rekordzie configu → rekord niezgodny z własną walidacją i schematem (`icon: string`).
- 4.5-b: App przekazuje Orchestratorowi zbiór brakujących ikon; `ShellSnapshotFactory` (snapshot startowy) o nim nie wie → pierwszy snapshot z nazwą pliku, następne z `null` (migotanie, zepsuty `<img>`).
- 4.3 generuje TS `icon: string`, a 4.4 pisze kod na tym typie; 4.5 zmienia na `string | null` i łamie kompilację cudzego kodu.

**Poprawka (AD-11 i AD-16):**

> App buduje z poprawnego configu listę `AppDescriptor { appId, category, type, title, icon: string? }` (rekord Core), z `icon = null` przy braku pliku, i przekazuje ją do Core. Jedna funkcja w Core buduje `AppSnapshot` z `AppDescriptor` i stanu; używają jej zarówno snapshot startowy, jak i Orchestrator. `AppSnapshot.icon` jest `string | null` od pierwszej story, która dodaje pole (4.3).

### M3: Zagnieżdżony `dotnet test` w buildzie

**Para:** 4.1 (eksport w `dotnet test --filter Category=SchemaExport`, wołany przez `npm run contracts`) × target `BuildWeb` (`BeforeTargets="BeforeBuild"` w `MediaStation.App.csproj`) × `dotnet build MediaStation.slnx` / `release.yml`.

Literalnie: `dotnet build` rozwiązania buduje Core i Core.Tests w równoległych węzłach, a w `BeforeBuild` App uruchamia `npm run contracts` → `dotnet test tests/MediaStation.Core.Tests` → drugi, niezależny build Core i Core.Tests w tych samych `obj/` i `bin/`. Na Windowsie daje to losowe MSB3026/MSB3027 (plik zablokowany) i niedeterministyczne czerwone buildy w pętli. Dodatkowo `npm test` (Vitest) na świeżym klonie i w CI nie ma `web/contracts/generated` (katalog w `.gitignore`), jeśli nie zależy od `contracts`.

**Poprawka (AD-16, punkt eksportu):**

> `BuildWeb` nie uruchamia `dotnet test` w trakcie buildu rozwiązania: schematy odświeża `npm run contracts` wołany ręcznie, przez `npm run build`/`npm test` albo przez `BuildWeb` tylko wtedy, gdy App jest budowany sam (`'$(BuildingSolutionFile)' != 'true'`), a `release.yml` woła `npm run contracts` po `dotnet test`. `npm test` w `web/` uruchamia najpierw `contracts`.

(Wariant równoważny: eksport jako krok `dotnet test` całego rozwiązania i `BuildWeb` czyta tylko istniejące schematy, z czytelnym błędem, gdy ich brak.)

### M4: Koperta mostu w TS pisana ręcznie

**Para:** 4.1/4.3 (generowanie typów) × 4.8/4.4 (parser wiadomości w Shell).

Eksportowane są rekordy (`ShellSnapshot`, `Action`, `LaunchApp`…), a nie kształt wiadomości mostu. Unia `{ v: 1, type: 'snapshot', payload: ShellSnapshot } | { v: 1, type: 'action', payload: Action }` i nazwy `launchApp`/`restartApp`/`closeApp` powstaną w TS ręcznie, a w C# żyją jako stałe w `Envelope` i nazwy z `MessageTypes`. Literę AD-16 („ręczna edycja wygenerowanych typów zakazana”) to spełnia, ale cel („rozjechane typy TS i C#”) nie.

**Poprawka (AD-16):**

> Core eksportuje schemat wiadomości mostu dla każdego kierunku (unia z `const` na `type` i `v`), a Shell parsuje i wysyła wyłącznie przez typy z niego wygenerowane.

### M5: Wskaźnik w Shell jako druga ścieżka mutacji

**Para:** 4.3 (Shell konsumuje „wyłącznie Actions z core”) × AD-8 (mysz i żyroskop w `shell` „przechodzą”; „Żyroskop pilota klika w Shellu jak zwykła mysz [ADOPTED]”).

Zmiana przenosi wyzwalacze AppMenu i `LaunchApp` do Shell, „z otrzymanej Action”. Klik żyroskopem w kafelek to zdarzenie DOM, nie Action. 4.3 może dać kafelkom `onClick` → `LaunchApp` (żyroskop „klika jak mysz”), 4.8 nie — wtedy klik w kafelek `Failed` uruchamia aplikację z pominięciem AppMenu (por. H2), a klik w tło nad otwartym menu nie robi nic.

**Poprawka (AD-8):**

> W Shell wskaźnik nie wysyła Intents i nie otwiera AppMenu; klik jest równoważny przeniesieniu fokusu na element pod kursorem i Action `Ok press` na nim, rozstrzyganej tą samą funkcją co Action z core. (Albo: kliknięcia w Shell są ignorowane — do wyboru, ale jedno.)

### M6: Niepełna tabela stan × faza OK

**Para:** 4.3 („OK na `NotRunning` albo `Background` → `LaunchApp`”) × 4.8 (wyzwalacze AppMenu).

AD-8 definiuje `long` dla wszystkich stanów i `press` dla `Failed` i `Starting`. `press` na `Stopping` i `Foreground` (widocznym w Shell np. przez spóźniony snapshot) jest nieokreślone — 4.3 może wysłać `LaunchApp` „dla wszystkiego poza `Starting`/`Failed`”.

**Poprawka (AD-8):**

> | Stan kafelka | `Ok press` | `Ok long` |
> | --- | --- | --- |
> | `NotRunning` | `LaunchApp` | nic |
> | `Starting`, `Stopping`, `Foreground` | nic | nic |
> | `Background` | `LaunchApp` | AppMenu |
> | `Failed` | AppMenu, fokus „Uruchom ponownie” | AppMenu, fokus „Uruchom ponownie” |

(Fokus startowy przy `long` na `Failed` też nie jest dziś określony w AD-8 — 4.8 mówi „przy `Failed`”, tabela to domyka.)

---

## Low

### L1: Origin Debug z dwóch źródeł

**Para:** ShellWindow (URL startowy) × ShellBridge (guard `e.Source`), 4.3.

„Debug origin Vite dev servera” — ShellWindow zna URL, ShellBridge musi go znać osobno. Vite bez `strictPort` przechodzi na kolejny port, gdy 5173 jest zajęty → guard odrzuca `ready`, pusty shell. Plik linkowany do testów z `#if SHELL_DEV_SERVER` (stała tylko w Debug App) kompiluje się w `MediaStation.Core.Tests` bez tej stałej, więc testy sprawdzają tylko jedną gałąź. Porównanie `e.Source` stringiem (`…example/` vs `…example`) też jest pułapką.

> Origin shella ma jedno źródło, z którego ShellWindow nawiguje i ShellBridge sprawdza (wstrzykiwany parametr, porównanie `Uri` scheme+host+port). Vite działa z `strictPort: true`. W plikach linkowanych do testów nie ma `#if` zależnych od konfiguracji App.

### L2: Usunięcie `ActionKind.AppMenu` bez właściciela

AD-8 usuwa `AppMenu` z kontraktu, ale żadna story 4.1–4.10 tego nie wymienia; `ConfigValidation` ma dwie gałęzie z dedykowanym komunikatem („„appMenu” niedozwolone …”), które po usunięciu członu przestaną się kompilować, a `input.json` z `appMenu` dostanie ogólny błąd `StrictEnumConverter`. EXPERIENCE.md (tabela „Model wejścia”) nadal wymienia `AppMenu` w kontrakcie, a 4.8 ma EXPERIENCE.md w referencjach. Poprawka: przypisać usunięcie do 4.3 (zmienia kontrakt `Action` i generowany `action.ts`), zachować czytelny błąd walidacji dla `appMenu`, poprawić EXPERIENCE.md przez `bmad-ux`.

### L3: Nowa instancja UI i `focusRequest`

AD-16: „po restarcie UI zaczyna od `focusRequest`”. `ShellSnapshotFactory` dziś wysyła `focusRequest: null`, a po przeładowaniu renderera najnowszy snapshot może nieść stary `<appId>` (awaria sprzed godziny). 4.7 zgodnie z literą albo nie stawia fokusu wcale, albo skacze na stary kafelek. Poprawka: „Instancja UI, która jeszcze nie zastosowała żadnego `seq`, przy `focusRequest: null` stosuje `default`.”

### L4: Nazwa pliku ikony i katalog `config/`

„Bez separatorów ścieżki i bez `..`” przepuszcza `netflix.png:ads` (strumień NTFS — `File.Exists` może zwrócić `true`, mapowanie virtual host już nie), `%`, `?`, `#`, nazwy zarezerwowane (`CON.png`). Sprawdzenie pliku (App, config loader) i mapowanie (ShellWindow) mogą liczyć katalog `config/` niezależnie (Debug vs `%LOCALAPPDATA%`). Poprawka: „`icon` pasuje do `^[A-Za-z0-9][A-Za-z0-9._-]*\.(png|webp|svg)$`; katalog `config/` ma jedno źródło używane przez loader i mapowanie.”

### L5: Liczba plików w Conventions

„(19 plików na 2026-10-26)” unieważnia każda story, która wydzieli logikę do pliku linkowanego (4.3 na pewno: walidacja wiadomości `ShellBridge`, kolejka z H1). Reviewer pętli może to zgłaszać jako dryf spine'u. Poprawka: usunąć liczbę.

---

## Co zmiana robi dobrze

- Jeden właściciel AppMenu, brak Intentu otwierającego i brak `kind` AppMenu usuwają realną dziurę z poprzedniej wersji (dwa źródła stanu menu).
- Origin ikon oddzielony od origin shella, z `DenyCors` — plik z `config/icons/` nie może wykonać się w origin, który przechodzi przez guard mostu.
- Eksport schematów w przebiegu testów zamienia „ciche stare typy” na widoczny czerwony test — to dobry kierunek; M3 dotyczy tylko sposobu wywołania.
- Pola view-modelu z `type` rozstrzygają „Wstrzymane” vs „Wyciszony” w jednym miejscu.
