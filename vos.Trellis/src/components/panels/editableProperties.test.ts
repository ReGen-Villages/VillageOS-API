import { describe, it, expect } from 'vitest';
import { withDeclaredTypes } from './editableProperties';
import type { EffectiveProperty } from '../../types/vos';

const resolved =(entries: Record<string, string>): Record<string, EffectiveProperty> =>
  Object.fromEntries(
    Object.entries(entries).map(([name, Type]) => [name, { Value: null, Type, IsInherited: false }]),
  );

// Feature #6146 — a save states the type the platform reports for the property. It was inferred
// from the typed text before, so a code stored as text was announced as a number.
describe('withDeclaredTypes', () => {
  it('carries the platform type alongside each value', () => {
    const paired = withDeclaredTypes(
      [['door_number', '4711'], ['open_ratio', 0.25]],
      resolved({ door_number: 'vos.String', open_ratio: 'vos.Decimal' }),
    );
    expect(paired).toEqual([
      { name: 'door_number', value: '4711', type: 'vos.String' },
      { name: 'open_ratio', value: 0.25, type: 'vos.Decimal' },
    ]);
  });

  it('keeps the declared type even when the value reads like another one', () => {
    const [property] = withDeclaredTypes([['door_number', '4711']], resolved({ door_number: 'vos.String' }));
    expect(property.type).toBe('vos.String');
  });

  it('drops a property the resolved set does not name a type for', () => {
    const paired = withDeclaredTypes(
      [['known', 1], ['unknown', 2]],
      resolved({ known: 'vos.LongInteger' }),
    );
    expect(paired.map((p) => p.name)).toEqual(['known']);
  });

  it('offers nothing until the resolved set has arrived', () => {
    expect(withDeclaredTypes([['door_number', '4711']], null)).toEqual([]);
  });
});
