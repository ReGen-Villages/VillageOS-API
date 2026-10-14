import type { VosThing, VosRelationship } from '../types/vos';

const PLAYS = 'plays a role in this fixture';

/** Says, in a fixture's properties, which role a Thing plays. `withRoles` turns it into what a model holds. */
export const playing = (roleId: string): Record<string, unknown> => ({ [PLAYS]: roleId });

/** The fixture as a model holds it: each role a Thing, binding the Thing that plays it along `binds`. */
export function withRoles(things: VosThing[], relationships: VosRelationship[]): [VosThing[], VosRelationship[]] {
  const binds: VosThing = { Id: 'binds', Name: 'binds', Properties: {} };
  const held: VosThing[] = [binds];
  const bindings: VosRelationship[] = [];
  for (const thing of things) {
    const { [PLAYS]: roleId, ...properties } = thing.Properties;
    held.push({ ...thing, Properties: properties });
    if (typeof roleId !== 'string') continue;
    held.push({ Id: roleId, Name: 'a role', Properties: {} });
    bindings.push({ Id: `binding ${thing.Id}`, Name: '', SubjectId: roleId, PredicateId: binds.Id, TargetId: thing.Id, Properties: {} });
  }
  return [held, [...relationships, ...bindings]];
}
