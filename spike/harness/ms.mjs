// MediaStation spike harness (S1–S8, wariant C). Run `node harness/ms.mjs --help`.
import { spawn } from 'node:child_process';
import { appendFileSync, cpSync, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import http from 'node:http';
import { isAbsolute, join, resolve } from 'node:path';
import readline from 'node:readline';
import { parseArgs } from 'node:util';
import { createCommands, HELP } from './commands.mjs';
import { Cdp, extServer, getJson, pwConnect, pwLaunch, rawDriver } from './drivers.mjs';

const ROOT = resolve(import.meta.dirname, '..');
const PROFILES = join(ROOT, 'profiles');
const LOGS = join(ROOT, 'logs');
const EXTENSION = join(ROOT, 'extension');
const SMOKE = join(ROOT, 'smoke');
const NETFLIX = 'https://www.netflix.com/browse';

const PF = process.env.ProgramFiles ?? 'C:\\Program Files';
const PF86 = process.env['ProgramFiles(x86)'] ?? 'C:\\Program Files (x86)';
const LOCAL = process.env.LOCALAPPDATA ?? '';
const BROWSERS = {
  edge: {
    port: 9223,
    channel: 'msedge',
    exe: [join(PF86, 'Microsoft/Edge/Application/msedge.exe'), join(PF, 'Microsoft/Edge/Application/msedge.exe')],
  },
  chrome: {
    port: 9224,
    channel: 'chrome',
    exe: [join(PF, 'Google/Chrome/Application/chrome.exe'), join(LOCAL, 'Google/Chrome/Application/chrome.exe')],
  },
};

const USAGE = `MediaStation spike harness

  launch      --browser edge|chrome [--profile NAME] [--port N] [--url URL] [--window normal|fullscreen|kiosk|app]
              [--spatial] [--watch-cdm] [--no-cdp] [--extra "--flag1 --flag2"] [--exe PATH] [--label L]
      Uruchamia przeglądarkę jak MediaStation: --user-data-dir + stały --remote-debugging-port, bez flag
      automatyzacji (S1, S4, S5). --no-cdp: bez portu (wariant C, profil <browser>-ext). Node kończy pracę,
      przeglądarka działa dalej.

  serve       --mode raw|pw|ext|pwlaunch [--browser edge|chrome] [--port N] [--ctl-port 9340] [--label L] [--quiet]
              pwlaunch: [--profile NAME] [--pw-defaults] [--spatial] [--url URL]
      Proces długotrwały: trzyma połączenie (CDP, Playwright albo WebSocket rozszerzenia), więc skrypty
      wstrzyknięte przez addScriptToEvaluateOnNewDocument nie znikają. Zdarzenia probe i komendy zapisuje
      do logs/<data>-<label>.jsonl. Po starcie wypisuje READY i zapisuje logs/serve-<ctl-port>.json.
      raw = surowe CDP bez Runtime.enable, pw = Playwright connectOverCDP, ext = rozszerzenie (port WS 9330),
      pwlaunch = kontrola negatywna S3 (przeglądarka żyje tyle co serve).

  cmd "KOMENDA" [--ctl-port 9340]
      Jedna komenda do serve. Wynik jako JSON na stdout; kod wyjścia 0 = ok, 1 = błąd komendy,
      2 = serve nie działa. Lista komend: cmd help.

  repl [--ctl-port 9340]
      Interaktywny klient serve (ten sam mechanizm co cmd). "exit" wychodzi, "quit" zatrzymuje serve.

  ext-temp    --browser edge|chrome [--profile NAME] [--url URL]
      Smoke test transportu C: przeglądarka przez --remote-debugging-pipe z rozszerzeniem wczytanym przez
      Extensions.loadUnpacked tylko na czas tej sesji (nic nie zostaje w profilu, webdriver=true). Działa do
      Ctrl+C. Do testów C na Netfliksie: ręczne "Load unpacked" (README).

  smoke-page [--port 9350]
      Lokalna strona testowa bez DRM (wiersze z przewijaniem, wyszukiwarka, <video> z canvas) do smoke testu.

  copy-cdm    --from PROFILE --to PROFILE
      S4: kopiuje katalog WidevineCdm między profilami (obie przeglądarki zamknięte).

Profile: spike/profiles/<NAME>. Porty: CDP edge 9223, chrome 9224; WS rozszerzenia 9330; serve 9340;
smoke-page 9350. Logi: spike/logs/.`;

const { positionals, values: o } = parseArgs({
  allowPositionals: true,
  options: {
    browser: { type: 'string', default: 'edge' },
    profile: { type: 'string' },
    port: { type: 'string' },
    url: { type: 'string', default: NETFLIX },
    window: { type: 'string', default: 'normal' },
    spatial: { type: 'boolean', default: false },
    'watch-cdm': { type: 'boolean', default: false },
    'no-cdp': { type: 'boolean', default: false },
    extra: { type: 'string', default: '' },
    exe: { type: 'string' },
    mode: { type: 'string', default: 'raw' },
    'ctl-port': { type: 'string', default: '9340' },
    'ws-port': { type: 'string', default: '9330' },
    'pw-defaults': { type: 'boolean', default: false },
    quiet: { type: 'boolean', default: false },
    from: { type: 'string' },
    to: { type: 'string' },
    label: { type: 'string' },
    help: { type: 'boolean', short: 'h', default: false },
  },
});

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const fail = (msg) => {
  throw new Error(msg);
};
const browserDef = () => BROWSERS[o.browser] ?? fail('--browser musi być edge albo chrome');
const browserExe = () => o.exe ?? browserDef().exe.find((p) => existsSync(p)) ?? fail(`nie znaleziono ${o.browser}; podaj --exe`);
const profileDir = (name) => (isAbsolute(name) ? name : join(PROFILES, name));
const stateFile = (ctlPort) => join(LOGS, `serve-${ctlPort}.json`);

function stamp() {
  const d = new Date();
  const p = (n) => String(n).padStart(2, '0');
  return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}-${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}`;
}

// Every log line is one JSON object; drivers call info()/event(), commands call record()/recent()/waitFor().
function createHub(file, echo) {
  mkdirSync(LOGS, { recursive: true });
  const recent = [];
  const waiters = new Set();
  const record = (obj) => {
    const entry = { ts: new Date().toISOString(), ...obj };
    appendFileSync(file, JSON.stringify(entry) + '\n'); // sync: the agent may read the file at any moment
    recent.push(entry);
    if (recent.length > 1000) recent.shift();
    if (echo) {
      const { ts, kind, type, ...rest } = entry;
      console.log(`${ts} ${kind}${type ? ' ' + type : ''} ${JSON.stringify(rest).slice(0, 400)}`);
    }
    for (const w of waiters) {
      if (w.match(entry)) {
        waiters.delete(w);
        w.resolve(entry);
      }
    }
  };
  return {
    file,
    record,
    info: (msg) => record({ kind: 'info', msg }),
    event: (payload) => {
      let e;
      try {
        e = JSON.parse(payload);
      } catch {
        e = { type: 'unparsed', payload };
      }
      record({ kind: 'event', ...e });
    },
    recent: (n) => recent.slice(-n),
    waitFor: (pred, ms) =>
      new Promise((resolve) => {
        const w = { match: (e) => e.kind === 'event' && pred(e), resolve };
        waiters.add(w);
        setTimeout(() => waiters.delete(w) && resolve(null), ms);
      }),
  };
}

async function waitFor(fn, timeoutMs) {
  const end = Date.now() + timeoutMs;
  for (;;) {
    try {
      return await fn();
    } catch (e) {
      if (Date.now() > end) throw e;
      await sleep(250);
    }
  }
}

async function logProcesses(wsUrl, log) {
  try {
    const browser = await Cdp.open(wsUrl);
    const { processInfo } = await browser.send('SystemInfo.getProcessInfo');
    browser.close();
    log.info(`procesy (S9): ${processInfo.map((p) => `${p.type}:${p.id}`).join(' ')}`);
  } catch (e) {
    log.info(`SystemInfo.getProcessInfo niedostępne: ${e.message}`);
  }
}

async function watchCdm(dir, t0, log) {
  const cdm = join(dir, 'WidevineCdm');
  log.info(`S4: czekam na ${cdm}\\<wersja>\\manifest.json (maks. 15 min, Ctrl+C przerywa)`);
  while (Date.now() - t0 < 15 * 60_000) {
    const versions = existsSync(cdm) ? readdirSync(cdm).filter((v) => existsSync(join(cdm, v, 'manifest.json'))) : [];
    if (versions.length) {
      log.info(`S4: Widevine CDM ${versions.join(', ')} gotowy po ${((Date.now() - t0) / 1000).toFixed(1)} s od startu`);
      return;
    }
    await sleep(1000);
  }
  log.info('S4: Widevine CDM nie pojawił się w ciągu 15 min');
}

async function launch() {
  const b = browserDef();
  const port = Number(o.port ?? b.port);
  const cdp = !o['no-cdp'];
  const dir = profileDir(o.profile ?? `${o.browser}-${cdp ? 'netflix' : 'ext'}`);
  const exe = browserExe();
  if (cdp && (await getJson(port, '/json/version').catch(() => null))) fail(`port ${port} jest zajęty przez inną przeglądarkę; zamknij ją albo użyj --port`);

  const log = createHub(join(LOGS, `${stamp()}-${o.label ?? `launch-${o.browser}${cdp ? '' : '-nocdp'}`}.jsonl`), true);
  const args = [`--user-data-dir=${dir}`, '--no-first-run', '--no-default-browser-check'];
  if (cdp) args.push(`--remote-debugging-port=${port}`);
  if (o.spatial) args.push('--enable-spatial-navigation');
  if (o.window === 'fullscreen') args.push('--start-fullscreen');
  else if (o.window === 'kiosk') args.push('--kiosk');
  else if (o.window !== 'normal' && o.window !== 'app') fail('--window: normal|fullscreen|kiosk|app');
  args.push(...o.extra.split(' ').filter(Boolean));
  args.push(o.window === 'app' ? `--app=${o.url}` : o.url);

  const fresh = !existsSync(dir);
  const t0 = Date.now();
  const child = spawn(exe, args, { detached: true, stdio: 'ignore' });
  child.unref();
  log.record({ kind: 'launch', pid: child.pid, freshProfile: fresh, exe, args });
  if (cdp) {
    const version = await waitFor(() => getJson(port, '/json/version'), 30_000);
    log.record({ kind: 'launch', cdpReadyMs: Date.now() - t0, browser: version.Browser, userAgent: version['User-Agent'], port });
    await logProcesses(version.webSocketDebuggerUrl, log);
  } else {
    log.info('bez portu CDP; PID-y procesów: win\\audio-mute.ps1 -Action list -ProfileDir <profil>');
  }
  if (o['watch-cdm']) await watchCdm(dir, t0, log);
  log.info(`log: ${log.file}`);
}

async function serve() {
  const mode = o.mode;
  if (!['raw', 'pw', 'ext', 'pwlaunch'].includes(mode)) fail('--mode raw|pw|ext|pwlaunch');
  const ctlPort = Number(o['ctl-port']);
  const target = mode === 'ext' ? 'extension' : o.browser;
  const hub = createHub(join(LOGS, `${stamp()}-${o.label ?? `serve-${target}-${mode}`}.jsonl`), !o.quiet);
  hub.record({ kind: 'serve', phase: 'starting', mode, target, argv: process.argv.slice(2) });

  let driver;
  if (mode === 'raw') driver = await rawDriver(Number(o.port ?? browserDef().port), hub);
  else if (mode === 'pw') driver = await pwConnect(Number(o.port ?? browserDef().port), hub);
  else if (mode === 'ext') driver = await extServer(Number(o['ws-port']), hub);
  else {
    const args = o.extra.split(' ').filter(Boolean);
    if (o.spatial) args.push('--enable-spatial-navigation');
    const userDataDir = profileDir(o.profile ?? `${o.browser}-pw`);
    driver = await pwLaunch({ channel: browserDef().channel, userDataDir, pwDefaults: o['pw-defaults'], args, url: o.url }, hub);
  }
  const run = createCommands(driver, hub);

  const reply = (res, body) => {
    res.writeHead(200, { 'content-type': 'application/json; charset=utf-8' });
    res.end(JSON.stringify(body));
  };
  const server = http.createServer(async (req, res) => {
    if (req.method === 'GET' && req.url === '/health') return reply(res, { ok: true, mode, target, driver: driver.name, events: hub.file });
    if (req.method !== 'POST' || req.url !== '/cmd') return reply(res, { ok: false, error: 'użyj POST /cmd' });
    let line = '';
    for await (const chunk of req) line += chunk;
    line = line.trim();
    const t0 = Date.now();
    if (line === 'quit') {
      hub.record({ kind: 'cmd', cmd: line, ok: true });
      reply(res, { ok: true, cmd: line, result: 'serve zatrzymany' });
      await driver.close();
      setTimeout(() => process.exit(0), 200);
      return;
    }
    try {
      const result = await run(line);
      hub.record({ kind: 'cmd', cmd: line, ok: true, ms: Date.now() - t0, result });
      reply(res, { ok: true, cmd: line, ms: Date.now() - t0, result: result ?? null });
    } catch (e) {
      hub.record({ kind: 'cmd', cmd: line, ok: false, error: e.message });
      reply(res, { ok: false, cmd: line, error: e.message });
    }
  });
  server.requestTimeout = 0; // `wait` may hold a request for minutes
  await new Promise((ok, ko) => server.once('error', ko).listen(ctlPort, '127.0.0.1', ok));

  const file = stateFile(ctlPort);
  writeFileSync(file, JSON.stringify({ pid: process.pid, ctlPort, mode, target, driver: driver.name, events: hub.file, started: new Date().toISOString() }, null, 2));
  process.on('exit', () => rmSync(file, { force: true }));
  process.on('SIGINT', () => process.exit(0));
  process.on('SIGTERM', () => process.exit(0));
  hub.record({ kind: 'serve', phase: 'ready', ctlPort, driver: driver.name, trusted: driver.trusted, events: hub.file, state: file });
  console.log(`READY ctl=http://127.0.0.1:${ctlPort} events=${hub.file}`);
}

