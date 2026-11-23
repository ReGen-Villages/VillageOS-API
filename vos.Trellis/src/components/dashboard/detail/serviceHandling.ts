/**
 * A service is reached through the connection that the predicate of a handled relationship, or the target of the
 * record the platform writes when a Thing enters a watched state, names along `notifies`, and the platform stamps that relationship
 * with the call's state and last attempt. So the model says who ran and when, and nothing here names a service,
 * predicate or archetype, only roles. The stamps live in the platform's memory, so a call from before the
 * model was loaded has no time, and {@link ServiceCall} leaves `at` absent rather than guess one.
 */
import { type ModelIndex, thingIdBoundBy, thingIdsOfArchetype } from '@tendril-ui/tendril';
import { CALL_RECORD_ROLE_ID, NOTIFIED_CONNECTION_ROLE_ID } from '@tendril-ui/tendril';
import { ROLE } from '../../../pipeline/model';
import type { VosRelationship, VosThing } from '@tendril-ui/tendril';
import { textOf } from './propertyText';

const IS_PREDICATE = 'is';

/** What the platform stamps onto a relationship it called a service through. */
const CALLED_AT = '__CallLastAttemptAt';
const CALL_STATE = '__CallState';
const CALL_ERROR = '__CallLastError';

export const CALL_STATES = ['Pending', 'Sent', 'Received', 'Completed', 'Failed', 'Refused', 'Blocked'] as const;

type CallState = (typeof CALL_STATES)[number];

export function isCallState(word: string): word is CallState {
  return (CALL_STATES as readonly string[]).includes(word);
}

/** The platform refuses a retry of a call in any other state. */
export function mayBeRetried(call: ServiceCall): boolean {
  return call.state === 'Failed' || call.state === 'Blocked';
}

/** A relationship on the Thing that a service is called through, and the service it reaches. */
interface ServiceEdge {
  relationshipId: string;
  /** The service Thing, so the card can open it. Absent where the connection binds none. */
  serviceId?: string;
  /** What to call the service — the connection's own name where it binds none. */
  serviceName: string;
  /** The connection the service call went out on, as a handled predicate or a watched state names it. */
  connectionName: string;
}

/** One service call, as the platform currently records it on the relationship. */
export interface ServiceCall extends ServiceEdge {
  /** When the platform last called it, or absent where it no longer holds a time. */
  at?: string;
  /** One of {@link CALL_STATES}, as the platform wrote it. */
  state?: string;
  /** Why the service call failed, was refused or was blocked. */
  error?: string;
}

interface ServiceWiring {
  connectionIds: Set<string>;
  serviceByConnection: Map<string, VosThing>;
  /** The connection a Thing names along `notifies`, for itself and every Thing that `is` it. */
  notifiedConnectionOf: Map<string, string>;
  archetypesOf: Map<string, string[]>;
  recordPredicateId?: string;
}

/** Held against the index rather than rebuilt per card: it walks every Thing and every relationship, the
 *  model cannot change without a new index, and several open cards ask the same question. */
const wiringByIndex = new WeakMap<ModelIndex, ServiceWiring>();

