import { describe, it, expect } from 'vitest';
import { BINDS_PREDICATE, DISPLAY_SETTINGS_ROLE_ID, PAGE_ROLE_ID, thingBoundBy } from './platformRoles';
import type { VosRelationship, VosThing } from '../types/vos';

function thing(Id: string, Name: string): VosThing {
  return { Id, Name, Properties: {} };
}
function relationship(Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
  return { Id, SubjectId, PredicateId, TargetId, Properties: {} };
}

const binds = thing('binds', BINDS_PREDICATE);
const role = thing(DISPLAY_SETTINGS_ROLE_ID, 'DisplaySettings');
const settings = thing('settings', 'how this model is drawn');

describe('thingBoundBy', () => {
  it('answers the Thing the role binds, whatever it is called', () => {
    const things = [binds, role, settings];
    const relationships = [relationship('b1', role.Id, binds.Id, settings.Id)];

    expect(thingBoundBy(DISPLAY_SETTINGS_ROLE_ID, things, relationships)).toBe(settings);
  });

  it('answers nothing where the role binds nothing, even beside a Thing of the conventional name', () => {
    const namesake = thing('namesake', 'GUI_Settings');

    expect(thingBoundBy(DISPLAY_SETTINGS_ROLE_ID, [binds, role, namesake], [])).toBeUndefined();
  });

  it('answers nothing where the set carries no binds predicate', () => {
    const relationships = [relationship('b1', role.Id, 'binds', settings.Id)];

    expect(thingBoundBy(DISPLAY_SETTINGS_ROLE_ID, [role, settings], relationships)).toBeUndefined();
  });

  it('reads only the edge along binds, not another edge leaving the role', () => {
    const isPredicate = thing('is', 'is');
    const roleArchetype = thing('role-archetype', 'Role');
    const things = [binds, isPredicate, role, roleArchetype, settings];
    const relationships = [
      relationship('t1', role.Id, isPredicate.Id, roleArchetype.Id),
      relationship('b1', role.Id, binds.Id, settings.Id),
    ];

    expect(thingBoundBy(DISPLAY_SETTINGS_ROLE_ID, things, relationships)).toBe(settings);
  });

  it('holds the identifiers every model declares the two roles under', () => {
    expect(PAGE_ROLE_ID).toBe('a18768bc-b4e7-513c-864f-842d674c1858');
    expect(DISPLAY_SETTINGS_ROLE_ID).toBe('1cb6206a-2726-5008-8188-698b299f5665');
  });
});
