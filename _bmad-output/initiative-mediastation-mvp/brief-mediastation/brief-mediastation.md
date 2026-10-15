---
title: "Product Brief: MediaStation"
status: final
created: 2026-10-10
updated: 2026-10-15
---

# Product Brief: MediaStation

## Problem

Mini PC Minisforum UM760 trafił pod telewizor z jednego powodu: jako klient Moonlight potrafi streamować gry w 4K@120 z HDR, czego nie potrafią ani telewizor, ani typowy TV box. Skoro jednak stoi tam mocny komputer z Windows 11, naturalnie powinien przejąć resztę roli TV boxa: serwisy streamingowe, Plex i bibliotekę z arr stacka, a także aplikacje, których nie ma na Hisense z VIDAA (np. Kodi).

Windows nie jest jednak zbudowany do obsługi z kanapy:

- **Serwisy streamingowe na Windows to strony WWW projektowane pod mysz.** Netflix z Microsoft Store to w praktyce PWA w Edge. Pilot G20S Pro BT emuluje mysz żyroskopem i da się tak nawigować, ale na dłuższą metę to męczące: trzeba celować wskaźnikiem w każdy element, w scrollbar czy mały przycisk trudno trafić, a strzałki, OK i Back nie działają tak jak na telewizorze.
- **Wpisywanie tekstu to najsłabszy punkt.** Wyszukiwanie tytułu czy logowanie wymaga klawiatury ekranowej, a wbudowana klawiatura Windows nie obsługuje D-pada. Celowanie w klawisze żyroskopem jest trudne.
- **Brak „domu”.** Nie ma jednego ekranu, do którego zawsze wraca przycisk Home. Wyjście z aplikacji kończy się na pulpicie Windows.
- **Aplikacje nie współistnieją jak na TV.** Przełączanie się między Moonlightem, Netflixem i Plexem oznacza żonglowanie oknami, a dźwięki z kilku aplikacji nakładają się na siebie.

Prawdziwą poprzeczkę wyznaczają jednak nie potrzeby autora, tylko potrzeby drugiej osoby w domu. Żona na co dzień korzysta z aplikacji wbudowanych w VIDAA. Nie ma backgroundu technicznego i nie otworzy Menedżera zadań, gdy coś ukradnie fokus. Dla niej każde rozwiązanie wymagające myszy, pulpitu albo wiedzy o tym, „co tam pod spodem działa”, przegrywa z pilotem od telewizora. Dziś nie ma powodu, żeby zmieniła nawyki.

Dostępne narzędzia nie zamykają tej luki. Flex Launcher, Playnite czy Steam Big Picture potrafią uruchomić przeglądarkę z pilota, ale nie sterują stroną serwisu, nie utrzymują kilku aplikacji przy życiu i nie pauzują ani nie wyciszają tej, którą się opuszcza. Entuzjaści HTPC sklejają to dziś skryptami AutoHotkey [na podstawie researchu, szczegóły w addendum].

## Rozwiązanie

MediaStation to pełnoekranowa nakładka na Windows 11, która zamienia mini PC w TV box obsługiwany wyłącznie pilotem, w stylu smart TV OS. Użytkownik nie widzi pulpitu, okien ani kursora. Widzi ekran główny z aplikacjami i serwisy, które reagują na strzałki, OK, Back i Home tak samo jak na telewizorze.

**Wieczór oczami żony:**

1. Włącza telewizor jego własnym pilotem, a telewizor od razu przełącza się na wejście HDMI MediaStation. Mini PC wybudza się przyciskiem na pilocie G20S. Pilot od TV służy tylko do włączania i wyłączania, tak jak w domach z dekoderem.
2. Widzi ekran główny MediaStation: kafelki Netflix, Moonlight, Plex i kolejnych aplikacji.
3. Strzałkami wybiera Netflix i naciska OK. Netflix otwiera się na pełnym ekranie, w przeglądarce dobranej pod ten serwis.
4. Wybiera profil, przechodzi po wierszach i wchodzi w odcinek wyłącznie D-padem. Gdy trzeba coś wpisać, pojawia się klawiatura ekranowa obsługiwana strzałkami.
5. Back cofa ekran po ekranie, a z ekranu głównego Netflixa wraca do MediaStation. Home w każdej chwili przenosi prosto do ekranu głównego MediaStation.
6. W trakcie odcinka przechodzi do innej aplikacji. Netflix pauzuje i milknie, ale się nie zamyka. Po powrocie jest dokładnie tam, gdzie była, z odcinkiem na pauzie. Wznawia go przyciskiem OK.

**Zasady obowiązujące w całym produkcie:**

- **Jeden dom.** Home zawsze działa i zawsze prowadzi do ekranu głównego MediaStation, niezależnie od tego, co jest na wierzchu.
- **Aplikacje żyją w tle.** Przełączanie nie zamyka procesów. Moonlight schowany przyciskiem Home nie kończy sesji gry.
- **Słychać tylko aktywną aplikację.** Pozostałe są wyciszane w mikserze systemowym, a serwisy streamingowe dodatkowo pauzowane.
- **Serwis decyduje o przeglądarce, nie odwrotnie.** Każdy serwis działa w przeglądarce, która sprawdza się dla niego najlepiej (np. w Edge, jeśli daje lepszy obraz lub dźwięk), a MediaStation steruje każdą z nich.
- **Netflix jako wzorzec.** Integracja z Netflixem (nawigacja D-padem, Back zgodny z TV, pauza, wykrywanie odtwarzania, klawiatura) wyznacza wzorzec dla kolejnych serwisów.

