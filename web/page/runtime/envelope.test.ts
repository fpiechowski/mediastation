import { describe, expect, it } from 'vitest';
import { ENVELOPE_VERSION, envelope, parseInbound } from './envelope';

describe('envelope', () => {
  it('ma wszystkie pola, a brak payloadu to null', () => {
    expect(ENVELOPE_VERSION).toBe(1);
    expect(envelope('hello', null)).toEqual({ v: 1, type: 'hello', id: null, payload: null });
    expect(Object.keys(envelope('hello', null))).toEqual(['v', 'type', 'id', 'payload']);
    expect(envelope('backResult', '7', { handled: true })).toEqual({
      v: 1,
      type: 'backResult',
      id: '7',
      payload: { handled: true },
    });
  });

  it('zachowuje jawne null i fałszywe wartości payloadu', () => {
    expect(envelope('x', null, null).payload).toBeNull();
    expect(envelope('x', null, false).payload).toBe(false);
    expect(envelope('x', null, 0).payload).toBe(0);
  });
});

describe('parseInbound', () => {
  it('przyjmuje kopertę v1 z id i payloadem null', () => {
    expect(parseInbound('{"v":1,"type":"back","id":"7","payload":null}')).toEqual({
      ok: true,
      envelope: { v: 1, type: 'back', id: '7', payload: null },
    });
  });

  it.each([
    ['nie-string', { v: 1, type: 'back', id: '7', payload: null }],
    ['śmieci', 'junk'],
    ['pusty tekst', ''],
    ['null', 'null'],
    ['liczba', '1'],
    ['tablica', '[1]'],
    ['brak v', '{"type":"back","id":"7","payload":null}'],
    ['v jako tekst', '{"v":"1","type":"back","id":"7","payload":null}'],
    ['v niecałkowite', '{"v":1.5,"type":"back","id":"7","payload":null}'],
    ['brak id', '{"v":1,"type":"back","payload":null}'],
    ['id null', '{"v":1,"type":"back","id":null,"payload":null}'],
    ['pusty id', '{"v":1,"type":"back","id":"","payload":null}'],
    ['id liczbowy', '{"v":1,"type":"back","id":7,"payload":null}'],
    ['brak type', '{"v":1,"id":"7","payload":null}'],
    ['pusty type', '{"v":1,"type":"","id":"7","payload":null}'],
    ['brak payloadu', '{"v":1,"type":"back","id":"7"}'],
  ])('%s → invalid', (_name, input) => {
    expect(parseInbound(input)).toEqual({ ok: false, result: 'invalid' });
  });

  it('inny major v → version, zanim sprawdzi resztę koperty', () => {
    expect(parseInbound('{"v":2,"type":"back","id":"7","payload":null}')).toEqual({ ok: false, result: 'version' });
    expect(parseInbound('{"v":2}')).toEqual({ ok: false, result: 'version' });
    expect(parseInbound('{"v":0,"type":"back"}')).toEqual({ ok: false, result: 'version' });
  });
});
