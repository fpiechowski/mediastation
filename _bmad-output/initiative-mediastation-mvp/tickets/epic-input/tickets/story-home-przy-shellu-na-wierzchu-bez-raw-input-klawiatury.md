---
id: 12
type: story
title: "Home przy shellu na wierzchu bez Raw Input klawiatury"
parent: epic-input
covers: [CAP-3]
after: [11]
refined: true
hitl: false
risk: high
---

# Home przy shellu na wierzchu bez Raw Input klawiatury

## Description

W zwykłym uruchomieniu InputHub odbiera klawisze nad shellem bez Raw Input klawiatury w procesie hooka. Raport HID Consumer potwierdza źródło Home/Back tylko po dopasowaniu pilota, czasu i właściwej pary usage–VK. Kalibracja nadal pozwala wybrać przycisk pilota z VK.

## Requirements

1. **Hook działa nad shellem bez Raw Input klawiatury** (epic-input, Done when 1 i 3; AD-8).
   **Given** zwykły start bez zmiennych eksperymentalnych i shell na wierzchu,
   **When** InputHub rejestruje wejście, a potem następuje naciśnięcie zmapowanego VK,
   **Then** w procesie z hookiem LL rejestrowany jest HID Consumer, lecz nie Raw Input klawiatury, i powstaje jedna właściwa Action na naciśnięcie, bez `inputHubError`.
   **And** trzy osobne uruchomienia na ALDER z syntetycznymi Home, Back i OK dają dla każdego naciśnięcia jeden `keyEnter` down i jedną Action przy shellu na wierzchu. W tych przebiegach włączony jest wyłącznie diagnostyczny `MS_INPUT_TRACE=1`; ścieżka domyślna nie wymaga zmiennej wariantu. Logi każdego procesu i sposób ustalenia aktywnych rejestracji trafiają do planu.
2. **Tylko właściwy raport pilota potwierdza Home lub Back** (AD-8).
   **Given** wstrzyknięty VK i raport HID Consumer w oknie korelacji AD-8,
   **When** sygnatura pasuje do urządzenia `remote` oraz para wynosi `0x0223` → `VK_BROWSER_HOME` albo `0x0224` → `VK_BROWSER_BACK`,
   **Then** jedna Action ma `source: remote` przy zarządzanym oknie; Home jest połknięty i daje Action również nad obcym oknem, a Back nad obcym oknem nadal jest przepuszczany bez Action.
   **And** brak raportu, obca sygnatura, raport poza oknem czasu, odwrotna para lub inny usage nie nadają `remote`. Bez potwierdzenia Home nad obcym oknem jest przepuszczany bez Action. Jeden raport nie potwierdza dwóch kolejnych naciśnięć.
3. **Zwykłe klawisze zachowują działanie i uczciwe źródło** (epic-input, Done when 1; AD-8).
   **Given** zarządzane okno na wierzchu i mapowanie VK w `input.json`,
   **When** pilot wysyła D-pad, OK lub inny zwykły klawisz bez dowodu urządzenia,
   **Then** każde naciśnięcie daje właściwą pojedynczą Action według VK z `source: keyboard`, także gdy mapowanie VK pochodzi z wpisu `remote`.
   **And** dotychczasowe reguły key-up, long, repeat, własnego znacznika `SendInput` i bramki foreground pozostają skuteczne.
4. **Kalibracja nadal pozwala wybrać przycisk Klawiatura** (epic-input, Done when 5; AD-8).
   **Given** `--calibrate-input` z pilotem o sygnaturze `remote` w `input.json`,
   **When** następują naciśnięcia przycisków pilota i zwykłej klawiatury,
   **Then** wpisy `rawInput` dotyczą wyłącznie pilota i podają sygnaturę, `hasVk` oraz VK, jeśli jest dostępny; przycisk z VK można przypisać do Action `Keyboard`.
   **And** tryb nie połyka klawiszy, kończy się po 60 sekundach lub fizycznym Esc oraz nie rejestruje Raw Input klawiatury równocześnie z aktywnym hookiem LL w tym samym procesie. Testy obejmują przycisk bez VK, filtr zwykłej klawiatury i obie drogi zakończenia.

## Boundaries

- Nie zastępować VK raportem HID jako wyzwalacza Action i nie zmieniać kontraktu Actions, routingu ani reguły jednej Action na naciśnięcie.
- Nie wprowadzać stale działającego procesu pomocniczego dla Raw Input klawiatury bez osobnego pomiaru. Tryb diagnostyczny kalibracji może korzystać z Raw Input klawiatury tylko bez aktywnego hooka w tym samym procesie.
- Zachować dotychczasowe zakończenie kalibracji po 60 sekundach albo Esc.
- Nie zmieniać polityki foreground dla klawiatury i pada ani statusu odbioru `epic-input`.
- Trzy przebiegi z wejściem syntetycznym na ALDER nie dowodzą zachowania fizycznego G20S Pro ani braku nowego okna przeglądarki przy `VK_BROWSER_HOME`. Użytkownik sprawdzi je razem z S10 16/16 podczas odbioru całej epiki na UM760 przez `fp-acceptance`.

## References

- parent — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input.md, Done when 1, 3 i 5
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-8 (Notes — spike 3.11)
- spike — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/tickets/spike-dlaczego-hook-wh-keyboard-ll-nie-dostaje-klawiszy-gdy-shell.md, Wynik

## Notes

- Decision: ticket wdraża decyzję AD-8 po spike'u 3.11; wynik ALDER jest syntetyczny, a sprzętowa walidacja należy do odbioru całej epiki na UM760 (2026-10-24).
- Risk: high — błąd może przepuścić Home do powłoki albo przypisać obcy klawisz do pilota.
- Dodatkowa kontrola: w przeglądzie zmiany potwierdzić, że żaden zwykły start InputHub nie rejestruje Raw Input klawiatury, a ścieżka kalibracji nie uruchamia jej równocześnie z hookiem LL.
- Decision: doprecyzowano warunek parowania usage–VK, zużycie raportu przez jedno naciśnięcie i osobną ścieżkę kalibracji; wybór techniki kalibracji zostaje w granicach AD-8 (2026-10-24).
- Weryfikacja na ALDER: plan zapisuje wynik testów polityki i kalibracji, build Release, trzy uruchomienia aplikacji z asercjami logów oraz N/A z powodem dla fizycznego pilota i UM760.
