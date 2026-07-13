/**
 * Generic, config-driven operations dashboard.
 *
 * Contains NO domain vocabulary. It discovers a model-resident `Dashboard`
 * config Thing (see src/types/dashboard.ts), renders its sections of generic
 * widgets, and resolves every widget's bindings through the generic resolver.
 * A model with no Dashboard config shows guidance instead.
 */
import { useEffect, useMemo, useState } from 'react';
import { LayoutDashboard } from 'lucide-react';
import { useModelStore } from '../stores/modelStore';
import { useSse } from '../hooks/useSse';
import { useResolveContext } from '../hooks/useDashboard';
import {
  buildModelIndex,
  discoverDashboards,
  scopeEntities as computeScopeEntities,
} from '../api/dashboardApi';
import type { DashboardSection } from '../types/dashboard';
import { WidgetRenderer } from '../components/dashboard/widgets/WidgetRenderer';

const REFRESH_EVENTS = [
  'StatesChanged',
  'RelationshipStatesChanged',
  'PropertyChanged',
  'RelationshipPropertyChanged',
  'ModelChanged',
];

function useIsWide(minWidth = 1024): boolean {
  const supported = typeof window !== 'undefined' && typeof window.matchMedia === 'function';
  const [wide, setWide] = useState(() => (supported ? window.matchMedia(`(min-width:${minWidth}px)`).matches : true));
  useEffect(() => {
    if (!supported) return;
    const mq = window.matchMedia(`(min-width:${minWidth}px)`);
    const on = () => setWide(mq.matches);
    mq.addEventListener('change', on);
    return () => mq.removeEventListener('change', on);
  }, [minWidth, supported]);
  return wide;
}

export function OperationsPage() {
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  const loaded = useModelStore((s) => s.loaded);
  const { on, connected } = useSse();

  const dashboards = useMemo(() => discoverDashboards(things, relationships), [things, relationships]);
  const [selected, setSelected] = useState(0);
  const dashboard = dashboards[Math.min(selected, Math.max(0, dashboards.length - 1))];
  const spec = dashboard?.spec;

  const idx = useMemo(() => buildModelIndex(things, relationships), [things, relationships]);
  const entities = useMemo(() => (spec ? computeScopeEntities(spec, idx) : []), [spec, idx]);

  const [scopeId, setScopeId] = useState<string | null>(null);
  const [nonce, setNonce] = useState(0);
  const ctx = useResolveContext(scopeId, spec?.compare?.archetype, nonce);

  // Live refresh: server-side bindings (state counts, services) re-resolve on
  // relevant events even when the local store didn't change.
  useEffect(() => {
    const unsubs = REFRESH_EVENTS.map((ev) => on(ev, () => setNonce((n) => n + 1)));
    return () => unsubs.forEach((u) => u());
  }, [on]);

  const isWide = useIsWide();

  if (!loaded) {
    return <Centered>Loading model…</Centered>;
  }
  if (!spec) {
    return (
      <Centered>
        <div className="max-w-md text-center">
          <LayoutDashboard className="mx-auto mb-3 text-zinc-300 dark:text-zinc-600" size={40} />
          <h2 className="text-lg font-bold text-zinc-800 dark:text-zinc-100 mb-1">No dashboard configured</h2>
          <p className="text-sm text-zinc-500 dark:text-zinc-400">
            This model defines no <code className="font-mono text-xs">Dashboard</code> config. Add a Thing of archetype{' '}
            <code className="font-mono text-xs">Dashboard</code> with a <code className="font-mono text-xs">spec</code> property to
            drive this page.
          </p>
        </div>
      </Centered>
    );
  }

  return (
    <div className="h-full flex flex-col bg-zinc-50 dark:bg-zinc-950">
      <header className="flex-shrink-0 flex items-center justify-between gap-4 flex-wrap px-6 pt-6 pb-4">
        <div className="flex items-center gap-3">
          <div className="w-9 h-9 rounded-lg grid place-items-center text-white font-extrabold bg-gradient-to-br from-blue-600 to-violet-500">
            {(spec.title[0] ?? 'D').toUpperCase()}
          </div>
          <div>
            <h2 className="text-xl font-bold text-zinc-900 dark:text-white leading-tight">{spec.title}</h2>
            {spec.subtitle && <div className="text-xs text-zinc-400 dark:text-zinc-500">{spec.subtitle}</div>}
          </div>
          {dashboards.length > 1 && (
            <select
              value={selected}
              onChange={(e) => setSelected(Number(e.target.value))}
              className="ml-2 text-xs bg-white dark:bg-zinc-800 border border-zinc-200 dark:border-zinc-700 rounded-md px-2 py-1"
            >
              {dashboards.map((d, i) => (
                <option key={d.id} value={i}>
                  {d.name}
                </option>
              ))}
            </select>
          )}
        </div>

        <div className="flex items-center gap-3">
          {spec.compare && entities.length > 0 && (
            <div className="inline-flex bg-white dark:bg-zinc-800 border border-zinc-200 dark:border-zinc-700 rounded-lg p-0.5">
              <ScopeButton active={scopeId === null} onClick={() => setScopeId(null)}>
                All {spec.compare.label}s
              </ScopeButton>
              {entities.map((e) => (
                <ScopeButton key={e.id} active={scopeId === e.id} onClick={() => setScopeId(e.id)}>
                  {e.name}
                </ScopeButton>
              ))}
            </div>
          )}
          <div className="text-[11px] text-zinc-400 dark:text-zinc-500 inline-flex items-center gap-1.5">
            <span className={`w-2 h-2 rounded-full ${connected ? 'bg-emerald-500' : 'bg-zinc-400'}`} />
            {connected ? 'live' : 'offline'}
          </div>
        </div>
      </header>

      <div className="flex-1 overflow-auto px-6 pb-10">
        {spec.sections.map((section, i) => (
          <Section key={i} section={section} ctx={ctx} isWide={isWide} />
        ))}
      </div>
    </div>
  );
}

