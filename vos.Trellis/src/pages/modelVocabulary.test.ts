import { describe, it, expect } from 'vitest';
import type { EffectiveProperty, VosRelationship, VosThing } from '@tendril-ui/tendril';
import { LAND_INTAKE_ROLE } from '../api/landIntakeRoles';
import {
  ALLOCATION_CATEGORY_ROLE,
  areaMethodConfigured,
  boundarySourcesBound,
  boundBy,
  termsBound,
  type ModelReading,
} from './modelVocabulary';

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

const namesBound = (read: ModelReading, roleId: string): string[] =>
  termsBound(read, roleId).map((term) => term.name);

describe('the terms a model declares under the archetype a role binds', () => {
  it('offers every term under the archetype the role binds, whatever the archetype is called', () => {
    expect(namesBound(reading(), ROLE)).toEqual(['growing', 'housing', 'roads']);
  });

  it('reads the terms whatever properties the archetype and its terms carry', () => {
    const carried = {
      'land-use': { stale: owned(true) },
      housing: { stale: owned(true) },
    };

    expect(namesBound(reading({ properties: carried }), ROLE)).toEqual(['growing', 'housing', 'roads']);
  });

  it('reaches a term grouped under a type of its own beneath the archetype', () => {
    const things = [...THINGS, thing('built', 'BuiltLand', true), thing('workshops', 'workshops')];
    const relationships = [
      ...EDGES,
      edge('e4', 'built', 'is', 'land-use'),
      edge('e5', 'workshops', 'is', 'built'),
    ];

    expect(namesBound(reading({ things, relationships }), ROLE)).toEqual([
      'growing',
      'housing',
      'roads',
      'workshops',
    ]);
  });

  it('offers nothing where the model binds the role to nothing', () => {
    expect(namesBound(reading({ relationships: EDGES.filter((one) => one.Id !== 'b1') }), ROLE)).toEqual([]);
  });

  it('offers nothing where the role binds two Things and neither can be said to hold the vocabulary', () => {
    const relationships = [...EDGES, edge('b2', ROLE, 'binds', 'unrelated')];

    expect(namesBound(reading({ relationships }), ROLE)).toEqual([]);
  });

  it('offers nothing where the model holds no binds predicate to read the binding through', () => {
    const things = THINGS.filter((one) => one.Id !== 'binds');

    expect(namesBound(reading({ things }), ROLE)).toEqual([]);
  });

  it('leaves out the archetype itself, which is not one of its own terms', () => {
    expect(namesBound(reading(), ROLE)).not.toContain('LandUse');
  });

  it('lists a term once where two paths reach it', () => {
    const things = [...THINGS, thing('built', 'BuiltLand', true)];
    const relationships = [
      ...EDGES,
      edge('e4', 'built', 'is', 'land-use'),
      edge('e5', 'housing', 'is', 'built'),
    ];

    expect(namesBound(reading({ things, relationships }), ROLE)).toEqual(['growing', 'housing', 'roads']);
  });

  it('answers a model whose types lead back to each other, rather than walking for ever', () => {
    const things = [...THINGS, thing('built', 'BuiltLand', true)];
    const relationships = [
      ...EDGES,
      edge('e4', 'built', 'is', 'land-use'),
      edge('e5', 'land-use', 'is', 'built'),
    ];

    expect(namesBound(reading({ things, relationships }), ROLE)).toEqual(['growing', 'housing', 'roads']);
  });

  it('leaves out an edge whose subject the reading holds no Thing for', () => {
    const relationships = [...EDGES, edge('e7', 'gone', 'is', 'land-use')];

    expect(namesBound(reading({ relationships }), ROLE)).toEqual(['growing', 'housing', 'roads']);
  });

  it('leaves out a Thing reached through some other predicate', () => {
    const relationships = [...EDGES, edge('e6', 'unrelated', 'covers', 'land-use')];

    expect(namesBound(reading({ relationships }), ROLE)).not.toContain('Willow Bend');
  });

  it('offers each term with its identifier, which is what a submission sends', () => {
    const things = [...THINGS, thing('term-7', 'workshops')];
    const relationships = [...EDGES, edge('e4', 'term-7', 'is', 'land-use')];

    expect(termsBound(reading({ things, relationships }), ROLE)).toContainEqual({ id: 'term-7', name: 'workshops' });
  });
});

