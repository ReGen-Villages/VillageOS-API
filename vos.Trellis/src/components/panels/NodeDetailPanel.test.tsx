import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { VosRelationship, VosThing } from '@tendril-ui/tendril';
import { NodeDetailPanel } from './NodeDetailPanel';
import { thingApi } from '../../api/thingApi';
import { useModelStore } from '../../stores/modelStore';

vi.mock('../../api/thingApi', () => ({
  thingApi: { getEffectiveProperties: vi.fn(), setProperty: vi.fn(), addProperty: vi.fn() },
}));

vi.mock('../../hooks/useNodeRangesData', () => ({
  useNodeRangesData: () => ({
    rangesData: null, statesData: null, rangesLoading: false, relationshipRangesEntries: [], refresh: vi.fn(),
  }),
}));

const isPredicate: VosThing = { Id: 'is', Name: 'is', Properties: {} };
const site: VosThing = {
  Id: 'site', Name: 'Site', IsArchetype: true,
  Properties: { rainfall: null, humidity: null, statedArea: null },
  PropertyWriteKinds: { rainfall: 'ObservationOnly', humidity: 'ObservationOnly', statedArea: 'FactOnly' },
};
const willowBend: VosThing = {
  Id: 'willow', Name: 'Willow Bend',
  Properties: { rainfall: 700, statedArea: 24 },
  PropertyWriteKinds: { rainfall: 'ObservationOnly', statedArea: 'FactOnly' },
};
const isSite: VosRelationship = { Id: 'r', SubjectId: 'willow', PredicateId: 'is', TargetId: 'site', Properties: {} };

beforeEach(() => {
  useModelStore.setState({ things: [isPredicate, site, willowBend], relationships: [isSite] });
  vi.mocked(thingApi.getEffectiveProperties).mockResolvedValue({
    rainfall: { Value: 700, Type: 'vos.Double', IsInherited: false },
    statedArea: { Value: 24, Type: 'vos.Double', IsInherited: false },
    humidity: { Value: null, Type: 'vos.Double', IsInherited: true, InheritedFrom: 'site' },
  });
});

async function editingProperties() {
  render(
    <NodeDetailPanel
      thing={willowBend}
      relationships={[isSite]}
      allThings={new Map([isPredicate, site, willowBend].map((thing) => [thing.Id, thing]))}
      onClose={vi.fn()}
      onSelectNode={vi.fn()}
      onDeleteProperty={vi.fn()}
    />,
  );
  fireEvent.click(screen.getByText('properties'));
  const editToggle = screen.getByTitle('Edit properties');
  await waitFor(() => expect(editToggle).toBeEnabled());
  fireEvent.click(editToggle);
  await screen.findByLabelText('statedArea');
}

describe('the properties tab of a Thing, while editing', () => {
  it('offers no control to change an own value taken from readings only', async () => {
    await editingProperties();
    expect(screen.queryByLabelText('rainfall')).toBeNull();
    expect(screen.getByLabelText('statedArea')).toBeInTheDocument();
  });

  it('offers no control to change an inherited value taken from readings only', async () => {
    await editingProperties();
    const navigateToSite = screen.getByTitle('Navigate to Site');
    fireEvent.click(navigateToSite.previousElementSibling as HTMLElement);
    expect(screen.getAllByText('taken from readings, not edited by hand')).toHaveLength(2);
    expect(screen.queryByLabelText('humidity')).toBeNull();
  });
});
