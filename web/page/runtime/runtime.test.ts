import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import runtimeSource from './runtime.js?raw';

type Binding = (payload: string) => void;
type RuntimeConfig = { binding: string; service: string; driverVersion: string | null };

// Plik runtime to samo wyrażenie funkcji: wykonujemy jego tekst (jak host w skrypcie inicjalizującym)
// z podstawionymi window/globalThis/navigator, bez DOM.
function load(fakeWindow: { top: unknown }, fakeGlobal: Record<string, unknown>, webdriver: unknown) {
  const factory = new Function(
    'window',
    'globalThis',
    'navigator',
    'setTimeout',
    `return (${runtimeSource});`,
  ) as (...args: unknown[]) => (config: RuntimeConfig) => void;
  return factory(fakeWindow, fakeGlobal, { webdriver }, (cb: () => void, ms: number) => setTimeout(cb, ms));
}

const config: RuntimeConfig = { binding: '__msBridge', service: 'netflix', driverVersion: '0.0.0' };

function topWindow() {
  const w: { top: unknown } = { top: undefined };
  w.top = w;
  return w;
}

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('page runtime', () => {
  it('buforuje hello do pojawienia się bindingu i wysyła kopertę v1', () => {
    const fakeGlobal: Record<string, unknown> = {};
    load(topWindow(), fakeGlobal, false)(config);

    vi.advanceTimersByTime(500);
    const binding = vi.fn<Binding>();
    fakeGlobal.__msBridge = binding;
    expect(binding).not.toHaveBeenCalled();

    vi.advanceTimersByTime(50);
    expect(binding).toHaveBeenCalledTimes(1);
    expect(JSON.parse(binding.mock.calls[0]![0])).toEqual({
      v: 1,
      type: 'hello',
      id: null,
      payload: { service: 'netflix', driverVersion: '0.0.0', webdriver: false },
    });

    vi.advanceTimersByTime(1000);
    expect(binding).toHaveBeenCalledTimes(1);
  });

  it('wysyła od razu, gdy binding już istnieje, i mierzy navigator.webdriver', () => {
    const binding = vi.fn<Binding>();
    load(topWindow(), { __msBridge: binding }, true)(config);

    expect(binding).toHaveBeenCalledTimes(1);
    expect(JSON.parse(binding.mock.calls[0]![0]).payload.webdriver).toBe(true);
  });

  it('traktuje brak navigator.webdriver jako false', () => {
    const binding = vi.fn<Binding>();
    load(topWindow(), { __msBridge: binding }, undefined)(config);

    expect(JSON.parse(binding.mock.calls[0]![0]).payload.webdriver).toBe(false);
  });

  it('w iframe nie wysyła niczego', () => {
    const binding = vi.fn<Binding>();
    const fakeGlobal: Record<string, unknown> = { __msBridge: binding };
    load({ top: {} }, fakeGlobal, false)(config);

    vi.advanceTimersByTime(60_000);
    expect(binding).not.toHaveBeenCalled();
  });

  it('przestaje czekać na binding po 30 s', () => {
    const fakeGlobal: Record<string, unknown> = {};
    load(topWindow(), fakeGlobal, false)(config);

    vi.advanceTimersByTime(30_100);
    const binding = vi.fn<Binding>();
    fakeGlobal.__msBridge = binding;
    vi.advanceTimersByTime(10_000);
    expect(binding).not.toHaveBeenCalled();
  });
});
