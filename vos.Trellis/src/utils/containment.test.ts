import { describe, it, expect } from 'vitest';
import { thingsContainedBy } from './containment';
import { CONTAINMENT_ROLE_ID } from '../api/platformRoles';
import { BINDS_PREDICATE_NAME } from '../api/landIntakeRoles';
import { IS_PREDICATE } from '../types/dashboard';
import type { VosRelationship, VosThing } from '../types/vos';

function thing(Id: string, Name: string, Properties: Record<string, unknown> = {}): VosThing {
  return { Id, Name, Properties } as VosThing;
}
function relationship(SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
  return { Id: `${SubjectId}-${PredicateId}-${TargetId}`, SubjectId, PredicateId, TargetId, Properties: {} };
}

const binds = thing('binds', BINDS_PREDICATE_NAME);
const isPredicate = thing('is', IS_PREDICATE);
const role = thing(CONTAINMENT_ROLE_ID, 'Containment');
const kind = thing('kind', 'ContainmentPredicate');
const contains = thing('contains', 'contains');
const storey = thing('storey', 'Storey 1');
const wall = thing('wall', 'Wall 1');
const door = thing('door', 'Door 1');

function mapOf(...things: VosThing[]): Map<string, VosThing> {
  return new Map(things.map((t) => [t.Id, t]));
}

describe('thingsContainedBy', () => {
  it('follows a predicate of the kind the containment role binds', () => {
    const things = mapOf(binds, isPredicate, role, kind, contains, storey, wall);
    const relationships = [
      relationship(role.Id, binds.Id, kind.Id),
      relationship(contains.Id, isPredicate.Id, kind.Id),
      relationship(storey.Id, contains.Id, wall.Id),
    ];

    expect(thingsContainedBy(storey, things, relationships)).toEqual([wall]);
  });

  it('follows a predicate of a kind beneath the bound kind', () => {
    const nesting = thing('nesting', 'Nesting');
    const hosts = thing('hosts', 'hosts');
    const things = mapOf(binds, isPredicate, role, kind, nesting, hosts, wall, door);
    const relationships = [
      relationship(role.Id, binds.Id, kind.Id),
      relationship(nesting.Id, isPredicate.Id, kind.Id),
      relationship(hosts.Id, isPredicate.Id, nesting.Id),
      relationship(wall.Id, hosts.Id, door.Id),
    ];

    expect(thingsContainedBy(wall, things, relationships)).toEqual([door]);
  });

  it('reads the role through its binds edge, not its other edges', () => {
    const roleArchetype = thing('role-archetype', 'Role');
    const things = mapOf(binds, isPredicate, role, roleArchetype, kind, contains, storey, wall);
    const relationships = [
      relationship(role.Id, isPredicate.Id, roleArchetype.Id),
      relationship(role.Id, binds.Id, kind.Id),
      relationship(contains.Id, isPredicate.Id, kind.Id),
      relationship(storey.Id, contains.Id, wall.Id),
    ];

    expect(thingsContainedBy(storey, things, relationships)).toEqual([wall]);
  });

  it('follows no predicate joined to the kind along anything but is', () => {
    const describes = thing('describes', 'describes');
    const hosts = thing('hosts', 'hosts');
    const things = mapOf(binds, isPredicate, role, kind, describes, hosts, storey, wall);
    const relationships = [
      relationship(role.Id, binds.Id, kind.Id),
      relationship(hosts.Id, describes.Id, kind.Id),
      relationship(storey.Id, hosts.Id, wall.Id),
    ];

    expect(thingsContainedBy(storey, things, relationships)).toEqual([]);
  });

  it('follows no predicate that carries only the old mark', () => {
    const marked = thing('marked', 'contains', { __IsMapContainmentPredicate: true });
    const things = mapOf(binds, isPredicate, role, kind, marked, storey, wall);
    const relationships = [
      relationship(role.Id, binds.Id, kind.Id),
      relationship(storey.Id, marked.Id, wall.Id),
    ];

    expect(thingsContainedBy(storey, things, relationships)).toEqual([]);
  });

  it('holds nothing in a model that binds no containment role', () => {
    const things = mapOf(binds, isPredicate, kind, contains, storey, wall);
    const relationships = [
      relationship(contains.Id, isPredicate.Id, kind.Id),
      relationship(storey.Id, contains.Id, wall.Id),
    ];

    expect(thingsContainedBy(storey, things, relationships)).toEqual([]);
  });

  it('follows every predicate of the kind, as a building model nests by two', () => {
    const aggregates = thing('aggregates', 'aggregates');
    const things = mapOf(binds, isPredicate, role, kind, contains, aggregates, storey, wall, door);
    const relationships = [
      relationship(role.Id, binds.Id, kind.Id),
      relationship(contains.Id, isPredicate.Id, kind.Id),
      relationship(aggregates.Id, isPredicate.Id, kind.Id),
      relationship(storey.Id, contains.Id, wall.Id),
      relationship(storey.Id, aggregates.Id, door.Id),
    ];

    expect(thingsContainedBy(storey, things, relationships)).toEqual([wall, door]);
  });

  it('ends when the kinds beneath the bound one loop back to it', () => {
    const nesting = thing('nesting', 'Nesting');
    const things = mapOf(binds, isPredicate, role, kind, nesting, contains, storey, wall);
    const relationships = [
      relationship(role.Id, binds.Id, kind.Id),
      relationship(nesting.Id, isPredicate.Id, kind.Id),
      relationship(kind.Id, isPredicate.Id, nesting.Id),
      relationship(contains.Id, isPredicate.Id, nesting.Id),
      relationship(storey.Id, contains.Id, wall.Id),
    ];

    expect(thingsContainedBy(storey, things, relationships)).toEqual([wall]);
  });

  it('skips a contained Thing the loaded set does not hold', () => {
    const things = mapOf(binds, isPredicate, role, kind, contains, storey);
    const relationships = [
      relationship(role.Id, binds.Id, kind.Id),
      relationship(contains.Id, isPredicate.Id, kind.Id),
      relationship(storey.Id, contains.Id, 'not-loaded'),
    ];

    expect(thingsContainedBy(storey, things, relationships)).toEqual([]);
  });

  it('lists only what the container itself holds, not what holds it', () => {
    const things = mapOf(binds, isPredicate, role, kind, contains, storey, wall);
    const relationships = [
      relationship(role.Id, binds.Id, kind.Id),
      relationship(contains.Id, isPredicate.Id, kind.Id),
      relationship(storey.Id, contains.Id, wall.Id),
    ];

    expect(thingsContainedBy(wall, things, relationships)).toEqual([]);
  });
});
