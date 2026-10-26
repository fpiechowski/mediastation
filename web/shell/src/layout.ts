// Geometria Home bez DOM (DESIGN.md → Layout & Spacing, focus-ring): rząd kafelków w title-safe.

export const SCREEN_WIDTH = 1920;
/** title-safe-x: 5% szerokości. */
export const TITLE_SAFE_X = 96;
export const GUTTER = 112;
export const TILE_WIDTH = 544;
export const TILE_GAP = 48;
/** focus-ring: skala kafelka w fokusie i grubość obrysu (obrys skaluje się razem z kafelkiem). */
export const FOCUS_SCALE = 1.08;
export const FOCUS_OUTLINE = 5;

/** Ile kafelek w fokusie wystaje poza swój prostokąt spoczynkowy na każdą stronę (skala + skalowany obrys). */
export const FOCUS_OVERHANG = ((FOCUS_SCALE - 1) * TILE_WIDTH) / 2 + FOCUS_OUTLINE * FOCUS_SCALE;

/**
 * Przesunięcie poziome rzędu (px), przy którym kafelek o indeksie `index` w fokusie (po skali i z obrysem) mieści się
 * w [TITLE_SAFE_X, SCREEN_WIDTH − TITLE_SAFE_X]. Najmniejsze co do modułu: gdy mieści się bez przesunięcia, 0.
 * `count` to liczba kafelków w rzędzie (indeks poza zakresem jest ucinany do rzędu).
 */
export function rowOffset(index: number, count: number): number {
  if (count <= 0) return 0;
  const i = Math.min(Math.max(0, Math.trunc(index)), count - 1);
  const left = GUTTER + i * (TILE_WIDTH + TILE_GAP) - FOCUS_OVERHANG;
  const right = GUTTER + i * (TILE_WIDTH + TILE_GAP) + TILE_WIDTH + FOCUS_OVERHANG;
  // Zaokrąglenia w stronę wnętrza ramki, żeby wynik nigdy nie wyszedł poza title-safe.
  const min = Math.ceil(TITLE_SAFE_X - left);
  const max = Math.floor(SCREEN_WIDTH - TITLE_SAFE_X - right);
  // Najbliższe 0 przesunięcie z przedziału [min, max] (niepusty: kafelek z obrysem jest węższy niż ramka).
  return Math.min(Math.max(0, min), max) || 0;
}
