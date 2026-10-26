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

  it('sendIntent wysyła kopertę launchApp', () => {
    const { postMessage } = stubWebview();

    sendIntent('launchApp', { appId: 'netflix' });

    expect(postMessage).toHaveBeenCalledWith({ v: 1, type: 'launchApp', payload: { appId: 'netflix' } });
  });
});
