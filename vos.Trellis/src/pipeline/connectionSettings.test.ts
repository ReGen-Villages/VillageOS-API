import { describe, it, expect } from 'vitest';
import { PipelineModel } from './model';
import { completionChoiceFragment, completionChoices, declaredType } from './connectionSettings';
import { connectionSettingsFixture } from './connectionSettings.test.fixture';

describe('declaredType', () => {
  it('reads the type of an own value by its name and of an inherited one by the last part of its path', () => {
    const { resolved } = connectionSettingsFixture();
    const withOwn = { ...resolved, Subdomain: { Value: 'estimate-rainfall', Type: 'vos.String', IsInherited: false } };

    expect(declaredType(withOwn, 'Subdomain')).toBe('vos.String');
    expect(declaredType(withOwn, 'callsAgainAtMost')).toBe('vos.Integer');
    expect(declaredType(withOwn, 'startsAfterSeconds')).toBeNull();
  });

  it('gives one type to a name reached along two paths from one archetype, and none to a name two archetypes declare', () => {
    const { resolved, id } = connectionSettingsFixture();
    const twoPaths = { ...resolved, 'Other.ServiceBinding.callsAgainAtMost': { Value: 5, Type: 'vos.Integer', IsInherited: true, InheritedFrom: id.connectionArchetype } };
    const twoArchetypes = { ...resolved, 'Other.callsAgainAtMost': { Value: '5', Type: 'vos.String', IsInherited: true, InheritedFrom: 'other' } };

    expect(declaredType(twoPaths, 'callsAgainAtMost')).toBe('vos.Integer');
    expect(declaredType(twoArchetypes, 'callsAgainAtMost')).toBeNull();
  });
});

describe('completionChoices', () => {
  it('offers every member of the archetype the role binds, in the page language, and never the archetype itself', () => {
    const { things, relationships, id } = connectionSettingsFixture();

    const choices = completionChoices(new PipelineModel(things, relationships), 'en');

    expect(choices).toEqual([
      { id: id.whenTheServiceAnswers, label: 'When the service answers' },
      { id: id.whenTheThingReachesTheDoneState, label: 'When the Thing reaches its done state' },
      { id: id.whenTheServiceReportsCompletion, label: 'whenTheServiceReportsCompletion' },
    ]);
  });

  it('words a member in the language of the page, and in English where the member has no words in it', () => {
    const { things, relationships, id } = connectionSettingsFixture();

    const choices = completionChoices(new PipelineModel(things, relationships), 'de-DE');

    expect(choices.find((choice) => choice.id === id.whenTheServiceAnswers)?.label).toBe('Wenn der Dienst antwortet');
    expect(choices.find((choice) => choice.id === id.whenTheThingReachesTheDoneState)?.label).toBe('When the Thing reaches its done state');
  });

  it('offers a member the model declares later, with no change to the console', () => {
    const { things, relationships, id } = connectionSettingsFixture();
    things.push({ Id: 'when-a-person-says-so', Name: 'whenAPersonSaysSo', Properties: {} });
    relationships.push({ Id: 'added', Name: '', SubjectId: 'when-a-person-says-so', PredicateId: 'is', TargetId: id.ending, Properties: {} });

    expect(completionChoices(new PipelineModel(things, relationships), 'en').map((choice) => choice.id)).toContain('when-a-person-says-so');
  });
});

describe('completionChoiceFragment', () => {
  it('writes a connection stating no choice its first one, retracting nothing', () => {
    const { things, relationships, id } = connectionSettingsFixture();

    const fragment = completionChoiceFragment(new PipelineModel(things, relationships), id.connection, id.whenTheServiceAnswers);

    expect(fragment).toEqual({
      Name: 'Estimate-Rainfall completesWhen whenTheServiceAnswers',
      Relationships: [{ Subject: id.connection, Predicate: id.completesWhen, Target: id.whenTheServiceAnswers }],
    });
  });

  it('retracts the connection\'s own choice in the same fragment that writes the new one', () => {
    const { things, relationships, id } = connectionSettingsFixture();
    relationships.push({ Id: 'own choice', Name: '', SubjectId: id.connection, PredicateId: id.completesWhen, TargetId: id.whenTheServiceAnswers, Properties: {} });

    const fragment = completionChoiceFragment(new PipelineModel(things, relationships), id.connection, id.whenTheThingReachesTheDoneState);

    expect(fragment).toEqual({
      Name: 'Estimate-Rainfall completesWhen whenTheThingReachesTheDoneState',
      Retractions: [{ Id: 'own choice' }],
      Relationships: [{ Subject: id.connection, Predicate: id.completesWhen, Target: id.whenTheThingReachesTheDoneState }],
    });
  });

  it('leaves the service\'s and the archetypes\' choices alone', () => {
    const { things, relationships, id } = connectionSettingsFixture();

    const fragment = completionChoiceFragment(new PipelineModel(things, relationships), id.connection, id.whenTheServiceAnswers);

    expect(fragment?.Retractions).toBeUndefined();
  });

  it('writes nothing where the model binds no predicate to the role', () => {
    const { things, relationships, id } = connectionSettingsFixture();
    const unbound = relationships.filter((relationship) => relationship.TargetId !== id.completesWhen || relationship.PredicateId !== 'binds');

    expect(completionChoiceFragment(new PipelineModel(things, unbound), id.connection, id.whenTheServiceAnswers)).toBeUndefined();
  });
});
