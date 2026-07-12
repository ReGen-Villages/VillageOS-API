import type { ExceptionWidget } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';
import { asNumber } from '../../../api/dashboardApi';
import { useBindings } from '../../../hooks/useDashboard';
import { WidgetCard } from './WidgetCard';

const SEV_TEXT: Record<string, string> = {
  good: 'text-emerald-600 dark:text-emerald-400',
  warn: 'text-amber-600 dark:text-amber-400',
  crit: 'text-red-600 dark:text-red-400',
};
const SEV_BAR: Record<string, string> = { good: 'var(--good)', warn: 'var(--warn)', crit: 'var(--crit)' };

/** Count chips + a stacked proportion bar for an exception / aging breakdown. */
export function ExceptionBar({ widget, ctx }: { widget: ExceptionWidget; ctx: ResolveContext }) {
  const results = useBindings(
    widget.buckets.map((b) => b.value),
    ctx,
  );
  const values = results.map((r) => asNumber(r.value) ?? 0);
  const total = values.reduce((a, b) => a + b, 0) || 1;

  return (
    <WidgetCard title={widget.title} hint={widget.hint}>
      <div className="flex gap-2.5 mt-3 mb-1">
        {widget.buckets.map((b, i) => (
          <div key={b.label} className="flex-1 text-center py-2.5 rounded-lg bg-zinc-50 dark:bg-zinc-700/40 border border-zinc-200 dark:border-zinc-700">
            <div className={`text-[22px] font-bold tabular-nums ${SEV_TEXT[b.severity]}`}>{values[i].toLocaleString('en-US')}</div>
            <div className="text-[10.5px] uppercase tracking-wide text-zinc-400 dark:text-zinc-500">{b.label}</div>
          </div>
        ))}
      </div>
      <div className="h-3.5 rounded-md overflow-hidden flex my-3 bg-zinc-100 dark:bg-zinc-700">
        {widget.buckets.map((b, i) => (
          <div key={b.label} style={{ width: `${(values[i] / total) * 100}%`, background: SEV_BAR[b.severity] }} />
        ))}
      </div>
      {widget.note && <div className="text-[11.5px] text-zinc-500 dark:text-zinc-400 mt-2">{widget.note}</div>}
    </WidgetCard>
  );
}
