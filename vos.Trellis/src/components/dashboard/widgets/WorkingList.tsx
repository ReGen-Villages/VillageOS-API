import { useTranslation } from 'react-i18next';
import type { WorkingRow, WorkingWidget } from '@tendril-ui/tendril';
import type { ResolveContext, Row } from '@tendril-ui/tendril';
import { asRows, asNumber, workingRuns } from '@tendril-ui/tendril';
import { useBindings } from '@tendril-ui/tendril';
import { WidgetCard } from './WidgetCard';
import { formatNumber } from './format';

/** The formula and input names are the model's, never Trellis's, because a client composing either would
 *  describe arithmetic it does not run. A figure the model does not derive shows its value alone, because an
 *  account invented here would read as the model's. */
export function WorkingList({ widget, context }: { widget: WorkingWidget; context: ResolveContext }) {
  const figures = useBindings(widget.rows.map((r) => r.value), context);
  const workings = useBindings(widget.rows.map((r) => r.working), context);

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
  const { t, i18n } = useTranslation();

  return (
    <div>
      <div className="flex items-baseline justify-between gap-4">
        <span className="text-sm font-semibold text-zinc-700 dark:text-zinc-200">{row.label}</span>
        <span className="text-lg font-bold tabular-nums text-zinc-900 dark:text-white">
          {formatNumber(figure, row.format)}
          {row.unit && <span className="text-xs text-zinc-400 dark:text-zinc-500 font-semibold ml-1">{row.unit}</span>}
        </span>
      </div>

      {workingRuns(inputs, i18n.language).map((run, r) => (
        <div key={r}>
          {run.formula && (
            <div className="mt-1 font-mono text-[11px] text-zinc-500 dark:text-zinc-400 overflow-x-auto">
              {run.formula}
            </div>
          )}
          {/* Where the model reduces over members the input's value is held by each of them, so the
              archetype they are of stands where a number would. The figure's own format is not applied
              to an input: the specification declares that format for the figure, and its inputs are rarely in
              the same terms as it. */}
          <table className="mt-1 w-full text-xs">
            <tbody>
              {run.inputs.map((input, i) => (
                <tr key={i}>
                  <td className="py-0.5 font-mono text-zinc-500 dark:text-zinc-400">{input.term}</td>
                  <td className="py-0.5 text-right tabular-nums text-zinc-700 dark:text-zinc-200">
                    {input.memberKind !== undefined ? (
                      <span className="font-mono text-zinc-400 dark:text-zinc-500">{input.memberKind}</span>
                    ) : (
                      input.value
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
