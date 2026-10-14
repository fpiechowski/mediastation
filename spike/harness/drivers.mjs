// Transports for the spike: raw CDP over WebSocket (no Runtime.enable), Playwright (connectOverCDP / launch)
// and the variant C extension (WebSocket server the extension's service worker connects to).
// Every driver exposes the same shape: { name, trusted, probe, evaluate, key, insertText, navigate, bringToFront, screenshot, setWindow, close }.
// screenshot() returns a base64 PNG of the page viewport.
import { readFileSync } from 'node:fs';
import WebSocket, { WebSocketServer } from 'ws';

export const PROBE = readFileSync(new URL('../extension/probe.js', import.meta.url), 'utf8').trim().replace(/;$/, '');
const BINDING = '__msBridge';

// Playwright default switches that hurt DRM/quality or mark automation (research: [9], addendum).
const PW_HARMFUL = [
  '--enable-automation',
  '--disable-component-update',
  '--disable-background-networking',
  '--disable-extensions',
  '--force-color-profile=srgb',
  '--disable-backgrounding-occluded-windows',
];

export async function getJson(port, path) {
  const res = await fetch(`http://127.0.0.1:${port}${path}`);
  if (!res.ok) throw new Error(`${path}: HTTP ${res.status}`);
  return res.json();
}

// Calls window.__msProbe[method](...args) in the page; null when the probe is not there.
const probeExpression = (method, args) =>
  `(() => { const p = window.__msProbe; return p ? p[${JSON.stringify(method)}](...${JSON.stringify(args)}) : null; })()`;

export class Cdp {
  static open(url) {
    return new Promise((resolve, reject) => {
      const ws = new WebSocket(url, { perMessageDeflate: false });
      ws.once('open', () => resolve(new Cdp(ws)));
      ws.once('error', reject);
    });
  }

  constructor(ws) {
    this.ws = ws;
    this.nextId = 0;
    this.pending = new Map();
    this.handlers = new Map();
    ws.on('message', (data) => {
      const msg = JSON.parse(data);
      if (msg.id) {
        const p = this.pending.get(msg.id);
        this.pending.delete(msg.id);
        if (p) msg.error ? p.reject(new Error(`${p.method}: ${msg.error.message}`)) : p.resolve(msg.result);
      } else {
        this.emit(msg.method, msg.params);
      }
    });
    ws.on('close', () => {
      for (const p of this.pending.values()) p.reject(new Error('CDP closed'));
      this.emit('close');
    });
  }

  send(method, params = {}) {
    const id = ++this.nextId;
    const result = new Promise((resolve, reject) => this.pending.set(id, { resolve, reject, method }));
    this.ws.send(JSON.stringify({ id, method, params }));
    return result;
  }

  on(event, fn) {
    this.handlers.set(event, [...(this.handlers.get(event) ?? []), fn]);
  }

  emit(event, params) {
    for (const fn of this.handlers.get(event) ?? []) fn(params);
  }

  close() {
    this.ws.close();
  }
}

// key spec: "ArrowDown", "Space", "f", "Ctrl+Alt+Shift+D"
const NAMED = {
  Space: [' ', 'Space', 32, ' '],
  Enter: ['Enter', 'Enter', 13, '\r'],
  Escape: ['Escape', 'Escape', 27],
  Backspace: ['Backspace', 'Backspace', 8],
  Tab: ['Tab', 'Tab', 9],
  ArrowUp: ['ArrowUp', 'ArrowUp', 38],
  ArrowDown: ['ArrowDown', 'ArrowDown', 40],
  ArrowLeft: ['ArrowLeft', 'ArrowLeft', 37],
  ArrowRight: ['ArrowRight', 'ArrowRight', 39],
  BrowserBack: ['BrowserBack', 'BrowserBack', 166],
  BrowserHome: ['BrowserHome', 'BrowserHome', 172],
};
const MODIFIERS = { Alt: 1, Ctrl: 2, Meta: 4, Shift: 8 };

export function parseKey(spec) {
  const parts = spec.split('+');
  const name = parts.pop();
  let modifiers = 0;
  for (const m of parts) {
    if (!MODIFIERS[m]) throw new Error(`nieznany modyfikator: ${m} (Alt, Ctrl, Meta, Shift)`);
    modifiers |= MODIFIERS[m];
  }
  let def = NAMED[name];
  if (!def && /^[a-z0-9]$/i.test(name)) {
    const up = name.toUpperCase();
    const key = modifiers & MODIFIERS.Shift ? up : name.toLowerCase();
    def = [key, /\d/.test(up) ? `Digit${up}` : `Key${up}`, up.charCodeAt(0), key];
  }
  if (!def) throw new Error(`nieznany klawisz: ${name}`);
  const [key, code, vk, text] = def;
  // Ctrl/Alt/Meta combos produce no text, like a real keyboard shortcut.
  return { key, code, vk, text: modifiers & ~MODIFIERS.Shift ? undefined : text, modifiers };
}

