// Sonda DW-56 (architektura, 2026-10-30): czy `Page.getFrameTree` odróżnia dokument podmieniony w trakcie przerwy CDP
// od zdrowego, bez `Runtime.enable`. Nie jest kodem produktu i nie dotyka `profiles\<appId>`: własny profil tymczasowy,
// port spoza zakresu AD-11, lokalny serwer HTTP na loopbacku. Bez argumentów używa `--headless=new`; `--headed` otwiera
// okno poza ekranem z flagami AD-1 bez `--kiosk`, bo `navigator.webdriver` trzeba mierzyć bez headless. Przerwa to zamknięcie
// WebSocketu przez klienta, nie reset TCP, więc sonda nie spełnia warunku DW-55. Wynik: JSON na stdout.
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const EDGE = 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const CDP_PORT = 9431;
const BINDING = '__msProbe';
const headed = process.argv.includes('--headed');
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// Skrypt jak PageRuntime: wysyła `hello` przez binding (z ponowieniami) i nie zostawia żadnej globalnej nazwy.
const INIT_SCRIPT = `(() => {
  if (window !== window.top) return;
  const send = (kind) => {
    const body = JSON.stringify({ kind, path: location.pathname, webdriver: navigator.webdriver === true });
    const attempt = (left) => {
      if (typeof window.${BINDING} === 'function') { window.${BINDING}(body); return; }
      if (left > 0) setTimeout(() => attempt(left - 1), 50);
    };
    attempt(600);
  };
  send('hello');
  document.addEventListener('probe-ping', () => send('alive'));
})();`;

// Strona sama wykonuje polecenia z parametrów URL na timerach, bo w trakcie przerwy host nie ma żadnego kanału.
const PAGE = (name) => `<!doctype html><title>${name}</title><script>
const q = new URLSearchParams(location.search);
const at = Number(q.get('pushAfter') || 0);
if (at) setTimeout(() => history.pushState({}, '', '/spa-route'), at);
const nav = Number(q.get('navAfter') || 0);
if (nav) setTimeout(() => { location.href = '/b'; }, nav);
const ping = Number(q.get('pingAfter') || 0);
if (ping) setTimeout(() => document.dispatchEvent(new Event('probe-ping')), ping);
</script>`;

const server = createServer((req, res) => {
  res.writeHead(200, { 'content-type': 'text/html', 'cache-control': 'no-store' });
  res.end(PAGE(req.url));
});
await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
const base = `http://127.0.0.1:${server.address().port}`;

class Cdp {
  static open(url) {
    return new Promise((resolve, reject) => {
      const ws = new WebSocket(url);
      const cdp = new Cdp(ws);
      ws.addEventListener('open', () => resolve(cdp));
      ws.addEventListener('error', () => reject(new Error('ws error')));
    });
  }

  constructor(ws) {
    this.ws = ws;
    this.nextId = 0;
    this.pending = new Map();
    this.events = [];
    this.sent = [];
    ws.addEventListener('message', (e) => {
      const m = JSON.parse(e.data);
      if (m.id && this.pending.has(m.id)) {
        const { resolve, reject } = this.pending.get(m.id);
        this.pending.delete(m.id);
        if (m.error) reject(new Error(m.error.message));
        else resolve(m.result);
      } else if (m.method) {
        this.events.push(m);
        // Jak BrowserHost.OnCdpEvent: bez Runtime.enable binding nie przechodzi na nowy dokument.
        if (m.method === 'Page.frameNavigated' && !m.params.frame.parentId) {
          this.send('Runtime.addBinding', { name: BINDING }).catch(() => {});
        }
      }
    });
  }

  send(method, params = {}) {
    if (method === 'Runtime.enable') throw new Error('AD-1');
    this.sent.push(method);
    const id = ++this.nextId;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.ws.send(JSON.stringify({ id, method, params }));
    });
  }

  close() {
    return new Promise((resolve) => {
      this.ws.addEventListener('close', () => resolve());
      this.ws.close();
    });
  }

  bindingCalls() {
    return this.events
      .filter((e) => e.method === 'Runtime.bindingCalled')
      .map((e) => JSON.parse(e.params.payload));
  }

  async waitBinding(kind, ms) {
    const until = Date.now() + ms;
    while (Date.now() < until) {
      const found = this.bindingCalls().find((c) => c.kind === kind);
      if (found) return found;
      await sleep(25);
    }
    return null;
  }
}

async function install(cdp) {
  await cdp.send('Runtime.addBinding', { name: BINDING });
  await cdp.send('Page.enable');
  await cdp.send('Page.addScriptToEvaluateOnNewDocument', { source: INIT_SCRIPT });
}

const frame = async (cdp) => (await cdp.send('Page.getFrameTree')).frameTree.frame;

async function target() {
  const list = await (await fetch(`http://127.0.0.1:${CDP_PORT}/json/list`)).json();
  return list.find((t) => t.type === 'page' && !t.url.startsWith('devtools://'));
}

