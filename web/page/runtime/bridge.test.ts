import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createBridge, MAX_ATTEMPTS, QUEUE_LIMIT, RETRY_MS, SEEN_IDS_LIMIT } from './bridge';

type Binding = (json: string) => void;

const BINDING = '__msBridge';

function setup() {
  const win: Record<string, unknown> = {};
  const runtime = createBridge(win, BINDING);
  const binding = vi.fn<Binding>();
  const attach = () => {
    win[BINDING] = binding;
  };
  const sent = () => binding.mock.calls.map(([json]) => JSON.parse(json) as Record<string, unknown>);
  return { win, binding, attach, sent, ...runtime };
}

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

describe('bridge: kolejka wychodząca', () => {
  it('stałe AD-7: 256 kopert, 50 ms, 30 s, 64 id', () => {
    expect(QUEUE_LIMIT).toBe(256);
    expect(RETRY_MS).toBe(50);
    expect(MAX_ATTEMPTS * RETRY_MS).toBe(30_000);
    expect(SEEN_IDS_LIMIT).toBe(64);
  });

  it('wysyła od razu, gdy binding istnieje, z id null i payloadem null przy braku', () => {
    const t = setup();
    t.attach();

    t.bridge.send('screen', { name: 'browse', root: true });
    t.bridge.send('ping');

    expect(t.sent()).toEqual([
      { v: 1, type: 'screen', id: null, payload: { name: 'browse', root: true } },
      { v: 1, type: 'ping', id: null, payload: null },
    ]);
  });

  it('buforuje do pojawienia się bindingu i wysyła w kolejności na najbliższym takcie 50 ms', () => {
    const t = setup();
    t.bridge.send('a');
    t.bridge.send('b');

    vi.advanceTimersByTime(500);
    t.attach();
    expect(t.binding).not.toHaveBeenCalled();

    vi.advanceTimersByTime(RETRY_MS);
    expect(t.sent().map((e) => e.type)).toEqual(['a', 'b']);

    vi.advanceTimersByTime(10_000);
    expect(t.binding).toHaveBeenCalledTimes(2);
  });

  it('po 30 s przestaje próbować, a kolejny send po pojawieniu się bindingu wysyła całą kolejkę w kolejności', () => {
    const t = setup();
    t.bridge.send('a');
    t.bridge.send('b');

    vi.advanceTimersByTime(30_000);
    expect(vi.getTimerCount()).toBe(0);
    t.attach();
    vi.advanceTimersByTime(60_000);
    expect(t.binding).not.toHaveBeenCalled();

    t.bridge.send('c');
    expect(t.sent().map((e) => e.type)).toEqual(['a', 'b', 'c']);
  });

  it('po rezygnacji send bez bindingu zaczyna nowy epizod czekania', () => {
    const t = setup();
    t.bridge.send('a');
    vi.advanceTimersByTime(30_000);
    expect(vi.getTimerCount()).toBe(0);

    t.bridge.send('b');
    expect(vi.getTimerCount()).toBe(1);
    vi.advanceTimersByTime(29_000);
    t.attach();
    vi.advanceTimersByTime(RETRY_MS);

    expect(t.sent().map((e) => e.type)).toEqual(['a', 'b']);
  });

  it('send w trakcie epizodu nie przedłuża go ponad 30 s', () => {
    const t = setup();
    t.bridge.send('a');
    vi.advanceTimersByTime(20_000);
    t.bridge.send('b');
    vi.advanceTimersByTime(10_000);

    expect(vi.getTimerCount()).toBe(0);
  });

  it('przy pełnej kolejce wypada najstarsza: z 300 wysyłek wychodzi 256 najnowszych w kolejności', () => {
    const t = setup();
    for (let i = 0; i < 300; i++) t.bridge.send('n', i);

    t.attach();
    vi.advanceTimersByTime(RETRY_MS);

    const payloads = t.sent().map((e) => e.payload);
    expect(payloads).toHaveLength(256);
    expect(payloads[0]).toBe(44);
    expect(payloads.at(-1)).toBe(299);
    expect(payloads).toEqual(Array.from({ length: 256 }, (_, i) => i + 44));
  });

  it('rzucający binding zostawia kopertę na początku kolejki i ponawia', () => {
    const t = setup();
    const delivered: string[] = [];
    // Dwie porażki: przy wysyłce `b` i przy kolejnym `send('c')`, który próbuje od początku kolejki.
    let failures = 2;
    t.win[BINDING] = (json: string) => {
      const type = (JSON.parse(json) as { type: string }).type;
      if (type === 'b' && failures-- > 0) throw new Error('binding');
      delivered.push(type);
    };

    t.bridge.send('a');
    t.bridge.send('b');
    t.bridge.send('c');
    expect(delivered).toEqual(['a']);

    vi.advanceTimersByTime(RETRY_MS);
    expect(delivered).toEqual(['a', 'b', 'c']);
  });
});

