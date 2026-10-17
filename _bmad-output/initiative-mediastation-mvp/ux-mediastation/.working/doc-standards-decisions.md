# Doc standards (bmad-review: structure → prose): decyzje facylitatora (2026-10-17)

## STRUCTURE: przyjęte wszystkie rekomendacje z wyjątkiem jednej

**Wyjątek: kolumna „Gdzie” w Open assumptions ZOSTAJE.** Rejestr służy człowiekowi, który potwierdza założenia. Lookup jest wygodniejszy niż grep.

Przyjęte:
1. Callout `bmad-correct-course` pod H1 EXPERIENCE: override AD-8 (pointer w `osk`), zawężenie AD-9 (R10). Szczegóły w Handoff.
2. Copy stanów kafelka (chip i linia stanu) ma jedno źródło: DESIGN → „Stany kafelka”. W EXPERIENCE → State Patterns → Kafelek zostają tylko kolumny Stan i Zachowanie oraz pointer do DESIGN.
3. Wiersz „Focus ring” w DESIGN → Components skrócić do pointera do Elevation & Depth.
4. Reguła 3 w DESIGN → Colors (kontrast fokusu) skrócić do progu i pointera do `components.focus-*`.
5. Peek/scroll wiersza „Do oceny”:
   - geometria tylko w DESIGN → Layout,
   - zachowanie tylko w EXPERIENCE → Interaction Primitives → Scroll Home,
   - pozostałe miejsca to pointery.
6. Sekcja „Do oceny” w EXPERIENCE: zostają triggery (Film, Serial, Fallback/`ratingPendingDays`) i wiersz „Odrzucone (forge)”. Wiersze powtórzone gdzie indziej usunąć.
7. Do EXPERIENCE → Foundation dodać:
   - Użytkownicy: Eliza (codzienna użytkowniczka, nietechniczna), autor (konfiguruje i naprawia),
   - krótki glosariusz pojęć procesu (forge, memlog, kanon, spine, Reviewer Gate).
8. DESIGN → Brand & Style, akapit Referencje wizualne: dodać „Tagi `[ASSUMPTION …]` rozwiązuje `EXPERIENCE.md` → Open assumptions.”
9. R10: w Foundation i IA tylko krótka forma z tagiem. Pełna treść zostaje w Interaction Primitives → OSK, Handoff i rejestrze.
10. R14 (higiena mocków): usunąć z Brand & Style i z wiersza `surface-dim`. Zostaje w Do's and Don'ts (forma preskryptywna).
11. Linia „Odrzucone kierunki wizualne” przechodzi z EXPERIENCE → Inspiration do DESIGN → Brand & Style.
12. Responsive & Platform dołączyć do Foundation jako wiersz „Okna / platforma” (WebView2 shell, Netflix w Edge, OSK jako okno topmost NOACTIVATE z fallbackiem otwartym w architekturze). Sekcja Responsive znika.
13. Akapit orientacyjny („DESIGN.md = wygląd…”) przenieść bezpośrednio pod H1 EXPERIENCE.
14. Notatki we frontmatterze DESIGN ograniczyć do faktów wizualnych. Behawior z `empty-state`, `rate-row` i `card-do-oceny` przenieść do EXPERIENCE, jeśli jeszcze go tam nie ma.
15. QR: payload i otwarty test z 3 m przenieść do EXPERIENCE (Component Patterns QR i rejestr R2). W DESIGN zostaje wygląd: rozmiar, quiet zone, ECC L, moduły.
16. State Patterns → Powierzchnie: „Błąd konfiguracji” przy wierszach Home/Shell, wiersze Netflixa razem.
17. Usunąć „IMDb — odrzucony” z Deferred.
18. Usunąć z J2 → Awaria zdanie o przełączniku pod Home.
19. Usunąć wiersz R11 i tag `[ASSUMPTION R11]` we wstępie rejestru. Zdanie wstępu zostaje.
20. Zostają bez zmian (PRESERVE): tabela Handoff oraz nota „spine'y wygrywają” w obu plikach.

## PROSE: przyjęte wszystkie poprawki, z takimi rozstrzygnięciami

- **Moonlight w tle:** „Aplikacja schowana i wyciszona (Netflix: pause → mute → minimize; Moonlight: mute → minimize, sesja żyje)”. W Home: „pause, jeśli aplikacja to wspiera → mute → minimize”.
- **Zakres kontrastu `focus-halo`:** wszędzie 15.3–19.0:1. Przelicz #0F1012 względem #FFFFFF (≈19.0) i jasnego artworku.
- **Undo:** „bez undo w MVP (kolejka nie usuwa kluczy, więc undo można dodać później bez utraty danych)”.
- **Timeout ~500 ms:** „jeśli strona nie potwierdzi obsługi Back w ~500 ms, host wraca na Home”.
- **„Limit slotu (1–2 min)”:** „Limit ekranów Netflixa (po zamknięciu w trakcie odtwarzania slot jest zajęty jeszcze 1–2 min)”.
- **„(memlog)”:** „(decyzja w memlogu)”.
- **Preselekcja AppMenu:** jednolicie „z fokusem na „Uruchom ponownie””.
- **Klawisz a ekran:** „klawisz Home”, gdy chodzi o przycisk.
- **Drobne poprawki:**
  - forma tagów zawsze `[ASSUMPTION <id>]`,
  - jednostki px przy gołych liczbach,
  - zapis „≥ 90%” wszędzie,
  - „roboczo”,
  - „fallback 4px” i token `focus-halo`,
  - przykład wariantów OSK (a → ą, z → ź ż),
  - „neutralne rodzajowo”,
  - „Failed także przy niezgodnej wersji major protokołu”,
  - szyk z Orchestratorem,
  - „nawigacja przestrzenna”,
  - „oparta na kluczach”, „każdy klucz trafia do kolejki tylko raz”,
  - fokus karty z czasownikiem, Plex,
  - „Otwiera się automatycznie tylko na `textFocus`”,
  - „nad nagłówkiem / pod nagłówkiem”,
  - podmiot „kontrast”, „artwork”,
  - przecinek w Voice Do,
  - „Świadomy wyjątek”,
  - J3 „Wyjątki:”.
- **Elevation & Depth lead-in:** „Głębia ma dwa poziomy — spoczynek i lift kafelka; pozostałe pozycje to warianty fokusu i nakładki bez liftu:”.

**Nie zmieniać** cudzysłowowanych stringów UI ani kolejności sekcji wymaganej przez spec.
