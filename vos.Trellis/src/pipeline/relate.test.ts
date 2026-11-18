import { describe, it, expect } from 'vitest';
import type { VosThing, VosRelationship } from '@tendril-ui/tendril';
import { PipelineModel, ROLE } from './model';
import { relateChoicesOf, relateRefusalReason, relatingLine } from './relate';
import { playing, withRoles } from './roles.test.fixture';

// The cases follow the outcomes of RelatingOf in vos.Service.Phloem/Model/PipelineDagBuilder.cs one for one.
function relateModel(edit: (things: VosThing[], relationships: VosRelationship[]) => void = () => {}): PipelineModel {
  const things: VosThing[] = [];
  const relationships: VosRelationship[] = [];
  let n = 0;
  const T = (id: string, name: string, props: Record<string, unknown> = {}, archetype = false) =>
    things.push({ Id: id, Name: name, Properties: props, ...(archetype ? { IsArchetype: true } : {}) });
  const R = (s: string, p: string, t: string) => relationships.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: {} });
  T('is', 'is'); T('has', 'has');
  T('CONNECTION', 'PlatformServiceConnection', playing(ROLE.Connection), true);
  T('SERVICE', 'Service', playing(ROLE.Service), true);
  T('notifies', 'notifies', playing(ROLE.NotifiedConnection));
  T('provenBy', 'provenBy', playing(ROLE.CompletionRange));
  T('worksOutService', 'works out service'); R('worksOutService', 'is', 'SERVICE');
  T('worksOutConnection', 'worksOut connection'); R('worksOutConnection', 'is', 'CONNECTION'); R('worksOutConnection', 'has', 'worksOutService');
  T('WORKED_OUT', 'Worked out'); R('worksOutConnection', 'provenBy', 'WORKED_OUT');
  T('worksOut', 'worksOut', { wording: JSON.stringify({ en: 'works out', de: 'rechnet aus' }) }); R('worksOut', 'notifies', 'worksOutConnection');
  T('plain', 'sits beside');
  T('Chore', 'Chore', {}, true); R('Chore', 'notifies', 'worksOutConnection');
  edit(things, relationships);
  return new PipelineModel(...withRoles(things, relationships));
}

describe('what a relate box may stand for', () => {
  it('accepts a predicate naming a connection that binds a service and is proven by one state, and reads both', () => {
    expect(relateModel().relatingOf('worksOut')).toEqual({
      accepted: true, connectionId: 'worksOutConnection', serviceId: 'worksOutService', stateId: 'WORKED_OUT',
    });
  });

  it('refuses a predicate that names no connection along the role', () => {
    expect(relateModel().relatingOf('plain')).toEqual({ accepted: false, refusal: 'namesNoConnection' });
  });

  it('refuses a connection standing for itself, even one naming another', () => {
    expect(relateModel().relatingOf('worksOutConnection')).toEqual({ accepted: false, refusal: 'standsForAConnection' });
  });

  it('refuses a predicate whose connection binds no service', () => {
    const model = relateModel((_, relationships) => relationships.splice(relationships.findIndex((r) => r.SubjectId === 'worksOutConnection' && r.TargetId === 'worksOutService'), 1));
    expect(model.relatingOf('worksOut')).toEqual({ accepted: false, refusal: 'connectionBindsNoService', connectionId: 'worksOutConnection' });
  });

  it('refuses a predicate whose connection names no state it is proven by', () => {
    const model = relateModel((_, relationships) => relationships.splice(relationships.findIndex((r) => r.TargetId === 'WORKED_OUT'), 1));
    expect(model.relatingOf('worksOut')).toEqual({ accepted: false, refusal: 'connectionNamesNoState', connectionId: 'worksOutConnection' });
  });

  it('refuses a predicate whose connection names two states it is proven by', () => {
    const model = relateModel((things, relationships) => {
      things.push({ Id: 'AGAIN', Name: 'Worked out again', Properties: {} });
      relationships.push({ Id: 'again', Name: '', SubjectId: 'worksOutConnection', PredicateId: 'provenBy', TargetId: 'AGAIN', Properties: {} });
    });
    expect(model.relatingOf('worksOut')).toEqual({ accepted: false, refusal: 'connectionNamesTwoStates', connectionId: 'worksOutConnection' });
  });

  it('offers only the predicates it accepts, never an archetype naming a connection', () => {
    expect(relateModel().relatablePredicates().map((predicate) => predicate.Id)).toEqual(['worksOut']);
  });

  it('offers nothing when no predicate is accepted', () => {
    const model = relateModel((_, relationships) => relationships.splice(relationships.findIndex((r) => r.TargetId === 'WORKED_OUT'), 1));
    expect(model.relatablePredicates()).toEqual([]);
  });
});