async function post(line) {
  const ctlPort = o['ctl-port'];
  let res;
  try {
    res = await fetch(`http://127.0.0.1:${ctlPort}/cmd`, { method: 'POST', body: line });
  } catch (e) {
    return { exit: 2, body: { ok: false, cmd: line, error: `serve nie odpowiada na 127.0.0.1:${ctlPort} (${e.cause?.code ?? e.message}); uruchom serve` } };
  }
  const body = await res.json();
  return { exit: body.ok ? 0 : 1, body };
}

async function cmd() {
  const line = positionals.slice(1).join(' ').trim();
  if (!line) fail('cmd "KOMENDA"; lista: cmd help');
  const { exit, body } = await post(line);
  console.log(JSON.stringify(body));
  process.exit(exit);
}

async function repl() {
  const rl = readline.createInterface({ input: process.stdin, output: process.stdout, prompt: 'ms> ' });
  console.log(`klient serve na porcie ${o['ctl-port']}; "help" = komendy, "exit" = wyjście, "quit" = zatrzymaj serve`);
  rl.prompt();
  for await (const raw of rl) {
    const line = raw.trim();
    if (line === 'exit') break;
    if (line) {
      const { body } = await post(line);
      console.log(typeof body.result === 'string' && body.ok ? body.result : JSON.stringify(body, null, 2));
      if (line === 'quit') break;
    }
    rl.prompt();
  }
  rl.close();
}

