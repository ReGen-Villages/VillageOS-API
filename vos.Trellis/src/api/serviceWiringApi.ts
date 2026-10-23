import { apiClient, ApiError } from './client';
import { buildModelIndex, type ModelIndex } from './dashboardApi';
import { BINDS_PREDICATE_NAME } from './landIntakeRoles';
import { DISPATCH_RECORD_ROLE_ID, NOTIFIED_CONNECTION_ROLE_ID, VIGIL_ROLE_ID } from './platformRoles';
import { ROLE } from '../pipeline/model';
import { IS_PREDICATE } from '../types/dashboard';
import type { SubscriptionSelector } from '../types/subscription';

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';

/**
 * What a detail card reads a Thing's dispatches through: the connection, service and vigil archetypes with their
 * members, and the predicates the vigil and dispatch-record roles bind. A dashboard's own subscription carries
 * only what its widgets draw, so the card asks for this itself when it opens.
 */
export const SERVICE_WIRING: SubscriptionSelector = {
  ids: [ROLE.Connection, ROLE.Service, VIGIL_ROLE_ID, NOTIFIED_CONNECTION_ROLE_ID, DISPATCH_RECORD_ROLE_ID],
  boundTypes: [ROLE.Connection, ROLE.Service, VIGIL_ROLE_ID],
  names: [IS_PREDICATE, BINDS_PREDICATE_NAME],
  traverse: [{ predicate: BINDS_PREDICATE_NAME, direction: 'outgoing', depth: 1 }],
};

/** The service wiring as the platform holds it now. The subscription is handed back at once, because the card
 *  reads it once a round and nothing would ever expire it. */
export async function fetchServiceWiring(signal?: AbortSignal): Promise<ModelIndex> {
  const token = await apiClient.ensureToken();
  const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` };
  const response = await fetch(`${BASE_URL}/api/subscriptions`, {
    method: 'POST',
    headers,
    credentials: 'include',
    body: JSON.stringify({ ...SERVICE_WIRING, includeSnapshot: true }),
    signal,
  });
  if (!response.ok) throw new ApiError(response.status, await response.text());

  const { subscriptionId, snapshot } = await response.json();
  void fetch(`${BASE_URL}/api/subscriptions/${subscriptionId}`, { method: 'DELETE', headers, credentials: 'include' })
    .catch(() => {});
  return buildModelIndex(snapshot.things, snapshot.relationships);
}
