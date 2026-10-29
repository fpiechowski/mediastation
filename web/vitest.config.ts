import { defineConfig } from 'vitest/config';

// Testy logiki shella bez DOM i Reacta (focus.ts, tileView.ts), renderToStaticMarkup (Tile.tsx) i page runtime
// (tekst runtime.js wykonany z podstawionym window/navigator): środowisko node.
export default defineConfig({
  test: {
    environment: 'node',
    include: ['shell/src/**/*.test.{ts,tsx}', 'page/**/*.test.ts'],
  },
});
