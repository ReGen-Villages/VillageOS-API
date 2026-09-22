/**
 * Which services the platform ran on one Thing, and when.
 *
 * A service is reached through a connection: the predicate of a handled relationship, or the target of the
 * record relationship the platform writes when a Thing enters a watched state. Either way the platform
 * stamps that relationship with the state of the dispatch and the instant of its last attempt, and the
 * connection reaches the service bound to it. So the model itself says who ran and when — nothing
 * here names a service, a predicate or an archetype, only the flags the platform marks its own
 * wiring with.
 *
 * The stamps live in the running platform's memory and are not written down, so a dispatch from
 * before this model was loaded leaves the relationship with no time on it. That is why {@link ServiceDispatch}
 * carries `at` as absent rather than guessing one.
 */
import { type ModelIndex, thingIdsOfArchetype } from '../../../api/dashboardApi';
import type { VosRelationship, VosThing } from '../../../types/vos';

/** How the platform marks the wiring it recognises, whatever this model calls the Things carrying it.
 *  A vigil is a service's request to be told once when one Thing enters one state; its record relationship
 *  targets the vigil, which names the connection to dispatch through. */
const WIRING_FLAGS = {
  connection: '__IsConnectionArchetype',
  service: '__IsServiceArchetype',
  recordPredicate: '__IsDispatchRecordPredicate',
  vigil: '__IsVigilArchetype',
  notifiedConnectionPredicate: '__IsNotifiedConnectionPredicate',
} as const;

type WiringRole = keyof typeof WIRING_FLAGS;

const ROLE_FLAGS = Object.entries(WIRING_FLAGS) as [WiringRole, string][];

/** What the platform stamps onto a relationship it dispatched a service through. */
const DISPATCHED_AT = '__DispatchLastAttemptAt';
const DISPATCH_STATE = '__DispatchState';
const DISPATCH_ERROR = '__DispatchLastError';

/** A relationship on the Thing that a service is dispatched through, and the service it reaches. */
export interface ServiceEdge {
  relationshipId: string;
  /** The service Thing, so the card can open it. Absent where the connection binds none. */
  serviceId?: string;
  /** What to call the service — the connection's own name where it binds none. */
  serviceName: string;
  /** The connection the dispatch went out on: a handled predicate, or a watched state's connection. */
  connectionName: string;
}

/** One dispatch, as the platform currently records it on the relationship. */
export interface ServiceDispatch extends ServiceEdge {
  /** When the platform last dispatched it, or absent where it no longer holds a time. */
  at?: string;
  /** Pending, Inflight, Done, Failed or Refused — the platform's own word, shown as it stands. */
  state?: string;
  /** What a service said when it turned the dispatch down or failed it. */
  error?: string;
}

interface ServiceWiring {
  connectionIds: Set<string>;
  serviceByConnection: Map<string, VosThing>;
  connectionByVigil: Map<string, string>;
  recordPredicateId?: string;
}

/** Held against the index rather than rebuilt per card: it walks every Thing and every relationship, the
 *  model cannot change without a new index, and several open cards ask the same question. */
const wiringByIndex = new WeakMap<ModelIndex, ServiceWiring>();

/**
 * The one Thing owning each flag, and nothing for a flag two Things claim.
 *
 * Read from what a Thing owns, never from what it inherited: marking an archetype hands the mark
 * down to every instance, which would make every connection look like the archetype naming them.
 * Two claimants answer the same as none — taking the first would have this card read a different
 * model than a reader that took the last.
 */
function soleClaimants(modelIndex: ModelIndex): Partial<Record<WiringRole, VosThing>> {
  const found: Partial<Record<WiringRole, VosThing>> = {};
  const contested = new Set<WiringRole>();

  for (const thing of modelIndex.byId.values()) {
    for (const [role, flag] of ROLE_FLAGS) {
      if (thing.Properties[flag] !== true) continue;
      if (found[role]) contested.add(role);
      else found[role] = thing;
    }
  }
  for (const role of contested) delete found[role];

  return found;
}

