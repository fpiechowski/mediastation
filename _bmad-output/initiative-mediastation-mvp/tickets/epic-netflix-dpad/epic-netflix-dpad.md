---
type: epic
title: "Netflix by D-pad"
parent: initiative-mediastation-mvp
covers: [CAP-8]
sources: [_bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, _bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/digests/spike-um760.md, _bmad-output/initiative-mediastation-mvp/deferred-work.md]
companions: [_bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, _bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md]
after: []
assignee: ""
risk: high
refined: true
---

# Netflix by D-pad

## Description

CAP-8: cały wieczór z Netflixem obsługuje się D-padem, z wstrzykniętym fokusem i Back zachowującym się jak na TV, a pauza, wznowienie i fakty o odtwarzaniu idą przez protokół strony. Epika rozszerza PageRuntime z epic-browserhost (silnik fokusu, podświetlenie `focus-ring-injected`, nasłuch `<video>`), dostarcza ServiceDriver `netflix` z `manifest.json` i fixture'ami DOM, BackPolicy oraz `--smoke netflix`.

## Outcome

Eliza ogląda Netflixa od profilu do wyjścia samym pilotem; sygnałem są techniczne warunki success signal specu i success CAP-8.

## Requirements

- R1 (spec CAP-8 success 1; AD-6, AD-7): Strona zgłasza `screen{name, root}` dla każdego z ekranów `profiles`, `browse`, `modal`, `title`, `search`, `watch`, `limit` i `error`; ekran nierozpoznany, np. logowanie, zgłasza się jak `error`.
- R2 (spec CAP-8 intent, Constraints; AD-6, AD-8; DESIGN `focus-ring-injected`; EXPERIENCE J1, J3): Fokus na stronie porusza się wyłącznie przez `action`, także przy trzymanej strzałce, po rzędach, nagłówku, polu wyszukiwania i wynikach, jest widoczny jako `focus-ring-injected` i nie wychodzi poza otwarty modal. Kursor jest ukryty w bezruchu, a fizyczna mysz działa. Surowe zdarzenia klawiszy nic nie robią, a pole tekstowe dostaje fokus dopiero po OK.
- R3 (spec CAP-8 success 1; AD-6; EXPERIENCE stany): Na `profiles` fokus stoi na ostatnio używanym profilu, a bez niego na pierwszym, i profil nigdy nie jest wybrany automatycznie; na `limit` i `error` fokus stoi na głównej akcji, bez UI MediaStation.
- R4 (spec CAP-8 success 1; AD-7; EXPERIENCE tabela Back; S7): Back na `watch` cofa historię, a bez historii przechodzi na `/browse`, na `modal` i `title` zamyka, na `search` wraca na `browse`; `handled:false` przychodzi wtedy i tylko wtedy, gdy ekran jest `root` (`browse`, `profiles`, `limit`, `error`), a `handled:false` albo brak odpowiedzi w ~500 ms pokazuje ekran główny.
- R5 (spec Constraints; AD-7; EXPERIENCE J2; S6): Na `watch` OK przełącza pauzę i odtwarzanie, lewo i prawo przewijają o 10 s, a widoczny przycisk pominięcia czołówki albo następnego odcinka dostaje OK zamiast pauzy; zaufany klawisz spoza zamkniętej listy hosta nie jest wykonany.
- R6 (spec CAP-8 success 1; AD-7, AD-12): Fakt `playback` ma kształt i jednostki AD-7, idzie przy każdej zmianie `state` i co ~30 s w `playing`, także przed poznaniem `duration`, przy odcinku niesie w `title` tytuł serialu, a `seriesId` ma wartość, gdy da się je ustalić.
- R7 (spec CAP-8 success 3, Constraints; AD-5, AD-10, AD-14; epic-windows-audio R7): Każde opuszczenie aplikacji `web` (Home, start innej aplikacji, przełączenie z zewnątrz, Back na ekranie `root`, adopcja, wybudzenie) zatrzymuje odtwarzanie, a brak potwierdzenia ani przerwa CDP nie blokują Home; powrót zastaje odcinek na pauzie, a OK go wznawia.
- R8 (spec Constraints; AD-5; EXPERIENCE `page-error`; S8): Po `page-error` strona przeładowuje `/watch/<id>` z zachowaną pozycją, bez UI MediaStation i bez wznowienia odtwarzania.
- R9 (AD-7, AD-10, AD-14; S8): Przed zamknięciem aplikacja przechodzi na `/browse`; bez `closeReady` w ~3 s albo przy przerwie CDP zamyka się mimo to.
- R10 (AD-1, AD-7, AD-10; Deferred „Transport host → page”): Host dostarcza komunikaty do strony bez `Runtime.enable`, także przy oknie zminimalizowanym, z `navigator.webdriver=false`; odpowiedź ze starym `id` jest ignorowana, a akcja w przerwie CDP jest odrzucona, nie buforowana.
- R11 (spec CAP-8 success 2; AD-1, AD-13; retro epic-foundation-contracts F2): `--smoke netflix` przechodzi start, `hello`, wybór profilu syntetycznym OK, odtwarzanie ≥ 1080p bez błędu DRM, pauzę i regułę Back, loguje key system przy każdym przebiegu, a `appId` z CLI podlega tej samej regule co w configu.
- R12 (spec Open Questions): Jest zapisany wynik pauzy trwającej kilka godzin bez uśpienia PC, w dwóch wariantach pauzy i chowania okna, z fixture'ami nowych ekranów błędu; wynik wymagający polityki w Core ma rozstrzygnięty change.
- R13 (AD-7, AD-9; epic-osk Waits on; DESIGN `input-active`; EXPERIENCE tabela Back): Strona zgłasza `textFocus{active}` bez wpisanego tekstu, `textFocus` aplikacji nieaktywnej jest ignorowany, przy `osk{active:true}` pole ma styl `input-active`, a strona nie rysuje własnego fokusu; po `osk{active:false}` fokus jest na wynikach.
- R14 (DESIGN Stany kafelka; refinement epiki 4): Linia stanu `Background` kafelka web ma postać „W tle · <tytuł>, wstrzymane”, a bez znanego tytułu zostaje linia dotychczasowa.
- R15 (AD-1, AD-10; DW-56): Strona, której dokument podmienił się w przerwie CDP, odzyskuje most albo kafelek przechodzi w `Failed`.
- R16 (AD-5, AD-15; DW-60, DW-61): Zmiana pierwszego planu, od której zależy pauza przy przełączeniu z zewnątrz, dociera do Orchestratora także przy starcie i po podniesieniu shella; przypadek bez wpisu ma nazwany powód.
- R17 (DW-58; AD-6): Kafelek `web` zawsze ma PageRuntime, bo harness nie uruchamia już `web`, a host harnessu `moonlight` zgłasza najwyżej jedno `ProcessExited` na proces.

