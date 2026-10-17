/bmad-correct-course: uzgodnienie briefu i architektury MediaStation MVP z finalnym UX (slug: mediastation)

TRIGGER ZMIANY
Run bmad-ux zakończył się 2026-10-17 (status: final). W trakcie journeys z użytkowniczką (Eliza) i w Reviewer Gate zapadły decyzje, które zmieniają albo doprecyzowują brief i architekturę (status: final). Nie ma jeszcze epików ani stories, więc impact dotyczy tylko briefu i architektury. Nie dotyczy sprintu.

ŹRÓDŁA (czytaj przez subagentów)
- _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, a w nim przede wszystkim:
  - callout pod H1,
  - sekcje „Handoff do architektury”, „Deferred / poza MVP” i „Open assumptions”.
- _bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md (tylko jako kontekst wizualny)
- _bmad-output/initiative-mediastation-mvp/ux-mediastation/.memlog.md: kanoniczny log decyzji UX. Typy wpisów:
  - decision: potwierdzone przez użytkownika,
  - assumption: przyjęte robocze, wymagają potwierdzenia,
  - override: świadome odstępstwo.
- _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md
- _bmad-output/initiative-mediastation-mvp/brief-mediastation/brief-mediastation.md (+ addendum.md)
- _bmad-output/initiative-mediastation-mvp/forge-filmweb-rating-prompt/forge-filmweb-rating-prompt.md

ZMIANY DO OCENY

A. Zmiany w architekturze (odstępstwa od zapisanych decyzji)
1. Override AD-8: w trybie `osk` pointer (żyroskop pilota) jest dozwolony. Dziś AD-8 blokuje mysz i żyroskop w `osk`. Decyzja użytkownika (memlog: override).
2. Zawężenie AD-9 i briefu: Action `Keyboard` z pilota nie wchodzi do MVP. OSK pojawia się wyłącznie automatycznie na `textFocus` w serwisach webowych. W Moonlight i Plex tekst wpisuje się z klawiatury albo gamepada. Wywołanie OSK z pilota idzie do deferred. Status: assumption R10, do potwierdzenia.
3. Rozszerzenie `apps.json` (AD-16): pole kategorii `video|audio|game`, bo ekran główny ma zakładki Video / Audio / Game. Brief mówi tylko o kafelkach. Zakładka Audio jest w MVP pusta (empty state). Decyzja użytkownika.
4. AD-8 + AD-10: OK na kafelku w stanie `Failed` daje Intent `AppMenu` z fokusem na „Uruchom ponownie”. Wyjście z `Failed` nadal prowadzi tylko przez AppMenu. Decyzja użytkownika.

B. Nowe zachowania, których architektura nie opisuje (assumptions z Reviewer Gate, do potwierdzenia)
5. R7, Foreground → `Failed`: Orchestrator sam pokazuje Home, fokus staje na kafelku `Failed` (AD-10, AD-15, AD-16).
6. R8, zerwane CDP przy żywym procesie: brak UI, D-pad niebuforowany, po limicie reconnectu przejście w `Failed` (AD-7, AD-10).
7. R9, profil Netflixa: ServiceDriver/PageRuntime rozpoznaje `profiles` i fokusuje ostatnio używany profil, ale go nie wybiera, bo wybiera Eliza. To doprecyzowuje AD-10 („wybór profilu robi driver”).
8. R1, fokus w trybie `osk`: PageRuntime przełącza ring pola tekstowego na styl `input-active`, a jedyny fokus na ekranie jest na klawiszu OSK (AD-7, AD-9).
9. R4, wstrzykiwany fokus: biały obrys z ciemną obwódką, promień elementu (`getComputedStyle`, fallback 4 px), bez skali (AD-7).
10. R12, OSK: `phase: press` wstawia literę, `phase: long` otwiera rząd polskich wariantów bez wstawiania litery bazowej (AD-8, AD-9).

C. Wartości, które architektura oddała UX-owi (tylko do wpisania, bez dyskusji)
- Mapowanie `AppMenu`: long-press OK (`phase: long`) na kafelku w trybie `shell`.
- `ratingPendingDays` = 7.
- Moonlight: `back=key:Escape`, `pointer` dozwolony.
- Plex / natywne: `back=home`, `pointer` dozwolony.
- UX ekranów `limit` i `error`: ekran Netflixa bez zmian, PageRuntime stawia fokus na głównej akcji.
- Fokus po wybudzeniu i zimnym starcie: zakładka Video, pierwszy kafelek.
- QR payload: krótki URL wyszukiwarki Filmwebu, ECC L (assumption R2; test skanowania z 3 m otwarty).

D. Brief i forge
- Etykieta akcji karty: „Ocenione” zamiast „Oceniłam” (neutralnie rodzajowo, zgodnie z forge). Decyzja użytkownika.
- Do deferred, jeśli brief ich jeszcze nie ma:
  - tło Home z rodzinnymi zdjęciami,
  - właściwe logo,
  - wywołanie OSK z pilota (pkt 2),
  - undo dla „Pomiń”.

OCZEKIWANIA
- Najpierw rozdziel, co jest potwierdzoną decyzją, a co założeniem. Założenia (R1–R16, A1–A26 w EXPERIENCE.md → Open assumptions) przedstaw mi do potwierdzenia albo odrzucenia, zanim trafią do architektury.
- Dla każdej zmiany: dotknięte AD i sekcje briefu, rekomendacja (direct adjustment / bez zmian / odrzucenie) i proponowany diff tekstu.
- Odrzucenie albo modyfikacja zmiany, którą UX uznał za decyzję, wymaga też Update w bmad-ux. Wskaż to wprost.
- Nie zmieniaj DESIGN.md ani EXPERIENCE.md. Właścicielem jest bmad-ux.
- Dokumenty po polsku, z anglicyzmami technicznymi.
