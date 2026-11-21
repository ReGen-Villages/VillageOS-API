import { thingApi } from './thingApi';
import { relationshipApi } from './relationshipApi';
import { modelApi } from './modelApi';
import { parseSpecification, thingIdBoundBy, type ModelIndex } from '@tendril-ui/tendril';
import { PAGE_ROLE_ID } from '@tendril-ui/tendril';
import type { VosRelationship } from '@tendril-ui/tendril';
import { DASHBOARD_SPECIFICATION_PROPERTY, IS_PREDICATE, type DashboardSpecification } from '@tendril-ui/tendril';
import i18n from '../i18n';

/** A page the console keeps is one more Thing of the page kind, found by the same discovery as seeded ones.
 *  Nothing checks a specification written in a browser as the seed's checks do, so one the discovery could
 *  not list is refused here before anything is written. */

const SPECIFICATION_TYPE = 'vos.String';

interface DashboardWriteContext {
  dashboardArchetypeId: string;
  isPredicateId: string;
  /** For finding the relationships a removal takes. */
  relationships: VosRelationship[];
}

/** Null where no role binds a page kind or the model names no `is`, because there would be nothing to make
 *  the new Thing one of, and the discovery would list nothing. */
export function dashboardWriteContext(modelIndex: ModelIndex): DashboardWriteContext | null {
  const dashboardArchetypeId = thingIdBoundBy(PAGE_ROLE_ID, modelIndex);
  const isPredicateId = modelIndex.predicateNameToId.get(IS_PREDICATE);
  if (!dashboardArchetypeId || !isPredicateId) return null;
  return { dashboardArchetypeId, isPredicateId, relationships: modelIndex.relationships };
}

function asTheDiscoveryReadsIt(specification: DashboardSpecification): string {
  const written = JSON.stringify(specification);
  if (!parseSpecification(written)) throw new Error(i18n.t('design.toast.notReadBack'));
  return written;
}

export const dashboardPages = {
  /** One fragment, so a page the model refuses leaves nothing behind; as three requests, a refusal after the
   *  first would leave a Thing of no kind that nobody could see to remove. The browser makes the identifier,
   *  because the fragment's own relationship points at the Thing it creates. */
  async keep(name: string, specification: DashboardSpecification, context: DashboardWriteContext): Promise<string> {
    const written = asTheDiscoveryReadsIt(specification);
    const id = crypto.randomUUID();
    await modelApi.applyFragment(JSON.stringify({
      Name: name,
      Things: [{ Id: id, Name: name, Properties: { [DASHBOARD_SPECIFICATION_PROPERTY]: { typeInfo: SPECIFICATION_TYPE, value: written } } }],
      Relationships: [{ Subject: id, Predicate: context.isPredicateId, Target: context.dashboardArchetypeId }],
    }));
    return id;
  },

  /** The Thing keeps its name, so the sidebar's address for it stays. */
  async write(id: string, specification: DashboardSpecification): Promise<void> {
    await thingApi.setProperty(id, DASHBOARD_SPECIFICATION_PROPERTY, SPECIFICATION_TYPE, asTheDiscoveryReadsIt(specification));
  },

  async retitle(id: string, specification: DashboardSpecification, title: string): Promise<void> {
    await dashboardPages.write(id, { ...specification, title });
  },

  /** Relationships first, because a Thing one still names can still be reached through it. */
  async remove(id: string, context: DashboardWriteContext): Promise<void> {
    const onThePage = context.relationships.filter((edge) => edge.SubjectId === id || edge.TargetId === id);
    for (const edge of onThePage) await relationshipApi.remove(edge.Id);
    await thingApi.remove(id);
  },
};
