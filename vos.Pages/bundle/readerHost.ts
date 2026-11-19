import i18next from 'i18next';
import {
  BASE_LANGUAGE,
  BINDS_PREDICATE_NAME,
  IS_PREDICATE,
  NAMES_PROPERTY_OF_PREDICATE,
  NAVIGATION_AND_SETTINGS,
  PROPERTY_NAME_ROLE,
  TENDRIL_NAMESPACE,
  TENDRIL_WORDS,
  asRows,
  buildModelIndex,
  discoverDashboardsFromIndex,
  effectiveDerivedDefinitions,
  effectiveProperties,
  findRange,
  formatNumberIn,
  inputsOf,
  nullableNumber,
  readWidget,
  resolveBinding,
  scopeEntities,
  statedProperties,
  subscriptionForSpecification,
  thingsInStatePath,
  unwrapRelationship,
  unwrapThing,
  valueOrigin,
  workingRuns,
  type DashboardDescriptor,
  type ModelIndex,
  type ModelReads,
  type RangeDto,
  type ResolveContext,
  type SubscriptionSelector,
  type ThingRangesResponse,
  type VosThing,
} from '../../vos.Tendril/src/reader';

/** What the C# side answers: a GET of a path on Mycelium, or a POST of a JSON body to one. The reader
 *  names the route, as the console's own reads do; the C# side decides whether to send it. */
