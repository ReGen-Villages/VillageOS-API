import { useState } from 'react';
import { NavLink } from 'react-router-dom';
import { Network, LayoutDashboard, Gauge, Clock, Search, Boxes, Box, Workflow, Terminal, ChevronLeft, ChevronRight } from 'lucide-react';
import clsx from 'clsx';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../hooks/useAuth';
import { LanguageSwitcher } from '../common/LanguageSwitcher';

const links = [
  { to: '/', icon: LayoutDashboard, labelKey: 'nav.dashboard' },
  { to: '/operations', icon: Gauge, labelKey: 'nav.operations' },
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
  const { t } = useTranslation();

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
        {links.map(({ to, icon: Icon, labelKey }) => {
          const label = t(labelKey);
          return (
            <NavLink
              key={to}
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
              <Icon size={18} />
              {!isCollapsed && label}
            </NavLink>
          );
        })}
      </nav>
      <div className="p-2 border-t border-zinc-200 dark:border-zinc-700">
        <LanguageSwitcher showIcon={!isCollapsed} />
      </div>
    </aside>
  );
}