function serviceWiring(modelIndex: ModelIndex): ServiceWiring {
  const remembered = wiringByIndex.get(modelIndex);
  if (remembered) return remembered;

  const roles = soleClaimants(modelIndex);
  const membersOf = (archetype: VosThing | undefined) =>
    archetype ? thingIdsOfArchetype(archetype.Name, modelIndex) : new Set<string>();
  const connectionIds = membersOf(roles.connection);
  const serviceIds = membersOf(roles.service);
  const vigilIds = membersOf(roles.vigil);

  // A connection binds its service through a relation of the model's own choosing, so the service
  // is found by what the edge reaches — never by what the predicate is called.
  const serviceByConnection = new Map<string, VosThing>();
  const connectionByVigil = new Map<string, string>();
  for (const edge of modelIndex.relationships) {
    if (connectionIds.has(edge.SubjectId) && serviceIds.has(edge.TargetId) && !serviceByConnection.has(edge.SubjectId)) {
      const service = modelIndex.byId.get(edge.TargetId);
      if (service) serviceByConnection.set(edge.SubjectId, service);
    }
    if (vigilIds.has(edge.SubjectId) && edge.PredicateId === roles.notifiedConnectionPredicate?.Id && !connectionByVigil.has(edge.SubjectId)) {
      connectionByVigil.set(edge.SubjectId, edge.TargetId);
    }
  }

  const wiring: ServiceWiring = {
    connectionIds,
    serviceByConnection,
    connectionByVigil,
    recordPredicateId: roles.recordPredicate?.Id,
  };
  wiringByIndex.set(modelIndex, wiring);
  return wiring;
}

/**
 * Every relationship the platform dispatched a service on this Thing through.
 *
 * The subject of the relationship is the Thing the work was done on, and only that end counts. The other
 * end is whatever the work pointed at, and reading it as handled would make every hub in a site
 * claim the whole run: a reservoir at the target end of every reading dispatched from a hundred
 * catchments would list a hundred rows naming one service, while nothing was ever run on it.
 * Work done on a related Thing is reached by opening that Thing's own card.
 */
export function serviceEdgesOn(thingId: string, modelIndex: ModelIndex): ServiceEdge[] {
  const wiring = serviceWiring(modelIndex);
  const edges: ServiceEdge[] = [];

  for (const edge of modelIndex.relationships) {
    if (edge.SubjectId !== thingId) continue;
    // A handled relationship carries its connection as the predicate; a record relationship, written when the Thing
    // entered a watched state, carries it as the target — or a vigil that names it. Either way it
    // has to be a connection: a relationship reaching anything else is somebody's own bookkeeping, and
    // reading it as a dispatch would put a row on the card naming a Thing that never ran.
    const connectionId =
      edge.PredicateId === wiring.recordPredicateId
        ? (wiring.connectionByVigil.get(edge.TargetId) ?? edge.TargetId)
        : edge.PredicateId;
    if (!wiring.connectionIds.has(connectionId)) continue;

    const connectionName = modelIndex.byId.get(connectionId)?.Name ?? connectionId;
    const service = wiring.serviceByConnection.get(connectionId);
    edges.push({
      relationshipId: edge.Id,
      serviceId: service?.Id,
      serviceName: service?.Name ?? connectionName,
      connectionName,
    });
  }

  return edges;
}

function stampedString(properties: Record<string, unknown>, name: string): string | undefined {
  const value = properties[name];
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

/**
 * The dispatches these relationships carry, oldest first, with one the platform holds no time for last —
 * an undated row sorted among the dated ones would claim a sequence nothing measured.
 *
 * `stamped` holds each relationship as it was read back from the platform, keyed by the relationship's id. The
 * stamps are written straight onto the relationship without becoming Facts, so they reach no client that
 * only follows the change stream: read them, or show a card whose dispatches never move off the
 * state they were created in.
 */
export function dispatchesFrom(
  edges: ServiceEdge[],
  stamped: Map<string, VosRelationship>,
): ServiceDispatch[] {
  return edges
    .map((edge) => {
      const properties = stamped.get(edge.relationshipId)?.Properties ?? {};
      return {
        ...edge,
        at: stampedString(properties, DISPATCHED_AT),
        state: stampedString(properties, DISPATCH_STATE),
        error: stampedString(properties, DISPATCH_ERROR),
      };
    })
    .sort((a, b) => {
      if (!a.at) return b.at ? 1 : 0;
      if (!b.at) return -1;
      return new Date(a.at).getTime() - new Date(b.at).getTime();
    });
}
