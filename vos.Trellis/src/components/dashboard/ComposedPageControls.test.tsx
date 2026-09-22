import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { DashboardDescriptor } from '../../types/dashboard';
import type { VosThing, VosRelationship } from '../../types/vos';

vi.mock('../../api/dashboardPages', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/dashboardPages')>()),
  dashboardPages: { retitle: vi.fn(), remove: vi.fn() },
}));

import { dashboardPages } from '../../api/dashboardPages';
import { buildModelIndex } from '../../api/dashboardApi';
import { ComposedPageControls } from './ComposedPageControls';

function thing(Id: string, Name: string, IsArchetype = false): VosThing {
  return { Id, Name, Properties: {}, IsArchetype };
}
function relationship(Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
  return { Id, SubjectId, PredicateId, TargetId, Properties: {} };
}

const index = buildModelIndex(
  [thing('is', 'is'), thing('dashboard', 'Dashboard', true), thing('kept', 'Springs by flow')],
  [relationship('i1', 'kept', 'is', 'dashboard')],
);

const composed: DashboardDescriptor = {
  id: 'kept',
  name: 'Springs by flow',
  routeKey: 'springs-by-flow',
  specification: { title: 'Springs by flow', sections: [{ widgets: [] }], composed: { kind: 'Spring', columns: [] } },
};
const seeded: DashboardDescriptor = {
  id: 'seeded',
  name: 'Water overview',
  routeKey: 'water-overview',
  specification: { title: 'Water overview', sections: [{ widgets: [] }] },
};

function show(dashboard: DashboardDescriptor) {
  return render(
    <MemoryRouter>
      <ComposedPageControls dashboard={dashboard} index={index} />
    </MemoryRouter>,
  );
}

describe('ComposedPageControls', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(dashboardPages.retitle).mockResolvedValue();
    vi.mocked(dashboardPages.remove).mockResolvedValue();
    vi.spyOn(window, 'confirm').mockReturnValue(true);
  });

  it('offers rename and remove on a page the console kept', () => {
    show(composed);
    expect(screen.getByRole('button', { name: 'Rename' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Remove page' })).toBeInTheDocument();
  });

  it('offers neither on a seeded page, which is left alone', () => {
    show(seeded);
    expect(screen.queryByRole('button', { name: 'Rename' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Remove page' })).toBeNull();
  });

  it('renames by rewriting the title, leaving the Thing its name', async () => {
    show(composed);
    fireEvent.click(screen.getByRole('button', { name: 'Rename' }));
    fireEvent.change(screen.getByLabelText('New title'), { target: { value: 'Springs, fastest first' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(dashboardPages.retitle).toHaveBeenCalledWith('kept', composed.specification, 'Springs, fastest first'));
  });

  it('asks before removing, and then takes the page out of the model', async () => {
    show(composed);
    fireEvent.click(screen.getByRole('button', { name: 'Remove page' }));

    expect(window.confirm).toHaveBeenCalled();
    await waitFor(() => expect(dashboardPages.remove).toHaveBeenCalledWith('kept', expect.objectContaining({ dashboardArchetypeId: 'dashboard' })));
  });

  it('removes nothing when the reader says no', () => {
    vi.mocked(window.confirm).mockReturnValue(false);
    show(composed);
    fireEvent.click(screen.getByRole('button', { name: 'Remove page' }));

    expect(dashboardPages.remove).not.toHaveBeenCalled();
  });
});
