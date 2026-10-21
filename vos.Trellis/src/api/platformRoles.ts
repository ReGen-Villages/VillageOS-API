import type { VosRelationship, VosThing } from '../types/vos';

/** The platform's roles the console reads a model's pages and display settings through. Each identifier is
 *  the standard version 5 identifier of the role's word, so every model declares the same role Thing, and the
 *  Thing it binds may be called anything. */
export const PAGE_ROLE_ID = 'a18768bc-b4e7-513c-864f-842d674c1858';
export const DISPLAY_SETTINGS_ROLE_ID = '1cb6206a-2726-5008-8188-698b299f5665';
/** The platform's own predicate a role binds its Thing along, matched by name like `is`. */
export const BINDS_PREDICATE = 'binds';

/** The Thing a role binds in a loaded set, or undefined where the set holds no binding for it. A role binds
 *  one Thing (the platform refuses a second), so the first binding found is the binding. */
export function thingBoundBy(
  roleId: string,
  things: readonly VosThing[],
  relationships: readonly VosRelationship[],
): VosThing | undefined {
  const binds = things.find((thing) => thing.Name === BINDS_PREDICATE);
  if (!binds) return undefined;
  const binding = relationships.find((r) => r.SubjectId === roleId && r.PredicateId === binds.Id);
  return binding ? things.find((thing) => thing.Id === binding.TargetId) : undefined;
}
