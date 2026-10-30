---
id: 21
type: story
title: "Skrypty smoke BrowserHost korzystają ze wspólnego modułu"
parent: none
covers: []
after: []
assignee: ""
refined: true
hitl: false
risk: low
---

# Skrypty smoke BrowserHost korzystają ze wspólnego modułu

## Description

Dziewięć funkcji (`Wait-For`, `Stop-Profile`, `Set-Front`, `Send-Key`, `Save-Evidence`, `Note`, `Get-RunEntries`, `Get-ProfileProcesses`, `Add-Jsonl`) jest zdefiniowanych 5 razy, po razie w skryptach tracer, reconnect, errors, adopt i restart. Story 6.9 musiała tę samą poprawkę wprowadzać pięć razy. Po tej story każda z tych funkcji ma jedną definicję. To refactor narzędzi testowych, bez kodu produktu.

## Requirements

1. **Jedna definicja każdej funkcji**
   **Given** skrypty `tools/check-browserhost-*.ps1`
   **When** ktoś policzy definicje tych 9 funkcji
   **Then** każda ma dokładnie jedną definicję, we wspólnym module dołączanym przez skrypty
   **And** funkcja, której wersje w skryptach się różniły, ma w planie zapisaną różnicę i wybraną wersję z powodem

2. **Skrypty działają tak samo**
   **Given** opublikowany build Release na bieżącej maszynie
   **When** uruchamiają się smoke'i tracer, reconnect, errors, adopt i restart oraz `tools/check-browserhost-e2e.ps1 -SelfTest`
   **Then** każdy kończy się kodem 0 z tymi samymi parametrami (także `-Service`, `-EvidenceDir`) i zapisuje dowody w tych samych miejscach
   **And** każda z 25 mutacji autotestu nadal oblewa dokładnie własny wiersz

3. **Moduł działa z każdego miejsca**
   **Given** skrypt uruchomiony z katalogu innego niż `tools/`, w Windows PowerShell i w `pwsh`, w zakresie, w jakim działał przed zmianą
   **When** skrypt dołącza moduł
   **Then** znajduje go po własnej ścieżce, a brak modułu kończy skrypt czytelnym błędem i kodem różnym od 0

## Boundaries

- Must not change: kod produktu, testy .NET i web, wiersze macierzy E2E.
- Poza zakresem: pozostałe 7 funkcji powtórzonych w 3 skryptach, chyba że przeniesienie wynika wprost z kryterium 1; skrypty innych epik; podział `BrowserHost.cs` (osobna story).

## References

- source — _bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/epic-browserhost-retrospective.md, F2, A3

## Notes

- Decision: story leży w backlogu, poza epikiem 6, bo ten czeka na odbiór na UM760 (użytkownik, 2026-10-30).
- Decision: budujemy ją po odbiorze epiki 6, bo odbiór korzysta z tych skryptów (użytkownik, 2026-10-30). Nie jest warunkiem wejścia w epic-netflix-dpad.
- Weryfikacja: maszynę ustala się przed pracą (reguła „Current machine” w `AGENTS.md`). Na maszynie bez Edge lub ekranu smoke'i oznacza się jako nie dotyczy z powodem, a autotest macierzy działa wszędzie.
