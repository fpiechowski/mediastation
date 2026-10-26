import { afterEach, describe, expect, it, vi } from 'vitest';
import { connectBridge, sendIntent } from './bridge';

type Listener = (event: { data: unknown }) => void;

function stubWebview() {
  let listener: Listener | undefined;
  const postMessage = vi.fn();
  vi.stubGlobal('window', {
    chrome: {
      webview: {
        postMessage,
        addEventListener: (_: string, l: Listener) => (listener = l),
        removeEventListener: () => {},
      },
    },
  });
  return { postMessage, emit: (data: unknown) => listener?.({ data }) };
}

afterEach(() => vi.unstubAllGlobals());

describe('bridge', () => {
  it('przekazuje poprawną action do onAction, a niepoprawną odrzuca', () => {
    const { emit } = stubWebview();
    const onAction = vi.fn();
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    connectBridge(() => {}, onAction);

    const valid = { kind: 'right', phase: 'press', source: 'keyboard', tsMono: 5 };
    emit({ v: 1, type: 'action', payload: valid });
    expect(onAction).toHaveBeenCalledExactlyOnceWith(valid);

    emit({ v: 1, type: 'action', payload: { ...valid, kind: 'nope' } });
    emit({ v: 1, type: 'action', payload: { ...valid, tsMono: 'x' } });
    emit({ v: 1, type: 'action', payload: null });
    emit({ v: 2, type: 'action', payload: valid });
    expect(onAction).toHaveBeenCalledTimes(1);
  });

  it('przyjmuje snapshot z icon (string albo null) i type, a odrzuca bez type lub z icon innego typu', () => {
    const { emit } = stubWebview();
    const onSnapshot = vi.fn();
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    connectBridge(onSnapshot, () => {});
    const app = { appId: 'netflix', category: 'video', state: 'notRunning', title: 'Netflix', icon: 'netflix.png', type: 'web' };
    const snapshot = (apps: unknown[], rev: number) => ({
      v: 1,
      type: 'snapshot',
      payload: { rev, screen: 'placeholder', configErrors: [], apps, focusRequest: null },
    });

    emit(snapshot([app], 1));
    emit(snapshot([{ ...app, icon: null }], 2));
    expect(onSnapshot).toHaveBeenCalledTimes(2);

    const { type: _type, ...withoutType } = app;
    emit(snapshot([withoutType], 3));
    emit(snapshot([{ ...app, icon: 5 }], 4));
    emit(snapshot([{ ...app, icon: undefined }], 5));
    emit(snapshot([{ ...app, type: 'foo' }], 6));
    expect(onSnapshot).toHaveBeenCalledTimes(2);
  });

  it('sendIntent wysyła kopertę launchApp', () => {
    const { postMessage } = stubWebview();

    sendIntent('launchApp', { appId: 'netflix' });

    expect(postMessage).toHaveBeenCalledWith({ v: 1, type: 'launchApp', payload: { appId: 'netflix' } });
  });
});
