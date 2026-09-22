import { describe, it, expect } from 'vitest';
import { en } from '../i18n/locales/en';
import { WIDGET_KINDS } from './gridLayout';
import {
  BINDING_FIELD_KEYS,
  BINDING_KINDS,
  BINDING_SCHEMAS,
  WIDGET_FIELD_KEYS,
  WIDGET_SCHEMAS,
  kindsForShape,
  type FieldSpecification,
} from './widgetSchema';

function keysOf(fields: FieldSpecification[]): string[] {
  return fields.map((field) => field.key);
}

describe('the widget schemas', () => {
  it('cover every kind the palette offers, each starting with its title', () => {
    for (const kind of WIDGET_KINDS) {
      expect(WIDGET_SCHEMAS[kind], kind).toBeDefined();
      expect(keysOf(WIDGET_SCHEMAS[kind])[0], kind).toBe('title');
    }
  });

  it('name a binding slot for what each widget draws', () => {
    const slot = (kind: keyof typeof WIDGET_SCHEMAS, key: string) => WIDGET_SCHEMAS[kind].find((field) => field.key === key);
    expect(slot('kpi', 'value')).toMatchObject({ kind: 'binding', shape: 'number' });
    expect(slot('table', 'rows')).toMatchObject({ kind: 'binding', shape: 'rows' });
    expect(slot('leaderboard', 'entities')).toMatchObject({ kind: 'binding', shape: 'rows' });
    expect(slot('rangeBar', 'months')).toMatchObject({ kind: 'group' });
    expect(slot('divergingBar', 'up')).toMatchObject({ kind: 'group' });
  });

  it('have English words for every key at every depth', () => {
    for (const key of WIDGET_FIELD_KEYS) expect(en.design.widgetField, key).toHaveProperty(key);
    for (const key of BINDING_FIELD_KEYS) expect(en.design.bindingField, key).toHaveProperty(key);
  });
});

describe('the binding schemas', () => {
  it('cover every kind the resolver answers, with words for the kind and what it costs', () => {
    expect(BINDING_KINDS.length).toBeGreaterThan(10);
    for (const kind of BINDING_KINDS) {
      expect(BINDING_SCHEMAS[kind], kind).toBeDefined();
      expect(en.design.bindingKind, kind).toHaveProperty(kind);
      expect(en.design.bindingCost, kind).toHaveProperty(kind);
    }
  });

  it('offer the kinds that fit a slot first and keep every kind reachable', () => {
    const forNumber = kindsForShape('number');
    expect(forNumber.slice(0, 2)).toEqual(['const', 'stateCount']);
    expect(forNumber).toHaveLength(BINDING_KINDS.length);
    expect(kindsForShape('rows')[0]).toBe('stateList');
    expect(kindsForShape('series')).toContain('history');
  });
});
