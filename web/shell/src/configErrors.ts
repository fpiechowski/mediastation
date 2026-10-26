// Czysta logika ekranu błędu konfiguracji (F17, R7): bez DOM i Reacta.
import type { ConfigError } from '../../contracts/generated/shell-snapshot';

/** Ile błędów ekran pokazuje naraz; limit stały, żeby lista mieściła się w title-safe (wysokość pozycji ogranicza line-clamp). */
export const MAX_VISIBLE_ERRORS = 5;

export interface VisibleErrors {
  shown: ConfigError[];
  /** Liczba błędów, które się nie zmieściły. */
  hidden: number;
}

/** Pierwsze `max` błędów i liczba pominiętych. */
export function visibleErrors(errors: readonly ConfigError[], max: number = MAX_VISIBLE_ERRORS): VisibleErrors {
  return { shown: errors.slice(0, max), hidden: Math.max(0, errors.length - max) };
}

/** Polska odmiana słowa „błąd” dla liczby k. */
export function errorNoun(k: number): string {
  if (k === 1) return 'błąd';
  const lastTwo = k % 100;
  const last = k % 10;
  return last >= 2 && last <= 4 && !(lastTwo >= 12 && lastTwo <= 14) ? 'błędy' : 'błędów';
}

/** Wiersz „…i jeszcze K błędów”. */
export function hiddenLabel(k: number): string {
  return `…i jeszcze ${k} ${errorNoun(k)}`;
}
