// Most WebView2 postMessage z kopertą { v, type, payload } (AD-16).
// UI → host: `ready` po starcie oraz Intents (`launchApp` …). Host → UI: `snapshot` z pełnym stanem
// (starszy `rev` jest odrzucany) i `action` (logiczny klawisz z pilota/klawiatury, tylko w trybie shell).
// Guard sprawdza też `apps[]` i `focusRequest`, bo UI kluczuje fokus po `appId`, nie po indeksach.
import type { Action } from '../../contracts/generated/action';
import type { CloseApp } from '../../contracts/generated/close-app';
import type { LaunchApp } from '../../contracts/generated/launch-app';
import type { RestartApp } from '../../contracts/generated/restart-app';
import type { AppType, ShellSnapshot } from '../../contracts/generated/shell-snapshot';

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

// Rekord po kluczach typu z generated: nowy `AppType` bez wpisu tutaj nie przejdzie typechecku.
const APP_TYPES: Record<AppType, true> = { web: true, moonlight: true, native: true };

function isAppSnapshot(value: unknown): boolean {
  if (typeof value !== 'object' || value === null) return false;
  const app = value as Record<string, unknown>;
  return (
    typeof app.appId === 'string' &&
    typeof app.category === 'string' &&
    typeof app.state === 'string' &&
    typeof app.title === 'string' &&
    (app.icon === null || typeof app.icon === 'string') &&
    typeof app.type === 'string' &&
    Object.hasOwn(APP_TYPES, app.type)
  );
}

function isFocusRequest(value: unknown): boolean {
  if (typeof value !== 'object' || value === null) return false;
  const focus = value as Record<string, unknown>;
  return typeof focus.target === 'string' && typeof focus.seq === 'number';
}

function isSnapshot(value: unknown): value is ShellSnapshot {
  if (typeof value !== 'object' || value === null) return false;
  const s = value as Record<string, unknown>;
  return (
    typeof s.rev === 'number' &&
    typeof s.screen === 'string' &&
    Array.isArray(s.configErrors) &&
    Array.isArray(s.apps) &&
    s.apps.every(isAppSnapshot) &&
    (s.focusRequest === null || isFocusRequest(s.focusRequest))
  );
}

// Rekordy po kluczach typu z generated: rozjazd z kontraktem nie przejdzie typechecku.
const ACTION_KINDS: Record<Action['kind'], true> = {
  up: true,
  down: true,
  left: true,
  right: true,
  ok: true,
  back: true,
  home: true,
  appMenu: true,
  keyboard: true,
};
const ACTION_PHASES: Record<Action['phase'], true> = { press: true, repeat: true, long: true };

function isAction(value: unknown): value is Action {
  if (typeof value !== 'object' || value === null) return false;
  const a = value as Record<string, unknown>;
  return (
    typeof a.kind === 'string' &&
    Object.hasOwn(ACTION_KINDS, a.kind) &&
    typeof a.phase === 'string' &&
    Object.hasOwn(ACTION_PHASES, a.phase) &&
    typeof a.source === 'string' &&
    typeof a.tsMono === 'number'
  );
}

type IntentPayloads = { launchApp: LaunchApp; restartApp: RestartApp; closeApp: CloseApp };

/** Wysyła Intent do hosta (UI → host); poza WebView2 tylko ostrzega. */
export function sendIntent<TType extends keyof IntentPayloads>(type: TType, payload: IntentPayloads[TType]): void {
  const webview = window.chrome?.webview;
  if (!webview) {
    console.warn('bridge: brak window.chrome.webview — intent nie zostal wyslany.', type);
    return;
  }
  const envelope: Envelope<IntentPayloads[TType]> = { v: PROTOCOL_VERSION, type, payload };
  webview.postMessage(envelope);
}

/**
 * Łączy się z hostem: wysyła `ready`, przekazuje kolejne snapshoty do `onSnapshot` i Actions do `onAction`.
 * Zwraca funkcję rozłączającą (cleanup efektu Reacta).
 */
export function connectBridge(
  onSnapshot: (snapshot: ShellSnapshot) => void,
  onAction: (action: Action) => void,
): () => void {
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
    if (message.type === 'action') {
      if (!isAction(message.payload)) {
        console.warn('bridge: niepoprawna action', message.payload);
        return;
      }
      onAction(message.payload);
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
