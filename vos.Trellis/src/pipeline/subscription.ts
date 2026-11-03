import { NAVIGATION_AND_SETTINGS } from '../api/dashboardSubscription';
import type { SubscriptionSelector } from '../types/subscription';
import { ROLE } from './model';

/**
 * What the Pipelines page asks the platform for: the Things its roles reach, and nothing else.
 *
 * Every role Thing travels by identifier, with its `binds` relationship, because the page tells
 * which Thing plays which role by following `binds` from the role. A role bound to an archetype the
 * page reads the members of — the pipelines, their nodes and wires, the runs — asks for those
 * members; every other role asks for the one Thing it binds, which is a predicate or a result. The
 * built-in predicates travel by name, as they are matched.
 *
 * A port offers Things, a node chooses one, a choice compares with some, a boundary node stands for
 * something, a connection is reached by a trigger and a state judges a kind: each is a Thing no role
 * reaches, found one step along the predicate the role binds. The page keeps following all of it, so
 * what it saves and what the orchestrator starts appear without reopening it.
 */
export const KINDS_READ = [
  ROLE.Pipeline, ROLE.PipelineNode, ROLE.PipelineInput, ROLE.PipelineOutput, ROLE.Connection, ROLE.Service,
  ROLE.Port, ROLE.PipelineWire, ROLE.DecisionPath, ROLE.PipelineRun, ROLE.NodeRun, ROLE.Range,
  ROLE.ExternalSystem, ROLE.MessageKind, ROLE.Cadence, ROLE.Orchestrator,
] as const;

export const FOLLOWED_ONE_STEP = [ROLE.StandsFor, ROLE.Choice, ROLE.ComparesWith, ROLE.Trigger, ROLE.JudgedThing] as const;

const EVERY_ROLE = Object.values(ROLE);

export const PIPELINE_PAGE: SubscriptionSelector = {
  ids: [...NAVIGATION_AND_SETTINGS.ids!, ...EVERY_ROLE],
  boundTypes: [...NAVIGATION_AND_SETTINGS.boundTypes!, ...KINDS_READ],
  boundArchetypes: [
    ...NAVIGATION_AND_SETTINGS.boundArchetypes!,
    ...EVERY_ROLE.filter((role) => !(KINDS_READ as readonly string[]).includes(role)),
  ],
  names: [...NAVIGATION_AND_SETTINGS.names!, 'has', 'of'],
  traverse: FOLLOWED_ONE_STEP.map((role) => ({ predicateRole: role })),
  includeLaterMatches: true,
};
