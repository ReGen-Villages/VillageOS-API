import { thingApi } from './thingApi';
import { relationshipApi } from './relationshipApi';
import { modelApi } from './modelApi';
import { parseSpec, type ModelIndex } from './dashboardApi';
import type { VosRelationship } from '../types/vos';
import { DASHBOARD_ARCHETYPE, DASHBOARD_SPEC_PROPERTY, IS_PREDICATE, type DashboardSpec } from '../types/dashboard';

/**
 * Writing a page back to the model.
 *
 * A page the console keeps is one more `Dashboard` Thing beside the seeded ones, and is found by
 * the same discovery. Nothing checks a spec written in a browser the way the seed's checks read a
 * seeded one, so a spec the discovery could not list is refused here before anything is written.
 */

const SPEC_TYPE = 'vos.String';

export interface DashboardWriteContext {
  dashboardArchetypeId: string;
  isPredicateId: string;
  /** The model as it stands, for finding the edges a removal takes. */
  relationships: VosRelationship[];
}

/** What the writes need from the model, or null where it names no `Dashboard` kind or no `is`:
 *  there would be nothing to make the new Thing one of, and the discovery would list nothing. */
export function dashboardWriteContext(modelIndex: ModelIndex): DashboardWriteContext | null {
  const kind = modelIndex.byName.get(DASHBOARD_ARCHETYPE);
  const isPredicateId = modelIndex.predicateNameToId.get(IS_PREDICATE);
  if (!kind || !isPredicateId) return null;
  return { dashboardArchetypeId: kind.Id, isPredicateId, relationships: modelIndex.relationships };
}

function asTheDiscoveryReadsIt(spec: DashboardSpec): string {
  const written = JSON.stringify(spec);
  if (!parseSpec(written)) throw new Error('The console could not read this page back, so it was not written.');
  return written;
}

export const dashboardPages = {
  /** Keeps a page as one fragment — the Thing, what it is and its spec together — so a page the
   *  model refuses leaves nothing behind. Written as three requests, a refusal after the first would
   *  leave a Thing of no kind that no discovery lists and nobody could see to remove. Answers the
   *  id, which the browser mints because the fragment points its own edge at the Thing it creates. */
  async keep(name: string, spec: DashboardSpec, context: DashboardWriteContext): Promise<string> {
    const written = asTheDiscoveryReadsIt(spec);
    const id = crypto.randomUUID();
    await modelApi.applyFragment(JSON.stringify({
      Name: name,
      Things: [{ Id: id, Name: name, Properties: { [DASHBOARD_SPEC_PROPERTY]: { typeInfo: SPEC_TYPE, value: written } } }],
      Relationships: [{ Subject: id, Predicate: context.isPredicateId, Target: context.dashboardArchetypeId }],
    }));
    return id;
  },

  /** Gives a page a new title. The Thing keeps its name, so the address the sidebar links to stays. */
  async retitle(id: string, spec: DashboardSpec, title: string): Promise<void> {
    await thingApi.setProperty(id, DASHBOARD_SPEC_PROPERTY, SPEC_TYPE, asTheDiscoveryReadsIt({ ...spec, title }));
  },

  /** Retracts a page. Edges first: a Thing still named by one is a Thing something can still be
   *  pointed at through. */
  async remove(id: string, context: DashboardWriteContext): Promise<void> {
    const onThePage = context.relationships.filter((edge) => edge.SubjectId === id || edge.TargetId === id);
    for (const edge of onThePage) await relationshipApi.remove(edge.Id);
    await thingApi.remove(id);
  },
};
