import { defineConfig } from 'vitest/config';

// Testy logiki shella bez DOM i Reacta (focus.ts, tileView.ts) i renderToStaticMarkup (Tile.tsx): środowisko node.
export default defineConfig({
  test: {
    environment: 'node',
    include: ['shell/src/**/*.test.{ts,tsx}'],
  },
});