## Constraints

- Lista metod CDP z AD-1 jest zamknięta: nowa metoda wymaga poprawki AD-1, a `Runtime.enable` jest zakazane w każdej ścieżce (AD-1).
- Driver rozmawia wyłącznie przez API PageRuntime, nigdy przez CDP ani `window.__*` hosta; logika serwisu nie trafia do C# (AD-6).
- Repo jest publiczne od 2026-10-18: fixture'y DOM są minimalne i oczyszczone, bez skryptów, danych inline, `authURL`, esn, adresu e-mail, nazw profili i adresów obrazów.
- Wpisany tekst nie trafia do logów, komunikatów ani view-modelu (spec Constraints, AD-9, AD-13).
- Okno przeglądarki nigdy nie dostaje `SW_HIDE`: D7361-1254 (S8).
- Plan Netflixa ma 2 ekrany, a zamknięcie na `/watch` trzyma ekran 1–2 min, więc przebiegi pomiaru idą po kolei i na własnej kopii profilu.
- Pomiar pauzy trwa równolegle do budowy. Żywa kontrola innego wpisu czeka na zwolnienie pliku blokady runnera, a `limit` w czasie pomiaru daje jej nie dotyczy z powodem, nie porażkę.
- Każdy komunikat strony jest rekordem Core z wyeksportowanym schematem i wygenerowanymi typami TS (AD-16); bez ręcznego parsowania w hoście.

