import type { Widget } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';
import { KpiCard } from './KpiCard';
import { Funnel } from './Funnel';
import { BulletChart } from './BulletChart';
import { DockGantt } from './DockGantt';
import { Leaderboard } from './Leaderboard';
import { ExceptionBar } from './ExceptionBar';
import { DataTable } from './DataTable';
import { WidgetCard } from './WidgetCard';

/** Renders a single widget by its `type`. The only place that knows the widget union. */
export function WidgetRenderer({ widget, ctx }: { widget: Widget; ctx: ResolveContext }) {
  switch (widget.type) {
    case 'kpi':
      return <KpiCard widget={widget} ctx={ctx} />;
    case 'funnel':
      return <Funnel widget={widget} ctx={ctx} />;
    case 'bullet':
      return <BulletChart widget={widget} ctx={ctx} />;
    case 'gantt':
      return <DockGantt widget={widget} ctx={ctx} />;
    case 'leaderboard':
      return <Leaderboard widget={widget} ctx={ctx} />;
    case 'exceptionBar':
      return <ExceptionBar widget={widget} ctx={ctx} />;
    case 'table':
      return (
        <WidgetCard title={widget.title} hint={widget.hint}>
          <DataTable
            columns={widget.columns}
            rowsBinding={widget.rows}
            ctx={ctx}
            minWidth={widget.minWidth}
            sortKey={widget.sortKey}
            sortDir={widget.sortDir}
          />
        </WidgetCard>
      );
    default:
      return null;
  }
}
