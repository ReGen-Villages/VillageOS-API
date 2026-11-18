import { describe, it, expect } from 'vitest';
import type { DerivedDefinition, VosRelationship, VosThing } from '../types/vos';
import { buildModelIndex, inputsOf } from './dashboardApi';

const reservoir = (definitions: Record<string, DerivedDefinition>): VosThing =>
  ({ Id: 'r1', Name: 'Spring reservoir', IsArchetype: false, Properties: {}, DerivedProperties: definitions }) as unknown as VosThing;

const read = (definitions: Record<string, DerivedDefinition>, figure?: string) => {
  const thing = reservoir(definitions);
  return inputsOf(thing, buildModelIndex([thing], [] as VosRelationship[]), figure);
};

describe('the inputs at the bottom of a Thing’s derived figures', () => {
  it('names each input a formula reads, its holder, and which way the figure moves with it', () => {
    const found = read({
      daysOfSupply: { Expression: 'storedLitres / drawnLitresPerDay', Reads: ['storedLitres', 'drawnLitresPerDay'], RisesWith: ['storedLitres'], FallsWith: ['drawnLitresPerDay'] },
    });

    expect(found.inputs).toEqual([
      { figure: 'daysOfSupply', term: 'storedLitres', heldBy: 'r1', figureMoves: 'with' },
      { figure: 'daysOfSupply', term: 'drawnLitresPerDay', heldBy: 'r1', figureMoves: 'against' },
    ]);
    expect(found.leftOut).toEqual([]);
  });

  it('walks through a figure defined on another figure and composes the directions', () => {
    const found = read({
      shortfallLitres: { Expression: 'neededLitres - daysOfSupply * drawnLitresPerDay', Reads: ['neededLitres', 'daysOfSupply'], RisesWith: ['neededLitres'], FallsWith: ['daysOfSupply'] },
      daysOfSupply: { Expression: 'storedLitres / lossFactor', Reads: ['storedLitres', 'lossFactor'], RisesWith: ['storedLitres'], FallsWith: ['lossFactor'] },
    }, 'shortfallLitres');

    expect(found.inputs).toEqual([
      { figure: 'shortfallLitres', term: 'neededLitres', heldBy: 'r1', figureMoves: 'with' },
      { figure: 'shortfallLitres', term: 'storedLitres', heldBy: 'r1', figureMoves: 'against' },
      { figure: 'shortfallLitres', term: 'lossFactor', heldBy: 'r1', figureMoves: 'with' },
    ]);
  });

  it('names the kind of member that holds an input a total reads, and no single holder', () => {
    const found = read({
      inflowLitresPerDay: { Function: 'sum', RelatedType: 'Spring', Reads: ['flowLitresPerDay'], RisesWith: ['flowLitresPerDay'] },
    });

    expect(found.inputs).toEqual([
      { figure: 'inflowLitresPerDay', term: 'flowLitresPerDay', memberKind: 'Spring', figureMoves: 'with' },
    ]);
  });

  it('leaves out an input the definition settles no direction for, and says so', () => {
    const found = read({
      marginLitres: { Expression: 'abs(storedLitres - targetLitres)', Reads: ['storedLitres', 'targetLitres'], RisesWith: [], FallsWith: [] },
    });

    expect(found.inputs).toEqual([]);
    expect(found.leftOut).toEqual([
      { figure: 'marginLitres', term: 'storedLitres', reason: 'noDirection' },
      { figure: 'marginLitres', term: 'targetLitres', reason: 'noDirection' },
    ]);
  });

  it('leaves out an input reached along two routes that move the figure opposite ways, and says so', () => {
    const found = read({
      netLitres: { Expression: 'gainedLitres - lostLitres', Reads: ['gainedLitres', 'lostLitres'], RisesWith: ['gainedLitres'], FallsWith: ['lostLitres'] },
      gainedLitres: { Expression: 'rainfall * area', Reads: ['rainfall', 'area'], RisesWith: ['rainfall', 'area'] },
      lostLitres: { Expression: 'evaporation * area', Reads: ['evaporation', 'area'], RisesWith: ['evaporation', 'area'] },
    }, 'netLitres');

    expect(found.inputs).toEqual([
      { figure: 'netLitres', term: 'rainfall', heldBy: 'r1', figureMoves: 'with' },
      { figure: 'netLitres', term: 'evaporation', heldBy: 'r1', figureMoves: 'against' },
    ]);
    expect(found.leftOut).toEqual([{ figure: 'netLitres', term: 'area', reason: 'routesDisagree' }]);
  });

  it('offers an input reached along two routes that agree once', () => {
    const found = read({
      totalLitres: { Expression: 'firstLitres + secondLitres', Reads: ['firstLitres', 'secondLitres'], RisesWith: ['firstLitres', 'secondLitres'] },
      firstLitres: { Expression: 'rainfall * 2', Reads: ['rainfall'], RisesWith: ['rainfall'] },
      secondLitres: { Expression: 'rainfall * 3', Reads: ['rainfall'], RisesWith: ['rainfall'] },
    }, 'totalLitres');

    expect(found.inputs).toEqual([{ figure: 'totalLitres', term: 'rainfall', heldBy: 'r1', figureMoves: 'with' }]);
  });

  it('reads a figure that names itself as an input rather than walking for ever', () => {
    const found = read({
      levelMetres: { Expression: 'levelMetres + inflowMetres', Reads: ['levelMetres', 'inflowMetres'], RisesWith: ['levelMetres', 'inflowMetres'] },
    });

    expect(found.inputs.map((input) => input.term)).toEqual(['levelMetres', 'inflowMetres']);
  });

  it('covers every derived figure of the Thing where none is named', () => {
    const found = read({
      daysOfSupply: { Expression: 'storedLitres / 7', Reads: ['storedLitres'], RisesWith: ['storedLitres'] },
      inflowLitresPerDay: { Function: 'sum', RelatedType: 'Spring', Reads: ['flowLitresPerDay'], RisesWith: ['flowLitresPerDay'] },
    });

    expect(found.inputs.map((input) => `${input.figure}:${input.term}`)).toEqual(['daysOfSupply:storedLitres', 'inflowLitresPerDay:flowLitresPerDay']);
  });

  it('answers nothing for a figure the model does not derive', () => {
    expect(read({}, 'daysOfSupply')).toEqual({ inputs: [], leftOut: [] });
  });
});