## Done when

1. Vitest na fixture'ach DOM Netflixa pokrywa rozpoznanie ekranów, regułę Back, fokus na profilu bez wyboru i na głównej akcji ekranów `limit` i `error`; fakt `playback` niesie pola potrzebne epic-do-oceny: `title` serialu przy odcinku, `seriesId`, `lastInSeason`, `position`, `duration` (AD-6, AD-7, AD-12).
2. `--smoke netflix` przechodzi na UM760: start, `hello`, profil, odtwarzanie ≥ 1080p bez błędu DRM, pauza i reguła Back (AD-13).
3. Powrót do Netflixa zastaje odcinek na pauzie, a OK go wznawia (AD-5).
4. Wynik pomiaru pauzy wielogodzinnej jest zapisany, a jeśli wymaga polityki w Core, change do `bmad-architecture` jest rozstrzygnięty.
5. Na TV z UM760 cały wieczór (profil, przeglądanie, odtwarzanie, Back, wyjście) przechodzi D-padem bez trybu myszy; wyszukiwanie domyka epic-osk.

## Boundaries

`web/page/runtime` (rozszerzenie), `web/page/drivers/netflix`, transport i `trustedKey` w BrowserHost, przekazanie Actions, BackPolicy i krok `pause` w Orchestratorze, tytuł w linii kafelka, `--smoke netflix`, wycofanie gałęzi `web` z dev harnessu i raport foreground z DW-60 i DW-61. Nie: OSK i bramka `Keyboard` (epic-osk), `RatingPolicy` (epic-do-oceny), kolejne serwisy S12 (Non-goals), automatyczna obsługa awarii (Non-goals), osobne kody wyjścia smoke (epic-deploy-resilience).

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-8, Open Questions (pauza wielogodzinna), Success signal
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-5, AD-6, AD-7, AD-10 (wybudzenie), AD-12, AD-13 (Smoke, Hotfix drivera)
- research — _bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/digests/spike-um760.md, claims S6, S7, S8
- ux — _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, Key Flows J1, J2; _bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md, `focus-ring-injected`, Stany kafelka
- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-17, DW-35, DW-40, DW-51, DW-56, DW-58, DW-59, DW-60, DW-61

## Notes

