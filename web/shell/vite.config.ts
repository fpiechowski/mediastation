import { defineConfig, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';

// CSP Release siedzi w meta index.html. Dev server potrzebuje tylko: własnego origin, ws: HMR i inline preamble Reacta.
const DEV_CSP =
  "script-src 'self' 'unsafe-inline'; connect-src 'self' ws://localhost:5173; img-src 'self' https://icons.mediastation.example; frame-src 'none'; object-src 'none'; base-uri 'self'; form-action 'none'";

function devCsp(): Plugin {
  return {
    name: 'shell-dev-csp',
    apply: 'serve',
    transformIndexHtml(html) {
      return html.replace(
        /(http-equiv="Content-Security-Policy"\s+content=")[^"]*(")/,
        (_m, a: string, b: string) => `${a}${DEV_CSP}${b}`,
      );
    },
  };
}

// Root = web/shell (`vite shell`). Debug: WebView2 ładuje dev server z 5173 (AD-13).
// Release: `vite build shell` → web/shell/dist, kopiowane przez MSBuild do <exe>/shell.
export default defineConfig({
  plugins: [react(), devCsp()],
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
