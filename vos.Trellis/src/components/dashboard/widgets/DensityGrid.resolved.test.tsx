import { describe, it, expect } from 'vitest';
import { render, waitFor } from '@testing-library/react';
import { buildModelIndex } from '@tendril-ui/tendril';
import type { DensityGridWidget, ModelReads, ResolveContext, VosRelationship, VosThing } from '@tendril-ui/tendril';
import { DensityGrid } from './DensityGrid';

const zone = { type: 'Polygon', coordinates: [[[20, 40], [24, 40], [24, 60], [20, 60], [20, 40]]] };

function siteStudiedWithAZone(): ResolveContext {
  const things: VosThing[] = [
    { Id: 'is', Name: 'is', Properties: {} },
    { Id: 'studies', Name: 'studies', Properties: {} },
    { Id: 'site1', Name: 'Submitted site', Properties: {} },
    { Id: 'study1', Name: 'Study', Properties: { summerComfortZone: { Json: JSON.stringify(zone) } } },
  ];
  const relationships: VosRelationship[] = [
    { Id: 'r1', Name: 'study1 studies site1', SubjectId: 'study1', PredicateId: 'studies', TargetId: 'site1', Properties: {} },
  ];
  const reads = {
    reduce: async () => ({ Groups: [{ Key: '20,40', Value: 10 }, { Key: '22,45', Value: 30 }], Samples: 40, UnusableSamples: 0 }),
  } as unknown as ModelReads;
  return { index: buildModelIndex(things, relationships), scopeId: 'site1', reads };
}

const widget: DensityGridWidget = {
  type: 'densityGrid',
  title: 'Humidity and temperature',
  cells: {
    kind: 'history', property: 'temperature', windowSeconds: 31_536_000, steps: [{ fold: 'cell', function: 'Count' }],
    bins: { width: 2 }, pairedWith: { property: 'humidity', bins: { width: 5, upTo: 100 } },
  },
  across: { label: 'Temperature', unit: '°C' },
  up: { label: 'Relative humidity', unit: '%' },
  outlines: [{
    label: 'Summer comfort', colour: '#e8a33d',
    shape: { kind: 'value', via: [{ predicate: 'studies', direction: 'in' }], property: 'summerComfortZone' },
  }],
};

describe('a density grid read through the model', () => {
  it('draws an outline the study holds as GeoJson', async () => {
    const { container } = render(<DensityGrid widget={widget} context={siteStudiedWithAZone()} />);

    await waitFor(() => expect(container.querySelector('path[data-outline="Summer comfort"]')).not.toBeNull());
    expect(container.querySelector('path[data-outline="Summer comfort"]')!.getAttribute('d')).toMatch(/Z$/);
  });
});
