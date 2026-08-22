import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { VosThing, VosRelationship } from '../../types/vos';

vi.mock('../../hooks/useAuth', () => ({
  useAuth: () => ({ modelName: 'test-model', logout: vi.fn(), switchModel: vi.fn() }),
}));

import i18n from '../../i18n';
import { useModelStore } from '../../stores/modelStore';
import { Sidebar } from './Sidebar';

interface PublishedDashboard {
  name: string;
  spec: Record<string, unknown>;
}

function publish(dashboards: PublishedDashboard[]) {
  const things: VosThing[] = [
    { Id: 'is', Name: 'is', Properties: {} },
    { Id: 'arch-dash', Name: 'Dashboard', Properties: {}, IsArchetype: true },
    ...dashboards.map((d, i) => ({ Id: `dash-${i}`, Name: d.name, Properties: { spec: JSON.stringify(d.spec) } })),
  ];
  const relationships: VosRelationship[] = dashboards.map((_, i) => ({
    Id: `dash-${i}-is`,
    Name: `dash-${i} is Dashboard`,
    SubjectId: `dash-${i}`,
    PredicateId: 'is',
    TargetId: 'arch-dash',
    Properties: {},
  }));
  useModelStore.setState({ things, relationships, loaded: true });
}

function entryLabels(): string[] {
  return screen.getAllByRole('link').map((link) => link.textContent ?? '');
}

function hrefOf(label: string): string | null {
  return screen.getByRole('link', { name: label }).getAttribute('href');
}

describe('Sidebar (Story 6582)', () => {
  beforeEach(() => {
    useModelStore.setState({ things: [], relationships: [], loaded: true });
  });

  afterEach(async () => {
    await i18n.changeLanguage('en');
  });

  function renderSidebar() {
    render(
      <MemoryRouter>
        <Sidebar />
      </MemoryRouter>,
    );
  }

  it('keeps the built-in operations entry while the model publishes no dashboard', () => {
    renderSidebar();

    expect(entryLabels()).toContain('Operations');
    expect(hrefOf('Operations')).toBe('/operations');
  });

  it('lists one entry per published dashboard in place of the built-in one', () => {
    publish([
      { name: 'Springs', spec: { title: 'Springs', icon: 'droplet', sections: [] } },
      { name: 'Arrays', spec: { title: 'Arrays', icon: 'grid-3x3', sections: [] } },
    ]);
    renderSidebar();

    const labels = entryLabels();
    expect(labels).toContain('Arrays');
    expect(labels).toContain('Springs');
    expect(labels).not.toContain('Operations');
  });

  it('gives each entry an address of its own', () => {
    publish([{ name: 'Site catchments', spec: { title: 'Catchments', sections: [] } }]);
    renderSidebar();

    expect(hrefOf('Catchments')).toBe('/operations/site-catchments');
  });

  it('labels an entry with the dashboard title in the active language', async () => {
    publish([
      { name: 'Springs', spec: { title: 'Springs', sections: [], translations: { nl: { Springs: 'Bronnen' } } } },
    ]);
    await i18n.changeLanguage('nl');
    renderSidebar();

    expect(entryLabels()).toContain('Bronnen');
  });

  it('still lists a dashboard whose spec names no icon', () => {
    publish([{ name: 'Reservoirs', spec: { title: 'Reservoirs', sections: [] } }]);
    renderSidebar();

    expect(entryLabels()).toContain('Reservoirs');
  });

  it('still lists a dashboard naming an icon the set does not have', () => {
    publish([{ name: 'Reservoirs', spec: { title: 'Reservoirs', icon: 'not-an-icon-name', sections: [] } }]);
    renderSidebar();

    expect(entryLabels()).toContain('Reservoirs');
  });
});
