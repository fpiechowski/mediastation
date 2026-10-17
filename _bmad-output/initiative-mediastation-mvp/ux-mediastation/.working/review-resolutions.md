# Rozstrzygnięcia znalezisk z Reviewer Gate (2026-10-17)

Źródło: soczewki rubric, impeccable critique i impeccable audit. Status: założenia (assumption) przyjęte po poleceniu „Dokończ”, do potwierdzenia przez użytkownika. Wzorzec: standard kategorii Google TV / Apple TV.

R1. **Jeden fokus w trybie `osk`.** Białe ringi przechodzą wyłącznie na klawisz OSK. Pole docelowe w Netflixie dostaje styl „active input”: kursor tekstowy i linia 2px `text-2` pod polem, bez białego ringu.

R2. **QR.**
- Payload: krótki URL wyszukiwarki Filmwebu, poziom korekcji błędów L.
- Quiet zone: co najmniej 4 moduły na białym tle, moduły w pełnych pikselach.
- Rozmiar: ok. 240 px w spoczynku, ok. 360 px, gdy fokus jest w wierszu „Do oceny”.
- Otwarte: test na prawdziwym TV dwoma telefonami z 3 m.

R3. **Title-safe i wiersz „Do oceny”.**
- Marginesy title-safe: 5% (96 px w poziomie, 54 px w pionie).
- W spoczynku wiersz „Do oceny” wystaje (peek) spod kafelków: widać nagłówek i górę kart.
- Gdy fokus wchodzi do wiersza, strona przewija się w górę i cały wiersz mieści się w title-safe (wzorzec Google TV).
- Większy odstęp nad nagłówkiem wiersza.

R4. **Fokus wstrzykiwany do Netflixa.**
- Biały obrys 5 px plus ciemna obwódka zewnętrzna 3 px `ground` (#0F1012), żeby był widoczny także na jasnych plakatach.
- Promień przejmuje promień elementu Netflixa (domyślnie 4 px), a nie 22 px.
- Bez skali i bez cienia.

R5. **Fokus małych kontrolek** (przyciski, klawisze OSK, pozycje AppMenu, zakładki): odwrócona pigułka, czyli białe wypełnienie i tekst `ground`, bez skali. „Pomiń” w spoczynku ma subtelne wypełnienie zamiast obrysu, który udawał ring.

R6. **Stany kafelka (chip + linia stanu).** Każdy stan ma krótki chip na kafelku i pełną linię stanu pod nazwą, tak jak stan „W tle”.

| Stan | Chip | Linia stanu |
|---|---|---|
| Background | „Wstrzymane” (Netflix) / „Wyciszony” (Moonlight) | „W tle · <tytuł>, wstrzymane” (Netflix) albo „W tle · wyciszony” (Moonlight) |
| Starting | „Uruchamiam…” | „Chwilka…” [copy assumption] |
| Stopping | „Zamykam…” | — |
| Failed | „Nie odpowiada” | „Coś się zacięło. Wciśnij OK” |

Ikony z jednego zestawu: Material Symbols Rounded (`pause`, `progress_activity`, `error`, `close`, `check`, `backspace`, `space_bar`).

R7. **Aplikacja pada na pierwszym planie** (Failed z Foreground): MediaStation sama wraca na Home, fokus staje na kafelku w stanie Failed, więc jedno OK otwiera AppMenu z „Uruchom ponownie”.

R8. **Zerwane CDP przy żywym procesie:** brak osobnego UI. Home zawsze ratuje. Jeśli reconnect nie uda się w limicie czasu, aplikacja przechodzi w Failed (R7). D-pad nie jest buforowany.

R9. **Profil Netflixa wybiera Eliza.** ServiceDriver/PageRuntime rozpoznaje ekran `profiles` i stawia fokus na ostatnio używanym profilu, ale go nie wybiera. Po wybudzeniu tak samo.

R10. **Action `Keyboard` z pilota:** nie w MVP. Trafia do Deferred i do Handoff jako zawężenie AD-9 dla pilota (do bmad-correct-course).

R11. **Sekcja „Open assumptions” w EXPERIENCE.md** zbiera wszystkie `[ASSUMPTION]` z obu spine'ów w tabeli (id, treść, miejsce). Tagi inline zostają z odnośnikiem.

R12. **OSK.**
- Klawisze: QWERTY, „Usuń” (backspace), „Spacja”, „123” (panel cyfr i symboli), „Gotowe”.
- Klawisze z wariantami (a c e l n o s z) mają mały znacznik w rogu.
- Krótki OK (phase press) wstawia literę. Długi OK (phase long) otwiera wiersz wariantów bez wstawiania litery bazowej, z fokusem na pierwszym wariancie. Back zamyka wiersz wariantów.

R13. **Empty state** jako komponent w obu spine'ach: typografia body, `text-2`, w pasie kafelków. Fokus zostaje na zakładce.

R14. **Mocki używają wyłącznie tokenów.** Hexy spoza palety (#24272C, #1B1D21) zastępuje token `surface-dim` (#1A1C20) albo `surface`.

R15. **„Pomiń” bez undo w MVP** (świadomie, bo kolejka i tak nigdy nie usuwa kluczy).

R16. **Długi tytuł karty „Do oceny”:** tytuł ma maksymalnie 2 linie z wielokropkiem. Podpowiedź „Zeskanuj telefonem i oceń na Filmwebie” ma 1 linię. Przyciski są zakotwiczone na dole karty i nigdy nie wypadają poza kartę.
