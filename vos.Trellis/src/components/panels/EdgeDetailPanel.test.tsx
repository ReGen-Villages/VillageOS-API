import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { VosRelationship, VosThing } from '@tendril-ui/tendril';
import { EdgeDetailPanel } from './EdgeDetailPanel';

vi.mock('../../hooks/useResolvedRelationshipProperties', () => ({
  useResolvedRelationshipProperties: () => ({
    flowLitresPerSecond: { Value: 1.5, Type: 'vos.Double', IsInherited: false },
    weight: { Value: 0.5, Type: 'vos.Double', IsInherited: false },
  }),
}));

vi.mock('../../api/rangeApi', () => ({
  relationshipRangeApi: { getAll: vi.fn().mockResolvedValue({}), getStates: vi.fn().mockResolvedValue({}) },
}));

const things = new Map<string, VosThing>([
  ['s', { Id: 's', Name: 'Upper Tank', Properties: {} }],
  ['p', { Id: 'p', Name: 'feeds', Properties: {} }],
  ['t', { Id: 't', Name: 'Lower Tank', Properties: {} }],
]);

const feeds: VosRelationship = {
  Id: 'r', SubjectId: 's', PredicateId: 'p', TargetId: 't',
  Properties: { flowLitresPerSecond: 1.5, weight: 0.5 },
  PropertyWriteKinds: { flowLitresPerSecond: 'ObservationOnly' },
};

describe('the properties tab of a relationship, while editing', () => {
  it('offers no control to change a value taken from readings only', async () => {
    render(
      <EdgeDetailPanel
        relationship={feeds}
        allThings={things}
        onClose={vi.fn()}
        onSelectNode={vi.fn()}
        onDeleteRelationship={vi.fn()}
      />,
    );
    fireEvent.click(screen.getByText('Properties (2)'));
    const editToggle = screen.getByTitle('Edit properties');
    await waitFor(() => expect(editToggle).toBeEnabled());
    fireEvent.click(editToggle);

    expect(screen.queryByLabelText('flowLitresPerSecond')).toBeNull();
    expect(screen.getByLabelText('weight')).toBeInTheDocument();
  });
});
