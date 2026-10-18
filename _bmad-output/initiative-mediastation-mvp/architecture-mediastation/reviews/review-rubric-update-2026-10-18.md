---
title: 'Review: update spine MediaStation MVP 2026-10-18 (rubric, delta)'
target: ../architecture-mediastation.md
decision-log: ../.memlog.md
driver: ../../spec-mediastation/spec-mediastation.md
date: 2026-10-18
lenses: [rubric-walker]
scope: 'AD-12 (tożsamość klucza serialu), AD-13 (wykrycie aktualizacji przeglądarki bez CDP), otwarte pytanie o pauzę wielogodzinną'
---

# Review: update spine'u 2026-10-18 (AD-12, AD-13, pauza)

**Werdykt:** kierunek jest dobry i ograniczenia usera są dotrzymane: zmieniły się tylko AD-12 i AD-13, a protokół został w v1 bez zmiany kształtu. Delta nie domyka jednak w pełni żadnego z dwóch pytań ze spec. W AD-13 sygnał `CdpUnavailable` zależy od timeoutu `Starting`, którego właścicielem jest Orchestrator (AD-14). To wyścig, w którym ginie właśnie ten sygnał, który miał być odróżnialny. W AD-12 reguła dopasowania po tytule jest dwuznaczna dla faktu bez `seriesId` i nie mówi, co zrobić, gdy pasuje kilka rekordów. Przy dosłownym odczycie wraca duplikat, który opcja b miała usunąć. Oba problemy da się naprawić w samych AD-12 i AD-13.

## Podsumowanie findings

