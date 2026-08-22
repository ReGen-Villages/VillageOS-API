import { useState, type ReactNode } from 'react';
import { NavLink } from 'react-router-dom';
import { Network, LayoutDashboard, Gauge, Clock, Search, Boxes, Box, Inbox, Workflow, Terminal, ChevronLeft, ChevronRight } from 'lucide-react';
import { DynamicIcon, iconNames, type IconName } from 'lucide-react/dynamic';
import clsx from 'clsx';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../hooks/useAuth';
import { useDashboards } from '../../hooks/useDashboard';
import { makeSpecTranslator } from '../../api/dashboardLocalization';
import { SessionControls } from './SessionControls';

/** The route the model's own dashboards live under. Its entry stands in for them while the model
 *  publishes none, and is replaced by one entry per dashboard once it does. */
const OPERATIONS_PATH = '/operations';

const links = [
  { to: '/', icon: LayoutDashboard, labelKey: 'nav.dashboard' },
  { to: OPERATIONS_PATH, icon: Gauge, labelKey: 'nav.operations' },
  { to: '/submissions', icon: Inbox, labelKey: 'nav.submissions' },
  { to: '/graph', icon: Network, labelKey: 'nav.graph' },
  { to: '/model', icon: Box, labelKey: 'nav.model' },
  { to: '/pipelines', icon: Workflow, labelKey: 'nav.pipelines' },
  { to: '/temporal', icon: Clock, labelKey: 'nav.temporal' },
  { to: '/things', icon: Boxes, labelKey: 'nav.things' },
  { to: '/properties', icon: Search, labelKey: 'nav.properties' },
  { to: '/logs', icon: Terminal, labelKey: 'nav.logs' },
] as const;

export function Sidebar() {
  const [isCollapsed, setIsCollapsed] = useState(false);
  const { modelName } = useAuth();
  const { t, i18n } = useTranslation();
  const dashboards = useDashboards();

  return (
    <aside
      className={clsx(
        'flex-shrink-0 bg-zinc-50 dark:bg-zinc-900 border-r border-zinc-200 dark:border-zinc-700 flex flex-col transition-all duration-300',
        isCollapsed ? 'w-16' : 'w-56'
      )}
    >
      <div className="p-4 border-b border-zinc-200 dark:border-zinc-700 flex items-center justify-between">
        {!isCollapsed && (
          <div>
            <h1 className="text-lg font-bold text-zinc-900 dark:text-white tracking-wide">{t('nav.appName')}</h1>
            {modelName ? (
              <span className="text-xs text-blue-500 dark:text-blue-400">{modelName}</span>
            ) : (
              <span className="text-xs text-zinc-500 dark:text-zinc-400">{t('nav.subtitle')}</span>
            )}
          </div>
        )}
        <button
          onClick={() => setIsCollapsed(!isCollapsed)}
          className="text-zinc-500 dark:text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 transition-colors"
          aria-label={isCollapsed ? t('nav.expandSidebar') : t('nav.collapseSidebar')}
        >
          {isCollapsed ? <ChevronRight size={20} /> : <ChevronLeft size={20} />}
        </button>
      </div>
      <nav className="flex-1 p-2 space-y-1">
        {links.map(({ to, icon: Icon, labelKey }) =>
          to === OPERATIONS_PATH && dashboards.length > 0 ? (
            dashboards.map((dashboard) => (
              <NavItem
                key={dashboard.routeKey}
                to={`${OPERATIONS_PATH}/${dashboard.routeKey}`}
                label={makeSpecTranslator(dashboard.spec, i18n.language)(dashboard.spec.title)}
                icon={<SpecIcon name={dashboard.spec.icon} />}
                isCollapsed={isCollapsed}
              />
            ))
          ) : (
            <NavItem key={to} to={to} label={t(labelKey)} icon={<Icon size={18} />} isCollapsed={isCollapsed} />
          ),
        )}
      </nav>
      <div className="p-2 border-t border-zinc-200 dark:border-zinc-700">
        <SessionControls isCollapsed={isCollapsed} />
      </div>
    </aside>
  );
}

function NavItem({
  to,
  label,
  icon,
  isCollapsed,
}: {
  to: string;
  label: string;
  icon: ReactNode;
  isCollapsed: boolean;
}) {
  return (
    <NavLink
      to={to}
      className={({ isActive }) =>
        clsx(
          'flex items-center gap-3 px-3 py-2 rounded-md text-sm font-medium transition-colors',
          isActive
            ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400'
            : 'text-zinc-600 dark:text-zinc-400 hover:bg-zinc-200 dark:hover:bg-zinc-800',
        )
      }
      title={isCollapsed ? label : undefined}
    >
      {icon}
      {!isCollapsed && label}
    </NavLink>
  );
}

/* Membership is asked once per navigation entry on every render, against every name the icon set
   ships — a scan of the list would repeat that walk each time. */
const ICON_NAMES: ReadonlySet<string> = new Set(iconNames);

/** Declared once so the icon still being fetched is the same element across renders. */
function GenericIcon() {
  return <Gauge size={18} />;
}

/** The icon a spec asks for, loaded on demand so a model can name any icon in the set without
 *  Trellis holding a list of the ones it will accept. A name the set does not have — or none at
 *  all — draws the generic dashboard icon, so the entry is never missing. */
function SpecIcon({ name }: { name?: string }) {
  if (!name || !ICON_NAMES.has(name)) return <GenericIcon />;
  return <DynamicIcon name={name as IconName} size={18} fallback={GenericIcon} />;
}
