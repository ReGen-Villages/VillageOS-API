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
import { Navigate, useParams } from 'react-router-dom';
import { useTranslation, Trans } from 'react-i18next';
import { useModelStore } from '../stores/modelStore';
import { useSse, useSubscription } from '../hooks/useSse';
import { useDashboards, useModelIndex, useResolveContext } from '../hooks/useDashboard';
import { scopeEntities as computeScopeEntities } from '../api/dashboardApi';
import { NAVIGATION_AND_SETTINGS, subscriptionForSpec } from '../api/dashboardSubscription';
import { localizeSpec } from '../api/dashboardLocalization';
import type { DashboardSection } from '../types/dashboard';
import { WidgetRenderer } from '../components/dashboard/widgets/WidgetRenderer';
import { useDetailWindows } from '../components/dashboard/detail/DetailWindowManager';

const REFRESH_EVENTS = [
  'StatesChanged',
  'RelationshipStatesChanged',
  'PropertyChanged',
  'RelationshipPropertyChanged',
  'ModelChanged',
];

/** Coalesce a burst of live events into one dashboard re-resolution. Without this every
 *  PropertyChanged re-runs every widget binding, and PropertyChanged is the highest-rate
 *  event a busy model emits — so the cost climbs as the model grows and the page gets
 *  less responsive over time. */
const REFRESH_DEBOUNCE_MS = 400;

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
  const loaded = useModelStore((s) => s.loaded);
  const { on, connected } = useSse();

  const idx = useModelIndex();
  const dashboards = useDashboards();
  const { dashboardKey } = useParams();
  const dashboard = dashboards.find((d) => d.routeKey === dashboardKey);
  const { t, i18n } = useTranslation();
  const spec = useMemo(
    () => (dashboard ? localizeSpec(dashboard.spec, i18n.language) : undefined),
    [dashboard, i18n.language],
  );

  const entities = useMemo(() => (spec ? computeScopeEntities(spec, idx) : []), [spec, idx]);

  const [scopeId, setScopeId] = useState<string | null>(null);
  const [nonce, setNonce] = useState(0);
  const ctx = useResolveContext(idx, scopeId, spec?.compare?.archetype, nonce);

  // What this page is about, said to the platform: it is sent the Things its widgets read and the
  // later changes to those, instead of every change in a model whose size it does not depend on.
  // Until a dashboard is chosen there is nothing to narrow to, so the shell's own declaration stands.
  useSubscription(
    useMemo(() => (spec ? subscriptionForSpec(spec, scopeId) : NAVIGATION_AND_SETTINGS), [spec, scopeId]),
  );

  // Live refresh: server-side bindings (state counts, services) re-resolve on relevant
  // events even when the local store didn't change. Debounced so a burst of events
  // triggers a single re-resolution instead of one per event.
  useEffect(() => {
    let timer: ReturnType<typeof setTimeout> | null = null;
    const bump = () => {
      if (timer) return;
      timer = setTimeout(() => { timer = null; setNonce((n) => n + 1); }, REFRESH_DEBOUNCE_MS);
    };
    const unsubs = REFRESH_EVENTS.map((ev) => on(ev, bump));
    return () => { if (timer) clearTimeout(timer); unsubs.forEach((u) => u()); };
  }, [on]);

  // A trailing-window widget slides with the clock, so the page also refreshes on the cadence the
  // spec asks for — not only when the model emits an event.
  const refreshSeconds = spec?.refreshSeconds ?? 0;
  useEffect(() => {
    if (refreshSeconds <= 0) return;
    const timer = setInterval(() => setNonce((n) => n + 1), refreshSeconds * 1000);
    return () => clearInterval(timer);
  }, [refreshSeconds]);

  const isWide = useIsWide();
  const { openDetail, windows } = useDetailWindows(idx, spec?.detail, nonce);

  if (!loaded) {
    return <Centered>{t('modelPage.loading')}</Centered>;
  }
  // An address naming no dashboard the model publishes — `/operations` itself, or a link to a
  // dashboard since renamed — settles on the first one, and says so in the address bar.
  if (!dashboard && dashboards.length > 0) {
    return <Navigate to={`/operations/${dashboards[0].routeKey}`} replace />;
  }
  if (!spec) {
    return (
      <Centered>
        <div className="max-w-md text-center">
          <LayoutDashboard className="mx-auto mb-3 text-zinc-300 dark:text-zinc-600" size={40} />
          <h2 className="text-lg font-bold text-zinc-800 dark:text-zinc-100 mb-1">{t('operationsPage.noDashboard')}</h2>
          <p className="text-sm text-zinc-500 dark:text-zinc-400">
            <Trans
              i18nKey="operationsPage.noDashboardBody"
              components={[
                <code className="font-mono text-xs" />,
                <code className="font-mono text-xs" />,
                <code className="font-mono text-xs" />,
              ]}
            />
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
          <Section key={i} section={section} ctx={ctx} isWide={isWide} openDetail={openDetail} />
        ))}
      </div>
      {windows}
    </div>
  );
}

function Section({
  section,
  ctx,
  isWide,
  openDetail,
}: {
  section: DashboardSection;
  ctx: ReturnType<typeof useResolveContext>;
  isWide: boolean;
  openDetail?: (thingId: string) => void;
}) {
  const layout = section.layout ?? (section.widgets.every((w) => w.type === 'kpi') ? 'kpi-strip' : 'single');
  /* Every track states a zero minimum. A bare `1fr` track is `minmax(auto, 1fr)`, which grows to
     whatever its widest content needs — one long unbreakable cell in a table then widens the page
     rather than scrolling inside the card it was put in. The card states a zero minimum of its own
     as well, for the same defect from the other side. */
  let gridTemplateColumns = 'minmax(0, 1fr)';
  if (isWide) {
    if (layout === 'kpi-strip') {
      gridTemplateColumns = `repeat(${Math.min(section.widgets.length, 4)}, minmax(0, 1fr))`;
    } else if (layout === 'split') {
      const widths = section.widths ?? section.widgets.map(() => 1);
      gridTemplateColumns = widths.map((w) => `minmax(0, ${w}fr)`).join(' ');
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
          <WidgetRenderer key={i} widget={widget} ctx={ctx} openDetail={openDetail} />
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