- Open question: pauza wielogodzinna bez uśpienia PC (S8 zmierzył 30 min). Rozstrzyga story pomiarowa: S8 dla kilku godzin, dwa warianty pauzy i chowania okna, fixture'y nowych ekranów błędu. Wynik wymagający polityki w Core wraca do `bmad-architecture` jako change. Ścieżka po Modern Standby jest ustalona (AD-10).
- Decision: story pomiarowa pauzy jest pierwsza w breakdownie tej epiki i nie ma prerequisites (spike harness), żeby wynik i ewentualny change do spine'u przyszły jak najwcześniej. Można incepcjonować tę epikę wcześniej niż wynika z kolejności, żeby ją uruchomić równolegle z wcześniejszymi epikami (2026-10-18).
- Decision: przekazanie Actions z Orchestratora do strony w trybie `app` + `web` (`action`/`back` jako Command do BrowserHost, gałąź w Orchestratorze) dostarcza ta epika; epic-orchestrator-lifecycle obsługuje z Actions tylko Home (inception epiki 2, 2026-10-21).
- Waits on epic-browserhost because: minimalny PageRuntime, build page bundle. `Input.dispatchKeyEvent` z pierwotnego wpisu nie powstał w epice 6 (w `src` nie ma żadnego wywołania), więc dostarcza go wpis 13 tej epiki; krok `pause` dokłada wpis 17 (R7 epic-windows-audio).
- Decision: warunkiem wejścia w tę epikę jest story „BrowserHost i jego testy są podzielone na pliki tematyczne bez zmiany zachowania” (dawna story backlogu 20, dziś wpis 2), bo ta epika rozszerza ten sam kod; retro epic-browserhost, F1, A2 (użytkownik, 2026-10-30).
- Decision: drugim warunkiem wejścia jest story „Strona, której dokument podmienił się w przerwie CDP, odzyskuje most albo kafelek przechodzi w Failed” (dawna story backlogu 22, dziś wpis 3), DW-56; bez niej D-pad może trafić na stronę bez mostu, raportowaną jako zdrowa (użytkownik, 2026-10-30).
- Decision: oba warunki wejścia są wpisami tej epiki, a nie plikami backlogu, żeby `after` je egzekwował i jeden przebieg pętli zbudował je w kolejności. `tickets.py move` przenosi tylko wpisy, a backlog ma same pliki, więc przeniesienie jest ręczne: pliki i ich `id`, `parent`, `after` (2026-10-30).
- Decision: story pomiarowa pauzy (Decision z 2026-10-18) nie dotyka BrowserHost i zostaje bez tego warunku (użytkownik, 2026-10-30).
- Deferred: odbiór tej epiki na TV (Done when 5) sprawdza też część DW-40 o TV: kiosk po zmianie trybu i po wyłączeniu oraz włączeniu TV, okno zakrywa ekran, a Home wraca; na monitorze Dell przeszło w odbiorze epic-browserhost (2026-10-30).
- Deferred: reguła kebab-case `appId` żyje tylko w walidacji configu, a `CliArgs.Parse` sprawdza tylko pustość i `-` na początku, więc `--smoke " "` i `--smoke "a b"` dają poprawny tryb Smoke; potrzebna jedna wspólna reguła. Źródło: F2 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`); retro wskazało epic-deploy-resilience, tu, bo ta epika implementuje `--smoke netflix`. Warunek: story, która implementuje `--smoke` i pierwsza używa `appId` z CLI poza logiem (2026-10-20).
- Decision: linia stanu `Background` kafelka web z tytułem treści („W tle · <tytuł>, wstrzymane”, DESIGN.md, Stany kafelka) to osobna pozycja tej epiki: pole tytułu w snapshocie z faktu `playback` i tytuł w linii Shell; epic-home-screen pokazuje linię bez tytułu (refinement epiki 4, 2026-10-26).
- Decision: tracer to wpisy 6 i 7 (podział ze względu na rozmiar sesji; widoczny wynik daje 7). Wpisy 1–5 go poprzedzają, bo 1 to decyzja użytkownika z 2026-10-18, 2 i 3 to warunki wejścia, 4 rozstrzyga transport, bez którego tracer nie ma ścieżki, a 5 usuwa drugą drogę startu `web`. Wpisy 6–23 tworzą jeden tor, bo dotykają tego samego drivera, Orchestratora i kontraktów (2026-10-30).
- Decision: transport host → strona to hipoteza `Runtime.evaluate` bez `Runtime.enable`, a rozstrzyga ją pomiar we wpisie 4, który zapisuje poprawkę AD-1 oraz kształt i termin potwierdzenia `pause` w AD-7 i AD-14; ten sam wzorzec co spike 6.1. Strażnik testów zakazujący dziś tego literału zawęża wpis 6 (2026-10-30).
- Decision: pomiar pauzy jest podzielony, bo sesja pętli ma limit 90 min, a pomiar trwa wiele godzin: wpis 1 buduje runner i uruchamia go w tle, wpis 24 tuż przed sweepem odczytuje wynik (2026-10-30).
- Decision: 25 wpisów, ponad typowe 8–12, ale bez podziału epiki: wszystkie służą jednej zdolności, pięć pierwszych to warunki i rozstrzygnięcia, a przekroczenie bierze się z zasady „dziel, nie obcinaj” przy limicie sesji 90 min. Bez osobnej story e2e, bo przebieg całości daje `--smoke netflix` i odbiór na UM760, a źródła nie mają planu testów (2026-10-30).
- Decision: triage ledgera przy inception (użytkownik, 2026-10-30). Wchodzą do budowy: DW-56 (wpis 3), DW-58 (wpis 5), DW-60 i DW-61 (wpis 16); warunkowo DW-35 (wpis 21). Wchodzą do odbioru: DW-40, DW-51, DW-59. Częściowo: DW-17, wpis 6 nie przekazuje `Keyboard` do strony, a wpis 20 daje `textFocus`, ale bramka i wpis zostają w epic-osk. Zostają poza epiką: DW-6, DW-10, DW-11, DW-22, DW-25 (epic-osk), DW-36, DW-47 (epic-moonlight-native), DW-20, DW-21 (narzędzia pętli), DW-31 (hung shell, wymaga fizycznego pilota), DW-62, DW-63 (dla następnego sweepu, bez związku z CAP-8).
- Deferred: odbiór tej epiki na TV sprawdza też DW-59: brak czarnego ekranu i szumu po starcie Netflixa z kafelka, z częstotliwością i głębią koloru, jeśli host je zgłasza. To ten sam objaw co bug 23 (P1, backlog), którego ta epika nie naprawia: jego wystąpienie w odbiorze wskazuje na ten bug (2026-10-30).
- Deferred: odbiór tej epiki sprawdza też DW-51: jedno Home na `/watch` daje jedną Action `home`, jedną pauzę i jedno `ShowShell`, a podwójny przebieg zapisuje źródło (2026-10-30).
- Assumption: `resume` jest obsłużone na stronie, ale Orchestrator go w MVP nie wysyła, bo AD-5 wznawia przez `action{ok}` (2026-10-30).
- Assumption: bez `plan_checkpoint` i `done_checkpoint`, bo epika idzie bez nadzoru (2026-10-30).
- Decision: bramka PO z wpisu 3 („PO pokazuje wynik użytkownikowi przed `done`”) przechodzi na granicę epiki, bo pętla bez nadzoru nie ma takiego zatrzymania, a AGENTS.md zabrania parkowania ticketu: wynik pomiaru wznowienia bez OK idzie do planu i handoffu, a użytkownik widzi go przy odbiorze. Zmiana decyzji użytkownika z 2026-10-30 w trybie auto, do potwierdzenia przy odbiorze (2026-10-30).
- Decision: `--smoke netflix` wybiera profil syntetycznym `action{ok}` i odtwarza pierwszą kartę z `/browse`; zakaz wyboru profilu z AD-6 dotyczy produktu. Wysokość obrazu i key system niesie dodatkowy komunikat strony, dopisany do AD-7 we wpisie 23 (2026-10-30).
- Decision: przyciski pominięcia czołówki i następnego odcinka są w zakresie (wpis 14), bo spec mówi, że D-pad wystarcza do każdej akcji; góra i dół na `watch` nic nie robią (2026-10-30).
- Decision: termin `prepareClose` liczy Orchestrator (AD-14), a wpis 19 wyrównuje zapis „host zamyka mimo to” w AD-7 (2026-10-30).
- Decision: trzymana strzałka na `watch` przewija kolejno o 10 s, bo `action` nie ma fazy (2026-10-30).
- Deferred: odbiór sprawdza też prawdziwe uśpienie (fokus na profilu, OK, pauza zachowana), fixture'y `limit` i `error` wobec prawdziwych ekranów oraz wynik pomiaru z kryterium 6 wpisu 3, w tym częstotliwość nawigacji przy wybudzeniu (2026-10-30).
- Assumption: ekran nierozpoznany, np. logowanie, zgłasza się jak `error`, bo lista `screen` w AD-7 nie ma innej nazwy (2026-10-30).
- Open question: czy kopia profilu odtwarza Netflixa w runnerze wpisu 1. Jeśli nie, pełny pomiar przechodzi do odbioru na UM760 i Done when 4 zależy od tego przebiegu.
