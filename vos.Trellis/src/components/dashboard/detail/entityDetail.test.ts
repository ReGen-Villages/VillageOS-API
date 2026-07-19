import { describe, it, expect } from 'vitest';
import { buildModelIndex } from '../../../api/dashboardApi';
import type { VosThing, VosRelationship, StateTransition } from '../../../types/vos';
import { resolveRelations, flattenRelatedIds, buildStateChanges } from './entityDetail';

function thing(id: string, name: string, properties: Record<string, unknown> = {}): VosThing {
  return { Id: id, Name: name, Properties: properties };
}
function rel(id: string, subjectId: string, predicateId: string, targetId: string): VosRelationship {
  return { Id: id, Name: id, SubjectId: subjectId, PredicateId: predicateId, TargetId: targetId, Properties: {} };
}

// order -has-> line -has-> alloc, order -has-> customer, order -references-> wave -contains-> order.
// `has` reaches both a line and a customer, so archetype filtering must tell them apart.
function fixture() {
  const things = [
    thing('order', 'ORD-1'),
    thing('line', 'ORD-1-L1', { quantity: 5, sku: 'SKU-9' }),
    thing('alloc', 'ORD-1-L1-A', { quantity: 5 }),
    thing('item', 'ITEM-9', { sku: 'SKU-9', description: 'Widget' }),
    thing('cust', 'CUST-1', { display_name: 'Acme' }),
    thing('wave', 'WAVE-1', { wave_number: 'W-1' }),
    thing('OrderLine', 'OrderLine'),
    thing('Allocation', 'Allocation'),
    thing('Item', 'Item'),
    thing('Customer', 'Customer'),
    thing('Wave', 'Wave'),
    thing('has', 'has'),
    thing('references', 'references'),
    thing('contains', 'contains'),
    thing('is', 'is'),
  ];
  const relationships = [
    rel('r1', 'order', 'has', 'line'),
    rel('r2', 'line', 'has', 'alloc'),
    rel('r3', 'order', 'has', 'cust'),
    rel('r4', 'order', 'references', 'wave'),
    rel('r5', 'wave', 'contains', 'order'),
    rel('r6', 'line', 'references', 'item'),
    rel('i1', 'line', 'is', 'OrderLine'),
    rel('i2', 'alloc', 'is', 'Allocation'),
    rel('i3', 'cust', 'is', 'Customer'),
    rel('i4', 'wave', 'is', 'Wave'),
    rel('i5', 'item', 'is', 'Item'),
  ];
  return buildModelIndex(things, relationships);
}

