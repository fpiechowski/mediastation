# Product

<!-- impeccable:product-schema 1 -->

> Kontekst produktu dla impeccable, zbudowany z `../.memlog.md` i źródeł inicjatywy (brief-mediastation, architecture-mediastation, forge-filmweb-rating-prompt). Właścicielem decyzji UX są `../DESIGN.md` i `../EXPERIENCE.md`; ten plik jest tylko roboczym wejściem.

## Platform

web

## Stack

React w WebView2 (`web/shell`), pełnoekranowe okno na Windows 11 (mini PC UM760) podłączonym do TV Hisense 1920×1080. Netflix działa w osobnym procesie Edge; MediaStation wstrzykuje do niego własne podświetlenie fokusu (PageRuntime). OSK to osobne okno nakładki MediaStation. Brak design systemu — tokeny od zera.

## Users

- **Eliza** (główna użytkowniczka): ogląda wieczorem, gdy dziecko śpi. Na co dzień używa VIDAA, bez backgroundu technicznego; „nie otworzy Menedżera zadań”. Przegrywa z nią wszystko, co wymaga myszy, pulpitu lub wiedzy o tym, co działa pod spodem.
- **Autor** (drugi domownik): gra przez Moonlight, konfiguruje system, loguje się do serwisów.

## Product Purpose

MediaStation to pełnoekranowy „smart TV OS” na Windows obsługiwany wyłącznie pilotem (G20S Pro BT: strzałki, OK, Back, Home), klawiaturą lub gamepadem. Ma zastąpić VIDAA w salonie: Netflix, Moonlight i Plex pod jednym przyciskiem Home, bez kursora, okien i pulpitu.

Sukces: Eliza przez 7 kolejnych seansów sama wybiera MediaStation zamiast VIDAA i przez tydzień nie prosi autora o pomoc.

## Positioning

Jedyna funkcja ponad parytet z VIDAA: wiersz „Do oceny” — po obejrzeniu filmu albo sezonu na ekranie głównym czeka karta z kodem QR do Filmwebu, którą Eliza skanuje telefonem.

## Operating Context

- Wieczór, za oknem ciemno, w salonie słabe ciepłe światło. Odległość kanapa–TV ok. 3 m. 10-foot UI.
- Jedna powierzchnia: TV 1920×1080. Brak mobile/responsive. Telefon tylko skanuje QR (strona Filmwebu poza UI).
- Ekran główny: zakładki Video / Audio / Game z kafelkami aplikacji (Video: Netflix, Plex; Game: Moonlight; Audio w MVP pusta), powitanie z profilem (dekoracja), nazwa MediaStation z logo w lewym górnym rogu. W Video, pod kafelkami, wiersz „Do oceny” (karty: tytuł, QR, akcje „Ocenione” / „Pomiń”; najnowsza z lewej; wiersz znika, gdy pusty).
- Aplikacje działają równolegle: Home chowa bieżącą (pauza, wyciszenie), powrót przez kafelek. Kafelki mają stany: uruchamianie, działa w tle, błąd (OK → menu z „Uruchom ponownie”).
- OSK MediaStation (QWERTY, przytrzymanie OK = polskie znaki, panel symboli, klawisz „Gotowe”) pojawia się nad Netflixem przy polu tekstowym.

## Capabilities and Constraints

- Fokus jest jedynym kursorem: musi być widoczny z 3 m, spójny w shellu i we wstrzykniętym podświetleniu Netflixa (styl dostosowany do cudzej strony, która może zmieniać DOM).
- Brak hover/myszy w shellu; w OSK działa też żyroskop pilota.
- Tytuły mogą być po angielsku (język profilu Netflixa).
- Poza MVP (nie projektować): przełącznik aplikacji pod przytrzymaniem Home, toast QR nad playerem, widgety rekomendacji, kafelki Moonlight per host/gra, tło z rodzinnymi zdjęciami.

## Brand Commitments

- Nazwa: MediaStation, w lewym górnym rogu obok logo (logo do wygenerowania po wyborze kierunku).
- Charakter: nowoczesny, estetyczny, ciemny, czysty.
- Inspiracje: Google TV, Apple TV. Eliza lubi YouTube i rodzinne zdjęcia.
- Stała preferencja (runda kierunków 2026-10-17): standard kategorii — Google TV / Apple TV wykonany z pełną starannością, bez ironii i przemyconych udziwnień. Poprzeczka jakości: Google TV i Apple TV.
- Anty-wzorce: reklamy, kafelki nastawione na marketing, kursor, okna, pulpit.
- Głos: ciepło, na ty, z lekkim humorem. Język UI: polski.

## Evidence on Hand

Brak logo, ikon aplikacji ani zdjęć. Ikony Netflix/Plex/Moonlight to cudze znaki — w mockach jako proste wordmarki/placeholdery. Nie wymyślać rekomendacji ani treści Netflixa poza przykładowymi tytułami (np. „Pamiętniki wampirów”).

## Product Principles

1. Pilot wygrywa: każdy krok z kanapy, D-padem, bez celowania.
2. Home zawsze ratuje: jeden przycisk przywraca znajomy ekran.
3. Nic nie krzyczy: zero marketingu, tylko aplikacje domowników i to, co czeka na nich.
4. Eliza nie musi wiedzieć, co jest pod spodem: stany mówią po ludzku, naprawa jest jednym OK.

## Accessibility & Inclusion

Czytelność z 3 m przy słabym świetle; fokus rozpoznawalny bez koloru (kształt/skala), kontrast WCAG AA dla tekstu na ciemnym tle.
