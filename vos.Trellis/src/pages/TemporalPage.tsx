import { useState, useEffect, useCallback } from 'react';
import { temporalApi } from '../api/temporalApi';
import { modelApi } from '../api/modelApi';
import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { toast } from '../components/common/Toast';
import type { ModelMutations, ThingMutations, RelationshipMutations, PropertyVersionsResponse, VosThing, VosRelationship, TemporalSnapshot } from '../types/vos';
import { stateApi } from '../api/stateApi';
import { formatDateTime, formatPropertyValue } from '../utils/formatters';
import clsx from 'clsx';

const tabs = ['Mutations', 'Thing Mutations', 'Relationship Mutations', 'Snapshot', 'Property History', 'State Query'] as const;
type Tab = typeof tabs[number];

export function TemporalPage() {
  const [tab, setTab] = useState<Tab>('Mutations');

  return (
    <div className="h-full overflow-auto p-6">
      <h2 className="text-xl font-bold mb-4">Temporal Queries</h2>

      <div className="flex gap-1 border-b border-zinc-200 dark:border-zinc-700 mb-6">
        {tabs.map((t) => (
          <button
            key={t}
            onClick={() => setTab(t)}
            className={clsx(
              'px-4 py-2 text-sm font-medium border-b-2 transition-colors',
              tab === t
                ? 'border-blue-500 text-blue-500'
                : 'border-transparent text-zinc-500 hover:text-zinc-300',
            )}
          >
            {t}
          </button>
        ))}
      </div>

      {tab === 'Mutations' && <MutationsPanel />}
      {tab === 'Thing Mutations' && <ThingMutationsPanel />}
      {tab === 'Relationship Mutations' && <RelationshipMutationsPanel />}
      {tab === 'Snapshot' && <SnapshotPanel />}
      {tab === 'Property History' && <PropertyHistoryPanel />}
      {tab === 'State Query' && <StateQueryPanel />}
    </div>
  );
}

// ─── Mutations Panel ────────────────────────────────────────────────────────

