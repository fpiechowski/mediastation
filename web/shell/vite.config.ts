import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Root = web/shell (`vite shell`). Debug: WebView2 ładuje dev server z 5173 (AD-13).
// Release: `vite build shell` → web/shell/dist, kopiowane przez MSBuild do <exe>/shell.
export default defineConfig({
  plugins: [react()],
  base: './',
  server: {
    port: 5173,
    strictPort: true,
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    target: 'es2022',
  },
});
