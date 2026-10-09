import { describe, it, expect } from 'vitest';
import type { Binding, Widget } from '../types/dashboard';
import { needsOneEntity, widgetNeedsOneEntity } from './selectedEntity';

const walkFromTheSite: Binding = { kind: 'related', via: [{ predicate: 'studies', direction: 'in' }], property: 'peopleWatered' };
const averagedAcrossSites: Binding = { kind: 'property', thing: '$scope', property: 'elevationMetres' };
const originOfTheSite: Binding = { kind: 'origin', property: 'elevationMetres', reads: {} };

describe('needsOneEntity', () => {
  it.each([
    ['a walk naming no Thing', walkFromTheSite],
    ['a walk from $scope', { ...walkFromTheSite, thing: '$scope' }],
    ['a verdict naming no Thing', { kind: 'verdict', states: [] }],
    ['a state naming no Thing', { kind: 'stateOf', states: ['flooded'] }],
    ['an origin naming no Thing', originOfTheSite],
    ['a working naming no Thing', { kind: 'working', property: 'peopleFed' }],
    ['a history, which reads one entity\'s series', { kind: 'history', property: 'temperatureCelsius', windowSeconds: 86400, steps: [] }],
    ['a ratio with a walking side', { kind: 'ratio', numerator: averagedAcrossSites, denominator: walkFromTheSite }],
  ] as [string, Binding][])('holds for %s', (_, binding) => {
    expect(needsOneEntity(binding)).toBe(true);
  });

  it.each([
    ['a $scope property, which averages across the entities', averagedAcrossSites],
    ['a walk from a Thing it names', { ...walkFromTheSite, thing: 'Reservoir-3' }],
    ['a service, which answers for every entity at once', { kind: 'service', endpoint: '/api/endpoints/reservoirs' }],
    ['a count of a state', { kind: 'stateCount', state: 'flooded' }],
  ] as [string, Binding][])('does not hold for %s', (_, binding) => {
    expect(needsOneEntity(binding)).toBe(false);
  });
});

describe('widgetNeedsOneEntity', () => {
  it('holds for a tile whose value walks from the selected entity', () => {
    const widget: Widget = { type: 'kpi', title: 'People watered', value: walkFromTheSite };
    expect(widgetNeedsOneEntity(widget)).toBe(true);
  });

  it('does not hold for a tile whose value averages, though its origin line walks', () => {
    const widget: Widget = { type: 'kpi', title: 'Elevation', value: averagedAcrossSites, origin: originOfTheSite };
    expect(widgetNeedsOneEntity(widget)).toBe(false);
  });

  it('does not hold for a table of rows each walking from its own Thing', () => {
    const widget: Widget = {
      type: 'table',
      title: 'Sources',
      rows: { kind: 'thingList', archetype: 'DataSource', computed: [{ key: 'covers', value: walkFromTheSite }] },
      columns: [{ key: 'covers', label: 'Covers' }],
    };
    expect(widgetNeedsOneEntity(widget)).toBe(false);
  });
});
