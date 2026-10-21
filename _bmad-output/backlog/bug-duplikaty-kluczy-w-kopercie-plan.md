---
title: 'Strict envelope read rejects duplicate keys and enum aliases'
type: 'bugfix'
ticket: 2
created: '2026-10-21'
status: 'built'
baseline_revision: '9a2e48189fcda83d5d7caf2df0308d1e3b00382c'
route: 'oneshot'
route_source: 'auto'
risk: 'low'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/backlog/bug-duplikaty-kluczy-w-kopercie.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `ContractJson.ReadOptions` dziedziczy `AllowDuplicateProperties = true`, więc koperta z powtórzonym `type`/`v` albo payload z powtórzonym `kind` są przyjmowane i wygrywa ostatnia wartość. `StrictEnumConverter` buduje mapę wartości przez `Add`, więc enum z dwiema nazwami tej samej wartości rzuca wyjątek spoza `JsonException`.

**Approach:** Ustawić `AllowDuplicateProperties = false` tylko w `ReadOptions` (zapis i schematy zostają pobłażliwe) i uczynić mapy konwertera odpornymi na aliasy: każda nazwa czytana, deterministyczna nazwa dla wartości, żaden wyjątek spoza `JsonException`.

</frozen-after-approval>

## Implementation Notes

- `ContractJson.CreateReadOptions`: `AllowDuplicateProperties = false`; `Options` dla zapisu i schematów bez zmian.
- `StrictEnumConverter`: iteruje `Enum.GetNames` (obejmuje aliasy) i używa indekserów, więc każda nazwa jest czytana, a zapis wybiera deterministyczną nazwę.
- Testy: duplikat w kopercie, duplikat w payloadzie, enum z aliasem i nieznana nazwa. Na kodzie przed poprawką 3 testy padały; po poprawce 262/262.

## Plan Change Log

## Review Triage Log

Pass 1 (quick): no findings.

## Verification

**Commands:**
- `dotnet test` -- expected: wszystkie zielone, w tym nowe testy duplikatów i aliasu.
- `dotnet build MediaStation.slnx -c Release` -- expected: 0 ostrzeżeń; `web/contracts/schema/*.schema.json` bez zmian.

**Manual checks (if no CLI):**
- Na kodzie przed poprawką test duplikatu koperty i payloadu musi padać; po poprawce przechodzić.
