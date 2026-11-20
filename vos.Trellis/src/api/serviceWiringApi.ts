import { apiClient, ApiError } from './client';
import { buildModelIndex, type ModelIndex } from '@tendril-ui/tendril';
import { BINDS_PREDICATE_NAME } from '@tendril-ui/tendril';
import { AWAITED_THING_ROLE_ID, CALL_RECORD_ROLE_ID, NOTIFIED_CONNECTION_ROLE_ID } from '@tendril-ui/tendril';
import { ROLE } from '../pipeline/model';
import { IS_PREDICATE } from '@tendril-ui/tendril';
import type { SubscriptionSelector } from '../types/subscription';

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';

/** A dashboard's subscription carries only what its widgets draw, so a detail card asks for this itself. The
 *  Things waiting for this one come with what they are, because a waiting Thing's archetype can name the
 *  connection it is answered through. */
export function serviceWiringOf(thingId: string): SubscriptionSelector {
  return {
    ids: [thingId, ROLE.Connection, ROLE.Service, AWAITED_THING_ROLE_ID, NOTIFIED_CONNECTION_ROLE_ID, CALL_RECORD_ROLE_ID],
    boundTypes: [ROLE.Connection, ROLE.Service],
    names: [IS_PREDICATE, BINDS_PREDICATE_NAME],
    traverse: [
      { predicate: BINDS_PREDICATE_NAME, direction: 'outgoing', depth: 1 },
      { predicateRole: AWAITED_THING_ROLE_ID, direction: 'incoming', depth: 1 },
    ],
  };
}

/** Through the route that registers no subscription, because the card reads it once per refresh. */
export async function fetchServiceWiring(thingId: string, signal?: AbortSignal): Promise<ModelIndex> {
  const token = await apiClient.ensureToken();
  const response = await fetch(`${BASE_URL}/api/snapshots`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    credentials: 'include',
    body: JSON.stringify(serviceWiringOf(thingId)),
    signal,
  });
  if (!response.ok) throw new ApiError(response.status, await response.text());

  const { snapshot } = await response.json();
  return buildModelIndex(snapshot.things, snapshot.relationships);
}
