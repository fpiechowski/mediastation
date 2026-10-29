// Buduje PageRuntime (AD-6, AD-13): jeden plik IIFE z web/page/runtime/main.ts do web/page/dist/runtime.js.
// Bez `globalName` (strona nie dostaje globalnej nazwy) i bez minifikacji (hotfix = podmiana czytelnego pliku).
// Host owija plik w `(function (__msPageConfig) {\n<plik>\n})(<config>);`. `--outfile <ścieżka>` zmienia wyjście.
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';

const BANNER = '// MediaStation PageRuntime (esbuild IIFE, AD-6)';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');

let outfile = join(root, 'page', 'dist', 'runtime.js');
const args = process.argv.slice(2);
for (let i = 0; i < args.length; i++) {
  if (args[i] === '--outfile' && args[i + 1]) {
    outfile = resolve(args[i + 1]);
    i++;
  } else {
    console.error(`build-page: nieznany argument ${args[i]} (dozwolone: --outfile <ścieżka>)`);
    process.exit(2);
  }
}

await build({
  entryPoints: [join(root, 'page', 'runtime', 'main.ts')],
  outfile,
  bundle: true,
  format: 'iife',
  platform: 'browser',
  target: 'es2022',
  minify: false,
  legalComments: 'none',
  banner: { js: BANNER },
  logLevel: 'warning',
});

console.log(`build-page: ${outfile}`);
