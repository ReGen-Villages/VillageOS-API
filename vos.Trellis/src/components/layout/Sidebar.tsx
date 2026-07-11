import { useState } from 'react';
import { NavLink } from 'react-router-dom';
import { Network, LayoutDashboard, Clock, Search, Boxes, Box, Workflow, Terminal, ChevronLeft, ChevronRight } from 'lucide-react';
import clsx from 'clsx';
import { useAuth } from '../../hooks/useAuth';

const links = [
  { to: '/', icon: LayoutDashboard, label: 'Dashboard' },
  { to: '/graph', icon: Network, label: 'Graph' },
  { to: '/model', icon: Box, label: 'Model' },
  { to: '/pipelines', icon: Workflow, label: 'Pipelines' },
  { to: '/temporal', icon: Clock, label: 'Temporal' },
  { to: '/things', icon: Boxes, label: 'Things' },
  { to: '/properties', icon: Search, label: 'Properties' },
  { to: '/logs', icon: Terminal, label: 'Logs' },
];

export function Sidebar() {
  const [isCollapsed, setIsCollapsed] = useState(false);
  const { modelName } = useAuth();

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
            <h1 className="text-lg font-bold text-zinc-900 dark:text-white tracking-wide">VILLAGEOS</h1>
            {modelName ? (
              <span className="text-xs text-blue-500 dark:text-blue-400">{modelName}</span>
            ) : (
              <span className="text-xs text-zinc-500 dark:text-zinc-400">Temporal Graph GUI</span>
            )}
          </div>
        )}
        <button
          onClick={() => setIsCollapsed(!isCollapsed)}
          className="text-zinc-500 dark:text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 transition-colors"
          aria-label={isCollapsed ? 'Expand sidebar' : 'Collapse sidebar'}
        >
          {isCollapsed ? <ChevronRight size={20} /> : <ChevronLeft size={20} />}
        </button>
      </div>
      <nav className="flex-1 p-2 space-y-1">
        {links.map(({ to, icon: Icon, label }) => (
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
        ))}
      </nav>
    </aside>
  );
}