// Extensions.loadUnpacked only works over --remote-debugging-pipe with --enable-unsafe-extension-debugging, and the
// extension lives only as long as that browser session (smoke test 2026-10-11: nothing persisted in the profile).
// The pipe also sets navigator.webdriver=true, so this is for testing the C transport, never for C measurements.
async function extTemp() {
  const dir = profileDir(o.profile ?? `${o.browser}-ext-temp`);
  const args = [`--user-data-dir=${dir}`, '--remote-debugging-pipe', '--enable-unsafe-extension-debugging', '--no-first-run', '--no-default-browser-check', 'about:blank'];
  const child = spawn(browserExe(), args, { stdio: ['ignore', 'ignore', 'ignore', 'pipe', 'pipe'] });
  const [toBrowser, fromBrowser] = [child.stdio[3], child.stdio[4]];
  const pending = new Map();
  let buffer = '';
  let nextId = 0;
  fromBrowser.on('data', (chunk) => {
    buffer += chunk.toString();
    for (let i; (i = buffer.indexOf('\0')) >= 0; ) {
      const msg = JSON.parse(buffer.slice(0, i));
      buffer = buffer.slice(i + 1);
      pending.get(msg.id)?.(msg);
    }
  });
  const send = (method, params = {}) =>
    new Promise((ok, ko) => {
      const id = ++nextId;
      pending.set(id, ok);
      setTimeout(() => ko(new Error(`${method}: brak odpowiedzi (czy profil ${dir} jest już otwarty?)`)), 20_000);
      toBrowser.write(JSON.stringify({ id, method, params }) + '\0');
    });
  const res = await send('Extensions.loadUnpacked', { path: EXTENSION });
  if (res.error) {
    child.kill();
    fail(`Extensions.loadUnpacked: ${res.error.message}`);
  }
  const { targetId } = (await send('Target.createTarget', { url: o.url })).result;
  console.log(
    JSON.stringify({ ok: true, temporary: true, webdriver: true, profile: dir, extensionId: res.result.id, targetId, url: o.url, stop: 'Ctrl+C albo zamknij okno' }),
  );
  child.on('exit', () => process.exit(0));
  process.on('SIGINT', () => send('Browser.close').finally(() => process.exit(0)));
}

