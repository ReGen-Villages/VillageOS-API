/**
 * Generic, configuration-driven operations dashboard.
 *
 * Contains NO domain vocabulary. It discovers a model-resident `Dashboard`
 * configuration Thing (see src/types/dashboard.ts), renders its sections of generic
 * widgets, and resolves every widget's bindings through the generic resolver.
 * A model with no Dashboard configuration shows guidance instead.
 */
import { useCallback, useEffect, useMemo, useState } from 'react';
import { LayoutDashboard } from 'lucide-react';
import { Navigate, useParams } from 'react-router-dom';
import { useTranslation, Trans } from 'react-i18next';
import { useModelStore } from '../stores/modelStore';
import { useSse, useSubscription } from '../hooks/useSse';
import { useDashboards, useModelIndex, useResolveContext } from '../hooks/useDashboard';
import { scopeEntities as computeScopeEntities } from '../api/dashboardApi';
import { brokerModelReads } from '../api/brokerModelReads';
import { NAVIGATION_AND_SETTINGS, subscriptionForSpec } from '../api/dashboardSubscription';
import { localizeSpec } from '../api/dashboardLocalization';
import { DashboardSections } from '../components/dashboard/DashboardSections';
import { useDetailWindows } from '../components/dashboard/detail/DetailWindowManager';
import { ComposedPageControls } from '../components/dashboard/ComposedPageControls';

const REFRESH_EVENTS = [
  'StatesChanged',
  'RelationshipStatesChanged',
  'PropertyChanged',
  'RelationshipPropertyChanged',
  'ModelChanged',
];

/** Coalesce a burst of live events into one refresh generation. Widgets resolving together then
 *  share one read per question, and a reader is not shown a page assembled from two moments. */
const REFRESH_DEBOUNCE_MILLISECONDS = 400;

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

  const index = useModelIndex();
  const dashboards = useDashboards();
  const { dashboardKey } = useParams();
  const dashboard = dashboards.find((d) => d.routeKey === dashboardKey);
  const { t, i18n } = useTranslation();
  const spec = useMemo(
    () => (dashboard?.spec ? localizeSpec(dashboard.spec, i18n.language) : undefined),
    [dashboard, i18n.language],
  );

  const entities = useMemo(() => (spec ? computeScopeEntities(spec, index) : []), [spec, index]);

  const [scopeId, setScopeId] = useState<string | null>(null);
  const [nonce, setNonce] = useState(0);
  const [serverRefresh, setServerRefresh] = useState(0);
  // A press taken by a writing widget starts a new generation of broker reads: what it changed is
  // read back, whether or not the change announced itself on the stream.
  const wrote = useCallback(() => setServerRefresh((n) => n + 1), []);
  const context = useResolveContext(index, scopeId, spec?.compare?.archetype, brokerModelReads, nonce, serverRefresh, wrote);

  // What this page is about, said to the platform: it is sent the Things its widgets read and the
  // later changes to those, instead of every change in a model whose size it does not depend on.
  // Until a dashboard is chosen there is nothing to narrow to, so the shell's own declaration stands.
  useSubscription(
    useMemo(() => (spec ? subscriptionForSpec(spec, scopeId) : NAVIGATION_AND_SETTINGS), [spec, scopeId]),
  );

  // A trailing-window widget slides with the clock, and the event that takes a Thing out of a state
  // names the states it still holds rather than the one it left — so a figure the broker answers
  // falls back on the cadence the spec states.
  const refreshSeconds = spec?.refreshSeconds ?? 0;

  // A live event starts a new generation. What each widget does with it is its own to decide: a
  // figure the broker answers waits on the states it is made of, so a burst of property changes
  // costs the page nothing. A page stating no cadence has no other beat, so there the event drives
  // the broker-answered figures as well.
  useEffect(() => {
    let timer: ReturnType<typeof setTimeout> | null = null;
    const bump = () => {
      if (timer) return;
      timer = setTimeout(() => {
        timer = null;
        setNonce((n) => n + 1);
        if (refreshSeconds <= 0) setServerRefresh((n) => n + 1);
      }, REFRESH_DEBOUNCE_MILLISECONDS);
    };
    const unsubs = REFRESH_EVENTS.map((ev) => on(ev, bump));
    return () => { if (timer) clearTimeout(timer); unsubs.forEach((u) => u()); };
  }, [on, refreshSeconds]);

  useEffect(() => {
    if (refreshSeconds <= 0) return;
    const timer = setInterval(() => setServerRefresh((n) => n + 1), refreshSeconds * 1000);
    return () => clearInterval(timer);
  }, [refreshSeconds]);

  // A reloaded model re-asks every broker-answered figure at once.
  useEffect(() => on('ModelChanged', () => setServerRefresh((n) => n + 1)), [on]);

  const isWide = useIsWide();
  const { openDetail, windows } = useDetailWindows(index, spec?.detail, nonce);

  if (!loaded) {
    return <Centered>{t('modelPage.loading')}</Centered>;
  }
  // An address naming no dashboard the model publishes — `/operations` itself, or a link to a
  // dashboard since renamed — settles on the first one, and says so in the address bar.
  if (!dashboard && dashboards.length > 0) {
    return <Navigate to={`/operations/${dashboards[0].routeKey}`} replace />;
  }
  // A Thing that declares itself a dashboard and carries a spec nothing can read. Named, so the
  // author knows which one to open, and told apart from a model that publishes no dashboard at all.
  if (dashboard && !dashboard.spec) {
    return (
      <Guidance title={t('operationsPage.unreadableSpec', { name: dashboard.name })}>
        {t('operationsPage.unreadableSpecBody')}
      </Guidance>
    );
  }
  if (!spec) {
    return (
      <Guidance title={t('operationsPage.noDashboard')}>
        <Trans
          i18nKey="operationsPage.noDashboardBody"
          components={[
            <code className="font-mono text-xs" />,
            <code className="font-mono text-xs" />,
            <code className="font-mono text-xs" />,
          ]}
        />
      </Guidance>
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
          {dashboard && <ComposedPageControls dashboard={dashboard} index={index} />}
          <div className="text-[11px] text-zinc-400 dark:text-zinc-500 inline-flex items-center gap-1.5">
            <span className={`w-2 h-2 rounded-full ${connected ? 'bg-emerald-500' : 'bg-zinc-400'}`} />
            {connected ? 'live' : 'offline'}
          </div>
        </div>
      </header>

      <div className="flex-1 overflow-auto px-6 pb-10">
        <DashboardSections
          sections={spec.sections}
          context={context}
          isWide={isWide}
          openDetail={openDetail}
          whenEmpty={<Centered>{t('operationsPage.emptyView')}</Centered>}
        />
      </div>
      {windows}
    </div>
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

function Guidance({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Centered>
      <div className="max-w-md text-center">
        <LayoutDashboard className="mx-auto mb-3 text-zinc-300 dark:text-zinc-600" size={40} />
        <h2 className="text-lg font-bold text-zinc-800 dark:text-zinc-100 mb-1">{title}</h2>
        <p className="text-sm text-zinc-500 dark:text-zinc-400">{children}</p>
      </div>
    </Centered>
  );
}