describe('why a relate box would be refused, in the page language', () => {
  it.each([
    ['plain', () => relateModel(), "'sits beside' names no connection along 'notifies', so a relationship along it calls nothing"],
    ['worksOutConnection', () => relateModel(),
      "'worksOut connection' is a connection: a relate box stands for the predicate that names a connection along 'notifies'"],
    ['worksOut', () => relateModel((_, rs) => rs.splice(rs.findIndex((r) => r.SubjectId === 'worksOutConnection' && r.TargetId === 'worksOutService'), 1)),
      "'works out' names 'worksOut connection', which binds no service, so nothing would be called"],
    ['worksOut', () => relateModel((_, rs) => rs.splice(rs.findIndex((r) => r.TargetId === 'WORKED_OUT'), 1)),
      "'works out' names 'worksOut connection', which names no state its work is proven by, so the run could not tell when the work is done"],
  ] as const)('gives the orchestrator\'s reason for %s', (thingId, build, reason) => {
    expect(relateRefusalReason(build(), thingId, 'en')).toBe(reason);
  });

  it('gives no reason for a predicate the orchestrator accepts', () => {
    expect(relateRefusalReason(relateModel(), 'worksOut', 'en')).toBeUndefined();
  });

  it('names the predicate along the role in the model\'s own word', () => {
    const model = relateModel((things) => { things.find((thing) => thing.Id === 'notifies')!.Name = 'calls on'; });
    expect(relateRefusalReason(model, 'plain', 'en')).toBe("'sits beside' names no connection along 'calls on', so a relationship along it calls nothing");
  });
});

describe('what the inspector offers a relate box', () => {
  it('lists the accepted predicates by their name in the page language', () => {
    expect(relateChoicesOf(relateModel(), 'de').predicates).toEqual([{ id: 'worksOut', name: 'rechnet aus' }]);
  });

  it('reads an accepted predicate\'s state and service, and a refused Thing\'s reason', () => {
    const choices = relateChoicesOf(relateModel(), 'en');
    expect(choices.readingOf('worksOut')).toEqual({ name: 'works out', state: 'Worked out', service: 'works out service' });
    expect(choices.readingOf('worksOutConnection')).toEqual({
      name: 'worksOut connection', refusal: relateRefusalReason(relateModel(), 'worksOutConnection', 'en'),
    });
  });
});

describe('what a relate box says on the canvas', () => {
  it('names the predicate it relates along and the state it then waits for, in the page language', () => {
    expect(relatingLine(relateModel(), 'worksOut', 'de')).toBe('verknüpft über rechnet aus, wartet dann auf Worked out');
  });

  it('gives the orchestrator\'s reason for a box it would refuse, as loaded from an older drawing', () => {
    expect(relatingLine(relateModel(), 'worksOutConnection', 'en')).toBe(relateRefusalReason(relateModel(), 'worksOutConnection', 'en'));
  });

  it('says a box standing for nothing relates along nothing yet', () => {
    expect(relatingLine(relateModel(), undefined, 'en')).toBe('relates along no predicate yet');
  });
});

describe('a Thing\'s name in the page language', () => {
  it('is its wording in that language, the base language\'s where it has none for that one, and its own name where it has no wording', () => {
    const model = relateModel();
    expect(model.labelOf('worksOut', 'de')).toBe('rechnet aus');
    expect(model.labelOf('worksOut', 'fr')).toBe('works out');
    expect(model.labelOf('WORKED_OUT', 'de')).toBe('Worked out');
  });
});
