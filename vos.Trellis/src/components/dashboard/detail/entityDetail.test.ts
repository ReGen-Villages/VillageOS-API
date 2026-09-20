import { describe, it, expect } from 'vitest';
import { buildModelIndex } from '../../../api/dashboardApi';
import type { VosThing, VosRelationship, StateTransition } from '../../../types/vos';
import { resolveRelations, buildStateChanges } from './entityDetail';

function thing(id: string, name: string, properties: Record<string, unknown> = {}): VosThing {
  return { Id: id, Name: name, Properties: properties };
}
function relationship(id: string, subjectId: string, predicateId: string, targetId: string): VosRelationship {
  return { Id: id, Name: id, SubjectId: subjectId, PredicateId: predicateId, TargetId: targetId, Properties: {} };
}

// project -has-> task -has-> assignment, project -has-> steward, project -references-> phase -contains-> project.
// `has` reaches both a task and a steward, so archetype filtering must tell them apart.
function fixture() {
  const things = [
    thing('project', 'PRJ-1'),
    thing('task', 'PRJ-1-T1', { quantity: 5, code: 'RES-9' }),
    thing('assignment', 'PRJ-1-T1-A', { quantity: 5 }),
    thing('resource', 'RES-9', { code: 'RES-9', description: 'Compost' }),
    thing('steward', 'STW-1', { display_name: 'Rowan' }),
    thing('phase', 'PHS-1', { phase_number: 'P-1' }),
    thing('ProjectTask', 'ProjectTask'),
    thing('Assignment', 'Assignment'),
    thing('Resource', 'Resource'),
    thing('Steward', 'Steward'),
    thing('Phase', 'Phase'),
    thing('has', 'has'),
    thing('references', 'references'),
    thing('contains', 'contains'),
    thing('is', 'is'),
  ];
  const relationships = [
    relationship('r1', 'project', 'has', 'task'),
    relationship('r2', 'task', 'has', 'assignment'),
    relationship('r3', 'project', 'has', 'steward'),
    relationship('r4', 'project', 'references', 'phase'),
    relationship('r5', 'phase', 'contains', 'project'),
    relationship('r6', 'task', 'references', 'resource'),
    relationship('i1', 'task', 'is', 'ProjectTask'),
    relationship('i2', 'assignment', 'is', 'Assignment'),
    relationship('i3', 'steward', 'is', 'Steward'),
    relationship('i4', 'phase', 'is', 'Phase'),
    relationship('i5', 'resource', 'is', 'Resource'),
  ];
  return buildModelIndex(things, relationships);
}

describe('resolveRelations', () => {
  it('follows an outbound predicate and keeps only the configured archetype', () => {
    const index = fixture();
    const [group] = resolveRelations('project', index, [
      { predicate: 'has', direction: 'out', archetype: 'ProjectTask', label: 'Project tasks' },
    ]);
    expect(group.label).toBe('Project tasks');
    expect(group.edges.map((e) => e.relatedName)).toEqual(['PRJ-1-T1']);
    // 'has' also reaches the steward, but the archetype filter excludes it.
    expect(group.edges.some((e) => e.relatedName === 'STW-1')).toBe(false);
  });

  it('names the subject and target of each edge by direction', () => {
    const index = fixture();
    const [out] = resolveRelations('project', index, [{ predicate: 'has', direction: 'out', archetype: 'ProjectTask' }]);
    expect(out.edges[0]).toMatchObject({ subjectName: 'PRJ-1', targetName: 'PRJ-1-T1' });
    const [inbound] = resolveRelations('project', index, [{ predicate: 'contains', direction: 'in', archetype: 'Phase' }]);
    expect(inbound.edges[0]).toMatchObject({ subjectName: 'PHS-1', targetName: 'PRJ-1' });
  });

  it('shows only the requested properties, or all with "*"', () => {
    const index = fixture();
    const [some] = resolveRelations('project', index, [
      { predicate: 'has', archetype: 'ProjectTask', properties: ['quantity'] },
    ]);
    expect(some.edges[0].properties).toEqual([['quantity', 5]]);
    const [all] = resolveRelations('project', index, [
      { predicate: 'has', archetype: 'ProjectTask', properties: '*' },
    ]);
    expect(all.edges[0].properties).toEqual(expect.arrayContaining([['quantity', 5], ['code', 'RES-9']]));
    const [none] = resolveRelations('project', index, [{ predicate: 'has', archetype: 'ProjectTask' }]);
    expect(none.edges[0].properties).toEqual([]);
  });

  it('nests child relations from each matched Thing', () => {
    const index = fixture();
    const [group] = resolveRelations('project', index, [
      {
        predicate: 'has',
        archetype: 'ProjectTask',
        relations: [{ predicate: 'has', archetype: 'Assignment', label: 'Assignment', properties: ['quantity'] }],
      },
    ]);
    const child = group.edges[0].children[0];
    expect(child.label).toBe('Assignment');
    expect(child.edges[0].relatedName).toBe('PRJ-1-T1-A');
    expect(child.edges[0].properties).toEqual([['quantity', 5]]);
  });

  it('folds an inline relation onto the parent row instead of nesting it', () => {
    const index = fixture();
    const [group] = resolveRelations('project', index, [
      {
        predicate: 'has',
        archetype: 'ProjectTask',
        properties: ['quantity'],
        relations: [{ predicate: 'references', archetype: 'Resource', properties: ['code'], inline: true }],
      },
    ]);
    const task = group.edges[0];
    // The resource's code is hoisted ahead of the task's own quantity; no nested Resource card remains.
    expect(task.properties).toEqual([['code', 'RES-9'], ['quantity', 5]]);
    expect(task.children).toEqual([]);
  });

  it('preserves the configured order of relation groups', () => {
    const index = fixture();
    const groups = resolveRelations('project', index, [
      { predicate: 'references', archetype: 'Phase', label: 'Phase' },
      { predicate: 'has', archetype: 'Steward', label: 'Steward' },
    ]);
    expect(groups.map((g) => g.label)).toEqual(['Phase', 'Steward']);
  });

  it('is cycle-guarded when a relation loops back to the root', () => {
    const index = fixture();
    // project -references-> phase -contains-> project: the nested contains must not re-expand the root.
    const [group] = resolveRelations('project', index, [
      { predicate: 'references', archetype: 'Phase', relations: [{ predicate: 'contains', direction: 'out' }] },
    ]);
    const phaseChildren = group.edges[0].children[0];
    expect(phaseChildren.edges.some((e) => e.thingId === 'project')).toBe(false);
  });
});

describe('buildStateChanges', () => {
  const transition = (at: string, entered: string[], exited: string[]): StateTransition => ({
    At: at,
    Entered: entered,
    Exited: exited,
    States: entered,
  });

  it('orders changes oldest first', () => {
    const changes = buildStateChanges([
      transition('2026-07-14T09:31:00Z', ['growing'], []),
      transition('2026-07-14T09:14:00Z', ['planted'], []),
    ]);
    expect(changes.map((c) => c.at)).toEqual(['2026-07-14T09:14:00Z', '2026-07-14T09:31:00Z']);
    expect(changes[0].entered).toEqual(['planted']);
  });

  it('drops transitions that neither enter nor exit a state', () => {
    const changes = buildStateChanges([
      transition('2026-07-14T09:14:00Z', ['planted'], []),
      transition('2026-07-14T09:20:00Z', [], []),
    ]);
    expect(changes).toHaveLength(1);
  });
});