function smokePage() {
  const port = Number(o.port ?? 9350);
  http
    .createServer((req, res) => {
      const path = req.url.split('?')[0] === '/' ? 'index.html' : null;
      if (!path) return res.writeHead(404).end();
      res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
      res.end(readFileSync(join(SMOKE, path)));
    })
    .listen(port, '127.0.0.1', () => console.log(`READY smoke page http://127.0.0.1:${port}/`));
}

function copyCdm() {
  if (!o.from || !o.to) fail('copy-cdm wymaga --from i --to');
  const from = join(profileDir(o.from), 'WidevineCdm');
  const to = join(profileDir(o.to), 'WidevineCdm');
  if (!existsSync(from)) fail(`brak ${from}`);
  mkdirSync(profileDir(o.to), { recursive: true });
  cpSync(from, to, { recursive: true });
  console.log(`skopiowano ${from} -> ${to}`);
}

const run = { launch, serve, cmd, repl, 'ext-temp': extTemp, 'smoke-page': smokePage, 'copy-cdm': copyCdm }[positionals[0]];
if (!run || o.help) {
  console.log(positionals[0] === 'cmd' ? HELP : USAGE);
  process.exit(run || o.help ? 0 : 1);
}
try {
  await run();
} catch (e) {
  console.error(`błąd: ${e.message}`);
  process.exit(1);
}