function MutationsPanel() {
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
      toast.error(err instanceof Error ? err.message : 'Failed to load mutations');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="max-w-3xl space-y-4">
      <p className="text-xs text-zinc-500">
        Show property value changes across the model. A mutation is recorded when a property value changes from one value to another.
        Creating a thing or setting an initial property value is not a mutation.
      </p>
      <div className="flex gap-3 items-end">
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">Start Time</label>
          <input
            type="datetime-local"
            value={startTime}
            onChange={(e) => setStartTime(e.target.value)}
            className="px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">End Time</label>
          <input
            type="datetime-local"
            value={endTime}
            onChange={(e) => setEndTime(e.target.value)}
            className="px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <button
          onClick={loadMutations}
          disabled={loading}
          className="px-3 py-1.5 text-sm rounded-md bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {loading ? 'Loading...' : 'Query'}
        </button>
      </div>

      {mutations && (
        <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
          <div className="flex justify-between text-sm mb-3">
            <span className="text-zinc-500">Total Mutations: <strong className="text-zinc-200">{mutations.TotalMutations}</strong></span>
            <span className="text-zinc-500 text-xs">
              {formatDateTime(mutations.StartTime)} — {formatDateTime(mutations.EndTime)}
            </span>
          </div>
          <div className="space-y-3">
            {Object.values(mutations.ThingMutations).map((tm) => (
              <div key={tm.ObjectId} className="border-l-2 border-blue-500 pl-3">
                <h4 className="text-sm font-medium">{tm.ObjectName}</h4>
                <div className="space-y-1 mt-1">
                  {tm.Mutations.map((m, i) => (
                    <div key={i} className="text-xs text-zinc-400">
                      <span className="font-mono text-zinc-500">{formatDateTime(m.Timestamp)}</span>
                      {' '}
                      <span className="text-amber-400">{m.PropertyName}</span>
                      {': '}
                      <span className="text-red-400">{formatPropertyValue(m.OldValue)}</span>
                      {' → '}
                      <span className="text-emerald-400">{formatPropertyValue(m.NewValue)}</span>
                    </div>
                  ))}
                </div>
              </div>
            ))}
            {Object.keys(mutations.ThingMutations).length === 0 && (
              <p className="text-sm text-zinc-500">No mutations in this time range.</p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

// ─── Thing Mutations Panel ───────────────────────────────────────────────────

function ThingMutationsPanel() {
  const [things, setThings] = useState<VosThing[]>([]);
  const [thingId, setThingId] = useState('');
  const [startTime, setStartTime] = useState('');
  const [endTime, setEndTime] = useState('');
  const [mutations, setMutations] = useState<ThingMutations | null>(null);
  const [loading, setLoading] = useState(false);

  const loadThings = useCallback(async () => {
    try { setThings(await thingApi.getAll()); } catch { /* ignore */ }
  }, []);

  useEffect(() => { loadThings(); }, [loadThings]);

  const loadMutations = async () => {
    if (!thingId) return;
    setLoading(true);
    try {
      const data = await temporalApi.getThingMutations(thingId, startTime || undefined, endTime || undefined);
      setMutations(data);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to load thing mutations');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="max-w-3xl space-y-4">
      <p className="text-xs text-zinc-500">
        View property mutations for a specific thing. Select a thing and optionally narrow by time range.
      </p>
      <div className="grid grid-cols-2 gap-3">
        <div className="col-span-2">
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">Thing</label>
          <select
            value={thingId}
            onChange={(e) => { setThingId(e.target.value); setMutations(null); }}
            className="w-full px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
          >
            <option value="">Select a thing...</option>
            {things.map((t) => <option key={t.Id} value={t.Id}>{t.Name}</option>)}
          </select>
        </div>
      </div>
      <div className="flex gap-3 items-end">
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">Start Time</label>
          <input type="datetime-local" value={startTime} onChange={(e) => setStartTime(e.target.value)}
            className="px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500" />
        </div>
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">End Time</label>
          <input type="datetime-local" value={endTime} onChange={(e) => setEndTime(e.target.value)}
            className="px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500" />
        </div>
        <button onClick={loadMutations} disabled={loading || !thingId}
          className="px-3 py-1.5 text-sm rounded-md bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50">
          {loading ? 'Loading...' : 'Query'}
        </button>
      </div>

      {mutations && (
        <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
          <div className="flex justify-between text-sm mb-3">
            <span className="text-zinc-500">
              <strong className="text-zinc-200">{mutations.ObjectName}</strong> — {mutations.Mutations.length} mutation{mutations.Mutations.length !== 1 ? 's' : ''}
            </span>
            <span className="text-zinc-500 text-xs">
              {formatDateTime(mutations.StartTime)} — {formatDateTime(mutations.EndTime)}
            </span>
          </div>
          <div className="space-y-1">
            {mutations.Mutations.map((m, i) => (
              <div key={i} className="text-xs text-zinc-400">
                <span className="font-mono text-zinc-500">{formatDateTime(m.Timestamp)}</span>
                {' '}
                <span className="text-amber-400">{m.PropertyName}</span>
                {': '}
                <span className="text-red-400">{formatPropertyValue(m.OldValue)}</span>
                {' → '}
                <span className="text-emerald-400">{formatPropertyValue(m.NewValue)}</span>
              </div>
            ))}
            {mutations.Mutations.length === 0 && (
              <p className="text-sm text-zinc-500">No mutations in this time range.</p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

// ─── Relationship Mutations Panel ───────────────────────────────────────────

function RelationshipMutationsPanel() {
  const [relationships, setRelationships] = useState<VosRelationship[]>([]);
  const [relId, setRelId] = useState('');
  const [startTime, setStartTime] = useState('');
  const [endTime, setEndTime] = useState('');
  const [mutations, setMutations] = useState<RelationshipMutations | null>(null);
  const [loading, setLoading] = useState(false);

  const loadRels = useCallback(async () => {
    try { setRelationships(await relationshipApi.getAll()); } catch { /* ignore */ }
  }, []);

  useEffect(() => { loadRels(); }, [loadRels]);

  const loadMutations = async () => {
    if (!relId) return;
    setLoading(true);
    try {
      const data = await temporalApi.getRelationshipMutations(relId, startTime || undefined, endTime || undefined);
      setMutations(data);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to load relationship mutations');
    } finally {
      setLoading(false);
    }
  };

  const selectedRel = relationships.find((r) => r.Id === relId);

  return (
    <div className="max-w-3xl space-y-4">
      <p className="text-xs text-zinc-500">
        View property mutations on a specific relationship edge. Select a relationship and optionally narrow by time range.
      </p>
      <div>
        <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">Relationship</label>
        <select
          value={relId}
          onChange={(e) => { setRelId(e.target.value); setMutations(null); }}
          className="w-full px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          <option value="">Select a relationship...</option>
          {relationships.map((r) => <option key={r.Id} value={r.Id}>{r.Name}</option>)}
        </select>
      </div>
      <div className="flex gap-3 items-end">
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">Start Time</label>
          <input type="datetime-local" value={startTime} onChange={(e) => setStartTime(e.target.value)}
            className="px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500" />
        </div>
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">End Time</label>
          <input type="datetime-local" value={endTime} onChange={(e) => setEndTime(e.target.value)}
            className="px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500" />
        </div>
        <button onClick={loadMutations} disabled={loading || !relId}
          className="px-3 py-1.5 text-sm rounded-md bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50">
          {loading ? 'Loading...' : 'Query'}
        </button>
      </div>

      {mutations && (
        <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
          <div className="flex justify-between text-sm mb-3">
            <span className="text-zinc-500">
              <strong className="text-zinc-200">{mutations.RelationshipName || selectedRel?.Name}</strong> — {mutations.Mutations.length} mutation{mutations.Mutations.length !== 1 ? 's' : ''}
            </span>
            <span className="text-zinc-500 text-xs">
              {formatDateTime(mutations.StartTime)} — {formatDateTime(mutations.EndTime)}
            </span>
          </div>
          <div className="space-y-1">
            {mutations.Mutations.map((m, i) => (
              <div key={i} className="text-xs text-zinc-400">
                <span className="font-mono text-zinc-500">{formatDateTime(m.Timestamp)}</span>
                {' '}
                <span className="text-amber-400">{m.PropertyName}</span>
                {': '}
                <span className="text-red-400">{formatPropertyValue(m.OldValue)}</span>
                {' → '}
                <span className="text-emerald-400">{formatPropertyValue(m.NewValue)}</span>
              </div>
            ))}
            {mutations.Mutations.length === 0 && (
              <p className="text-sm text-zinc-500">No mutations in this time range.</p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

// ─── Snapshot Panel ──────────────────────────────────────────────────────────

function SnapshotPanel() {
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
      toast.error(err instanceof Error ? err.message : 'Failed to load snapshot');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="max-w-3xl space-y-4">
      <p className="text-xs text-zinc-500">
        View the entire model as it existed at a specific point in time. Leave the timestamp empty to see the current state.
      </p>
      <div className="flex gap-3 items-end">
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">Timestamp</label>
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
          {loading ? 'Loading...' : 'Snapshot'}
        </button>
      </div>

      {snapshot && (
        <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
          <div className="text-sm text-zinc-500 mb-3">
            Snapshot at: <strong className="text-zinc-200">{formatDateTime(snapshot.Timestamp)}</strong>
            {' — '}
            {snapshot.Things.length} things, {snapshot.Relationships.length} relationships
          </div>
          <div className="space-y-3">
            {snapshot.Things.map((t) => (
              <div key={t.Id} className="border-l-2 border-emerald-500 pl-3">
                <h4 className="text-sm font-medium">{t.Name}</h4>
                {Object.keys(t.Properties).length > 0 ? (
                  <div className="mt-1 space-y-0.5">
                    {Object.entries(t.Properties).map(([k, v]) => (
                      <div key={k} className="text-xs text-zinc-400">
                        <span className="text-amber-400">{k}</span>
                        {': '}
                        <span className="text-zinc-300">{formatPropertyValue(v)}</span>
                      </div>
                    ))}
                  </div>
                ) : (
                  <p className="text-xs text-zinc-600 mt-1">No properties</p>
                )}
              </div>
            ))}
            {snapshot.Things.length === 0 && (
              <p className="text-sm text-zinc-500">No things existed at this timestamp.</p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

// ─── Property History Panel ──────────────────────────────────────────────────

function PropertyHistoryPanel() {
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
      // ignore
    }
  }, []);

  useEffect(() => {
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
      toast.error(err instanceof Error ? err.message : 'Failed to load versions');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="max-w-3xl space-y-4">
      <p className="text-xs text-zinc-500">
        View the full version history of a specific property on a thing. Each version shows the value and when it was set.
      </p>
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">Thing</label>
          <select
            value={thingId}
            onChange={(e) => { setThingId(e.target.value); setPropertyName(''); setVersions(null); }}
            className="w-full px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
          >
            <option value="">Select a thing...</option>
            {things.map((t) => (
              <option key={t.Id} value={t.Id}>{t.Name}</option>
            ))}
          </select>
        </div>
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">Property</label>
          <select
            value={propertyName}
            onChange={(e) => { setPropertyName(e.target.value); setVersions(null); }}
            disabled={!thingId}
            className="w-full px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50"
          >
            <option value="">Select a property...</option>
            {propertyNames.map((p) => (
              <option key={p} value={p}>{p}</option>
            ))}
          </select>
        </div>
      </div>
      <div className="flex gap-3 items-end">
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">Start Time</label>
          <input
            type="datetime-local"
            value={startTime}
            onChange={(e) => setStartTime(e.target.value)}
            className="px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <div>
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">End Time</label>
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
          {loading ? 'Loading...' : 'Query'}
        </button>
      </div>

      {versions && (
        <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
          <div className="text-sm text-zinc-500 mb-3">
            <strong className="text-zinc-200">{versions.PropertyName}</strong> on {selectedThing?.Name}
            {' — '}
            {versions.Versions.length} version{versions.Versions.length !== 1 ? 's' : ''}
          </div>
          <div className="space-y-1">
            {versions.Versions.map((v, i) => (
              <div key={i} className="flex items-baseline gap-3 text-xs">
                <span className="font-mono text-zinc-500 flex-shrink-0">{formatDateTime(v.Timestamp)}</span>
                <span className="text-zinc-300">{formatPropertyValue(v.Value)}</span>
              </div>
            ))}
            {versions.Versions.length === 0 && (
              <p className="text-sm text-zinc-500">No versions in this time range.</p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

// ─── State Query Panel ──────────────────────────────────────────────────────

function StateQueryPanel() {
  const [stateName, setStateName] = useState('');
  const [results, setResults] = useState<Array<{ Id: string; Name: string }> | null>(null);
  const [loading, setLoading] = useState(false);

  const query = async () => {
    if (!stateName.trim()) return;
    setLoading(true);
    try {
      const data = await stateApi.getThingsInState(stateName.trim());
      setResults(data.Things);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to query state');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="max-w-3xl space-y-4">
      <p className="text-xs text-zinc-500">
        Find all things currently matching a specific state. States are determined by range evaluations — a thing is "in" a state when its range criteria evaluate to active.
      </p>
      <div className="flex gap-3 items-end">
        <div className="flex-1">
          <label className="block text-xs font-medium text-zinc-500 dark:text-zinc-400 mb-1">State Name</label>
          <input
            type="text"
            value={stateName}
            onChange={(e) => setStateName(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && query()}
            placeholder="e.g. overheating"
            className="w-full px-3 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </div>
        <button
          onClick={query}
          disabled={loading || !stateName.trim()}
          className="px-3 py-1.5 text-sm rounded-md bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {loading ? 'Loading...' : 'Query'}
        </button>
      </div>

      {results !== null && (
        <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
          <div className="text-sm text-zinc-500 mb-3">
            <strong className="text-zinc-200">{results.length}</strong> thing{results.length !== 1 ? 's' : ''} in state <strong className="text-amber-400">{stateName}</strong>
          </div>
          {results.length === 0 ? (
            <p className="text-sm text-zinc-500">No things are currently in this state.</p>
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