function serviceWiring(modelIndex: ModelIndex): ServiceWiring {
  const remembered = wiringByIndex.get(modelIndex);
  if (remembered) return remembered;

  const bound = (roleId: string) => {
    const id = thingIdBoundBy(roleId, modelIndex);
    return id === undefined ? undefined : modelIndex.byId.get(id);
  };
  const membersOf = (roleId: string) => {
    const archetype = bound(roleId);
    return archetype ? thingIdsOfArchetype(archetype.Name, modelIndex) : new Set<string>();
  };
  const connectionIds = membersOf(ROLE.Connection);
  const serviceIds = membersOf(ROLE.Service);
  const notifiedConnectionPredicateId = bound(NOTIFIED_CONNECTION_ROLE_ID)?.Id;

  // A connection binds its service through a predicate of the model's choosing, so the service is found by what
  // the relationship reaches, never by the predicate's name.
  const serviceByConnection = new Map<string, VosThing>();
  const notifiedConnectionOf = new Map<string, string>();
  const archetypesOf = new Map<string, string[]>();
  for (const edge of modelIndex.relationships) {
    if (connectionIds.has(edge.SubjectId) && serviceIds.has(edge.TargetId) && !serviceByConnection.has(edge.SubjectId)) {
      const service = modelIndex.byId.get(edge.TargetId);
      if (service) serviceByConnection.set(edge.SubjectId, service);
    }
    if (edge.PredicateId === notifiedConnectionPredicateId && !notifiedConnectionOf.has(edge.SubjectId)) {
      notifiedConnectionOf.set(edge.SubjectId, edge.TargetId);
    }
    if (modelIndex.predicateIdToName.get(edge.PredicateId) === IS_PREDICATE) {
      archetypesOf.set(edge.SubjectId, [...(archetypesOf.get(edge.SubjectId) ?? []), edge.TargetId]);
    }
  }

  const wiring: ServiceWiring = {
    connectionIds,
    serviceByConnection,
    notifiedConnectionOf,
    archetypesOf,
    recordPredicateId: bound(CALL_RECORD_ROLE_ID)?.Id,
  };
  wiringByIndex.set(modelIndex, wiring);
  return wiring;
}

/** Resolved as the platform's own ConnectionOf resolves it, so the card names the connection the call went out on. */
function connectionCalledBy(callingId: string, wiring: ServiceWiring): string {
  const seen = new Set<string>();
  let frontier = [callingId];
  while (frontier.length > 0) {
    const next: string[] = [];
    for (const id of frontier) {
      if (seen.has(id)) continue;
      seen.add(id);
      const named = wiring.notifiedConnectionOf.get(id);
      if (named) return named;
      next.push(...(wiring.archetypesOf.get(id) ?? []));
    }
    frontier = next;
  }
  return callingId;
}

/**
 * Only the subject end counts, because reading the other end as handled would make a Thing many relationships
 * point at claim the whole run: a reservoir targeted by readings from a hundred catchments would list a hundred
 * rows while nothing ran on it. Relationships come from `modelIndex` and the wiring from `wiringIndex`, because
 * a dashboard's index holds the Thing's relationships but none of the wiring.
 */
export function serviceEdgesOn(thingId: string, modelIndex: ModelIndex, wiringIndex: ModelIndex = modelIndex): ServiceEdge[] {
  const wiring = serviceWiring(wiringIndex);
  const edges: ServiceEdge[] = [];

  for (const edge of modelIndex.relationships) {
    if (edge.SubjectId !== thingId) continue;
    // A relationship that reaches no connection is somebody's own bookkeeping, and listing it would name a Thing that never ran.
    const connectionId = connectionCalledBy(edge.PredicateId === wiring.recordPredicateId ? edge.TargetId : edge.PredicateId, wiring);
    if (!wiring.connectionIds.has(connectionId)) continue;

    const connectionName = wiringIndex.byId.get(connectionId)?.Name ?? connectionId;
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

/**
 * An undated call sorts last, because among the dated ones it would claim an order nothing measured. The stamps
 * are written onto the relationship without entering the model's history, so no client following only the
 * change stream sees them; `stamped` holds each relationship as read back, keyed by its identifier.
 */
export function serviceCallsFrom(
  edges: ServiceEdge[],
  stamped: Map<string, VosRelationship>,
): ServiceCall[] {
  return edges
    .map((edge) => {
      const properties = stamped.get(edge.relationshipId)?.Properties ?? {};
      return {
        ...edge,
        at: textOf(properties, CALLED_AT),
        state: textOf(properties, CALL_STATE),
        error: textOf(properties, CALL_ERROR),
      };
    })
    .sort((a, b) => {
      if (!a.at) return b.at ? 1 : 0;
      if (!b.at) return -1;
      return new Date(a.at).getTime() - new Date(b.at).getTime();
    });
}
