import { defineConfig } from 'vitest/config';

// Testy logiki shella bez DOM i Reacta (focus.ts, tileView.ts), renderToStaticMarkup (Tile.tsx) i PageRuntime
// (moduły TS z fałszywym window, a bundle.test.ts buduje IIFE esbuildem i wykonuje go jak host): środowisko node.
export default defineConfig({
  test: {
    environment: 'node',
    include: ['shell/src/**/*.test.{ts,tsx}', 'page/**/*.test.ts'],
  },
});
