/**
 * The subscription a dashboard opens in the console: what Tendril's selector says the page needs, and
 * the roles the console reads beside the page.
 */
import { subscriptionForSpecification as selectorFor, type DashboardSpecification, type SubscriptionSelector } from '@tendril-ui/tendril';
import { LAND_INTAKE_ROLE } from './landIntakeRoles';

export { NAVIGATION_AND_SETTINGS, bindingsOf, slotsOf } from '@tendril-ui/tendril';

/** A detail card explains the decisions about the Thing it opens on, so every dashboard is sent the roles
 *  binding the decision predicates and the constraint archetype; the predicates themselves arrive along `binds`. */
const DECISION_ROLES_A_DETAIL_CARD_READS = [
  LAND_INTAKE_ROLE.Constraint,
  LAND_INTAKE_ROLE.DecisionSubject,
  LAND_INTAKE_ROLE.Chosen,
  LAND_INTAKE_ROLE.DecidedUnder,
  LAND_INTAKE_ROLE.Refusal,
  LAND_INTAKE_ROLE.Support,
  LAND_INTAKE_ROLE.ConsiderationCause,
  LAND_INTAKE_ROLE.ConsiderationEvidence,
  LAND_INTAKE_ROLE.RefusedCandidate,
];

export function subscriptionForSpecification(specification: DashboardSpecification, scopeId: string | null): SubscriptionSelector {
  return selectorFor(specification, scopeId, DECISION_ROLES_A_DETAIL_CARD_READS);
}
