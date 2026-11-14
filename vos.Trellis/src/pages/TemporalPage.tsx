import { useState, useEffect, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { temporalApi } from '../api/temporalApi';
import { modelApi } from '../api/modelApi';
import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { toast } from '../components/common/toastStore';
import { ImageryTimeline } from '../components/assets/ImageryTimeline';
import type { ModelMutations, MutationDto, PropertyVersionsResponse, VosThing, VosRelationship, TemporalSnapshot } from '../types/vos';
import { stateApi } from '../api/stateApi';
import { formatDateTime, formatPropertyValue } from '../utils/formatters';
import { relationshipLabel } from '../utils/relationshipLabel';
import { valuesAtAnInstant } from '../utils/valuesAtAnInstant';
import { useModelStore } from '../stores/modelStore';
import { useSubscription } from '../hooks/useSse';
import { WHOLE_MODEL } from '../types/subscription';
import clsx from 'clsx';
import { TEMPORAL_TABS } from './temporalTabs';

type Tab = typeof TEMPORAL_TABS[number];

export function TemporalPage() {
  useSubscription(WHOLE_MODEL);
  const { t } = useTranslation();
  const [tab, setTab] = useState<Tab>('mutations');

  return (
    <div className="h-full overflow-auto p-6">
      <h2 className="text-xl font-bold mb-4">{t('temporal.title')}</h2>

      <div className="flex gap-1 overflow-x-auto border-b border-zinc-200 dark:border-zinc-700 mb-6">
        {TEMPORAL_TABS.map((key) => (
          <button
            key={key}
            onClick={() => setTab(key)}
            className={clsx(
              'px-4 py-2 text-sm font-medium whitespace-nowrap border-b-2 transition-colors',
              tab === key
                ? 'border-blue-500 text-blue-500'
                : 'border-transparent text-zinc-500 hover:text-zinc-300',
            )}
          >
            {t(`temporal.tabs.${key}`)}
          </button>
        ))}
      </div>

      {tab === 'mutations' && <MutationsPanel />}
      {tab === 'thingMutations' && <ThingMutationsPanel />}
      {tab === 'relationshipMutations' && <RelationshipMutationsPanel />}
      {tab === 'snapshot' && <SnapshotPanel />}
      {tab === 'propertyHistory' && <PropertyHistoryPanel />}
      {tab === 'stateQuery' && <StateQueryPanel />}
    </div>
  );
}

function MutationsPanel() {
  const { t } = useTranslation();
  const [startTime, setStartTime] = useState('');
  const [endTime, setEndTime] = useState('');
  const [mutations, setMutations] = useState<ModelMutations | null>(null);
  const [loading, setLoading] = useState(false);

  const loadMutations = async () => {
    setLoading(true);
    try {
      const data = await temporalApi.getModelMutations(
        startTime || undefined,
        endTime || undefined,
      );
      setMutations(data);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('temporal.toast.loadMutationsFailed'));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="max-w-3xl space-y-4">
      <p className="text-xs text-zinc-500">
        {t('temporal.mutationsIntro')}
      </p>
      <TimeRangeQuery
        startTime={startTime}
        endTime={endTime}
        onStartTime={setStartTime}
        onEndTime={setEndTime}
        onQuery={loadMutations}
        loading={loading}
        ready
      />

      {mutations && (
        <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
          <div className="flex justify-between text-sm mb-3">
            <span className="text-zinc-500">{t('temporal.totalMutations')}: <strong className="text-zinc-200">{mutations.TotalMutations}</strong></span>
            <span className="text-zinc-500 text-xs">
              {formatDateTime(mutations.StartTime)} — {formatDateTime(mutations.EndTime)}
            </span>
          </div>
          <div className="space-y-3">
            {Object.values(mutations.ThingMutations).map((tm) => (
              <div key={tm.ObjectId} className="border-l-2 border-blue-500 pl-3">
                <h4 className="text-sm font-medium">{tm.ObjectName}</h4>
                <div className="space-y-1 mt-1">
                  {tm.Mutations.map((m, i) => <MutationLine key={i} mutation={m} />)}
                </div>
              </div>
            ))}
            {Object.keys(mutations.ThingMutations).length === 0 && (
              <p className="text-sm text-zinc-500">{t('temporal.noMutations')}</p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

function ThingMutationsPanel() {
  const { t } = useTranslation();
  const [things, setThings] = useState<VosThing[]>([]);

  const loadThings = useCallback(async () => {
    try { setThings(await thingApi.getAll()); } catch { /* ignore */ }
  }, []);

  // eslint-disable-next-line react-hooks/set-state-in-effect -- every state write in the loader is after an await, so nothing is set while the effect runs; the rule does not model that boundary
  useEffect(() => { loadThings(); }, [loadThings]);

  return (
    <OneObjectMutationsPanel
      intro={t('temporal.thingMutationsIntro')}
      label={t('temporal.thing')}
      placeholder={t('temporal.selectThing')}
      failure={t('temporal.toast.loadThingMutationsFailed')}
      options={things.map((thing) => ({ id: thing.Id, label: thing.Name }))}
      read={(id, start, end) => temporalApi.getThingMutations(id, start, end)}
      nameOf={(changes) => changes.ObjectName}
    />
  );
}

function RelationshipMutationsPanel() {
  const { t } = useTranslation();
  // From the store the app shell already loaded, rather than a second read: the label needs the
  // endpoint names, and every Thing in the model is already here.
  const things = useModelStore((s) => s.things);
  const thingNames = useMemo(() => new Map(things.map((thing) => [thing.Id, thing.Name])), [things]);
  const [relationships, setRelationships] = useState<VosRelationship[]>([]);

  const loadRelationships = useCallback(async () => {
    try { setRelationships(await relationshipApi.getAll()); } catch { /* ignore */ }
  }, []);

  // eslint-disable-next-line react-hooks/set-state-in-effect -- every state write in the loader is after an await, so nothing is set while the effect runs; the rule does not model that boundary
  useEffect(() => { loadRelationships(); }, [loadRelationships]);

  return (
    <OneObjectMutationsPanel
      intro={t('temporal.relationshipMutationsIntro')}
      label={t('temporal.relationship')}
      placeholder={t('temporal.selectRelationship')}
      failure={t('temporal.toast.loadRelationshipMutationsFailed')}
      options={relationships.map((r) => ({ id: r.Id, label: relationshipLabel(r, (id) => thingNames.get(id)) }))}
      read={(id, start, end) => temporalApi.getRelationshipMutations(id, start, end)}
      nameOf={(changes, id) => changes.RelationshipName || relationships.find((r) => r.Id === id)?.Name}
    />
  );
}

interface TimeRangeQueryProperties {
  startTime: string;
  endTime: string;
  onStartTime: (value: string) => void;
  onEndTime: (value: string) => void;
  onQuery: () => void;
  loading: boolean;
  ready: boolean;
}

function TimeRangeQuery({ startTime, endTime, onStartTime, onEndTime, onQuery, loading, ready }: TimeRangeQueryProperties) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-wrap gap-3 items-end">
      <div>
        <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">{t('temporal.startTime')}</label>
        <input type="datetime-local" value={startTime} onChange={(e) => onStartTime(e.target.value)}
          className="px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500" />
      </div>
      <div>
        <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">{t('temporal.endTime')}</label>
        <input type="datetime-local" value={endTime} onChange={(e) => onEndTime(e.target.value)}
          className="px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500" />
      </div>
      <button onClick={onQuery} disabled={loading || !ready}
        className="px-3 py-1.5 text-sm rounded-md bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50">
        {loading ? t('common.loading') : t('common.query')}
      </button>
    </div>
  );
}

function MutationLine({ mutation }: { mutation: MutationDto }) {
  return (
    <div className="text-xs text-zinc-400">
      <span className="font-mono text-zinc-500">{formatDateTime(mutation.Timestamp)}</span>
      {' '}
      <span className="text-amber-400">{mutation.PropertyName}</span>
      {': '}
      <span className="text-red-400">{formatPropertyValue(mutation.OldValue)}</span>
      {' → '}
      <span className="text-emerald-400">{formatPropertyValue(mutation.NewValue)}</span>
    </div>
  );
}

interface OneObjectChanges {
  StartTime: string;
  EndTime: string;
  Mutations: MutationDto[];
}

interface OneObjectMutationsPanelProperties<Changes extends OneObjectChanges> {
  intro: string;
  label: string;
  placeholder: string;
  failure: string;
  options: { id: string; label: string }[];
  read: (id: string, start?: string, end?: string) => Promise<Changes>;
  nameOf: (changes: Changes, id: string) => string | undefined;
}

function OneObjectMutationsPanel<Changes extends OneObjectChanges>(
  { intro, label, placeholder, failure, options, read, nameOf }: OneObjectMutationsPanelProperties<Changes>,
) {
  const { t } = useTranslation();
  const [chosenId, setChosenId] = useState('');
  const [startTime, setStartTime] = useState('');
  const [endTime, setEndTime] = useState('');
  const [mutations, setMutations] = useState<Changes | null>(null);
  const [loading, setLoading] = useState(false);

  const loadMutations = async () => {
    if (!chosenId) return;
    setLoading(true);
    try {
      setMutations(await read(chosenId, startTime || undefined, endTime || undefined));
    } catch (err) {
      toast.error(err instanceof Error ? err.message : failure);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="max-w-3xl space-y-4">
      <p className="text-xs text-zinc-500">{intro}</p>
      <div>
        <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">{label}</label>
        <select
          value={chosenId}
          onChange={(e) => { setChosenId(e.target.value); setMutations(null); }}
          className="w-full px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          <option value="">{placeholder}</option>
          {options.map((option) => <option key={option.id} value={option.id}>{option.label}</option>)}
        </select>
      </div>
      <TimeRangeQuery
        startTime={startTime}
        endTime={endTime}
        onStartTime={setStartTime}
        onEndTime={setEndTime}
        onQuery={loadMutations}
        loading={loading}
        ready={chosenId !== ''}
      />

      {mutations && (
        <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
          <div className="flex justify-between text-sm mb-3">
            <span className="text-zinc-500">
              <strong className="text-zinc-200">{nameOf(mutations, chosenId)}</strong> — {t('temporal.mutationCount', { count: mutations.Mutations.length })}
            </span>
            <span className="text-zinc-500 text-xs">
              {formatDateTime(mutations.StartTime)} — {formatDateTime(mutations.EndTime)}
            </span>
          </div>
          <div className="space-y-1">
            {mutations.Mutations.map((m, i) => <MutationLine key={i} mutation={m} />)}
            {mutations.Mutations.length === 0 && (
              <p className="text-sm text-zinc-500">{t('temporal.noMutations')}</p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

function SnapshotPanel() {
  const { t } = useTranslation();
  const [timestamp, setTimestamp] = useState('');
  const [snapshot, setSnapshot] = useState<TemporalSnapshot | null>(null);
  const [loading, setLoading] = useState(false);

  const loadSnapshot = async () => {
    setLoading(true);
    try {
      const ts = timestamp || new Date().toISOString();
      const data = await modelApi.getAtTime(ts);
      setSnapshot(data as TemporalSnapshot);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('temporal.toast.loadSnapshotFailed'));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="max-w-3xl space-y-4">
      <p className="text-xs text-zinc-500">
        {t('temporal.snapshotIntro')}
      </p>
      <div className="flex flex-wrap gap-3 items-end">
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">{t('temporal.timestamp')}</label>
          <input
            type="datetime-local"
            value={timestamp}
            onChange={(e) => setTimestamp(e.target.value)}
            className="px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <button
          onClick={loadSnapshot}
          disabled={loading}
          className="px-3 py-1.5 text-sm rounded-md bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {loading ? t('common.loading') : t('temporal.snapshotButton')}
        </button>
      </div>

      {snapshot && (
        <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
          <div className="text-sm text-zinc-500 mb-3">
            {t('temporal.snapshotAt')} <strong className="text-zinc-200">{formatDateTime(snapshot.Timestamp)}</strong>
            {' — '}
            {t('temporal.thingsCount', { count: snapshot.Things.length })}, {t('temporal.relationshipsCount', { count: snapshot.Relationships.length })}
          </div>
          <div className="space-y-3">
            {snapshot.Things.map((thing) => {
              const values = Object.entries(valuesAtAnInstant(thing));
              return (
                <div key={thing.Id} className="border-l-2 border-emerald-500 pl-3">
                  <h4 className="text-sm font-medium">{thing.Name}</h4>
                  {values.length > 0 ? (
                    <div className="mt-1 space-y-0.5">
                      {values.map(([k, v]) => (
                        <div key={k} className="text-xs text-zinc-400">
                          <span className="text-amber-400">{k}</span>
                          {': '}
                          <span className="text-zinc-300">{formatPropertyValue(v)}</span>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <p className="text-xs text-zinc-600 mt-1">{t('temporal.noProperties')}</p>
                  )}
                </div>
              );
            })}
            {snapshot.Things.length === 0 && (
              <p className="text-sm text-zinc-500">{t('temporal.noThingsAtTimestamp')}</p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

function PropertyHistoryPanel() {
  const { t } = useTranslation();
  const [things, setThings] = useState<VosThing[]>([]);
  const [thingId, setThingId] = useState('');
  const [propertyName, setPropertyName] = useState('');
  const [startTime, setStartTime] = useState('');
  const [endTime, setEndTime] = useState('');
  const [versions, setVersions] = useState<PropertyVersionsResponse | null>(null);
  const [loading, setLoading] = useState(false);

  const loadThings = useCallback(async () => {
    try {
      setThings(await thingApi.getAll());
    } catch {
      // A failed read leaves the picker empty; the query below reports its own failure.
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- every state write in the loader is after an await, so nothing is set while the effect runs; the rule does not model that boundary
    loadThings();
  }, [loadThings]);

  const selectedThing = things.find((t) => t.Id === thingId);
  const propertyNames = selectedThing ? Object.keys(selectedThing.Properties) : [];

  const loadVersions = async () => {
    if (!thingId || !propertyName) return;
    setLoading(true);
    try {
      const data = await temporalApi.getPropertyVersions(
        thingId,
        propertyName,
        startTime || undefined,
        endTime || undefined,
      );
      setVersions(data);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('temporal.toast.loadVersionsFailed'));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="max-w-3xl space-y-4">
      <p className="text-xs text-zinc-500">
        {t('temporal.propertyHistoryIntro')}
      </p>
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">{t('temporal.thing')}</label>
          <select
            value={thingId}
            onChange={(e) => { setThingId(e.target.value); setPropertyName(''); setVersions(null); }}
            className="w-full px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
          >
            <option value="">{t('temporal.selectThing')}</option>
            {things.map((thing) => (
              <option key={thing.Id} value={thing.Id}>{thing.Name}</option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">{t('temporal.property')}</label>
          <select
            value={propertyName}
            onChange={(e) => { setPropertyName(e.target.value); setVersions(null); }}
            disabled={!thingId}
            className="w-full px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50"
          >
            <option value="">{t('temporal.selectProperty')}</option>
            {propertyNames.map((p) => (
              <option key={p} value={p}>{p}</option>
            ))}
          </select>
        </div>
      </div>
      <div className="flex flex-wrap gap-3 items-end">
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">{t('temporal.startTime')}</label>
          <input
            type="datetime-local"
            value={startTime}
            onChange={(e) => setStartTime(e.target.value)}
            className="px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">{t('temporal.endTime')}</label>
          <input
            type="datetime-local"
            value={endTime}
            onChange={(e) => setEndTime(e.target.value)}
            className="px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <button
          onClick={loadVersions}
          disabled={loading || !thingId || !propertyName}
          className="px-3 py-1.5 text-sm rounded-md bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {loading ? t('common.loading') : t('common.query')}
        </button>
      </div>

      {versions && (
        <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
          <div className="text-sm text-zinc-500 mb-3">
            <strong className="text-zinc-200">{versions.PropertyName}</strong> {t('temporal.on')} {selectedThing?.Name}
            {' — '}
            {t('temporal.versionCount', { count: versions.Versions.length })}
          </div>
          <ImageryTimeline
            key={`${versions.ObjectId}:${versions.PropertyName}`}
            versions={versions.Versions}
          />
          <div className="space-y-1">
            {versions.Versions.map((v, i) => (
              <div key={i} className="flex items-baseline gap-3 text-xs">
                <span className="font-mono text-zinc-500 flex-shrink-0">{formatDateTime(v.Timestamp)}</span>
                <span className="text-zinc-300">{formatPropertyValue(v.Value)}</span>
              </div>
            ))}
            {versions.Versions.length === 0 && (
              <p className="text-sm text-zinc-500">{t('temporal.noVersions')}</p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

function StateQueryPanel() {
  const { t } = useTranslation();
  const [stateName, setStateName] = useState('');
  const [results, setResults] = useState<Array<{ Id: string; Name: string }> | null>(null);
  const [loading, setLoading] = useState(false);

  const query = async () => {
    if (!stateName.trim()) return;
    setLoading(true);
    try {
      const data = await stateApi.getThingsInState(stateName.trim());
      setResults(data.Things ?? []);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('temporal.toast.queryStateFailed'));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="max-w-3xl space-y-4">
      <p className="text-xs text-zinc-500">
        {t('temporal.stateQueryIntro')}
      </p>
      <div className="flex flex-wrap gap-3 items-end">
        <div className="flex-1">
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">{t('temporal.stateName')}</label>
          <input
            type="text"
            value={stateName}
            onChange={(e) => setStateName(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && query()}
            placeholder={t('temporal.stateNamePlaceholder')}
            className="w-full px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <button
          onClick={query}
          disabled={loading || !stateName.trim()}
          className="px-3 py-1.5 text-sm rounded-md bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {loading ? t('common.loading') : t('common.query')}
        </button>
      </div>

      {results !== null && (
        <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
          <div className="text-sm text-zinc-500 mb-3">
            <strong className="text-zinc-200">{t('temporal.thingsCount', { count: results.length })}</strong> {t('temporal.inState')} <strong className="text-amber-400">{stateName}</strong>
          </div>
          {results.length === 0 ? (
            <p className="text-sm text-zinc-500">{t('temporal.noThingsInState')}</p>
          ) : (
            <div className="space-y-1">
              {results.map((t) => (
                <div key={t.Id} className="flex items-baseline gap-2 text-xs">
                  <span className="text-zinc-300">{t.Name}</span>
                  <span className="font-mono text-zinc-600 text-[10px]">{t.Id}</span>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