| # | Severity | AD | Finding | Sugerowany fix |
| --- | --- | --- | --- | --- |
| C1 | critical | AD-13 × AD-14 | `CdpUnavailable` „do końca timeoutu `Starting`”: timer ma Orchestrator, więc BrowserHost musi mieć drugi timer albo się spóźnia. Wynik zależy od wyścigu i sygnał ginie | Własny limit połączenia CDP w BrowserHost (`cdpConnectTimeout` w `settings.json`, krótszy niż `Starting`). Orchestrator na `CommandFailed` startu od razu przechodzi w `Failed` |
| H1 | high | AD-12 | „Po tytule tylko wśród rekordów z `seriesId` równym `null` albo takim samym”: przy fakcie bez `seriesId` dosłowny odczyt pomija rekord ze znanym `seriesId`, więc wracają duplikat i fałszywy `pending`. Brak rozstrzygnięcia przy więcej niż jednym kandydacie | Rozpisać regułę osobno dla faktu z `seriesId` i bez niego, dodać deterministyczny tie-break, objąć wszystkie statusy i poprawić tekst ryzyka |
| M1 | medium | AD-12 | `titleNorm` jest trwały w `data/`, ale nic nie wiąże wersji normalizacji ze `schemaVersion`. Rekord nie deklaruje surowego `title` (potrzebny do QR) | Jedna funkcja normalizacji w Core. Jej zmiana podnosi `schemaVersion` z migracją `titleNorm` (klucz bez zmian). Rekord trzyma `title` i `titleNorm` |
| M2 | medium | AD-13 × AD-11 × Konwencje | „Poprzednią wersję trzyma poza `data/`” zostawia otwarte `config/` (walidowany, z odrzucaniem nieznanych pól) i piąty katalog spoza konwencji Pliki | Zamknąć listę miejsc: `profiles\<appId>\` albo stan samej przeglądarki. Nigdy `config/` ani `data/` |
| M3 | medium | AD-13 × AD-10 × AD-15 | Po `CdpUnavailable` przeglądarka żyje bez CDP. Okno bez `AppWindowAttached` jest poza zasięgiem WindowManagera. Ponowny start albo smoke trafia na hand-off do żywej instancji profilu i dostaje `ProcessExited` zamiast `CdpUnavailable` | „Proces żyje” = proces trzymający `profiles\<appId>`. `AppWindowAttached` niezależny od CDP. Smoke i start wykrywają ten stan przed launchem |
| M4 | medium | AD-13 | Binds AD-13 nie obejmuje BrowserHost, Orchestratora ani `ErrorCode` w Core, a nowa reguła je wiąże | Dopisać do Binds: BrowserHost, Orchestrator, Core (`ErrorCode`) |
| L1 | low | AD-13 | `BrowserVersionChanged{…}` wygląda jak Event Core, a tekst mówi „wpis w logu” | Napisać wprost, że to tylko wpis w logu, a nie Event |
| L2 | low | AD-12 × AD-7 | Semantyka `playback.title` dla odcinka żyje tylko w AD-12, a tabela AD-7 milczy | Opis pola w rekordzie Core (trafi do JSON Schema/TS, AD-16), fixture w vitest drivera |
| L3 | low | AD-13 | Nieokreślone: rozbieżność `Browser.getVersion` z wersją sprzed startu; „po `Failed`” bez zawężenia do web; `CdpUnavailable` a `Failed` po `cdpReconnectTimeout` | Trzy krótkie doprecyzowania w AD-13 |
| L4 | low | poza AD | Deferred „Wikidata”, spec Open Questions i success CAP-7/CAP-11 nie odzwierciedlają rozstrzygnięć | Aktualizacja wiersza Deferred i spec (bmad-spec) |
| L5 | low | Open questions | „Dwa warianty chowania okna” w story pomiarowej mogą obejść AD-5 (`SW_HIDE`) | Dopisać, że wariant poza AD-5 jest tylko pomiarem porównawczym |

---

## Co jest w porządku (sprawdzone)

- Diff dotyka wyłącznie AD-12, AD-13, otwartego pytania o pauzę i frontmatteru (`updated`, `sources`). Inne AD bez zmian.
- Protokół v1: kształt `playback` bez zmian. Semantyka `title` dla odcinka wcześniej nie była ustalona, więc to doprecyzowanie, a nie zmiana niezgodna (AD-7 „pola dodaje się wstecznie zgodnie”), zwłaszcza że driver jeszcze nie istnieje.
- Niezmienny klucz jest spójny z AD-16: klucz „Do oceny” jest stabilnym id fokusu UI, a niezmienność to gwarantuje.
- Store dalej tylko persystuje cały dokument. Uzupełnienie `seriesId` to wynik czystej `RatingPolicy`, a `Persist` wysyła Orchestrator (AD-3). Jedno źródło zapisu `data/` zostaje.
- `CdpUnavailable` jako wartość enumu `ErrorCode` w Core jest zgodny z konwencją „Błędy”. Kafelek pokazuje zwykły `Failed`, więc nie ma zmiany UX (odrzucone 3b w memlogu).
- `seriesId` w rekordzie, uzupełniany później, lepiej służy przyszłemu mapowaniu Wikidata niż klucz z tytułu.
- Pytanie o pauzę jest zgodne słowo w słowo ze spec (linia 191), ma miejsce rozstrzygnięcia i ścieżkę powrotu jako change.
- Poziom szczegółowości: dopasowanie w AD-12 implementuje jeden komponent (`RatingPolicy`), ale ustala format trwałych danych i domyka jawne pytanie spec, więc może być w spine'ie. „Źródło wersji i miejsce zapisu ustala story” to dobry podział w AD-13.

---

## C1 (critical): `CdpUnavailable` zależy od cudzego timera

**Problem.** AD-14: „Każdy timeout (Back, `Starting`, …) to `TimerElapsed`, zaplanowany przez Orchestrator”. Konwencja „Błędy”: o `Failed` decyduje Orchestrator. AD-13 każe natomiast BrowserHostowi odpowiedzieć `CdpUnavailable`, gdy port nie przyjmie połączenia „do końca timeoutu `Starting`”. BrowserHost nie zna tego timera. Story CAP-7 zrobi własny timer o tej samej długości, a story Orchestratora obsłuży `TimerElapsed(Starting)` → `Failed`. Oba odpalą się mniej więcej w tej samej chwili:
- gdy pierwszy przyjdzie `TimerElapsed`, aplikacja jest już w `Failed` z powodu „timeout startu”. Późny `CommandFailed{CdpUnavailable}` może zostać zignorowany (AD-14: stare wyniki są ignorowane), więc w logu nie ma odróżnialnego sygnału;
- smoke dostaje losowo jeden z dwóch kodów wyjścia, więc trigger planu C (AD-1) jest niedeterministyczny.

Sformułowanie „odróżnialnym od timeoutu startu” zakłada, że to Orchestrator klasyfikuje przyczynę, ale reguła daje klasyfikację BrowserHostowi.

**Fix (tylko w AD-13):**
> - BrowserHost ma własny limit połączenia z portem CDP, `cdpConnectTimeout` w `settings.json`, który walidacja wymusza jako krótszy niż timeout `Starting`. To limit I/O wykonawcy, a nie timer Core. Gdy proces z profilem żyje, a port nie przyjął połączenia przed `cdpConnectTimeout`, BrowserHost odpowiada `CommandFailed{ cmdId, errorCode: CdpUnavailable }`.
> - Orchestrator na `CommandFailed` komendy startu przechodzi w `Failed` od razu i loguje `errorCode`. `TimerElapsed(Starting)` zostaje zabezpieczeniem z własnym powodem w logu.

AD-11 już przewiduje „pozostałe timeouty” w `settings.json`, więc AD-11 się nie zmienia. Alternatywa: BrowserHost raportuje fakty (`BrowserProcessStarted`, `CdpConnectRefused`), a przyczynę klasyfikuje Orchestrator przy `TimerElapsed`. To jest zgodne z AD-14, ale wymaga nowych Events i jest cięższe.

## H1 (high): dopasowanie po tytule jest dwuznaczne i nie ma tie-breaku

**Problem 1: kierunek odwrotny do pytania spec.** Pytanie spec obejmuje sytuację „najpierw tytuł, potem `seriesId`” i to jest domknięte: fakt z `seriesId = X` nie trafia po `seriesId`, trafia po tytule na rekord z `null` i uzupełnia `seriesId`. Kierunek odwrotny jest częsty: rekord powstał z `seriesId = Y` (start ze strony tytułu), a później przychodzi fakt bez `seriesId` (Continue Watching, `/watch/<id>`). „Takim samym” odnosi się do `seriesId` faktu, czyli `null`. Dosłownie dopasowanie obejmuje więc tylko rekordy z `null` i pomija rekord Y. Wtedy powstaje nowy rekord z kluczem z tytułu, czyli duplikat, a przy `lastInSeason == null` także fałszywa karta `pending`. To dokładnie ta wada, przez którą memlog odrzucił opcję a. Druga story może czytać regułę „intencjonalnie”, więc dwie implementacje się rozjadą.

**Problem 2: wielu kandydatów.** Fakt bez `seriesId` i tytuł T, a w danych są rekordy `(Y, T)` i `(Z, T)`, czyli dwa różne seriale o tym samym tytule, oba ze znanym id. Albo `(null, T)` i `(Y, T)`, co jest możliwe po problemie 1. Reguła nie mówi, który rekord wybrać.

**Problem 3: statusy.** Nie jest powiedziane, że dopasowanie obejmuje rekordy `rated` i `skipped`. Story, które dopasowuje tylko `pending|queued`, łamie zasadę „raz na zawsze”.

**Problem 4: tekst ryzyka jest zaniżony.** Scalenie dwóch seriali zachodzi także wtedy, gdy fakt zna `seriesId`, a rekord nie: fakt `(X, T)` trafia na rekord `(null, T)` innego serialu i nadaje mu X. Tekst mówi „gdy oba nie znają”.

**Fix (zastąpić punkt „Tożsamość serialu”):**
> - Tożsamość serialu: rekord serialu trzyma `seriesId` (albo `null`), `title` i `titleNorm`. `RatingPolicy` dopasowuje fakt do rekordów we wszystkich statusach:
>   - fakt z `seriesId`: rekord z tym samym `seriesId`, a gdy go nie ma, rekord z tym samym `titleNorm` i `seriesId == null`. To dopasowanie uzupełnia `seriesId` w rekordzie;
>   - fakt bez `seriesId`: rekord z tym samym `titleNorm`, niezależnie od jego `seriesId`;
>   - gdy pasuje więcej niż jeden rekord: wygrywa rekord z najwcześniejszym pierwszym zapisem (deterministycznie, bez scalania rekordów).
> - Zaakceptowane ryzyka: duplikat przy różnych językach profilu, gdy któryś rekord nie zna `seriesId`; przypisanie faktu do innego serialu o tym samym `titleNorm`, gdy fakt albo rekord nie zna `seriesId`.

Tie-break może być inny (np. ostatnio widziany), ale musi być w spine'ie, bo wynik trwale zapisuje się w `data/`. Dodatkowo do success CAP-11 w spec warto dopisać testy obu kierunków (L4).

## M1 (medium): `titleNorm` jest trwały, a normalizacja może się zmienić

AD-12 słusznie przypisuje normalizację wyłącznie `RatingPolicy`, więc dwie implementacje się nie rozjadą. `titleNorm` jest jednak zapisany w `rating-queue.json`. Poprawka normalizacji (np. inne traktowanie interpunkcji albo diakrytyków) sprawi, że nowe fakty przestaną trafiać na stare rekordy. Skutek to ciche duplikaty i fałszywe `pending`. Rekord nie deklaruje też surowego `title`, a karta buduje z niego URL do QR.

**Fix:** w AD-12 dopisać: „`titleNorm` liczy jedna funkcja w Core. Jej zmiana podnosi `schemaVersion` `rating-queue.json`, a migracja przelicza `titleNorm` z zapisanego `title`. Klucz się nie zmienia.” Sam algorytm (NFC, wielkość liter, spacje, interpunkcja) zostaje w story `RatingPolicy`.

## M2 (medium): „poza `data/`” to za szeroka granica

Zakaz zapisu w `data/` chroni jedynego writera (Store). Jednak:
- `config/` to pliki użytkownika ze schematem w Core i odrzucaniem nieznanych pól (AD-11). Zapis wersji do `settings.json` wywoła błąd walidacji przy następnym starcie;
- nowy katalog (np. `state/`) rozszerza konwencję „Pliki” (`{config,data,profiles\<appId>,logs}`) bez decyzji;
- odczyt „poprzedniej wersji” z logów jest kruchy przy rotacji dziennej.

**Fix:** „Poprzednia wersja żyje w `profiles\<appId>\` (plik hosta albo stan zapisany przez samą przeglądarkę). Nigdy w `config/`, `data/` ani w logach.” Wskazówka dla story: Chromium/Edge trzyma w katalogu `--user-data-dir` plik `Last Version`. Jeśli to się potwierdzi, MediaStation nie musi trzymać żadnego stanu. Wersję „to” można odczytać z `FileVersion` pliku `msedge.exe`.

## M3 (medium): stan „przeglądarka żyje bez CDP” nie ma kontraktu

Po `CdpUnavailable` proces przeglądarki zostaje przy życiu. Brakują trzy rzeczy:
1. **Okno.** AD-15: WindowManager działa tylko na `hwnd` z `AppWindowAttached`. Jeśli BrowserHost raportuje `AppWindowAttached` dopiero po podpięciu CDP, świeżo uruchomione okno przeglądarki zostaje na wierzchu, a Orchestrator nie może go zminimalizować przy przejściu na ekran główny (AD-10).
2. **Ponowny start i adopcja.** `ProbeExisting` nie podepnie się do żywej przeglądarki bez CDP. Ponowny launch `msedge.exe` z tym samym `--user-data-dir` przekazuje polecenie żywej instancji i kończy się od razu. BrowserHost zobaczy wtedy `ProcessExited` uruchomionego pid, a nie `CdpUnavailable`.
3. **Smoke.** Reguła każe autorowi uruchomić smoke „po `Failed`”, a smoke wymaga zatrzymanej instancji głównej. Zatrzymanie MediaStation nie zabija przeglądarki (to celowe, adopcja AD-10), więc smoke trafia w scenariusz z punktu 2 i zwraca zły kod wyjścia. Trigger planu C przepada na głównej ścieżce.

**Fix (w AD-13):**
> - „Proces żyje” oznacza proces trzymający `profiles\<appId>`, a nie pid z launchu.
> - BrowserHost raportuje `AppWindowAttached{ appId, hwnd, rootPid }` z Win32, niezależnie od CDP.
> - Przed launchem (także w `--smoke`) BrowserHost sprawdza, czy profil trzyma żywy proces bez CDP. Jeśli tak, odpowiada `CdpUnavailable` bez launchu, a zamknięcie zostawia ścieżce `AppMenu`/`Stopping` (AD-7: `prepareClose` bez `closeReady` → zamknięcie).

## M4 (medium): Binds AD-13 nie obejmuje nowych uczestników

Binds: „build, deploy, środowiska, testy, logi, smoke, bezpieczeństwo”. Nowa reguła wiąże BrowserHost (odczyt wersji, `CdpUnavailable`), Orchestrator (obsługa `CommandFailed` startu) i Core (`ErrorCode`). Story filtrująca AD po Binds (np. Orchestrator) może przeoczyć AD-13. Spec CAP-7 wymienia AD-13, ale story Orchestratora go nie zobaczy.

**Fix:** Binds AD-13 += BrowserHost, Orchestrator, `ErrorCode` (Core). Można też dopisać do Prevents „plan C uruchomiony za późno, bo `Failed` jest nieodróżnialny”.

## L1 (low): `BrowserVersionChanged` jako wpis w logu czy Event

Nazwa w PascalCase w czasie przeszłym z polami w klamrach wygląda jak Event Core (konwencja „Komunikaty w Core”). Jedna story doda rekord Event i jego obsługę w Orchestratorze, a druga tylko linię w logu. **Fix:** „wpis w logu `browserVersionChanged { appId, from, to }`. To nie jest Event Core i Orchestrator go nie obsługuje.”

## L2 (low): semantyka `playback.title` poza AD-7

Driver implementowany według tabeli AD-7 może nie zauważyć zdania w AD-12. AD-12 ma ServiceDriver w Binds, więc formalnie wystarcza, a AD-7 nie wolno zmieniać. **Fix bez zmiany AD-7:** opis pola (`/// <summary>` w rekordzie Core), który trafi do JSON Schema i typów TS (AD-16), plus fixture vitest drivera: „na `/watch` odcinka `title` = tytuł serialu”.

