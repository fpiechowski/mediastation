// Most WebView2 postMessage z kopertą { v, type, payload } (AD-16).
// UI → host: `ready` po starcie. Host → UI: `snapshot` z pełnym stanem; starszy `rev` jest odrzucany.
import type { ShellSnapshot } from '../../contracts/generated/shell-snapshot';

export const PROTOCOL_VERSION = 1;

export interface Envelope<TPayload> {
  v: number;
  type: string;
  payload: TPayload;
}

type MessageListener = (event: MessageEvent<unknown>) => void;

interface WebViewBridge {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: MessageListener): void;
  removeEventListener(type: 'message', listener: MessageListener): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebViewBridge };
  }
}

function isEnvelope(value: unknown): value is Envelope<unknown> {
  if (typeof value !== 'object' || value === null) return false;
  const e = value as Record<string, unknown>;
  return typeof e.v === 'number' && typeof e.type === 'string' && 'payload' in e;
}

function isSnapshot(value: unknown): value is ShellSnapshot {
  if (typeof value !== 'object' || value === null) return false;
  const s = value as Record<string, unknown>;
  return typeof s.rev === 'number' && typeof s.screen === 'string' && Array.isArray(s.configErrors);
}

/**
 * Łączy się z hostem: wysyła `ready` i przekazuje kolejne snapshoty do `onSnapshot`.
 * Zwraca funkcję rozłączającą (cleanup efektu Reacta).
 */
export function connectBridge(onSnapshot: (snapshot: ShellSnapshot) => void): () => void {
  const webview = window.chrome?.webview;
  if (!webview) {
    console.warn('bridge: brak window.chrome.webview — shell działa poza WebView2, snapshot nie przyjdzie.');
    return () => {};
  }

  let lastRev = Number.NEGATIVE_INFINITY;

  const listener: MessageListener = (event) => {
    const message = event.data;
    if (!isEnvelope(message) || message.v !== PROTOCOL_VERSION) {
      console.warn('bridge: odrzucona koperta', message);
      return;
    }
    if (message.type !== 'snapshot') return;
    if (!isSnapshot(message.payload)) {
      console.warn('bridge: niepoprawny snapshot', message.payload);
      return;
    }
    if (message.payload.rev < lastRev) return;
    lastRev = message.payload.rev;
    onSnapshot(message.payload);
  };

  webview.addEventListener('message', listener);
  const ready: Envelope<null> = { v: PROTOCOL_VERSION, type: 'ready', payload: null };
  webview.postMessage(ready);

  return () => webview.removeEventListener('message', listener);
}
