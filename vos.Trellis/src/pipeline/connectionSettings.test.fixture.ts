import type { EffectiveProperty, VosRelationship, VosThing } from '@tendril-ui/tendril';
import type { CallSettings } from '../api/callSettingsApi';
import { ROLE } from './model';
import { playing, withRoles } from './roles.test.fixture';

/** Estimate-Rainfall states nothing itself: the platform answers each of its settings from rainfall-estimator,
 *  that service's archetype, or its built-in value. */
interface ConnectionSettingsFixture {
  things: VosThing[];
  relationships: VosRelationship[];
  id: Record<
    'connection' | 'connectionArchetype' | 'service' | 'serviceArchetype' | 'completesWhen' | 'ending'
    | 'whenTheServiceAnswers' | 'whenTheThingReachesTheDoneState' | 'whenTheServiceReportsCompletion',
    string
  >;
  callSettings: CallSettings;
  /** The platform's answer for the connection's resolved properties, keyed by the path of archetypes. */
  resolved: Record<string, EffectiveProperty>;
}

const wording = (words: Record<string, string>) => JSON.stringify(words);

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
  const completesWhen = thing('completesWhen', 'completesWhen', playing(ROLE.CompletesWhen));

  const ending = thing('ending', 'Ending', playing(ROLE.CallCompletion), true);
  const whenTheServiceAnswers = thing('when-the-service-answers', 'whenTheServiceAnswers', {
    wording: wording({ en: 'When the service answers', de: 'Wenn der Dienst antwortet' }),
  });
  const whenTheThingReachesTheDoneState = thing('when-the-thing-reaches-the-done-state', 'whenTheThingReachesTheDoneState', {
    wording: wording({ en: 'When the Thing reaches its done state' }),
  });
  const whenTheServiceReportsCompletion = thing('when-the-service-reports-completion', 'whenTheServiceReportsCompletion');
  for (const member of [whenTheServiceAnswers, whenTheThingReachesTheDoneState, whenTheServiceReportsCompletion]) relate(member, is, ending);

  const connectionArchetype = thing('service-binding', 'ServiceBinding', {
    ...playing(ROLE.Connection),
    callsAgainWhileItsThingStaysIn: null,
    doesNotCallAgainForSeconds: null,
    callsAgainForNoLongerThanSeconds: null,
    callsAgainAtMost: null,
  }, true);
  relate(connectionArchetype, completesWhen, ending);

  const capability = thing('capability', 'Capability', playing(ROLE.Service), true);
  const serviceArchetype = thing('rainfall-estimator-prototype', 'rainfall-estimator prototype', { callsAgainForNoLongerThanSeconds: 900 }, true);
  relate(serviceArchetype, is, capability);
  relate(serviceArchetype, completesWhen, whenTheServiceReportsCompletion);
  const service = thing('rainfall-estimator', 'rainfall-estimator');
  relate(service, is, serviceArchetype);

  const range = thing('range', 'Range', playing(ROLE.Range), true);
  const catchment = thing('catchment', 'Catchment', {}, true);
  const outOfDate = thing('out-of-date', 'OutOfDate');
  relate(outOfDate, is, range);
  relate(outOfDate, judges, catchment);

  const connection = thing('estimate-rainfall', 'Estimate-Rainfall');
  relate(connection, is, connectionArchetype);
  relate(connection, triggeredBy, state);
  relate(connection, watches, outOfDate);
  relate(connection, has, service);

  const declared = (name: string, Type: string): [string, EffectiveProperty] =>
    [`ServiceBinding.${name}`, { Value: null, Type, IsInherited: true, InheritedFrom: connectionArchetype }];

  const [held, heldRelationships] = withRoles(things, relationships);
  return {
    things: held,
    relationships: heldRelationships,
    id: {
      connection, connectionArchetype, service, serviceArchetype, completesWhen, ending,
      whenTheServiceAnswers, whenTheThingReachesTheDoneState, whenTheServiceReportsCompletion,
    },
    callSettings: {
      completesWhen: {
        value: whenTheServiceReportsCompletion,
        source: 'serviceArchetype',
        from: { id: serviceArchetype, name: 'rainfall-estimator prototype' },
      },
      callsAgainWhileItsThingStaysIn: { value: true, source: 'builtIn', from: null },
      doesNotCallAgainForSeconds: { value: 10, source: 'builtIn', from: null },
      callsAgainForNoLongerThanSeconds: {
        value: 900,
        source: 'serviceArchetype',
        from: { id: serviceArchetype, name: 'rainfall-estimator prototype' },
      },
      callsAgainAtMost: { value: 5, source: 'builtIn', from: null },
    },
    resolved: Object.fromEntries([
      declared('callsAgainWhileItsThingStaysIn', 'vos.Boolean'),
      declared('doesNotCallAgainForSeconds', 'vos.Integer'),
      declared('callsAgainForNoLongerThanSeconds', 'vos.Integer'),
      declared('callsAgainAtMost', 'vos.Integer'),
    ]),
  };
}
