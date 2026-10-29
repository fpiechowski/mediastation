import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { start, type PageConfig, type PageWindow } from './runtime';

type Binding = (json: string) => void;
type PageShow = (event: { readonly persisted: boolean }) => void;

const config: PageConfig = { binding: '__msBridge', service: 'netflix', driverVersion: '0.0.0' };

interface FakeWindow extends PageWindow {
  [key: string]: unknown;
  pageshow: PageShow[];
}

function fakeWindow(webdriver: unknown = false, inFrame = false): FakeWindow {
  const pageshow: PageShow[] = [];
  const win: FakeWindow = {
    top: null,
    navigator: { webdriver } as { webdriver?: boolean },
    pageshow,
    addEventListener(type: string, listener: PageShow) {
      if (type === 'pageshow') pageshow.push(listener);
    },
  };
  (win as { top: unknown }).top = inFrame ? {} : win;
  return win;
}

function attach(win: FakeWindow) {
  const binding = vi.fn<Binding>();
  win[config.binding] = binding;
  return binding;
}

const parsed = (binding: ReturnType<typeof vi.fn<Binding>>) =>
  binding.mock.calls.map(([json]) => JSON.parse(json) as Record<string, unknown>);

const hello = { v: 1, type: 'hello', id: null, payload: { service: 'netflix', driverVersion: '0.0.0', webdriver: false } };

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('PageRuntime.start', () => {
  it('buforuje hello do pojawienia się bindingu i wysyła dokładnie jedną kopertę v1 na najbliższym takcie', () => {
    const win = fakeWindow();
    expect(start(config, win)).not.toBeNull();

    vi.advanceTimersByTime(500);
    const binding = attach(win);
    expect(binding).not.toHaveBeenCalled();

    vi.advanceTimersByTime(50);
    expect(parsed(binding)).toEqual([hello]);

    vi.advanceTimersByTime(1000);
    expect(binding).toHaveBeenCalledTimes(1);
  });

  it('wysyła od razu, gdy binding już istnieje, i mierzy navigator.webdriver', () => {
    const win = fakeWindow(true);
    const binding = attach(win);
    start(config, win);

    expect(binding).toHaveBeenCalledTimes(1);
    expect(parsed(binding)[0]!.payload).toEqual({ service: 'netflix', driverVersion: '0.0.0', webdriver: true });
  });

  it.each([undefined, 'true', 1])('webdriver %s to false', (webdriver) => {
    const win = fakeWindow(webdriver);
    const binding = attach(win);
    start(config, win);

    expect((parsed(binding)[0]!.payload as { webdriver: unknown }).webdriver).toBe(false);
  });

  it('przekazuje null driverVersion', () => {
    const win = fakeWindow();
    const binding = attach(win);
    start({ ...config, driverVersion: null }, win);

    expect((parsed(binding)[0]!.payload as { driverVersion: unknown }).driverVersion).toBeNull();
  });

  it('w iframe zwraca null i nic nie wysyła ani nie nasłuchuje', () => {
    const win = fakeWindow(false, true);
    const binding = attach(win);

    expect(start(config, win)).toBeNull();
    vi.advanceTimersByTime(60_000);

    expect(binding).not.toHaveBeenCalled();
    expect(win.pageshow).toHaveLength(0);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('przestaje czekać na binding po 30 s, a hello zostaje w kolejce do kolejnego send', () => {
    const win = fakeWindow();
    const runtime = start(config, win)!;

    vi.advanceTimersByTime(30_100);
    const binding = attach(win);
    vi.advanceTimersByTime(10_000);
    expect(binding).not.toHaveBeenCalled();

    runtime.bridge.send('screen', { name: 'browse', root: true });
    expect(parsed(binding).map((e) => e.type)).toEqual(['hello', 'screen']);
  });

  it('nowy dokument ma własny runtime: własne hello i własną kolejkę', () => {
    const first = fakeWindow();
    start(config, first);
    const second = fakeWindow();
    const secondBinding = attach(second);

    start(config, second);
    expect(parsed(secondBinding)).toEqual([hello]);

    const firstBinding = attach(first);
    vi.advanceTimersByTime(50);
    expect(parsed(firstBinding)).toEqual([hello]);
    expect(secondBinding).toHaveBeenCalledTimes(1);
  });

  it('pageshow z persisted:true wysyła hello jeszcze raz; bez persisted nic', () => {
    const win = fakeWindow();
    const binding = attach(win);
    start(config, win);
    expect(win.pageshow).toHaveLength(1);

    win.pageshow[0]!({ persisted: false });
    expect(binding).toHaveBeenCalledTimes(1);

    win.pageshow[0]!({ persisted: true });
    expect(parsed(binding)).toEqual([hello, hello]);
  });

  it('pageshow z persisted:true zaczyna nowy epizod czekania na binding', () => {
    const win = fakeWindow();
    start(config, win);
    vi.advanceTimersByTime(25_000);

    win.pageshow[0]!({ persisted: true });
    vi.advanceTimersByTime(20_000);
    expect(vi.getTimerCount()).toBe(1);
    const binding = attach(win);
    vi.advanceTimersByTime(50);

    expect(parsed(binding)).toEqual([hello, hello]);
  });

  it('deliver z runtime trafia do handlera bridge.receive', () => {
    const win = fakeWindow();
    const binding = attach(win);
    const runtime = start(config, win)!;
    runtime.bridge.receive('back', (_payload, reply) => {
      reply('backResult', { handled: true });
    });

    expect(runtime.deliver('{"v":1,"type":"back","id":"7","payload":null}')).toBe('handled');
    expect(parsed(binding)[1]).toEqual({ v: 1, type: 'backResult', id: '7', payload: { handled: true } });
  });
});
