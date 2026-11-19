import { type EffectiveProperty } from '@tendril-ui/tendril';
import { ROLE, type PipelineModel } from './model';

/** The platform keys an inherited property by the path of archetypes it came through, and refuses a name two
 *  archetypes declare, so such a name has no type to write with. */
export function declaredType(resolved: Record<string, EffectiveProperty>, name: string): string | null {
  if (name in resolved) return resolved[name].Type;
  const inherited = Object.entries(resolved).filter(([path]) => path.endsWith(`.${name}`)).map(([, property]) => property);
  return new Set(inherited.map((property) => property.InheritedFrom)).size === 1 ? inherited[0].Type : null;
}

export const COMPLETES_WHEN = 'completesWhen';

export interface CompletionChoice {
  id: string;
  label: string;
}

export function completionChoices(model: PipelineModel, language: string): CompletionChoice[] {
  return model.thingsOf(ROLE.CallCompletion).map((member) => ({
    id: member.Id,
    label: model.labelOf(member.Id, language),
  }));
}

interface CompletionChoiceFragment {
  Name: string;
  Retractions?: { Id: string }[];
  Relationships: { Subject: string; Predicate: string; Target: string }[];
}

/** The platform refuses a second choice on a Thing, so the connection's own choice is retracted in the fragment
 *  that writes the new one, which the platform applies whole. */
export function completionChoiceFragment(model: PipelineModel, connectionId: string, memberId: string): CompletionChoiceFragment | undefined {
  const predicateId = model.boundTo(ROLE.CompletesWhen);
  const connection = model.thing(connectionId);
  const member = model.thing(memberId);
  if (!predicateId || !connection || !member) return undefined;
  const own = model.relationshipsAndTargetsAlong(connectionId, ROLE.CompletesWhen);
  return {
    Name: `${connection.Name} ${COMPLETES_WHEN} ${member.Name}`,
    ...(own.length > 0 ? { Retractions: own.map((choice) => ({ Id: choice.relationshipId })) } : {}),
    Relationships: [{ Subject: connectionId, Predicate: predicateId, Target: memberId }],
  };
}
