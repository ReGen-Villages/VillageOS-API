import type { EffectiveProperty, VosRelationship, VosThing } from '@tendril-ui/tendril';
import { ROLE } from './model';
import { playing, withRoles } from './roles.test.fixture';

/** The connection states nothing itself: every setting it reads is declared on SiteAnalysisConnection or on ServiceBinding above it. */
export interface ConnectionSettingsFixture {
  things: VosThing[];
  relationships: VosRelationship[];
  id: Record<'connection' | 'siteAnalysisConnection' | 'serviceBinding' | 'outOfDate' | 'catchment', string>;
  /** The platform's answer for the connection's resolved properties, keyed by the path of archetypes. */
  resolved: Record<string, EffectiveProperty>;
}

export function connectionSettingsFixture(): ConnectionSettingsFixture {
  const things: VosThing[] = [];
  const relationships: VosRelationship[] = [];
  const thing = (id: string, name: string, properties: Record<string, unknown> = {}, isArchetype = false) => {
    things.push({ Id: id, Name: name, Properties: properties, ...(isArchetype ? { IsArchetype: true } : {}) });
    return id;
  };
  const relate = (subject: string, predicate: string, target: string) =>
    relationships.push({ Id: `${subject} ${predicate} ${target}`, Name: '', SubjectId: subject, PredicateId: predicate, TargetId: target, Properties: {} });

  const is = thing('is', 'is');
  const has = thing('has', 'has');
  const triggeredBy = thing('triggeredBy', 'triggeredBy', playing(ROLE.Trigger));
  const watches = thing('watches', 'watches', playing(ROLE.StateWatch));
  const judges = thing('judges', 'judges', playing(ROLE.JudgedThing));
  const state = thing('state', 'state', playing(ROLE.StateTrigger));

  const serviceBinding = thing('service-binding', 'ServiceBinding', {
    ...playing(ROLE.Connection),
    waitsForTheHandler: false,
    callsAgainWhileItsThingStaysIn: true,
    doesNotCallAgainForSeconds: 10,
    callsAgainForNoLongerThanSeconds: 60,
    callsAgainAtMost: 5,
  }, true);
  const siteAnalysisConnection = thing('site-analysis-connection', 'SiteAnalysisConnection', { completion: 'ack', done_within: 300 }, true);
  relate(siteAnalysisConnection, is, serviceBinding);

  const service = thing('capability', 'Capability', playing(ROLE.Service), true);
  const rainfallEstimator = thing('rainfall-estimator', 'rainfall-estimator');
  relate(rainfallEstimator, is, service);

  const range = thing('range', 'Range', playing(ROLE.Range), true);
  const catchment = thing('catchment', 'Catchment', {}, true);
  const outOfDate = thing('out-of-date', 'OutOfDate');
  relate(outOfDate, is, range);
  relate(outOfDate, judges, catchment);

  const connection = thing('estimate-rainfall', 'Estimate-Rainfall');
  relate(connection, is, siteAnalysisConnection);
  relate(connection, triggeredBy, state);
  relate(connection, watches, outOfDate);
  relate(connection, has, rainfallEstimator);

  const fromSiteAnalysis = (name: string, Type: string, Value: unknown): [string, EffectiveProperty] =>
    [`SiteAnalysisConnection.${name}`, { Value, Type, IsInherited: true, InheritedFrom: siteAnalysisConnection }];
  const fromPlatform = (name: string, Type: string, Value: unknown): [string, EffectiveProperty] =>
    [`SiteAnalysisConnection.ServiceBinding.${name}`, { Value, Type, IsInherited: true, InheritedFrom: serviceBinding }];

  const [held, heldRelationships] = withRoles(things, relationships);
  return {
    things: held,
    relationships: heldRelationships,
    id: { connection, siteAnalysisConnection, serviceBinding, outOfDate, catchment },
    resolved: Object.fromEntries([
      fromSiteAnalysis('completion', 'vos.String', 'ack'),
      fromSiteAnalysis('done_within', 'vos.Integer', 300),
      fromPlatform('waitsForTheHandler', 'vos.Boolean', false),
      fromPlatform('callsAgainWhileItsThingStaysIn', 'vos.Boolean', true),
      fromPlatform('doesNotCallAgainForSeconds', 'vos.Integer', 10),
      fromPlatform('callsAgainForNoLongerThanSeconds', 'vos.Integer', 60),
      fromPlatform('callsAgainAtMost', 'vos.Integer', 5),
    ]),
  };
}