const profile = mkdtempSync(join(tmpdir(), 'dw56-probe-'));
const args = [
  `--user-data-dir=${profile}`,
  `--remote-debugging-port=${CDP_PORT}`,
  '--no-first-run',
  '--no-default-browser-check',
  '--autoplay-policy=no-user-gesture-required',
  ...(headed ? ['--window-position=-2400,-2400', '--window-size=400,300'] : ['--headless=new']),
  'about:blank',
];
const env = { ...process.env };
delete env.__COMPAT_LAYER;
const edge = spawn(EDGE, args, { env, stdio: 'ignore', windowsHide: true });

const results = { headed, edgeArgs: args.filter((a) => !a.startsWith('--user-data-dir')), scenarios: {} };
try {
  let t = null;
  for (let i = 0; i < 120 && !t; i++) {
    await sleep(250);
    t = await target().catch(() => null);
  }
  if (!t) throw new Error('no page target');
  results.product = (await (await fetch(`http://127.0.0.1:${CDP_PORT}/json/version`)).json()).Browser;
  const endpoint = t.webSocketDebuggerUrl;

  // Każdy scenariusz: start na /a z parametrami, zerwanie gniazda, przerwa, nowa sesja jak w reconnect (trzy wywołania).
  const scenario = async (query, outageMs) => {
    const first = await Cdp.open(endpoint);
    await install(first);
    const nav = await first.send('Page.navigate', { url: `${base}/a?${query}` });
    const firstHello = await first.waitBinding('hello', 5000);
    const committed = first.events.filter((e) => e.method === 'Page.frameNavigated' && !e.params.frame.parentId).at(-1);
    const before = await frame(first);
    await first.close();
    await sleep(outageMs);
    const second = await Cdp.open(endpoint);
    await install(second);
    const after = await frame(second);
    const row = {
      helloOnFirstSession: firstHello !== null,
      navigateReplyLoaderId: nav.loaderId,
      frameNavigatedLoaderId: committed?.params.frame.loaderId ?? null,
      treeLoaderIdBefore: before.loaderId,
      treeLoaderIdAfter: after.loaderId,
      loaderChanged: before.loaderId !== after.loaderId,
      urlAfter: after.url.replace(base, ''),
    };
    return { row, second };
  };

  // 1. Zdrowa strona: bez nawigacji w przerwie. Stary dokument ma się odezwać nowej sesji po reconnect (por. DW-55).
  {
    const { row, second } = await scenario('pingAfter=2500', 500);
    const alive = await second.waitBinding('alive', 4000);
    row.oldDocumentReachesNewSession = alive !== null;
    row.helloOnNewSession = second.bindingCalls().some((c) => c.kind === 'hello');
    results.scenarios.healthyNoNavigation = row;
    await second.close();
  }

  // 2. SPA: `history.pushState` w przerwie zmienia URL, ale nie dokument.
  {
    const { row, second } = await scenario('pushAfter=300', 800);
    results.scenarios.spaPushStateDuringOutage = row;
    await second.close();
  }

  // 3. Nawigacja głównej ramki w przerwie: nowy dokument bez skryptu, potem jeden `Page.reload` na nowej sesji.
  {
    const { row, second } = await scenario('navAfter=300', 1200);
    row.helloWithoutReload = (await second.waitBinding('hello', 2500)) !== null;
    second.events.length = 0;
    await second.send('Page.reload');
    const hello = await second.waitBinding('hello', 5000);
    row.helloAfterReload = hello !== null;
    row.webdriverAfterReload = hello?.webdriver ?? null;
    row.pathAfterReload = hello?.path ?? null;
    row.loaderIdAfterReload = (await frame(second)).loaderId;
    row.cdpMethodsOnNewSession = second.sent;
    results.scenarios.mainFrameNavigationDuringOutage = row;
    await second.close();
  }

  // 4. Reload przy niedostępnym serwerze: czy strona błędu dostaje skrypt i `hello` (próg watchdoga po reloadzie).
  {
    const { row, second } = await scenario('', 200);
    server.close();
    server.closeAllConnections();
    second.events.length = 0;
    await second.send('Page.reload').catch((e) => { row.reloadError = e.message; });
    row.helloOnErrorPage = (await second.waitBinding('hello', 4000)) !== null;
    row.urlAfterFailedReload = (await frame(second)).url.replace(base, '');
    results.scenarios.reloadWithServerDown = row;
    await second.close();
  }
} catch (error) {
  results.error = String(error);
} finally {
  edge.kill();
  spawn('taskkill', ['/PID', String(edge.pid), '/T', '/F'], { stdio: 'ignore' });
  await sleep(1500);
  server.close();
  try {
    rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 500 });
  } catch {
    results.profileLeft = profile;
  }
}

console.log(JSON.stringify(results, null, 2));
process.exit(0);