// Trusted input through the browser's input pipeline (S6).
export async function dispatchKey(send, spec) {
  const k = parseKey(spec);
  const base = { key: k.key, code: k.code, windowsVirtualKeyCode: k.vk, nativeVirtualKeyCode: k.vk, modifiers: k.modifiers };
  await send('Input.dispatchKeyEvent', { type: k.text ? 'keyDown' : 'rawKeyDown', text: k.text, unmodifiedText: k.text, ...base });
  await send('Input.dispatchKeyEvent', { type: 'keyUp', ...base });
}

export async function rawDriver(port, log) {
  const page = (await getJson(port, '/json/list')).find((t) => t.type === 'page' && !t.url.startsWith('devtools://'));
  if (!page) throw new Error(`brak karty "page" na porcie ${port}`);
  log.info(`raw CDP -> ${page.url} (target ${page.id})`);

  const cdp = await Cdp.open(page.webSocketDebuggerUrl);
  let closing = false;
  cdp.on('Runtime.bindingCalled', (p) => p.name === BINDING && log.event(p.payload));
  cdp.on('close', () => {
    if (closing) return;
    log.info('CDP rozłączony (przeglądarka zamknięta?)');
    process.exit(1);
  });

  // Deliberately no Runtime.enable: it is the detectable CDP leak [37]. bindingCalled arrives without it.
  // But without Runtime.enable the binding is not carried over to new documents (UM760, Edge 155), so it is
  // re-added on every main-frame navigation; probe.js queues events until the binding shows up.
  await cdp.send('Runtime.addBinding', { name: BINDING });
  cdp.on('Page.frameNavigated', (p) => {
    if (!p.frame.parentId) cdp.send('Runtime.addBinding', { name: BINDING }).catch((e) => log.info(`addBinding po nawigacji: ${e.message}`));
  });
  await cdp.send('Page.enable');
  await cdp.send('Page.addScriptToEvaluateOnNewDocument', { source: PROBE });
  log.info('CDP: Runtime.addBinding, Page.enable, Page.addScriptToEvaluateOnNewDocument; bez Runtime.enable');

  const send = (method, params) => cdp.send(method, params);
  const evaluate = async (expression) => {
    const r = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description ?? r.exceptionDetails.text);
    return r.result.value;
  };
  await evaluate(PROBE); // the document that is already open

  return {
    name: 'raw-cdp',
    trusted: true,
    probe: (method, ...args) => evaluate(probeExpression(method, args)),
    evaluate,
    key: (spec) => dispatchKey(send, spec),
    insertText: (text) => send('Input.insertText', { text }),
    navigate: (url) => send('Page.navigate', { url }),
    bringToFront: () => send('Page.bringToFront'),
    screenshot: async () => (await send('Page.captureScreenshot', { format: 'png' })).data,
    setWindow: async (windowState) => {
      // Browser domain lives on the browser target, so open a short second connection.
      const browser = await Cdp.open((await getJson(port, '/json/version')).webSocketDebuggerUrl);
      try {
        const { windowId } = await browser.send('Browser.getWindowForTarget', { targetId: page.id });
        await browser.send('Browser.setWindowBounds', { windowId, bounds: { windowState } });
      } finally {
        browser.close();
      }
    },
    close: async () => {
      closing = true;
      cdp.close();
    },
  };
}

async function wrapPlaywrightPage(name, context, page, log, close) {
  await context.exposeBinding(BINDING, (_source, payload) => log.event(payload));
  await context.addInitScript(PROBE);
  await page.evaluate(PROBE);
  // Same CDP Input calls as the raw driver, so S6 compares transports and not key synthesis.
  const cdp = await context.newCDPSession(page);
  const send = (method, params) => cdp.send(method, params);
  return {
    name,
    trusted: true,
    probe: (method, ...args) => page.evaluate(probeExpression(method, args)),
    evaluate: (expression) => page.evaluate(expression),
    key: (spec) => dispatchKey(send, spec),
    insertText: (text) => send('Input.insertText', { text }),
    navigate: (url) => page.goto(url, { waitUntil: 'commit' }),
    bringToFront: () => page.bringToFront(),
    screenshot: async () => (await send('Page.captureScreenshot', { format: 'png' })).data,
    setWindow: async (windowState) => {
      const { windowId } = await send('Browser.getWindowForTarget');
      await send('Browser.setWindowBounds', { windowId, bounds: { windowState } });
    },
    close,
  };
}