function Section({
  section,
  ctx,
  isWide,
}: {
  section: DashboardSection;
  ctx: ReturnType<typeof useResolveContext>;
  isWide: boolean;
}) {
  const layout = section.layout ?? (section.widgets.every((w) => w.type === 'kpi') ? 'kpi-strip' : 'single');
  let gridTemplateColumns = '1fr';
  if (isWide) {
    if (layout === 'kpi-strip') {
      gridTemplateColumns = `repeat(${Math.min(section.widgets.length, 4)}, minmax(0, 1fr))`;
    } else if (layout === 'split') {
      const widths = section.widths ?? section.widgets.map(() => 1);
      gridTemplateColumns = widths.map((w) => `${w}fr`).join(' ');
    }
  }

  return (
    <section className="mb-2">
      {section.title && (
        <div className="flex items-center gap-3 mt-6 mb-3">
          <h3 className="text-[12px] uppercase tracking-wider font-bold text-zinc-400 dark:text-zinc-500">{section.title}</h3>
          {section.hint && <span className="text-[11px] text-zinc-400 dark:text-zinc-500">{section.hint}</span>}
          <div className="flex-1 h-px bg-zinc-200 dark:bg-zinc-800" />
        </div>
      )}
      <div className="grid gap-3.5" style={{ gridTemplateColumns }}>
        {section.widgets.map((widget, i) => (
          <WidgetRenderer key={i} widget={widget} ctx={ctx} />
        ))}
      </div>
    </section>
  );
}

function ScopeButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button
      onClick={onClick}
      className={`text-xs font-semibold px-3 py-1.5 rounded-md transition-colors ${
        active ? 'bg-blue-600 text-white' : 'text-zinc-500 dark:text-zinc-400 hover:text-zinc-800 dark:hover:text-zinc-200'
      }`}
    >
      {children}
    </button>
  );
}

function Centered({ children }: { children: React.ReactNode }) {
  return <div className="h-full flex items-center justify-center p-8 text-sm text-zinc-500 dark:text-zinc-400">{children}</div>;
}
