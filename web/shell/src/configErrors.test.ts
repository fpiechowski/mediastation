import { describe, expect, it } from 'vitest';
import type { ConfigError } from '../../contracts/generated/shell-snapshot';
import { errorNoun, hiddenLabel, visibleErrors } from './configErrors';

const errors = (n: number): ConfigError[] =>
  Array.from({ length: n }, (_, i) => ({ file: `f${i}.json`, path: null, reason: `r${i}` }));

describe('visibleErrors', () => {
  it.each([1, 3, 5])('%i błędów: wszystkie, brak ukrytych', (n) => {
    const { shown, hidden } = visibleErrors(errors(n));
    expect(shown).toHaveLength(n);
    expect(hidden).toBe(0);
  });

  it('10 błędów: 5 pierwszych i 5 ukrytych', () => {
    const { shown, hidden } = visibleErrors(errors(10));
    expect(shown.map((e) => e.file)).toEqual(['f0.json', 'f1.json', 'f2.json', 'f3.json', 'f4.json']);
    expect(hidden).toBe(5);
  });
});

describe('errorNoun', () => {
  it.each([
    [1, 'błąd'],
    [2, 'błędy'],
    [4, 'błędy'],
    [5, 'błędów'],
    [12, 'błędów'],
    [22, 'błędy'],
  ])('%i -> %s', (k, noun) => {
    expect(errorNoun(k)).toBe(noun);
  });

  it('hiddenLabel składa wiersz', () => {
    expect(hiddenLabel(5)).toBe('…i jeszcze 5 błędów');
  });
});