export async function pwConnect(port, log) {
  const { chromium } = await import('playwright-core');
  const browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`);
  browser.on('disconnected', () => {
    log.info('Playwright: rozłączono');
    process.exit(1);
  });
  const context = browser.contexts()[0];
  const page = context?.pages().find((p) => !p.url().startsWith('devtools://'));
  if (!page) throw new Error(`brak karty na porcie ${port}`);
  log.info(`Playwright connectOverCDP -> ${page.url()} (Playwright woła Runtime.enable)`);
  // Leave the browser running on quit: we only attached to it.
  return wrapPlaywrightPage('pw-connectOverCDP', context, page, log, async () => {});
}

export async function pwLaunch({ channel, userDataDir, pwDefaults, args, url }, log) {
  const { chromium } = await import('playwright-core');
  const ignoreDefaultArgs = pwDefaults ? undefined : PW_HARMFUL;
  // viewport: null keeps the real window size; Playwright's default 1280x720 emulation would skew S3 quality.
  const context = await chromium.launchPersistentContext(userDataDir, { channel, headless: false, viewport: null, args, ignoreDefaultArgs });
  context.on('close', () => {
    log.info('Playwright: przeglądarka zamknięta');
    process.exit(0);
  });
  log.info(`Playwright launch channel=${channel} profile=${userDataDir} args=${args.join(' ') || '-'}`);
  log.info(`ignoreDefaultArgs=${ignoreDefaultArgs ? ignoreDefaultArgs.join(' ') : '(brak, pełne domyślne flagi)'}; transport pipe => navigator.webdriver=true`);
  const page = context.pages()[0] ?? (await context.newPage());
  const driver = await wrapPlaywrightPage('pw-launch', context, page, log, () => context.close());
  await page.goto(url, { waitUntil: 'commit' });
  return driver;
}

// Variant C: no CDP at all. The extension's service worker connects here; input can only be untrusted.
export async function extServer(port, log) {
  const wss = new WebSocketServer({ host: '127.0.0.1', port });
  const pending = new Map();
  let socket = null;
  let nextId = 0;
  let connected;
  const firstConnection = new Promise((resolve) => (connected = resolve));

  wss.on('connection', (ws) => {
    socket = ws;
    ws.on('message', (data) => {
      const msg = JSON.parse(data);
      if (msg.type === 'hello') {
        log.record({ kind: 'info', msg: `rozszerzenie połączone (v${msg.version}): ${msg.ua}`, swStats: msg.stats });
        connected();
      } else if (msg.type === 'event') {
        log.event(msg.payload);
      } else if (msg.type === 'result') {
        const p = pending.get(msg.id);
        pending.delete(msg.id);
        if (p) msg.ok ? p.resolve(msg.value) : p.reject(new Error(msg.error));
      }
    });
    ws.on('close', () => {
      if (socket !== ws) return;
      socket = null;
      log.info('rozszerzenie rozłączone (uśpiony service worker albo zamknięta przeglądarka); połączy się ponownie samo');
    });
  });
  log.info(`ext: serwer ws://127.0.0.1:${port}; czekam na service worker rozszerzenia (otwórz albo odśwież kartę Netflixa)`);
  await firstConnection;

  const call = (msg) => {
    if (!socket) return Promise.reject(new Error('rozszerzenie niepołączone; odśwież kartę Netflixa'));
    const id = ++nextId;
    const result = new Promise((resolve, reject) => {
      pending.set(id, { resolve, reject });
      setTimeout(() => pending.delete(id) && reject(new Error('timeout')), 20000);
    });
    socket.send(JSON.stringify({ id, ...msg }));
    return result;
  };
  const probe = (method, ...args) => call({ op: 'probe', method, args });

  return {
    name: 'extension',
    trusted: false,
    probe,
    evaluate: null, // Netflix CSP blocks eval in the page; only probe methods are available
    key: async (spec) => {
      if (spec.includes('+')) throw new Error('wariant C: kombinacje klawiszy niedostępne');
      await probe('jsKey', spec);
    },
    insertText: (text) => probe('insertText', text, 'exec'),
    navigate: (url) => call({ op: 'navigate', url }),
    bringToFront: () => call({ op: 'front' }),
    screenshot: async () => (await call({ op: 'screenshot' })).replace(/^data:image\/png;base64,/, ''),
    setWindow: (state) => call({ op: 'window', state }),
    close: async () => wss.close(),
  };
}