## Kryteria sukcesu

Sukcesem MVP jest wygranie uznania żony. To, że projekt działa u autora, nie jest miarą sukcesu, bo autorowi wystarczyłby żyroskop i Moonlight.

- **Wybór z własnej woli:** żona przez 7 kolejnych seansów z Netflixem sama wybiera MediaStation zamiast VIDAA.
- **Samodzielność:** przez tydzień ani razu nie musi prosić autora o pomoc.

Warunki techniczne, bez których powyższe kryteria nie mają szans się spełnić:

- Cały wieczór z Netflixem (profil, przeglądanie, wyszukiwanie, odtwarzanie, Back, wyjście) da się obsłużyć D-padem, bez trybu myszy.
- Netflix gra co najmniej w Full HD (SDR), czyli w jakości obecnego planu. Wyższa jakość jest mile widziana, ale nie jest wymaganiem. Wymaganie 4K@120 HDR dotyczy Moonlighta i jest już spełnione.
- Home zawsze przywraca ekran główny MediaStation, także gdy fokus ukradło inne okno.

## Zakres MVP

**W MVP:**

- Pełnoekranowy ekran główny w stylu smart TV, obsługiwany pilotem G20S Pro BT, klawiaturą i gamepadem.
- Uruchamianie aplikacji, w tym kilku naraz, z przełączaniem bez zamykania procesów.
- Audio focus: słychać tylko aktywną aplikację, pozostałe są wyciszone w mikserze systemowym.
- Pełna integracja Netflixa: nawigacja D-padem, Back zgodny z TV, pauza przy wyjściu, wykrywanie odtwarzania, przeglądarka dobrana pod ten serwis.
- Klawiatura ekranowa obsługiwana D-padem. W serwisach webowych pojawia się automatycznie, a w pozostałych aplikacjach pokazuje się ją i chowa przyciskiem `[ASSUMPTION: do potwierdzenia w architekturze]`.
- Moonlight: uruchamianie; Home tylko chowa okno i wycisza Moonlighta, bez kończenia sesji.
- Plex i inne aplikacje natywne: tylko uruchamianie i powrót do ekranu głównego.
- Home działający zawsze, także gdy MediaStation nie ma fokusu.
- Akcja „zamknij / uruchom ponownie aplikację” z poziomu ekranu głównego, dostępna z pilota.

**Poza MVP:**

- Kolejne serwisy streamingowe (Prime Video, HBO Max itd.), które dojdą po sprawdzeniu wzorca na Netfliksie.
- Nawigacja D-padem w Plexie i aplikacjach natywnych.
- Przełącznik otwartych aplikacji pod przytrzymaniem Home.
- Widgety z poleceniami na ekranie głównym, żeby MediaStation był czymś więcej niż launcherem (szczegóły w addendum).
- Kafelki Moonlight per host i per gra zamiast jednej ikony Moonlighta (szczegóły w addendum).
- Automatyczna obsługa awarii (popupy aktualizacji, wylogowanie, utrata sieci). W MVP rozwiązywane ręcznie.
- Autostart z Windowsem i ukrycie pulpitu, które autor konfiguruje sam, poza produktem.
- HDMI-CEC, którego UM760 nie obsługuje. Telewizor włącza się jego własnym pilotem.
- Wydanie open source.

## Wizja

MediaStation całkowicie zastępuje VIDAA w salonie. Każdy serwis streamingowy, z którego korzysta dom, działa z pilota jak na TV, w najlepszej jakości, jaką daje sprzęt. Gry z Moonlighta, biblioteka z arr stacka i serwisy streamingowe współistnieją pod jednym przyciskiem Home. Jeśli projekt się sprawdzi, może zostać wydany jako open source dla innych entuzjastów HTPC na Windows.

## Otwarte pytania i ryzyka

- **Wykrywanie automatyzacji:** czy Netflix i inne serwisy blokują przeglądarki sterowane z zewnątrz? Do sprawdzenia w pierwszym spike'u.
- **Jakość per serwis:** HDR i Atmos w Netfliksie są dziś dostępne tylko w Edge, a Prime Video, Disney+ i HBO Max w przeglądarkach desktopowych mają podobno limit ok. 720p. Przy obecnym celu Full HD nie blokuje to MVP, ale może wpłynąć na wybór przeglądarki dla kolejnych serwisów.
- **Kruchość nawigacji D-padem:** zmiany UI Netflixa mogą psuć nawigację. W MVP akceptujemy, że autor dostarcza poprawki ręcznie.
- **Klawiatura ekranowa poza serwisami webowymi:** koszt jej automatycznego pokazywania jest do oszacowania.
- **Ocena na Filmwebie / IMDb po seansie:** pod koniec filmu MediaStation mógłby zaproponować ocenę obejrzanej produkcji. Żona dużo ocenia na Filmwebie, a VIDAA tego nie daje, więc to potencjalnie mocny powód do zmiany nawyku. Otwarte: identyfikacja tytułu, brak publicznego API do ocen, tańszy wariant z kodem QR. Decyzja „MVP czy kolejna inicjatywa” zapadnie po `bmad-forge-idea`.
