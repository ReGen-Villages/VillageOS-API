import { useState } from 'react';
import type { FunnelWidget } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';
import { asNumber } from '../../../api/dashboardApi';
import { useBindings } from '../../../hooks/useDashboard';
import { WidgetCard } from './WidgetCard';
import { DataTable } from './DataTable';

const DEFAULT_COLORS = ['#f43f5e', '#f59e0b', '#f59e0b', '#0ea5e9', '#3b82f6', '#3b82f6', '#10b981'];

/** Pipeline funnel: one bar per stage, click a stage to drill into its rows. */
export function Funnel({ widget, ctx }: { widget: FunnelWidget; ctx: ResolveContext }) {
  const counts = useBindings(
    widget.stages.map((s) => s.count),
    ctx,
  );
  const [drill, setDrill] = useState<number | null>(null);

  const values = counts.map((c) => asNumber(c.value) ?? 0);
  const max = Math.max(1, ...values);
  const active = drill !== null ? widget.stages[drill] : null;

  return (
    <WidgetCard title={widget.title} hint={widget.hint}>
      <div className="flex flex-col gap-1.5 mt-2">
        {widget.stages.map((stage, i) => {
          const count = values[i];
          const color = stage.color ?? DEFAULT_COLORS[i % DEFAULT_COLORS.length];
          const w = Math.max(6, (count / max) * 100);
          const conv = i === 0 ? null : Math.round((values[i] / (values[i - 1] || 1)) * 100);
          const isActive = drill === i;
          const clickable = !!stage.drill;
          return (
            <div
              key={stage.label}
              onClick={() => clickable && setDrill(isActive ? null : i)}
              className={`grid items-center gap-3 rounded-lg px-1 py-0.5 ${
                clickable ? 'cursor-pointer' : ''
              } ${isActive ? 'bg-blue-50 dark:bg-blue-900/20 ring-1 ring-blue-400' : clickable ? 'hover:bg-zinc-50 dark:hover:bg-zinc-700/40' : ''}`}
              style={{ gridTemplateColumns: '104px 1fr 96px' }}
            >
              <div className="text-right">
                <div className="text-[12.5px] font-semibold text-zinc-600 dark:text-zinc-300 leading-tight">{stage.label}</div>
                {stage.sublabel && <div className="text-[10px] text-zinc-400 dark:text-zinc-500">{stage.sublabel}</div>}
              </div>
              <div className="h-[26px] flex items-center">
                <div
                  className="h-[26px] rounded-md flex items-center pl-2.5 text-white text-[12px] font-bold tabular-nums transition-[width] duration-500"
                  style={{ width: `${w}%`, background: color, minWidth: 34 }}
                >
                  {counts[i].loading ? '' : count.toLocaleString('en-US')}
                </div>
              </div>
              <div className="text-right text-[11px] text-zinc-400 dark:text-zinc-500">
                {conv !== null ? `${conv}% kept` : 'entry'}
              </div>
            </div>
          );
        })}
      </div>

      {active && active.drill && (
        <div className="mt-4 pt-3 border-t border-zinc-200 dark:border-zinc-700">
          <div className="flex items-center justify-between mb-2">
            <div className="text-[13px] font-bold text-zinc-800 dark:text-zinc-100 flex items-center gap-2">
              <span className="w-2.5 h-2.5 rounded-sm inline-block" style={{ background: active.color ?? DEFAULT_COLORS[drill! % DEFAULT_COLORS.length] }} />
              {active.label}
            </div>
            <button
              onClick={() => setDrill(null)}
              className="text-xs font-semibold px-2.5 py-1 rounded-md border border-zinc-200 dark:border-zinc-600 text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200"
            >
              ✕ close
            </button>
          </div>
          <DataTable
            columns={widget.drillColumns ?? [{ key: 'name', label: 'Thing', render: 'id' }]}
            rowsBinding={active.drill}
            ctx={ctx}
            sortKey={widget.drillColumns?.[0]?.key}
          />
        </div>
      )}
    </WidgetCard>
  );
}
