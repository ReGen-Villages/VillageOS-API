import { describe, it, expect } from 'vitest';
import type { EffectiveProperty, VosRelationship, VosThing } from '../types/vos';
import { ALLOCATION_CATEGORY_ROLE, boundBy, termsBound, type ModelReading } from './modelVocabulary';

// Every name here is spelled differently from the shipped land-intake template, so a reader answering
// only to that spelling fails rather than passes.
const thing = (Id: string, Name: string, IsArchetype = false): VosThing => ({
  Id,
  Name,
  IsArchetype,
  Properties: {},
});

const edge = (Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship => ({
  Id,
  SubjectId,
  PredicateId,
  TargetId,
  Properties: {},
});

const owned = (Value: unknown): EffectiveProperty => ({ Value, Type: 'vos.Boolean', IsInherited: false });

const ROLE = ALLOCATION_CATEGORY_ROLE;

const THINGS: VosThing[] = [
  thing('is', 'is'),
  thing('binds', 'binds'),
  thing(ROLE, 'allocationCategory'),
  thing('land-use', 'LandUse', true),
  thing('housing', 'housing'),
  thing('growing', 'growing'),
  thing('roads', 'roads'),
  thing('unrelated', 'Willow Bend'),
];

const EDGES: VosRelationship[] = [
  edge('b1', ROLE, 'binds', 'land-use'),
  edge('e1', 'housing', 'is', 'land-use'),
  edge('e2', 'growing', 'is', 'land-use'),
  edge('e3', 'roads', 'is', 'land-use'),
];

function reading(patch: Partial<ModelReading> = {}): ModelReading {
  return {
    things: THINGS,
    relationships: EDGES,
    properties: {},
    ...patch,
  };
}

describe('the terms a model declares under the archetype a role binds', () => {
  it('offers every term under the archetype the role binds, whatever the archetype is called', () => {
    expect(termsBound(reading(), ROLE)).toEqual(['growing', 'housing', 'roads']);
  });

  it('reads the terms whatever properties the archetype and its terms carry', () => {
    const carried = {
      'land-use': { stale: owned(true) },
      housing: { stale: owned(true) },
    };

    expect(termsBound(reading({ properties: carried }), ROLE)).toEqual(['growing', 'housing', 'roads']);
  });

  it('reaches a term grouped under a type of its own beneath the archetype', () => {
    const things = [...THINGS, thing('built', 'BuiltLand', true), thing('workshops', 'workshops')];
    const relationships = [
      ...EDGES,
      edge('e4', 'built', 'is', 'land-use'),
      edge('e5', 'workshops', 'is', 'built'),
    ];

    expect(termsBound(reading({ things, relationships }), ROLE)).toEqual([
      'growing',
      'housing',
      'roads',
      'workshops',
    ]);
  });

  it('offers nothing where the model binds the role to nothing', () => {
    expect(termsBound(reading({ relationships: EDGES.filter((one) => one.Id !== 'b1') }), ROLE)).toEqual([]);
  });

  it('offers nothing where the role binds two Things and neither can be said to hold the vocabulary', () => {
    const relationships = [...EDGES, edge('b2', ROLE, 'binds', 'unrelated')];

    expect(termsBound(reading({ relationships }), ROLE)).toEqual([]);
  });

  it('offers nothing where the model holds no binds predicate to read the binding through', () => {
    const things = THINGS.filter((one) => one.Id !== 'binds');

    expect(termsBound(reading({ things }), ROLE)).toEqual([]);
  });

  it('leaves out the archetype itself, which is not one of its own terms', () => {
    expect(termsBound(reading(), ROLE)).not.toContain('LandUse');
  });

  it('lists a term once where two paths reach it', () => {
    const things = [...THINGS, thing('built', 'BuiltLand', true)];
    const relationships = [
      ...EDGES,
      edge('e4', 'built', 'is', 'land-use'),
      edge('e5', 'housing', 'is', 'built'),
    ];

    expect(termsBound(reading({ things, relationships }), ROLE)).toEqual(['growing', 'housing', 'roads']);
  });

  it('answers a model whose types lead back to each other, rather than walking for ever', () => {
    const things = [...THINGS, thing('built', 'BuiltLand', true)];
    const relationships = [
      ...EDGES,
      edge('e4', 'built', 'is', 'land-use'),
      edge('e5', 'land-use', 'is', 'built'),
    ];

    expect(termsBound(reading({ things, relationships }), ROLE)).toEqual(['growing', 'housing', 'roads']);
  });

  it('leaves out an edge whose subject the reading holds no Thing for', () => {
    const relationships = [...EDGES, edge('e7', 'gone', 'is', 'land-use')];

    expect(termsBound(reading({ relationships }), ROLE)).toEqual(['growing', 'housing', 'roads']);
  });

  it('leaves out a Thing reached through some other predicate', () => {
    const relationships = [...EDGES, edge('e6', 'unrelated', 'covers', 'land-use')];

    expect(termsBound(reading({ relationships }), ROLE)).not.toContain('Willow Bend');
  });
});

describe('what a role binds', () => {
  it('is read off the role Thing\'s own binds relationship', () => {
    expect(boundBy(reading(), ROLE)).toBe('land-use');
  });

  it('is nothing where the role reaches a Thing through another predicate', () => {
    const relationships = [edge('x1', ROLE, 'is', 'land-use')];

    expect(boundBy(reading({ relationships }), ROLE)).toBeNull();
  });
});
