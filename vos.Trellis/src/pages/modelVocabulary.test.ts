import { describe, it, expect } from 'vitest';
import type { EffectiveProperty, VosRelationship, VosThing } from '../types/vos';
import { ALLOCATION_CATEGORY_ARCHETYPE_FLAG, termsMarked } from './modelVocabulary';
import type { ModelReading } from './submissionReview';

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
const inherited = (Value: unknown): EffectiveProperty => ({ Value, Type: 'vos.Boolean', IsInherited: true });

const THINGS: VosThing[] = [
  thing('is', 'is'),
  thing('land-use', 'LandUse', true),
  thing('housing', 'housing'),
  thing('growing', 'growing'),
  thing('roads', 'roads'),
  thing('unrelated', 'Willow Bend'),
];

const EDGES: VosRelationship[] = [
  edge('e1', 'housing', 'is', 'land-use'),
  edge('e2', 'growing', 'is', 'land-use'),
  edge('e3', 'roads', 'is', 'land-use'),
];

const FLAG = ALLOCATION_CATEGORY_ARCHETYPE_FLAG;

function reading(patch: Partial<ModelReading> = {}): ModelReading {
  return {
    things: THINGS,
    relationships: EDGES,
    properties: { 'land-use': { [FLAG]: owned(true) } },
    ...patch,
  };
}

describe('the terms a model declares under a marked archetype', () => {
  it('offers every term under the archetype carrying the mark, whatever the archetype is called', () => {
    expect(termsMarked(reading(), FLAG)).toEqual(['growing', 'housing', 'roads']);
  });

  it('reads the terms even though each inherits the mark from the archetype it is a kind of', () => {
    const inheritedByEveryTerm = {
      'land-use': { [FLAG]: owned(true) },
      housing: { [FLAG]: inherited(true) },
      growing: { [FLAG]: inherited(true) },
      roads: { [FLAG]: inherited(true) },
    };

    expect(termsMarked(reading({ properties: inheritedByEveryTerm }), FLAG)).toEqual([
      'growing',
      'housing',
      'roads',
    ]);
  });

  it('reaches a term grouped under a type of its own beneath the archetype', () => {
    const things = [...THINGS, thing('built', 'BuiltLand', true), thing('workshops', 'workshops')];
    const relationships = [
      ...EDGES,
      edge('e4', 'built', 'is', 'land-use'),
      edge('e5', 'workshops', 'is', 'built'),
    ];

    expect(termsMarked(reading({ things, relationships }), FLAG)).toEqual([
      'growing',
      'housing',
      'roads',
      'workshops',
    ]);
  });

  it('offers nothing where the model marks no archetype at all', () => {
    expect(termsMarked(reading({ properties: {} }), FLAG)).toEqual([]);
  });

  it('offers nothing where two Things own the mark and neither can be said to hold the vocabulary', () => {
    const twoCarriers = {
      'land-use': { [FLAG]: owned(true) },
      unrelated: { [FLAG]: owned(true) },
    };

    expect(termsMarked(reading({ properties: twoCarriers }), FLAG)).toEqual([]);
  });

  it('leaves out the archetype itself, which is not one of its own terms', () => {
    expect(termsMarked(reading(), FLAG)).not.toContain('LandUse');
  });

  it('lists a term once where two paths reach it', () => {
    const things = [...THINGS, thing('built', 'BuiltLand', true)];
    const relationships = [
      ...EDGES,
      edge('e4', 'built', 'is', 'land-use'),
      edge('e5', 'housing', 'is', 'built'),
    ];

    expect(termsMarked(reading({ things, relationships }), FLAG)).toEqual(['growing', 'housing', 'roads']);
  });

  it('answers a model whose types lead back to each other, rather than walking for ever', () => {
    const things = [...THINGS, thing('built', 'BuiltLand', true)];
    const relationships = [
      ...EDGES,
      edge('e4', 'built', 'is', 'land-use'),
      edge('e5', 'land-use', 'is', 'built'),
    ];

    expect(termsMarked(reading({ things, relationships }), FLAG)).toEqual(['growing', 'housing', 'roads']);
  });

  it('leaves out an edge whose subject the reading holds no Thing for', () => {
    const relationships = [...EDGES, edge('e7', 'gone', 'is', 'land-use')];

    expect(termsMarked(reading({ relationships }), FLAG)).toEqual(['growing', 'housing', 'roads']);
  });

  it('leaves out a Thing reached through some other predicate', () => {
    const relationships = [...EDGES, edge('e6', 'unrelated', 'covers', 'land-use')];

    expect(termsMarked(reading({ relationships }), FLAG)).not.toContain('Willow Bend');
  });
});
