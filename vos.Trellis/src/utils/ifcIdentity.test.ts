import { describe, it, expect } from 'vitest';
import { ifcGlobalIdOf } from './ifcIdentity';
import type { VosThing } from '../types/vos';

function thing(partial: Partial<VosThing>): VosThing {
  return { Id: 'id', Name: 'name', Properties: {}, ...partial };
}

describe('ifcGlobalIdOf (Bug #6191)', () => {
  it('reads the identifier a Thing owns', () => {
    expect(ifcGlobalIdOf(thing({ Properties: { ifcGlobalId: 'own-guid' } }))).toBe('own-guid');
  });

  // The case that broke the viewer: an instance and its type both define the
  // name, so the instance's real identifier is stored as an override.
  it('reads the identifier a Thing overrides, which is where an IFC instance keeps its own', () => {
    const instance = thing({
      Properties: { Dimensions_Area: 1.97 },
      InheritedOverrides: {
        'type-id': {
          SourceId: 'type-id',
          SourceName: 'Solar_Panel-Tesla:Solar Panel',
          InheritedAt: '2026-07-05T00:00:00+00:00',
          Properties: { ifcGlobalId: '03j6lIiubFo9saFbb3qAEn', ifcClass: 'IfcBuildingElementProxy' },
        },
      },
    });

    expect(ifcGlobalIdOf(instance)).toBe('03j6lIiubFo9saFbb3qAEn');
  });

  it('prefers the owned identifier over an override', () => {
    const both = thing({
      Properties: { ifcGlobalId: 'own-guid' },
      InheritedOverrides: {
        'type-id': { SourceId: 'type-id', SourceName: 't', InheritedAt: '', Properties: { ifcGlobalId: 'override-guid' } },
      },
    });

    expect(ifcGlobalIdOf(both)).toBe('own-guid');
  });

  it('finds the identifier whichever source holds it', () => {
    const many = thing({
      InheritedOverrides: {
        a: { SourceId: 'a', SourceName: 'a', InheritedAt: '', Properties: { Identity_Data_Workset: 'SOLAR' } },
        b: { SourceId: 'b', SourceName: 'b', InheritedAt: '', Properties: { ifcGlobalId: 'from-b' } },
      },
    });

    expect(ifcGlobalIdOf(many)).toBe('from-b');
  });

  // Ordinary, not a failure: materials and classifications are not rooted in the
  // IFC and have no identifier to find.
  it('answers null when the Thing has no identifier anywhere', () => {
    expect(ifcGlobalIdOf(thing({ Properties: { ifcClass: 'IfcMaterial' } }))).toBeNull();
    expect(ifcGlobalIdOf(thing({}))).toBeNull();
  });

  it('ignores an empty or non-string identifier rather than mapping to it', () => {
    expect(ifcGlobalIdOf(thing({ Properties: { ifcGlobalId: '' } }))).toBeNull();
    expect(ifcGlobalIdOf(thing({ Properties: { ifcGlobalId: 42 } }))).toBeNull();
    expect(ifcGlobalIdOf(thing({
      InheritedOverrides: { a: { SourceId: 'a', SourceName: 'a', InheritedAt: '', Properties: { ifcGlobalId: '' } } },
    }))).toBeNull();
  });
});
