import type { VosRelationship, VosThing } from '../types/vos';
import { BINDS_PREDICATE_NAME } from './landIntakeRoles';

/** The platform's roles the console reads a model's pages and display settings through. Each identifier is
 *  the standard version 5 identifier of the role's word, so every model declares the same role Thing, and the
 *  Thing it binds may be called anything. */
export const PAGE_ROLE_ID = 'a18768bc-b4e7-513c-864f-842d674c1858';
export const DISPLAY_SETTINGS_ROLE_ID = '1cb6206a-2726-5008-8188-698b299f5665';

/** What a detail card reads a state entry's service call through: the archetype of a service's request to be told
 *  once when one Thing enters one state, the predicate naming the connection such a request is answered through, and
 *  the predicate the platform writes each call record along. */
export const VIGIL_ROLE_ID = 'b1cea8ff-7b7e-584d-9f8e-1e7813f52c7e';
export const NOTIFIED_CONNECTION_ROLE_ID = '8ac302e3-3de9-51db-8960-65f33396838e';
export const CALL_RECORD_ROLE_ID = 'dfe02bba-b36e-56ca-9dc3-58ca35c71940';

/** The Thing a role binds in a loaded set, or undefined where the set holds no binding for it. A role binds
 *  one Thing (the platform refuses a second), so the first binding found is the binding. */
export function thingBoundBy(
  roleId: string,
  things: readonly VosThing[],
  relationships: readonly VosRelationship[],
): VosThing | undefined {
  const binds = things.find((thing) => thing.Name === BINDS_PREDICATE_NAME);
  if (!binds) return undefined;
  const binding = relationships.find((r) => r.SubjectId === roleId && r.PredicateId === binds.Id);
  return binding ? things.find((thing) => thing.Id === binding.TargetId) : undefined;
}
