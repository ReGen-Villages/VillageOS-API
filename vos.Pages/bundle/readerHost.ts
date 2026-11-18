import i18next from 'i18next';
import {
  BASE_LANGUAGE,
  NAVIGATION_AND_SETTINGS,
  TENDRIL_NAMESPACE,
  TENDRIL_WORDS,
  buildModelIndex,
  discoverDashboardsFromIndex,
  resolveBinding,
  scopeEntities,
  slotsOf,
  subscriptionForSpecification,
  unwrapRelationship,
  unwrapThing,
  type DashboardDescriptor,
  type ModelIndex,
  type ModelReads,
} from '../../vos.Tendril/src/reader';

/** What the C# side answers: one question by the name of the read and its arguments as JSON. */
interface Host {
  read(method: string, argumentsJson: string): Promise<string>;
}

void i18next.init({
  lng: BASE_LANGUAGE,
  initAsync: false,
  resources: Object.fromEntries(Object.entries(TENDRIL_WORDS).map(([language, words]) => [language, { [TENDRIL_NAMESPACE]: words }])),
});

const held = new Map<string, ModelIndex>();
let toldOfAWarning: ((warning: string) => void) | undefined;

function indexHeldAs(key: string): ModelIndex {
  const index = held.get(key);
  if (!index) throw new Error(`No Things are held as "${key}".`);
  return index;
}

function pageOf(index: ModelIndex, pageId: string): DashboardDescriptor {
  const page = discoverDashboardsFromIndex(index).find((candidate) => candidate.id === pageId);
  if (!page) throw new Error(`The Things held include no page with the identifier ${pageId}.`);
  return page;
}

function readsAnsweredBy(host: Host): ModelReads {
  const ask = async (method: string, ...given: unknown[]): Promise<unknown> => JSON.parse(await host.read(method, JSON.stringify(given)));
  return {
    thingsInState: (state, narrowing) => ask('thingsInState', state, narrowing ?? null) as ReturnType<ModelReads['thingsInState']>,
    thingRanges: (thingId) => ask('thingRanges', thingId) as ReturnType<ModelReads['thingRanges']>,
    aggregate: (query) => ask('aggregate', query) as ReturnType<ModelReads['aggregate']>,
    reduce: (query) => ask('reduce', query) as ReturnType<ModelReads['reduce']>,
    fromService: (endpoint, body) => ask('fromService', endpoint, body ?? null),
  };
}

const pageReader = {
  tellWarningsTo(told: (warning: string) => void): void {
    toldOfAWarning = told;
  },

  navigationSelector(): string {
    return JSON.stringify(NAVIGATION_AND_SETTINGS);
  },

  hold(key: string, thingsJson: string, relationshipsJson: string): void {
    held.set(key, buildModelIndex(JSON.parse(thingsJson).map(unwrapThing), JSON.parse(relationshipsJson).map(unwrapRelationship)));
  },

  release(key: string): void {
    held.delete(key);
  },

  listPages(key: string, language: string): string {
    return JSON.stringify(discoverDashboardsFromIndex(indexHeldAs(key), language).map((page) => ({
      id: page.id,
      name: page.name,
      address: page.routeKey,
      readable: page.specification !== null,
    })));
  },

  /** What one page needs sent, to be read for no scope and for each of the given ones. Nothing is
   *  asked to be kept up to date: the Things are read once and let go. */
  selectorFor(key: string, pageId: string, scopeIdsJson: string): string {
    const page = pageOf(indexHeldAs(key), pageId);
    if (!page.specification) throw new Error(`The description of the page "${page.name}" could not be read.`);
    const selector = subscriptionForSpecification(page.specification, null);
    const scopeIds: string[] = JSON.parse(scopeIdsJson);
    return JSON.stringify({ ...selector, ids: [...new Set([...(selector.ids ?? []), ...scopeIds])], includeLaterMatches: false, includeObservations: false });
  },

  scopesOf(key: string, pageId: string, language: string): string {
    const index = indexHeldAs(key);
    const page = pageOf(index, pageId);
    return JSON.stringify(page.specification ? scopeEntities(page.specification, index, language) : []);
  },

  async readPage(key: string, pageId: string, scopeId: string | null, language: string, host: Host): Promise<string> {
    const index = indexHeldAs(key);
    const page = pageOf(index, pageId);
    if (!page.specification) throw new Error(`The description of the page "${page.name}" could not be read.`);
    const context = {
      index,
      scopeId,
      compareArchetype: page.specification.compare?.archetype,
      reads: readsAnsweredBy(host),
      language,
      warn: toldOfAWarning,
    };
    const sections = [];
    for (const section of page.specification.sections) {
      const widgets = [];
      for (const widget of section.widgets) {
        const bindings = [];
        for (const binding of slotsOf(widget)) {
          try {
            bindings.push({ kind: binding.kind, result: await resolveBinding(binding, context) });
          } catch (refused) {
            bindings.push({ kind: binding.kind, unreadable: refused instanceof Error ? refused.message : String(refused) });
          }
        }
        widgets.push({ type: widget.type, title: widget.title ?? null, bindings });
      }
      sections.push({ title: section.title ?? null, widgets });
    }
    return JSON.stringify({ page: page.name, scopeId, language, sections });
  },
};

(globalThis as { pageReader?: typeof pageReader }).pageReader = pageReader;
