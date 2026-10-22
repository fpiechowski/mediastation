---
id: 2
type: bug
title: "Ścisły odczyt koperty przyjmuje zduplikowane klucze i aliasy enumów"
parent: none
covers: []
after: []
assignee: ""
refined: true
hitl: false
risk: low
severity: P3
---

# Ścisły odczyt koperty przyjmuje zduplikowane klucze i aliasy enumów

## Description

Wiadomość z tym samym kluczem dwa razy jest przyjmowana i wygrywa ostatnia wartość, choć walidacja configu odrzuca duplikaty. Kontrakty mają odrzucać duplikaty tak samo, a enum z aliasem nie może wywracać odczytu wyjątkiem spoza `JsonException`.

## Reproduction

1. `dotnet test` w repo; test lub REPL na `ContractJson.Deserialize<Envelope<JsonElement?>>`.
2. Wejście: `{"v":1,"type":"snapshot","type":"ready","payload":null}`.
3. Actual: obiekt odczytany, `type` = `ready`. Expected: `JsonException`.
4. To samo dla payloadu `Action`: `{"kind":"up","kind":"down","phase":"press","source":"keyboard","tsMono":1}` — actual: `kind` = `down`, expected: `JsonException`.
5. Enum z dwiema nazwami tej samej wartości odczytany przez `ContractJson.Deserialize<TEnum>`: `StrictEnumConverter` rzuca `ArgumentException`/`TargetInvocationException` zamiast `JsonException`.

## Cause Hypothesis

`ContractJson.CreateReadOptions` nie ustawia `AllowDuplicateProperties = false`, inaczej niż `ConfigJson`. `StrictEnumConverter` dodaje nazwy do słownika bez guardu na duplikat, więc enum z aliasem wywraca odczyt wyjątkiem spoza `JsonException`.

## Acceptance Criteria

1. **Duplikat w kopercie**
   **Given** `{"v":1,"type":"snapshot","type":"ready","payload":null}`
   **When** odczyt przez `ContractJson.Deserialize<Envelope<JsonElement?>>`
   **Then** `JsonException`

2. **Duplikat w payloadzie**
   **Given** koperta `action` z payloadem `{"kind":"up","kind":"down","phase":"press","source":"keyboard","tsMono":1}`
   **When** odczyt przez `DeserializeEnvelope<Action>`
   **Then** `JsonException`

3. **Alias enuma**
   **Given** enum z dwiema nazwami tej samej wartości
   **When** odczyt
   **Then** wynik jest deterministyczny albo `JsonException`
   **And** żaden wyjątek spoza `JsonException` nie wychodzi

4. **Zapis i schematy bez zmian**
   **Given** `ContractJson.Options` używane do zapisu i eksportu schematów
   **When** `dotnet build -c Release`
   **Then** `web/contracts/schema/*.schema.json` bez zmian

5. **Testy pokrywają znaleziony warunek**
   **Given** zestaw testów
   **When** `dotnet test`
   **Then** testy pokrywają duplikat w kopercie, duplikat w payloadzie i enum z aliasem, a istniejące testy przechodzą
   **And** na kodzie przed poprawką co najmniej jeden z nich pada

## Boundaries

- Must not change: `ContractJson.Options` dla zapisu i schematów zostaje pobłażliwe; zmiana dotyczy tylko `ReadOptions`.
- Must not change: zachowanie `ConfigJson` i ekranu błędu configu.

## References

- parent — none
- retro — _bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md, N7, R6
- source — src/MediaStation.Core/Contracts/ContractJson.cs
- source — src/MediaStation.Core/Contracts/StrictEnumConverter.cs
- source — src/MediaStation.Core/Config/ConfigValidation.cs, `ConfigJson`
- tests — tests/MediaStation.Core.Tests/ConfigValidationTests.cs, CoreMessageContractTests.cs

## Notes

- Decision: guard aliasów robimy teraz razem z duplikatami, zamiast odkładać do pierwszego nowego enuma (2026-10-21).
- Decision: `ContractJson.Options` pozostaje publiczny i pobłażliwy dla zapisu i schematów (N8, accepted, 2026-10-21).
- Severity P3: dziś nie ma ścieżki, która wykorzystuje duplikat; renderer jest nasz. Poprawka jest prewencyjna.
- Decision: logowanie `bridgeMessageRejected` i test mostu zostają poza tym ticketem; most już łapie `JsonException`, a jego testy są odłożone do epic-home-screen (N3, R4, 2026-10-21).
