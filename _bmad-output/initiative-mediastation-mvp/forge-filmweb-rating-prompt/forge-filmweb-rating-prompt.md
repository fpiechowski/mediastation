---
title: "Forge: przypomnienie o ocenie na Filmwebie"
status: hardened
created: 2026-10-15
---

# Przypomnienie o ocenie na Filmwebie

**Decyzja:** wchodzi do MVP w wariancie minimalnym. Uzasadnienie: reszta zakresu MVP to parytet z VIDAA, a to jedyna funkcja, która daje żonie powód, żeby *wolała* MediaStation.

## Dlaczego

- Dziś żona ocenia po seansie, po chwili rozmowy, w aplikacji Filmweb na telefonie. Musi szukać tytułu i czasem zapomina ocenić, a zależy jej na kompletności ocen.
- Funkcja rozwiązuje oba problemy: szukanie (QR prowadzi do tytułu) i zapominanie (karta czeka na ekranie głównym).

## Zakres MVP

- **Wiersz „Do oceny” na ekranie głównym.** Karta z tytułem i kodem QR czeka, aż ktoś wybierze „Ocenione” albo „Pomiń”. Jeden tytuł trafia do kolejki tylko raz.
- **Kod QR z URL-em wyszukiwania Filmwebu** z tytułem z playera Netflixa. Ocenę wystawia się w aplikacji na telefonie, zalogowanej na konto tego, kto skanuje.
- **Film:** trafia do kolejki po obejrzeniu ok. 90% `duration`. Seans przerwany wcześniej nie trafia.
- **Serial:** oceniany jako całość, trafia do kolejki po ostatnim odcinku sezonu (`SxEy` z playera porównane z liczbą odcinków sezonu). Pojedynczych odcinków nie oceniamy.
- **Dowolny profil Netflixa.** Wspólne seriale bywają na profilu każdego z nas. Tekst karty jest neutralny, a MediaStation nie trzyma żadnego konta Filmwebu.

## Poza MVP (kolejna inicjatywa)

- **Toast z QR przy napisach lub na post-playu,** nad pełnoekranowym Edge, bez zabierania fokusu i bez pauzowania odtwarzania. Trafia w moment rozmowy po seansie. Ryzyko: topmost okno nad fullscreenem nie było testowane w spike'u.
- **Mapowanie na stronę tytułu zamiast wyszukiwania:** Wikidata, Netflix ID (P1874) → Filmweb film ID (P5032). Wymaga id serialu, a nie odcinka. Pokrycie nowych tytułów nie zostało sprawdzone.

## Odrzucone

- **Strona Filmwebu w Chrome na TV z nawigacją D-padem:** nie daje nic ponad QR, bo żona i tak trzyma telefon w ręce. Do tego wymaga logowania w profilu przeglądarki, cookie-walla i reklam, czyli kolejnej kruchej integracji webowej.
- **Wystawianie oceny z pilota:** ani Filmweb, ani IMDb nie mają oficjalnego API do ocen. Zostają nieoficjalne API albo cookies sesji, co jest kruche i stoi w szarej strefie ToS.
- **Pokazywanie karty tylko po seansach na profilu żony:** gubi wspólne seriale oglądane na profilu autora.
- **IMDb:** żona ocenia na Filmwebie.

## Do sprawdzenia przy implementacji

- Wykrycie ostatniego odcinka sezonu przy starcie z „Continue Watching” (czy lista odcinków jest dostępna bez modala). Fallback rozstrzygnięty w architekturze (architecture spine, AD-12): gdy nie da się ustalić końca sezonu, serial czeka jako „w zawieszeniu”, a karta pojawia się po N dniach bez kolejnego odcinka.
- Czy QR z linkiem filmweb.pl otwiera aplikację Filmweb na telefonie żony, czy przeglądarkę mobilną (sprawdzenie zajmuje minutę).
- Język tytułu w playerze zależy od języka profilu (np. angielski na profilu autora). Trzeba sprawdzić, czy wyszukiwarka Filmwebu znajduje tytuły oryginalne i angielskie.
- „Ocenione” jest ręczne, bo MediaStation nie wie, czy ocena została wystawiona.