describe('the boundary source each way of drawing a boundary is bound to', () => {
  const DRAWN = LAND_INTAKE_ROLE.BoundaryDrawnByHand;
  const IMPORTED = LAND_INTAKE_ROLE.BoundaryImportedFromFile;
  const SOURCES: VosThing[] = [
    thing('binds', 'binds'),
    thing(DRAWN, 'boundaryDrawnByHand'),
    thing(IMPORTED, 'boundaryImportedFromFile'),
    thing('source-1', 'DrawnByHand'),
    thing('source-2', 'ImportedFromFile'),
    thing('source-3', 'FetchedFromRegister'),
  ];

  it('gives each way the identifier of the source its role binds', () => {
    const relationships = [edge('b1', DRAWN, 'binds', 'source-1'), edge('b2', IMPORTED, 'binds', 'source-2')];

    expect(boundarySourcesBound(reading({ things: SOURCES, relationships }))).toEqual({
      drawn: 'source-1',
      imported: 'source-2',
    });
  });

  it('gives no source to a way whose role binds two', () => {
    const relationships = [edge('b1', DRAWN, 'binds', 'source-1'), edge('b2', DRAWN, 'binds', 'source-3')];

    expect(boundarySourcesBound(reading({ things: SOURCES, relationships }))).toEqual({});
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

describe('how the model measures a border', () => {
  const methodThings: VosThing[] = [
    thing('binds', 'binds'),
    thing(LAND_INTAKE_ROLE.Parcel, 'parcel'),
    thing(LAND_INTAKE_ROLE.MeasuresAreaWith, 'measuresAreaWith'),
    thing(LAND_INTAKE_ROLE.MeanSphereAreaMethod, 'meanSphereAreaMethod'),
    thing(LAND_INTAKE_ROLE.Wgs84EllipsoidAreaMethod, 'wgs84EllipsoidAreaMethod'),
    thing('plot', 'Plot', true),
    thing('chooses', 'choosesMeasure'),
    thing('round', 'Round earth'),
    thing('oblate', 'Flattened earth'),
  ];
  const bindings: VosRelationship[] = [
    edge('m1', LAND_INTAKE_ROLE.Parcel, 'binds', 'plot'),
    edge('m2', LAND_INTAKE_ROLE.MeasuresAreaWith, 'binds', 'chooses'),
    edge('m3', LAND_INTAKE_ROLE.MeanSphereAreaMethod, 'binds', 'round'),
    edge('m4', LAND_INTAKE_ROLE.Wgs84EllipsoidAreaMethod, 'binds', 'oblate'),
  ];
  const measuring = (...choices: string[]): ModelReading => ({
    things: methodThings,
    relationships: [...bindings, ...choices.map((target, at) => edge(`c${at}`, 'plot', 'chooses', target))],
    properties: {},
  });

  it.each([
    ['round', 'meanSphereAreaMethod'],
    ['oblate', 'wgs84EllipsoidAreaMethod'],
  ])('reads the method the parcel archetype measures with, %s, through the roles', (chosen, method) => {
    expect(areaMethodConfigured(measuring(chosen))).toBe(method);
  });

  it('reads no method where the parcel archetype chooses none', () => {
    expect(areaMethodConfigured(measuring())).toBeNull();
  });

  it('reads no method where the parcel archetype chooses two', () => {
    expect(areaMethodConfigured(measuring('round', 'oblate'))).toBeNull();
  });

  it('reads no method where the chosen Thing is no method a role binds', () => {
    expect(areaMethodConfigured(measuring('plot'))).toBeNull();
  });
});
