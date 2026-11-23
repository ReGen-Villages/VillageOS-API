import { describe, it, expect } from 'vitest';
import { subscriptionForSpecification } from './dashboardSubscription';
import { LAND_INTAKE_ROLE } from './landIntakeRoles';

describe('the decisions a detail card explains', () => {
  // The card finds each predicate through its role, so the role Things come by id and their bindings are
  // followed last, after every walk the specification takes.
  it('asks every dashboard for the decision roles and follows their bindings', () => {
    const selector = subscriptionForSpecification({ title: 'Ops', sections: [] }, null);

    expect(selector.ids).toEqual(expect.arrayContaining([
      LAND_INTAKE_ROLE.Constraint,
      LAND_INTAKE_ROLE.DecisionSubject,
      LAND_INTAKE_ROLE.Chosen,
      LAND_INTAKE_ROLE.DecidedUnder,
      LAND_INTAKE_ROLE.Refusal,
      LAND_INTAKE_ROLE.Support,
      LAND_INTAKE_ROLE.ConsiderationCause,
      LAND_INTAKE_ROLE.ConsiderationEvidence,
      LAND_INTAKE_ROLE.RefusedCandidate,
    ]));
    expect(selector.traverse?.at(-1)).toEqual({ predicate: 'binds', direction: 'outgoing', depth: 1 });
    expect(selector.names).toContain('binds');
  });
});
