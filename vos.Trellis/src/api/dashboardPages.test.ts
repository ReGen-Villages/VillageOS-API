import { describe, it, expect, vi, beforeEach } from 'vitest';
import type { DashboardSpec } from '../types/dashboard';
import type { VosThing, VosRelationship } from '../types/vos';

vi.mock('./modelApi', () => ({ modelApi: { applyFragment: vi.fn() } }));
vi.mock('./thingApi', () => ({ thingApi: { setProperty: vi.fn(), remove: vi.fn() } }));
vi.mock('./relationshipApi', () => ({ relationshipApi: { remove: vi.fn() } }));

import { modelApi } from './modelApi';
import { thingApi } from './thingApi';
import { relationshipApi } from './relationshipApi';
import { buildModelIndex } from './dashboardApi';
import { dashboardPages, dashboardWriteContext } from './dashboardPages';

function thing(Id: string, Name: string, IsArchetype = false): VosThing {
  return { Id, Name, Properties: {}, IsArchetype };
}
function relationship(Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
  return { Id, SubjectId, PredicateId, TargetId, Properties: {} };
}

const PAGE: DashboardSpec = {
  title: 'Springs by flow',
  sections: [{ widgets: [] }],
  composed: { kind: 'Spring', columns: [] },
};

function index() {
  return buildModelIndex(
    [thing('is', 'is'), thing('dashboard', 'Dashboard', true), thing('kept', 'Springs by flow'), thing('spring', 'Spring', true)],
    [relationship('i1', 'kept', 'is', 'dashboard'), relationship('i2', 'spring', 'is', 'spring')],
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal('crypto', { randomUUID: () => 'minted-id' });
});

describe('what the writes need from the model', () => {
  it('finds the Dashboard kind and the `is` predicate', () => {
    expect(dashboardWriteContext(index())).toMatchObject({ dashboardArchetypeId: 'dashboard', isPredicateId: 'is' });
  });

  it('is nothing where the model declares no Dashboard kind, since a kept page would be of no kind', () => {
    expect(dashboardWriteContext(buildModelIndex([thing('is', 'is')], []))).toBeNull();
  });
});

describe('keeping a page', () => {
  it('writes the Thing, what it is and its spec as one fragment, and nothing else', async () => {
    vi.mocked(modelApi.applyFragment).mockResolvedValue({ thingsCreated: 1, thingsUpdated: 0, relationshipsCreated: 1, things: [] });

    const id = await dashboardPages.keep('Springs by flow', PAGE, dashboardWriteContext(index())!);

    expect(id).toBe('minted-id');
    expect(modelApi.applyFragment).toHaveBeenCalledTimes(1);
    const fragment = JSON.parse(vi.mocked(modelApi.applyFragment).mock.calls[0][0]);
    expect(fragment).toEqual({
      Name: 'Springs by flow',
      Things: [{ Id: 'minted-id', Name: 'Springs by flow', Properties: { spec: { typeInfo: 'vos.String', value: JSON.stringify(PAGE) } } }],
      Relationships: [{ Subject: 'minted-id', Predicate: 'is', Target: 'dashboard' }],
    });
    expect(thingApi.setProperty).not.toHaveBeenCalled();
  });

  it('refuses a spec the console’s own discovery could not read back, before anything is written', async () => {
    await expect(dashboardPages.keep('Broken', { title: 'Broken' } as DashboardSpec, dashboardWriteContext(index())!)).rejects.toThrow(/could not read/);
    expect(modelApi.applyFragment).not.toHaveBeenCalled();
  });
});

describe('retitling a page', () => {
  it('rewrites the spec’s title and leaves the Thing’s name, so the address stays', async () => {
    await dashboardPages.retitle('kept', PAGE, 'Springs, fastest first');

    expect(thingApi.setProperty).toHaveBeenCalledWith('kept', 'spec', 'vos.String', JSON.stringify({ ...PAGE, title: 'Springs, fastest first' }));
  });
});

describe('writing a page over', () => {
  it('rewrites the whole spec on the Thing the page stands as', async () => {
    const laidOut = { ...PAGE, designed: true as const };
    await dashboardPages.write('kept', laidOut);

    expect(thingApi.setProperty).toHaveBeenCalledWith('kept', 'spec', 'vos.String', JSON.stringify(laidOut));
  });

  it('refuses a spec the discovery could not read back', async () => {
    await expect(dashboardPages.write('kept', { ...PAGE, sections: undefined } as never)).rejects.toThrow();
    expect(thingApi.setProperty).not.toHaveBeenCalled();
  });
});

describe('removing a page', () => {
  it('retracts the edges on the page and then the Thing', async () => {
    const order: string[] = [];
    vi.mocked(relationshipApi.remove).mockImplementation(async (id) => { order.push(`edge ${id}`); return { message: '' }; });
    vi.mocked(thingApi.remove).mockImplementation(async (id) => { order.push(`thing ${id}`); return { message: '' }; });

    await dashboardPages.remove('kept', dashboardWriteContext(index())!);

    expect(order).toEqual(['edge i1', 'thing kept']);
  });
});
