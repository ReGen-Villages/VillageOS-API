import { NAVIGATION_AND_SETTINGS } from '../api/dashboardSubscription';
import type { SubscriptionSelector } from '../types/subscription';
import { ROLE } from './model';

/**
 * Each role Thing comes by identifier with its `binds` relationship, because the page finds what plays a role
 * by following `binds`. A role bound to an archetype whose members the page reads asks for those members, any
 * other for the one Thing it binds, and the built-in predicates come by name, as they are matched. Things one
 * step along a bound predicate, or one step back from a connection, are followed too and kept followed, so what
 * the page saves and what the orchestrator starts appear without reopening it.
 */
export const KINDS_READ = [
  ROLE.Pipeline, ROLE.PipelineNode, ROLE.PipelineInput, ROLE.PipelineOutput, ROLE.Connection, ROLE.Service,
  ROLE.Port, ROLE.PipelineWire, ROLE.DecisionPath, ROLE.PipelineRun, ROLE.NodeRun, ROLE.Range,
  ROLE.ExternalSystem, ROLE.MessageKind, ROLE.Cadence, ROLE.Orchestrator, ROLE.CallCompletion,
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
  traverse: [
    ...FOLLOWED_ONE_STEP.map((role) => ({ predicateRole: role })),
    { predicateRole: ROLE.NotifiedConnection, direction: 'incoming' },
  ],
  includeLaterMatches: true,
};
