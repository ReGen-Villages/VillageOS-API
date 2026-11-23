import { describe, it, expect } from 'vitest';
import type { Binding } from '../types/dashboard';
import type { VosRelationship, VosThing } from '../types/vos';
import type { ModelReads } from './modelReads';
import { buildModelIndex, discoverDashboardsFromIndex, resolveBinding, scopeEntities, type ResolveContext } from './dashboardApi';
import { PAGE_ROLE_ID } from './platformRoles';

const thing = (Id: string, Name: string, Properties: Record<string, unknown> = {}, IsArchetype = false): VosThing =>
  ({ Id, Name, IsArchetype, Properties }) as unknown as VosThing;
const edge = (SubjectId: string, PredicateId: string, TargetId: string): VosRelationship =>
  ({ Id: `${SubjectId}-${PredicateId}-${TargetId}`, SubjectId, PredicateId, TargetId, Properties: {} }) as VosRelationship;

const page = (title: string) => JSON.stringify({ title, compare: { label: 'Reservoir', archetype: 'Reservoir' }, sections: [] });

// English orders Å with A, ahead of Z. Swedish orders Å after Z.
const index = () => buildModelIndex(
  [
    thing('is', 'is'),
    thing('binds', 'binds'),
    thing('feeds', 'feeds'),
    thing(PAGE_ROLE_ID, 'Page'),
    thing('page-kind', 'Page', {}, true),
    thing('reservoir-kind', 'Reservoir', {}, true),
    thing('page-z', 'Zone levels', { specification: page('Zone levels') }),
    thing('page-a', 'Åsen levels', { specification: page('Åsen levels') }),
    thing('zone', 'Zone reservoir'),
    thing('asen', 'Åsen reservoir'),
    thing('spring', 'Spring 1'),
  ],
  [
    edge(PAGE_ROLE_ID, 'binds', 'page-kind'),
    edge('page-z', 'is', 'page-kind'),
    edge('page-a', 'is', 'page-kind'),
    edge('zone', 'is', 'reservoir-kind'),
    edge('asen', 'is', 'reservoir-kind'),
    edge('spring', 'feeds', 'zone'),
    edge('spring', 'feeds', 'asen'),
  ],
);

const context = (language?: string): ResolveContext => ({ index: index(), scopeId: null, reads: {} as ModelReads, language });

const names = (rows: unknown) => (rows as { name: string }[]).map((row) => row.name);

describe('the order a reader sees names in', () => {
  it('lists pages in the order of the language asked for', () => {
    const held = index();
    expect(discoverDashboardsFromIndex(held, 'en').map((found) => found.name)).toEqual(['Åsen levels', 'Zone levels']);
    expect(discoverDashboardsFromIndex(held, 'sv').map((found) => found.name)).toEqual(['Zone levels', 'Åsen levels']);
  });

  it('gives a page the same address whichever language it is listed in', () => {
    const held = index();
    const addresses = (language: string) => new Map(discoverDashboardsFromIndex(held, language).map((found) => [found.id, found.routeKey]));
    expect(addresses('sv')).toEqual(addresses('en'));
  });

  it('hands back the same list for the same language, so a reader holding it sees no change', () => {
    const held = index();
    expect(discoverDashboardsFromIndex(held, 'sv')).toBe(discoverDashboardsFromIndex(held, 'sv'));
  });

  it('lists the Things a page compares in the order of the language asked for', () => {
    const held = index();
    const specification = discoverDashboardsFromIndex(held, 'en')[0].specification!;
    expect(scopeEntities(specification, held, 'en').map((entity) => entity.name)).toEqual(['Åsen reservoir', 'Zone reservoir']);
    expect(scopeEntities(specification, held, 'sv').map((entity) => entity.name)).toEqual(['Zone reservoir', 'Åsen reservoir']);
  });

  it('orders the rows of a list of Things by the language of the context', async () => {
    const binding = { kind: 'thingList', archetype: 'Reservoir' } as const;
    expect(names(await resolveBinding(binding, context('en')))).toEqual(['Åsen reservoir', 'Zone reservoir']);
    expect(names(await resolveBinding(binding, context('sv')))).toEqual(['Zone reservoir', 'Åsen reservoir']);
  });

  it('joins the names a walk reaches in the order of the language of the context', async () => {
    const binding: Binding = { kind: 'related', thing: 'spring', via: [{ predicate: 'feeds' }] };
    expect(await resolveBinding(binding, context('en'))).toBe('Åsen reservoir, Zone reservoir');
    expect(await resolveBinding(binding, context('sv'))).toBe('Zone reservoir, Åsen reservoir');
  });

  it('orders the Things a page compares by name, whatever order they were loaded in', async () => {
    const binding: Binding = { kind: 'compareEntities', properties: [] };
    const loadedInTheOtherOrder = (language: string): ResolveContext => {
      const held = index();
      return {
        ...context(language),
        compareArchetype: 'Reservoir',
        index: buildModelIndex([...held.byId.values()].reverse(), held.relationships),
      };
    };

    expect(names(await resolveBinding(binding, { ...context('en'), compareArchetype: 'Reservoir' }))).toEqual(['Åsen reservoir', 'Zone reservoir']);
    expect(names(await resolveBinding(binding, loadedInTheOtherOrder('en')))).toEqual(['Åsen reservoir', 'Zone reservoir']);
    expect(names(await resolveBinding(binding, loadedInTheOtherOrder('sv')))).toEqual(['Zone reservoir', 'Åsen reservoir']);
  });

  it('orders as English does where no language is given', async () => {
    expect(names(await resolveBinding({ kind: 'thingList', archetype: 'Reservoir' }, context()))).toEqual(['Åsen reservoir', 'Zone reservoir']);
  });
});