describe('resolveRelations', () => {
  it('follows an outbound predicate and keeps only the configured archetype', () => {
    const idx = fixture();
    const [group] = resolveRelations('order', idx, [
      { predicate: 'has', direction: 'out', archetype: 'OrderLine', label: 'Order lines' },
    ]);
    expect(group.label).toBe('Order lines');
    expect(group.edges.map((e) => e.relatedName)).toEqual(['ORD-1-L1']);
    // 'has' also reaches the customer, but the archetype filter excludes it.
    expect(group.edges.some((e) => e.relatedName === 'CUST-1')).toBe(false);
  });

  it('names the subject and target of each edge by direction', () => {
    const idx = fixture();
    const [out] = resolveRelations('order', idx, [{ predicate: 'has', direction: 'out', archetype: 'OrderLine' }]);
    expect(out.edges[0]).toMatchObject({ subjectName: 'ORD-1', targetName: 'ORD-1-L1' });
    const [inbound] = resolveRelations('order', idx, [{ predicate: 'contains', direction: 'in', archetype: 'Wave' }]);
    expect(inbound.edges[0]).toMatchObject({ subjectName: 'WAVE-1', targetName: 'ORD-1' });
  });

  it('shows only the requested properties, or all with "*"', () => {
    const idx = fixture();
    const [some] = resolveRelations('order', idx, [
      { predicate: 'has', archetype: 'OrderLine', properties: ['quantity'] },
    ]);
    expect(some.edges[0].properties).toEqual([['quantity', 5]]);
    const [all] = resolveRelations('order', idx, [
      { predicate: 'has', archetype: 'OrderLine', properties: '*' },
    ]);
    expect(all.edges[0].properties).toEqual(expect.arrayContaining([['quantity', 5], ['sku', 'SKU-9']]));
    const [none] = resolveRelations('order', idx, [{ predicate: 'has', archetype: 'OrderLine' }]);
    expect(none.edges[0].properties).toEqual([]);
  });

  it('nests child relations from each matched Thing', () => {
    const idx = fixture();
    const [group] = resolveRelations('order', idx, [
      {
        predicate: 'has',
        archetype: 'OrderLine',
        relations: [{ predicate: 'has', archetype: 'Allocation', label: 'Allocation', properties: ['quantity'] }],
      },
    ]);
    const child = group.edges[0].children[0];
    expect(child.label).toBe('Allocation');
    expect(child.edges[0].relatedName).toBe('ORD-1-L1-A');
    expect(child.edges[0].properties).toEqual([['quantity', 5]]);
  });

  it('folds an inline relation onto the parent row instead of nesting it', () => {
    const idx = fixture();
    const [group] = resolveRelations('order', idx, [
      {
        predicate: 'has',
        archetype: 'OrderLine',
        properties: ['quantity'],
        relations: [{ predicate: 'references', archetype: 'Item', properties: ['sku'], inline: true }],
      },
    ]);
    const line = group.edges[0];
    // The item's sku is hoisted ahead of the line's own quantity; no nested Item card remains.
    expect(line.properties).toEqual([['sku', 'SKU-9'], ['quantity', 5]]);
    expect(line.children).toEqual([]);
  });

  it('preserves the configured order of relation groups', () => {
    const idx = fixture();
    const groups = resolveRelations('order', idx, [
      { predicate: 'references', archetype: 'Wave', label: 'Wave' },
      { predicate: 'has', archetype: 'Customer', label: 'Customer' },
    ]);
    expect(groups.map((g) => g.label)).toEqual(['Wave', 'Customer']);
  });

  it('is cycle-guarded when a relation loops back to the root', () => {
    const idx = fixture();
    // order -references-> wave -contains-> order: the nested contains must not re-expand the root.
    const [group] = resolveRelations('order', idx, [
      { predicate: 'references', archetype: 'Wave', relations: [{ predicate: 'contains', direction: 'out' }] },
    ]);
    const waveChildren = group.edges[0].children[0];
    expect(waveChildren.edges.some((e) => e.thingId === 'order')).toBe(false);
  });
});

describe('flattenRelatedIds', () => {
  it('collects every related id across the nested tree', () => {
    const idx = fixture();
    const relations = resolveRelations('order', idx, [
      { predicate: 'has', archetype: 'OrderLine', relations: [{ predicate: 'has', archetype: 'Allocation' }] },
    ]);
    expect(new Set(flattenRelatedIds(relations))).toEqual(new Set(['line', 'alloc']));
  });
});

describe('buildStateChanges', () => {
  const transition = (at: string, entered: string[], exited: string[]): StateTransition => ({
    At: at,
    Entered: entered,
    Exited: exited,
    States: entered,
  });

  it('orders changes oldest first', () => {
    const changes = buildStateChanges([
      transition('2026-07-14T09:31:00Z', ['allocated'], []),
      transition('2026-07-14T09:14:00Z', ['released'], []),
    ]);
    expect(changes.map((c) => c.at)).toEqual(['2026-07-14T09:14:00Z', '2026-07-14T09:31:00Z']);
    expect(changes[0].entered).toEqual(['released']);
  });

  it('drops transitions that neither enter nor exit a state', () => {
    const changes = buildStateChanges([
      transition('2026-07-14T09:14:00Z', ['released'], []),
      transition('2026-07-14T09:20:00Z', [], []),
    ]);
    expect(changes).toHaveLength(1);
  });
});