## L3 (low): doprecyzowania AD-13

- Gdy `Browser.getVersion` po podpięciu różni się od wersji sprzed startu: zalogować obie wartości, bez drugiego `BrowserVersionChanged`.
- „Po `BrowserVersionChanged` albo `Failed`” zawęzić do `Failed` aplikacji `web`.
- `CdpUnavailable` dotyczy tylko startu. `Failed` po `cdpReconnectTimeout` (AD-10) to inna przyczyna i nie powinien dostać tego kodu.

## L4 (low): otoczenie spoza AD

- Deferred, „Mapowanie przez Wikidata”: zamiast „protokół ma, ale może być `null`” lepiej napisać „rekord AD-12 trzyma `seriesId`, uzupełniany przy późniejszym fakcie”.
- Spec: Open Questions nadal pokazują „Wykrycie zablokowanego portu CDP” i „Stabilność klucza serialu” jako otwarte. Trzeba je zamknąć odsyłaczem do AD-13 i AD-12 (update w `bmad-spec`). Success CAP-11 powinien zawierać testy obu kierunków dopasowania z H1, a success CAP-7 rozróżnienie `CdpUnavailable` i kod wyjścia smoke.

## L5 (low): pytanie o pauzę a AD-5

„Dwa warianty pauzy i chowania okna” mogą sugerować pomiar z `SW_HIDE`, który AD-5 zakazuje w produkcie. **Fix:** dopisać „wariant spoza AD-5 to wyłącznie pomiar porównawczy. Zmiana wymaga change do AD-5”.