interface Host {
  get(path: string): Promise<string>;
  post(path: string, bodyJson: string): Promise<string>;
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

function thingOf(index: ModelIndex, thingId: string): VosThing {
  const thing = index.byId.get(thingId);
  if (!thing) throw new Error(`The model holds no Thing with the identifier ${thingId}.`);
  return thing;
}

function readsAnsweredBy(host: Host): ModelReads {
  const got = async (path: string): Promise<unknown> => JSON.parse(await host.get(path));
  const posted = async (path: string, body: unknown): Promise<unknown> => JSON.parse(await host.post(path, JSON.stringify(body ?? {})));
  return {
    thingsInState: (state, narrowing) => got(thingsInStatePath(state, narrowing)) as ReturnType<ModelReads['thingsInState']>,
    // A Thing whose ranges cannot be read still reads: a verdict it holds is shown without its target.
    thingRanges: (thingId) => (got(`/api/things/${thingId}/ranges`) as Promise<ThingRangesResponse>).catch(() => null),
    aggregate: (query) => posted('/api/temporal/aggregate', query) as ReturnType<ModelReads['aggregate']>,
    reduce: (query) => posted('/api/temporal/reduce', query) as ReturnType<ModelReads['reduce']>,
    fromService: (endpoint, body) => posted(endpoint, body),
  };
}

function contextFor(index: ModelIndex, scopeId: string | null, language: string, host: Host, compareArchetype?: string): ResolveContext {
  return { index, scopeId, compareArchetype, reads: readsAnsweredBy(host), language, warn: toldOfAWarning };
}

function everyRangeOf(ranges: ThingRangesResponse | null): RangeDto[] {
  if (!ranges) return [];
  const inherited = (sets: ThingRangesResponse['InheritedRanges']): RangeDto[] => sets.flatMap((set) => [...set.Ranges, ...inherited(set.Inherited)]);
  const byName = new Map<string, RangeDto>();
  for (const range of [...ranges.OwnRanges, ...inherited(ranges.InheritedRanges)]) {
    if (!byName.has(range.Name)) byName.set(range.Name, findRange(range.Name, ranges) ?? range);
  }
  return [...byName.values()];
}

/** The ranges that judge one figure of a Thing: the state each names, what it compares the figure
 *  with, and whether the Thing holds that state now. */
function judgementsOf(thing: VosThing, figure: string, ranges: ThingRangesResponse | null) {
  // A selection sends each Thing with the states it holds, which Tendril's type for a Thing does not name.
  const holds = new Set((thing as VosThing & { States?: string[] }).States ?? []);
  return everyRangeOf(ranges)
    .map((range) => ({ range, compared: range.Comparisons?.[0] }))
    .filter(({ compared }) => compared?.PropertyName === figure)
    .map(({ range, compared }) => ({
      state: range.Name,
      comparison: compared!.Operator,
      target: nullableNumber(compared!.Value),
      held: holds.has(range.Name),
    }));
}

/** Which way an input has to go for the figure to leave a state held by sitting on one side of a
 *  target. A state held by any other comparison has no one direction out. */
function directionToLeave(comparison: string, figureMoves: 'with' | 'against'): 'raise' | 'lower' | null {
  const figureMustRise = comparison === '<' || comparison === '<=' ? true : comparison === '>' || comparison === '>=' ? false : null;
  if (figureMustRise === null) return null;
  return (figureMoves === 'with') === figureMustRise ? 'raise' : 'lower';
}

/** Where the Thing's value for a property is held: among its own properties, stated by it under a
 *  name a kind declares, or only inherited from a kind; with what the kinds alone would give it. */
function holdingOf(thing: VosThing, property: string, index: ModelIndex) {
  const own = thing.Properties?.[property] != null;
  const stated = statedProperties(thing)[property] != null;
  const withoutItsOwn = { ...thing, Properties: {}, InheritedOverrides: undefined } as VosThing;
  const inherited = effectiveProperties(withoutItsOwn, index)[property];
  const from = valueOrigin(withoutItsOwn, property, index).assumedFrom;
  return {
    held: own ? 'own' : stated ? 'statedOverInherited' : inherited != null ? 'inheritedOnly' : 'notHeld',
    value: effectiveProperties(thing, index)[property] ?? null,
    ...(inherited != null ? { inheritedValue: inherited, inheritedFrom: from } : {}),
    origin: valueOrigin(thing, property, index).origin,
    derived: effectiveDerivedDefinitions(thing, index)[property] !== undefined,
  };
}

async function inputsRead(index: ModelIndex, thing: VosThing, figure: string | null, language: string, ranges: ThingRangesResponse | null) {
  const found = inputsOf(thing, index, figure ?? undefined);
  return {
    thing: { id: thing.Id, name: thing.Name },
    inputs: found.inputs.map((input) => ({
      figure: input.figure,
      term: input.term,
      ...(input.words ? { name: input.words[language] ?? input.words[language.split('-')[0]] ?? input.words[BASE_LANGUAGE] ?? null } : {}),
      figureMoves: input.figureMoves,
      ...(input.memberKind !== undefined ? { heldByEachMemberOfKind: input.memberKind } : { heldBy: input.heldBy, ...holdingOf(thing, input.term, index) }),
      toLeave: judgementsOf(thing, input.figure, ranges)
        .filter((judged) => judged.held)
        .map((judged) => ({ state: judged.state, direction: directionToLeave(judged.comparison, input.figureMoves) }))
        .filter((leaving) => leaving.direction !== null),
    })),
    leftOut: found.leftOut,
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

  /** What one Thing needs sent for its figures to be explained: the Thing, the kinds it is, and the
   *  Things that word its inputs, with the kinds whose members hold an input where it totals over them. */
  selectorForThing(thingId: string, memberKindsJson: string): string {
    const memberKinds: string[] = JSON.parse(memberKindsJson);
    const selector: SubscriptionSelector = {
      ids: [thingId, PROPERTY_NAME_ROLE],
      boundTypes: [PROPERTY_NAME_ROLE],
      names: [IS_PREDICATE, BINDS_PREDICATE_NAME, NAMES_PROPERTY_OF_PREDICATE, ...memberKinds],
      traverse: [
        { predicate: NAMES_PROPERTY_OF_PREDICATE, direction: 'outgoing', depth: 1 },
        { predicate: BINDS_PREDICATE_NAME, direction: 'outgoing', depth: 1 },
      ],
    };
    return JSON.stringify(selector);
  },

  /** The kinds whose members hold an input of the Thing's figures, which have to be loaded as well
   *  before those inputs can be named in the reader's language. */
  memberKindsOf(key: string, thingId: string): string {
    const index = indexHeldAs(key);
    const kinds = inputsOf(thingOf(index, thingId), index).inputs.flatMap((input) => (input.memberKind ? [input.memberKind] : []));
    return JSON.stringify([...new Set(kinds)]);
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
    const context = contextFor(index, scopeId, language, host, page.specification.compare?.archetype);
    const sections = [];
    for (const section of page.specification.sections) {
      const widgets = [];
      for (const widget of section.widgets) {
        try {
          widgets.push(await readWidget(widget, context));
        } catch (refused) {
          widgets.push({ type: widget.type, ...(widget.title !== undefined ? { title: widget.title } : {}), unreadable: refused instanceof Error ? refused.message : String(refused) });
        }
      }
      sections.push({ ...(section.title !== undefined ? { title: section.title } : {}), widgets });
    }
    return JSON.stringify({ page: page.name, scopeId, language, sections });
  },

  async inputsOfThing(key: string, thingId: string, figure: string | null, language: string, host: Host): Promise<string> {
    const index = indexHeldAs(key);
    const thing = thingOf(index, thingId);
    const ranges = await readsAnsweredBy(host).thingRanges(thingId);
    return JSON.stringify(await inputsRead(index, thing, figure, language, ranges));
  },

  originOf(key: string, thingId: string, property: string): string {
    const index = indexHeldAs(key);
    const thing = thingOf(index, thingId);
    return JSON.stringify({ thing: { id: thing.Id, name: thing.Name }, property, ...holdingOf(thing, property, index), assumedFrom: valueOrigin(thing, property, index).assumedFrom });
  },

  async explain(key: string, thingId: string, figure: string, language: string, host: Host): Promise<string> {
    const index = indexHeldAs(key);
    const thing = thingOf(index, thingId);
    const context = contextFor(index, thingId, language, host);
    const ranges = await context.reads.thingRanges(thingId);
    const worked = asRows(await resolveBinding({ kind: 'working', thing: thingId, property: figure }, context));
    const read = await inputsRead(index, thing, figure, language, ranges);
    return JSON.stringify({
      thing: read.thing,
      figure,
      ...holdingOf(thing, figure, index),
      worded: formatNumberIn(nullableNumber(effectiveProperties(thing, index)[figure]), undefined, language),
      judgedBy: judgementsOf(thing, figure, ranges),
      workedOutFrom: workingRuns(worked, language),
      inputs: read.inputs,
      inputsLeftOut: read.leftOut,
    });
  },
};

(globalThis as { pageReader?: typeof pageReader }).pageReader = pageReader;
