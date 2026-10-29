// Artefakt AD-6: prawdziwy build esbuild (proces potomny) i wykonanie pliku tak, jak robi to host
// (BrowserLaunch.BuildInitScript). Plik jest poza `tsc` (tsconfig.json: exclude), bo potrzebuje API Node bez @types/node;
// Vitest i tak go transpiluje i uruchamia.
import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';

const BANNER = '// MediaStation PageRuntime (esbuild IIFE, AD-6)';
const script = fileURLToPath(new URL('../../scripts/build-page.mjs', import.meta.url));

let dir = '';
let bundle = '';

beforeAll(() => {
  dir = mkdtempSync(join(tmpdir(), 'ms-page-bundle-'));
  const outfile = join(dir, 'runtime.js');
  execFileSync(process.execPath, [script, '--outfile', outfile], { stdio: 'pipe' });
  bundle = readFileSync(outfile, 'utf8');
}, 60_000);

afterAll(() => {
  if (dir) rmSync(dir, { recursive: true, force: true });
});

// Ten sam kształt co BrowserLaunch.BuildInitScript w C#.
function initScript(file: string, config: unknown): string {
  return `(function (__msPageConfig) {\n${file}\n})(${JSON.stringify(config)});`;
}

function run(file: string, win: Record<string, unknown>, config: unknown) {
  new Function('window', initScript(file, config))(win);
}

function fakeWindow(): Record<string, unknown> {
  const win: Record<string, unknown> = {
    navigator: { webdriver: false },
    addEventListener: () => {},
  };
  win.top = win;
  return win;
}

const config = { binding: '__msBridge', service: 'netflix', driverVersion: '0.0.0' };

describe('page bundle (esbuild IIFE)', () => {
  it('zaczyna się od bannera i nie ma importów ani eksportów na najwyższym poziomie', () => {
    expect(bundle.split(/\r?\n/)[0]).toBe(BANNER);
    expect(bundle).not.toMatch(/^\s*(import|export)\b/m);
    expect(bundle).not.toMatch(/^\s*var\s+\w+\s*=\s*\(\(\)\s*=>/m);
  });

  it('owinięty jak w hoście wysyła hello do bindingu i nie dodaje kluczy do window', () => {
    const win = fakeWindow();
    const binding = vi.fn<(json: string) => void>();
    win.__msBridge = binding;
    const keys = Object.keys(win).sort();

    run(bundle, win, config);

    expect(binding).toHaveBeenCalledTimes(1);
    expect(JSON.parse(binding.mock.calls[0]![0])).toEqual({
      v: 1,
      type: 'hello',
      id: null,
      payload: { service: 'netflix', driverVersion: '0.0.0', webdriver: false },
    });
    expect(Object.keys(win).sort()).toEqual(keys);
  });

  it.each([';', '// x'])('plik zakończony %s nadal jest poprawnym skryptem', (tail) => {
    const win = fakeWindow();
    const binding = vi.fn<(json: string) => void>();
    win.__msBridge = binding;

    run(`${bundle}\n${tail}`, win, { ...config, driverVersion: null });

    expect(binding).toHaveBeenCalledTimes(1);
    expect(JSON.parse(binding.mock.calls[0]![0]).payload.driverVersion).toBeNull();
  });
});
