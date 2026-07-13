import type { GanttWidget } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';
import { useBinding } from '../../../hooks/useDashboard';
import { WidgetCard } from './WidgetCard';

interface GanttBar {
  label?: string;
  start: number;
  end: number;
  color?: string;
}
interface GanttRow {
  row: string;
  bars: GanttBar[];
}

/** Schedule/timeline widget: rows of time-positioned bars, 0..1 across the axis. */
export function Gantt({ widget, ctx }: { widget: GanttWidget; ctx: ResolveContext }) {
  const { loading, value } = useBinding(widget.rows, ctx);
  const rows: GanttRow[] = Array.isArray(value) ? (value as unknown as GanttRow[]) : [];
  const now = widget.now;

  return (
    <WidgetCard title={widget.title} hint={widget.hint}>
      {loading ? (
        <div className="py-6 text-center text-xs text-zinc-400">Loading…</div>
      ) : !rows.length ? (
        <div className="py-8 text-center text-xs text-zinc-400 dark:text-zinc-500">
          No schedule data available for this timeline.
        </div>
      ) : (
        <div className="mt-2">
          {widget.ticks && (
            <div className="grid mb-1" style={{ gridTemplateColumns: '66px 1fr' }}>
              <div />
              <div className="flex justify-between text-[10px] text-zinc-400 dark:text-zinc-500">
                {widget.ticks.map((t) => (
                  <span key={t}>{t}</span>
                ))}
              </div>
            </div>
          )}
          {rows.map((r) => (
            <div key={r.row} className="grid items-center h-[22px]" style={{ gridTemplateColumns: '66px 1fr' }}>
              <div className="text-[11px] font-semibold text-zinc-600 dark:text-zinc-300">{r.row}</div>
              <div className="relative h-4 bg-zinc-100 dark:bg-zinc-700 rounded">
                {r.bars.map((b, i) => (
                  <div
                    key={i}
                    title={b.label}
                    className="absolute top-0 h-4 rounded flex items-center px-1.5 text-[9.5px] text-white font-semibold overflow-hidden whitespace-nowrap"
                    style={{
                      left: `${b.start * 100}%`,
                      width: `${Math.max(0, (b.end - b.start) * 100)}%`,
                      background: b.color ?? '#3b82f6',
                    }}
                  >
                    {b.end - b.start > 0.16 ? b.label : ''}
                  </div>
                ))}
                {now !== undefined && (
                  <div className="absolute -top-[3px] h-[22px] w-0.5" style={{ left: `${now * 100}%`, background: 'var(--crit)' }} />
                )}
              </div>
            </div>
          ))}
        </div>
      )}
    </WidgetCard>
  );
}
