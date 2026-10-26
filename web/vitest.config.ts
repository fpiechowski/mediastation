import { defineConfig } from 'vitest/config';

// Testy logiki shella bez DOM i Reacta (focus.ts): środowisko node.
export default defineConfig({
  test: {
    environment: 'node',
    include: ['shell/src/**/*.test.ts'],
  },
});
