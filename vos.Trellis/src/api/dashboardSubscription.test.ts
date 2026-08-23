import { describe, it, expect } from 'vitest';
import { NAVIGATION_AND_SETTINGS, subscriptionForSpec } from './dashboardSubscription';
import type { DashboardSpec } from '../types/dashboard';

const SCOPE_ID = '11111111-2222-3333-4444-555555555555';

function specWith(over: Partial<DashboardSpec>): DashboardSpec {
  return { title: 'Ops', sections: [], ...over };
}

/** A spec whose one widget carries the binding under test. */
function specDrawing(value: DashboardSpec['sections'][number]['widgets'][number]): DashboardSpec {
  return specWith({ sections: [{ widgets: [value] }] });
}

describe('subscriptionForSpec', () => {
  it('keeps asking for what the navigation and the display settings are read from', () => {
    const selector = subscriptionForSpec(specWith({}), null);

    expect(selector.types).toEqual(expect.arrayContaining(NAVIGATION_AND_SETTINGS.types!));
    expect(selector.names).toEqual(expect.arrayContaining(NAVIGATION_AND_SETTINGS.names!));
  });

  it('asks for the entities the scope switcher offers', () => {
    const selector = subscriptionForSpec(specWith({ compare: { label: 'site', archetype: 'Site' } }), null);

    expect(selector.types).toContain('Site');
  });

  it('names the selected entity as an identifier', () => {
    const selector = subscriptionForSpec(specWith({ compare: { label: 'site', archetype: 'Site' } }), SCOPE_ID);

    expect(selector.ids).toEqual([SCOPE_ID]);
  });

  // The platform answers a count from the state endpoint and a series from its reduction, so their
  // members would arrive only to be counted again and thrown away — and a type with a member per
  // event is exactly where that is worst.
  it('leaves out the type of a binding the platform answers', () => {
    const selector = subscriptionForSpec(
      specWith({
        sections: [{
          widgets: [
            { type: 'kpi', title: 'open', value: { kind: 'stateCount', state: 'open', archetype: 'Delivery' } },
            {
              type: 'kpi',
              title: 'flow',
              value: {
                kind: 'timeseries',
                archetype: 'Reading',
                happenedAt: 'happenedAt',
                op: 'count',
                bucketSeconds: 3600,
                buckets: 8,
              },
            },
          ],
        }],
      }),
      null,
    );

    expect(selector.types).not.toContain('Delivery');
    expect(selector.types).not.toContain('Reading');
  });

  it('asks for the type a roster draws', () => {
    const selector = subscriptionForSpec(
      specDrawing({ type: 'table', columns: [], rows: { kind: 'thingList', archetype: 'Parcel' } }),
      null,
    );

    expect(selector.types).toContain('Parcel');
  });

  // A binding narrowed to the selected entity reaches its rows along that entity's edges, so asking
  // for its type as well would pull in every other entity's rows for a page showing one.
  it('leaves out a scoped binding\'s type once an entity is selected, and follows its edge instead', () => {
    const scoped = specDrawing({
      type: 'table',
      columns: [],
      rows: { kind: 'thingList', archetype: 'Parcel', scope: { viaPredicate: 'has' } },
    });

    expect(subscriptionForSpec(scoped, SCOPE_ID).types).not.toContain('Parcel');
    expect(subscriptionForSpec(scoped, SCOPE_ID).traverse)
      .toContainEqual({ predicate: 'has', direction: 'outgoing', depth: expect.any(Number) });
  });

  // With no entity selected the binding does run over every member of the type, so the page holds
  // them all — the same answer the resolver gives.
  it('asks for a scoped binding\'s type while every entity is being shown', () => {
    const selector = subscriptionForSpec(
      specDrawing({
        type: 'table',
        columns: [],
        rows: { kind: 'thingList', archetype: 'Parcel', scope: { viaPredicate: 'has' } },
      }),
      null,
    );

    expect(selector.types).toContain('Parcel');
  });

  // A scope walk is transitive: the entity relates to Things that in turn relate to the rows.
  it('follows a scope predicate as far as it reaches, and a binding\'s step one hop', () => {
    const selector = subscriptionForSpec(
      specWith({
        sections: [{
          widgets: [
            { type: 'kpi', title: 'a', value: { kind: 'aggregate', archetype: 'Parcel', op: 'count', scope: { viaPredicate: 'contains' } } },
            { type: 'kpi', title: 'b', value: { kind: 'related', via: [{ predicate: 'studies', direction: 'in' }] } },
          ],
        }],
      }),
      SCOPE_ID,
    );

    const contains = selector.traverse!.find((rule) => rule.predicate === 'contains')!;
    const studies = selector.traverse!.find((rule) => rule.predicate === 'studies')!;
    expect(contains.depth).toBeGreaterThan(1);
    expect(studies).toEqual({ predicate: 'studies', direction: 'incoming', depth: 1 });
  });

  // The platform applies each rule to everything selected before it, and applies it once. A walk of
  // two steps is only reproduced if its first edge is asked for before its second.
  it('asks for a walk\'s edges in the order the walk takes them', () => {
    const selector = subscriptionForSpec(
      specDrawing({
        type: 'verdict',
        rows: [{
          label: 'Water',
          verdicts: { kind: 'verdict', states: [], via: [{ predicate: 'studies', direction: 'in' }, { predicate: 'has' }] },
        }],
      }),
      SCOPE_ID,
    );

    const followed = selector.traverse!.map((rule) => rule.predicate);
    expect(followed.indexOf('studies')).toBeLessThan(followed.indexOf('has'));
  });

  // A detail window walks the same way a binding does, and it opens on a row of a page that has
  // already narrowed — so what it reaches has to be in the page's own set.
  it('follows the edges a detail card walks, root first', () => {
    const selector = subscriptionForSpec(
      specWith({
        detail: { relations: [{ predicate: 'has', relations: [{ predicate: 'categorizedAs' }] }] },
      }),
      SCOPE_ID,
    );

    const followed = selector.traverse!.map((rule) => rule.predicate);
    expect(followed).toEqual(['has', 'categorizedAs']);
  });

  it('finds the bindings nested inside a ratio and a computed column', () => {
    const selector = subscriptionForSpec(
      specWith({
        sections: [{
          widgets: [
            {
              type: 'kpi',
              title: 'rate',
              value: {
                kind: 'ratio',
                numerator: { kind: 'aggregate', archetype: 'Harvest', op: 'sum', property: 'volume' },
                denominator: { kind: 'aggregate', archetype: 'Parcel', op: 'count' },
              },
            },
            {
              type: 'table',
              columns: [],
              rows: {
                kind: 'thingList',
                archetype: 'Site',
                computed: [{ key: 'reach', value: { kind: 'related', via: [{ predicate: 'supplies' }] } }],
              },
            },
          ],
        }],
      }),
      null,
    );

    expect(selector.types).toEqual(expect.arrayContaining(['Harvest', 'Parcel', 'Site']));
    expect(selector.traverse!.map((rule) => rule.predicate)).toContain('supplies');
  });

  // A spec's Thing reference is an id or a name, and the platform reads the two as different
  // questions: a name sent as an id is refused rather than looked up.
  it('tells a named Thing apart from one referred to by identifier', () => {
    const selector = subscriptionForSpec(
      specWith({
        sections: [{
          widgets: [
            { type: 'kpi', title: 'a', value: { kind: 'property', thing: 'Reservoir', property: 'volume' } },
            { type: 'kpi', title: 'b', value: { kind: 'property', thing: SCOPE_ID, property: 'volume' } },
            { type: 'kpi', title: 'c', value: { kind: 'property', thing: '$scope', property: 'volume' } },
          ],
        }],
      }),
      null,
    );

    expect(selector.names).toContain('Reservoir');
    expect(selector.ids).toEqual([SCOPE_ID]);
    expect(selector.names).not.toContain('$scope');
  });
});
