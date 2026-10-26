import { describe, expect, it } from 'vitest';
import { FOCUS_OVERHANG, GUTTER, SCREEN_WIDTH, TILE_GAP, TILE_WIDTH, TITLE_SAFE_X, rowOffset } from './layout';

/** Widoczne granice kafelka w fokusie po przesunięciu rzędu. */
const bounds = (index: number, count: number) => {
  const left = GUTTER + index * (TILE_WIDTH + TILE_GAP) + rowOffset(index, count);
  return { left: left - FOCUS_OVERHANG, right: left + TILE_WIDTH + FOCUS_OVERHANG };
};

describe('rowOffset', () => {
  for (let count = 1; count <= 4; count++) {
    for (let index = 0; index < count; index++) {
      it(`kafelek ${index + 1} z ${count} w fokusie mieści się w title-safe`, () => {
        const { left, right } = bounds(index, count);
        expect(left).toBeGreaterThanOrEqual(TITLE_SAFE_X);
        expect(right).toBeLessThanOrEqual(SCREEN_WIDTH - TITLE_SAFE_X);
      });
    }
  }

  it('drugi kafelek rzędu nie wymaga przesunięcia', () => {
    expect(rowOffset(1, 3)).toBe(0);
  });

  it('pierwszy kafelek dosuwa rząd w prawo, trzeci w lewo', () => {
    expect(rowOffset(0, 3)).toBeGreaterThan(0);
    expect(rowOffset(2, 3)).toBeLessThan(0);
  });

  it('pusty rząd i indeks poza zakresem nie wybuchają', () => {
    expect(rowOffset(0, 0)).toBe(0);
    expect(rowOffset(9, 2)).toBe(rowOffset(1, 2));
    expect(rowOffset(-1, 2)).toBe(rowOffset(0, 2));
  });
});
