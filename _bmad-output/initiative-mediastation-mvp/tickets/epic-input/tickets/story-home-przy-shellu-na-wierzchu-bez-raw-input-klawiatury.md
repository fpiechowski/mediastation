---
id: 12
type: story
title: "Home przy shellu na wierzchu bez Raw Input klawiatury"
parent: epic-input
covers: [CAP-3]
after: [11]
refined: false
hitl: false
risk: high
---

# Home przy shellu na wierzchu bez Raw Input klawiatury

## Description

Wprowadza do zwykłej pracy InputHub wariant bez rejestracji Raw Input klawiatury w procesie hooka, zachowuje HID Consumer i wyzwalanie Actions przez VK, rozróżnia źródło Home/Back po właściwej parze HID usage–VK oraz utrzymuje kalibrację przycisku pilota z VK. Dokumentuje fallback `source: keyboard` dla zwykłych klawiszy pilota bez potwierdzenia urządzenia.

## Requirements

1. **Hook odbiera klawisze przy shellu na wierzchu** (epic-input, Done when 1 i 3; AD-8). W zwykłym uruchomieniu InputHub nie rejestruje Raw Input klawiatury w procesie hooka, a HID Consumer pozostaje dostępny. Na ALDER trzy osobne uruchomienia z shellem na wierzchu dają dla syntetycznych Home, Back i OK po jednym `keyEnter` down i jednej właściwej Action na naciśnięcie, bez `inputHubError`. Bez zmiennej wariantu aplikacja używa nowej ścieżki.
2. **Źródło Home/Back ma dowód urządzenia** (AD-8). Sparowany raport pilota `0x0223` → `VK_BROWSER_HOME` albo `0x0224` → `VK_BROWSER_BACK` daje `source: remote` w oknie korelacji. Brak raportu, obca sygnatura, przeterminowany raport albo inny HID usage nie nadaje `remote` wstrzykniętemu VK. Home z potwierdzonego pilota jest połykany i daje jedną Action także nad obcym oknem; Home bez tego dowodu respektuje bramkę foreground.
3. **Zwykłe klawisze i kalibracja nie tracą funkcji** (epic-input, Done when 1 i 5; AD-8). D-pad i OK nadal dają właściwe Actions według VK przy zarządzanym oknie. Gdy brak dowodu urządzenia, mają jawnie `source: keyboard`; wpis `source: remote` z `input.json` nie jest im fałszywie przypisywany. `--calibrate-input` nadal loguje `rawInput` z sygnaturą, VK i `hasVk` dla przycisków pilota, bez logowania zwykłej klawiatury; rozpoznany przycisk z VK można wpisać do `input.json` jako Action `Keyboard`. W tej ścieżce nie ma jednoczesnej rejestracji Raw Input klawiatury i aktywnego hooka LL w tym samym procesie.
4. **Dowody bieżącej maszyny i granica odbioru** (epic-input, Done when 3). Plan zapisuje wyniki builda, testów polityki i kalibracji oraz trzy przebiegi aplikacji na ALDER z asercjami logów. Fizyczny G20S Pro, `VK_BROWSER_HOME`, brak nowego okna przeglądarki i S10 16/16 pozostają do sprawdzenia przez użytkownika w odbiorze całej epiki na UM760 przez `fp-acceptance` po dostarczeniu zmiany; nie są warunkiem zamknięcia tego ticketu na ALDER.

## Boundaries

- Nie zastępować VK raportem HID jako wyzwalacza Action i nie zmieniać kontraktu Actions, routingu ani reguły jednej Action na naciśnięcie.
- Nie wprowadzać stale działającego procesu pomocniczego dla Raw Input klawiatury bez osobnego pomiaru. Tryb diagnostyczny kalibracji może korzystać z Raw Input klawiatury tylko bez aktywnego hooka w tym samym procesie.
- Zachować dotychczasowe zakończenie kalibracji po 60 sekundach albo Esc.
- Nie zmieniać polityki foreground dla klawiatury i pada ani statusu odbioru `epic-input`.

## References

- parent — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input.md, Done when 1, 3 i 5
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-8 (Notes — spike 3.11)
- spike — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/tickets/spike-dlaczego-hook-wh-keyboard-ll-nie-dostaje-klawiszy-gdy-shell.md, Wynik

## Notes

- Decision: ticket wdraża decyzję AD-8 po spike'u 3.11; wynik ALDER jest syntetyczny, a sprzętowa walidacja należy do odbioru całej epiki na UM760 (2026-10-24).
- Risk: high — błąd może przepuścić Home do powłoki albo przypisać obcy klawisz do pilota.
- Dodatkowa kontrola: w przeglądzie zmiany potwierdzić, że żaden zwykły start InputHub nie rejestruje Raw Input klawiatury, a ścieżka kalibracji nie uruchamia jej równocześnie z hookiem LL.
