import { useEffect, useState } from 'react';
import { X } from 'lucide-react';
import { thingApi } from '../../api/thingApi';
import type { VosThing } from '../../types/vos';

interface FragmentsMetadataPanelProps {
  thingId: string;
  onClose: () => void;
}

type PanelState =
  | { status: 'loading' }
  | { status: 'loaded'; thing: VosThing }
  | { status: 'error'; message: string };

/**
 * Side panel that shows the VosThing identity + properties for the element
 * currently picked in the Fragments viewer. Deliberately lightweight — the
 * rich graph-centric editor lives in NodeDetailPanel on the Graph page.
 */
export function FragmentsMetadataPanel({ thingId, onClose }: FragmentsMetadataPanelProps) {
  const [state, setState] = useState<PanelState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;
    setState({ status: 'loading' });
    (async () => {
      try {
        const thing = await thingApi.get(thingId);
        if (!cancelled) setState({ status: 'loaded', thing });
      } catch (err: unknown) {
        if (cancelled) return;
        setState({
          status: 'error',
          message: err instanceof Error ? err.message : 'Failed to load thing',
        });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [thingId]);

  return (
    <aside
      data-testid="fragments-metadata-panel"
      className="w-80 flex-shrink-0 border-l border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-900 flex flex-col"
    >
      <header className="flex items-center justify-between p-4 border-b border-zinc-200 dark:border-zinc-700">
        <h2 className="text-sm font-semibold text-zinc-900 dark:text-zinc-100 truncate">
          {state.status === 'loaded' ? state.thing.Name : 'Selected element'}
        </h2>
        <button
          onClick={onClose}
          aria-label="Close metadata panel"
          className="text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200"
        >
          <X size={18} />
        </button>
      </header>

      <div className="flex-1 overflow-y-auto p-4 text-sm text-zinc-700 dark:text-zinc-300">
        {state.status === 'loading' && <p className="italic text-zinc-500">Loading…</p>}
        {state.status === 'error' && (
          <p className="text-red-600 dark:text-red-400">{state.message}</p>
        )}
        {state.status === 'loaded' && <ThingContent thing={state.thing} />}
      </div>
    </aside>
  );
}

function ThingContent({ thing }: { thing: VosThing }) {
  const own = thing.Properties ?? {};
  const inherited = thing.InheritedProperties ?? {};
  const ownEntries = Object.entries(own);
  const inheritedSets = Object.values(inherited);

  return (
    <div className="space-y-5">
      <section>
        <Meta label="Id" value={thing.Id} mono />
      </section>

      {ownEntries.length > 0 && (
        <section>
          <h3 className="text-xs font-semibold uppercase tracking-wide text-zinc-500 mb-2">
            Properties
          </h3>
          <dl className="space-y-1.5">
            {ownEntries.map(([name, value]) => (
              <Meta key={name} label={name} value={formatValue(value)} />
            ))}
          </dl>
        </section>
      )}

      {inheritedSets.length > 0 && (
        <section>
          <h3 className="text-xs font-semibold uppercase tracking-wide text-zinc-500 mb-2">
            Inherited
          </h3>
          <div className="space-y-3">
            {inheritedSets.map((set) => (
              <div key={set.SourceId}>
                <p className="text-xs font-medium text-zinc-600 dark:text-zinc-400 mb-1">
                  from {set.SourceName}
                </p>
                <dl className="space-y-1.5 pl-3 border-l border-zinc-200 dark:border-zinc-700">
                  {Object.entries(set.Properties ?? {}).map(([name, value]) => (
                    <Meta key={name} label={name} value={formatValue(value)} />
                  ))}
                </dl>
              </div>
            ))}
          </div>
        </section>
      )}
    </div>
  );
}

function Meta({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-xs text-zinc-500">{label}</dt>
      <dd className={mono ? 'font-mono text-xs break-all' : 'break-words'}>{value}</dd>
    </div>
  );
}

function formatValue(v: unknown): string {
  if (v === null || v === undefined) return '—';
  if (typeof v === 'object') return JSON.stringify(v);
  return String(v);
}
