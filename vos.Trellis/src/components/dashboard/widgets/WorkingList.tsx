import { useTranslation } from 'react-i18next';
import type { WorkingRow, WorkingWidget } from '../../../types/dashboard';
import type { ResolveContext, Row } from '../../../api/dashboardApi';
import { asRows, asNumber } from '../../../api/dashboardApi';
import { useBindings } from '../../../hooks/useDashboard';
import { WidgetCard } from './WidgetCard';
import { formatNumber } from './format';

/**
 * What each figure was worked out from: the formula the model holds, and every input it reads — with
 * that input's own value where the Thing computing the figure holds one.
 *
 * The formula and the input names are the model's, never Trellis's — a client that composed either
 * would be describing arithmetic it does not run. A figure the model does not derive resolves to no
 * working and shows its value alone: an account invented here would read as the model's own.
 */
export function WorkingList({ widget, ctx }: { widget: WorkingWidget; ctx: ResolveContext }) {
  const figures = useBindings(widget.rows.map((r) => r.value), ctx);
  const workings = useBindings(widget.rows.map((r) => r.working), ctx);

  return (
    <WidgetCard title={widget.title} hint={widget.hint}>
      <div className="mt-2 flex flex-col gap-4">
        {widget.rows.map((row, i) => (
          <WorkingRowView
            key={row.label}
            row={row}
            figure={asNumber(figures[i].value)}
            inputs={asRows(workings[i].value)}
            resolving={workings[i].loading}
          />
        ))}
      </div>
    </WidgetCard>
  );
}

/** Consecutive inputs sharing one formula. A `via` walk reaching several Things computing the figure
 *  returns each one's rows in turn, and a formula accounts only for the rows it produced. */
function runsOfOneFormula(inputs: Row[]): { formula: string | null; inputs: Row[] }[] {
  const runs: { formula: string | null; inputs: Row[] }[] = [];
  for (const input of inputs) {
    const formula = typeof input.formula === 'string' ? input.formula : null;
    const current = runs[runs.length - 1];
    if (current && current.formula === formula) current.inputs.push(input);
    else runs.push({ formula, inputs: [input] });
  }
  return runs;
}

function WorkingRowView({
  row,
  figure,
  inputs,
  resolving,
}: {
  row: WorkingRow;
  figure: number | null;
  inputs: Row[];
  resolving: boolean;
}) {
  const { t } = useTranslation();

  return (
    <div>
      <div className="flex items-baseline justify-between gap-4">
        <span className="text-sm font-semibold text-zinc-700 dark:text-zinc-200">{row.label}</span>
        <span className="text-lg font-bold tabular-nums text-zinc-900 dark:text-white">
          {formatNumber(figure, row.format)}
          {row.unit && <span className="text-xs text-zinc-400 dark:text-zinc-500 font-semibold ml-1">{row.unit}</span>}
        </span>
      </div>

      {runsOfOneFormula(inputs).map((run, r) => (
        <div key={r}>
          {run.formula && (
            <div className="mt-1 font-mono text-[11px] text-zinc-500 dark:text-zinc-400 overflow-x-auto">
              {run.formula}
            </div>
          )}
          {/* Where the model reduces over members the input's value is held by each of them, so the
              archetype they are of stands where a number would. The figure's own format is not applied
              to an input: the spec declares that format for the figure, and its inputs are rarely in
              the same terms as it. */}
          <table className="mt-1 w-full text-xs">
            <tbody>
              {run.inputs.map((input, i) => (
                <tr key={i}>
                  <td className="py-0.5 font-mono text-zinc-500 dark:text-zinc-400">{String(input.term)}</td>
                  <td className="py-0.5 text-right tabular-nums text-zinc-700 dark:text-zinc-200">
                    {'memberArchetype' in input ? (
                      <span className="font-mono text-zinc-400 dark:text-zinc-500">
                        {input.memberArchetype ? String(input.memberArchetype) : ''}
                      </span>
                    ) : (
                      formatNumber(input.value as number | null)
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ))}

      {!resolving && inputs.length === 0 && (
        <div className="mt-1 text-[11px] text-zinc-400 dark:text-zinc-500">{t('widgets.working.notDerived')}</div>
      )}
    </div>
  );
}