describe('bridge: komunikaty host → page', () => {
  it('żądanie i odpowiedź: odpowiedź powtarza id, reply zwraca true', () => {
    const t = setup();
    t.attach();
    let result: boolean | undefined;
    t.bridge.receive('back', (payload, reply) => {
      expect(payload).toBeNull();
      result = reply('backResult', { handled: true });
    });

    expect(t.deliver('{"v":1,"type":"back","id":"7","payload":null}')).toBe('handled');

    expect(result).toBe(true);
    expect(t.sent()).toEqual([{ v: 1, type: 'backResult', id: '7', payload: { handled: true } }]);
  });

  it('odpowiedź bez payloadu ma payload null', () => {
    const t = setup();
    t.attach();
    t.bridge.receive('prepareClose', (_payload, reply) => {
      reply('closeReady');
    });

    t.deliver('{"v":1,"type":"prepareClose","id":"c1","payload":null}');

    expect(t.sent()).toEqual([{ v: 1, type: 'closeReady', id: 'c1', payload: null }]);
  });

  it('spóźniona odpowiedź na żądanie zastąpione nowszym tego samego type nic nie wysyła', () => {
    const t = setup();
    t.attach();
    const replies: Array<(type: string, payload?: unknown) => boolean> = [];
    t.bridge.receive('back', (_payload, reply) => {
      replies.push(reply);
    });

    t.deliver('{"v":1,"type":"back","id":"7","payload":null}');
    t.deliver('{"v":1,"type":"back","id":"8","payload":null}');

    expect(replies[0]!('backResult', { handled: true })).toBe(false);
    expect(t.binding).not.toHaveBeenCalled();
    expect(replies[1]!('backResult', { handled: false })).toBe(true);
    expect(replies[1]!('backResult', { handled: true })).toBe(false);
    expect(t.sent()).toEqual([{ v: 1, type: 'backResult', id: '8', payload: { handled: false } }]);
  });

  it('nowsze żądanie innego type nie unieważnia odpowiedzi', () => {
    const t = setup();
    t.attach();
    let late: ((type: string, payload?: unknown) => boolean) | undefined;
    t.bridge.receive('back', (_payload, reply) => {
      late = reply;
    });
    t.bridge.receive('pause', () => {});

    t.deliver('{"v":1,"type":"back","id":"7","payload":null}');
    t.deliver('{"v":1,"type":"pause","id":"9","payload":null}');

    expect(late!('backResult', { handled: true })).toBe(true);
  });

  it.each([
    ['inny major v', '{"v":2,"type":"back","id":"7","payload":null}', 'version'],
    ['brak id', '{"v":1,"type":"back","payload":null}', 'invalid'],
    ['śmieci', 'junk', 'invalid'],
    ['nie-string', 42, 'invalid'],
  ])('%s → %s bez wysyłki i bez wywołania handlera', (_name, input, expected) => {
    const t = setup();
    t.attach();
    const handler = vi.fn();
    t.bridge.receive('back', handler);

    expect(t.deliver(input)).toBe(expected);

    expect(handler).not.toHaveBeenCalled();
    expect(t.binding).not.toHaveBeenCalled();
  });

  it('powtórzone id → duplicate; id spoza ostatnich 64 znów przechodzi', () => {
    const t = setup();
    t.attach();
    const handler = vi.fn();
    t.bridge.receive('back', handler);

    expect(t.deliver('{"v":1,"type":"back","id":"0","payload":null}')).toBe('handled');
    expect(t.deliver('{"v":1,"type":"back","id":"0","payload":null}')).toBe('duplicate');
    for (let i = 1; i < SEEN_IDS_LIMIT; i++) t.deliver(`{"v":1,"type":"back","id":"${i}","payload":null}`);
    expect(t.deliver('{"v":1,"type":"back","id":"0","payload":null}')).toBe('duplicate');

    t.deliver(`{"v":1,"type":"back","id":"${SEEN_IDS_LIMIT}","payload":null}`);
    expect(t.deliver('{"v":1,"type":"back","id":"0","payload":null}')).toBe('handled');
    expect(handler).toHaveBeenCalledTimes(SEEN_IDS_LIMIT + 2);
    expect(t.binding).not.toHaveBeenCalled();
  });

  it('brak handlera → unhandled bez wysyłki', () => {
    const t = setup();
    t.attach();

    expect(t.deliver('{"v":1,"type":"action","id":"1","payload":{"key":"up"}}')).toBe('unhandled');
    expect(t.binding).not.toHaveBeenCalled();
  });

  it('rzucający handler → error, a runtime działa dalej', () => {
    const t = setup();
    t.attach();
    t.bridge.receive('pause', () => {
      throw new Error('driver');
    });
    t.bridge.receive('back', (_payload, reply) => {
      reply('backResult', { handled: true });
    });

    expect(t.deliver('{"v":1,"type":"pause","id":"1","payload":null}')).toBe('error');
    expect(t.deliver('{"v":1,"type":"back","id":"2","payload":null}')).toBe('handled');
    t.bridge.send('ping');

    expect(t.sent().map((e) => e.type)).toEqual(['backResult', 'ping']);
  });

  it('receive zastępuje wcześniejszy handler, a wyrejestrowanie starego nie usuwa nowego', () => {
    const t = setup();
    const first = vi.fn();
    const second = vi.fn();
    const offFirst = t.bridge.receive('back', first);
    const offSecond = t.bridge.receive('back', second);

    offFirst();
    expect(t.deliver('{"v":1,"type":"back","id":"1","payload":null}')).toBe('handled');
    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledWith(null, expect.any(Function));

    offSecond();
    expect(t.deliver('{"v":1,"type":"back","id":"2","payload":null}')).toBe('unhandled');
  });

  it('odpowiedź bez bindingu czeka w tej samej kolejce co send', () => {
    const t = setup();
    t.bridge.receive('back', (_payload, reply) => {
      reply('backResult', { handled: false });
    });

    t.bridge.send('hello');
    t.deliver('{"v":1,"type":"back","id":"7","payload":null}');
    t.attach();
    vi.advanceTimersByTime(RETRY_MS);

    expect(t.sent()).toEqual([
      { v: 1, type: 'hello', id: null, payload: null },
      { v: 1, type: 'backResult', id: '7', payload: { handled: false } },
    ]);
  });
});
