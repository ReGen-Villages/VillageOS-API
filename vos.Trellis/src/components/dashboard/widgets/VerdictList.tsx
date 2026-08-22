import type { VerdictRow, VerdictWidget } from '../../../types/dashboard';
import type { ResolveContext, Row } from '../../../api/dashboardApi';
import { asRows, nullableNumber } from '../../../api/dashboardApi';
import { useBindings } from '../../../hooks/useDashboard';
import { WidgetCard } from './WidgetCard';
import { verdictSentence } from './verdictSentence';

/**
 * Each judged quantity read as a sentence: what was measured, what it was judged against, and the
 * verdict in words.
 *
 * Every word of every verdict comes from the model — Trellis supplies the layout and the number
 * formatting and nothing else. A balance the model returned no verdict for is left unsaid rather
 * than filled with a dash: the client has no wording of its own to put there, and a placeholder
 * would read as an answer.
 */
export function VerdictList({ widget, ctx }: { widget: VerdictWidget; ctx: ResolveContext }) {
  const results = useBindings(
    widget.rows.map((r) => r.verdicts),
    ctx,
  );

  return (
    <WidgetCard title={widget.title} hint={widget.hint}>
      <div className="mt-2 flex flex-col gap-3">
        {widget.rows.map((row, i) => (
          <VerdictRowView key={row.label} row={row} verdicts={asRows(results[i].value)} />
        ))}
      </div>
    </WidgetCard>
  );
}

function VerdictRowView({ row, verdicts }: { row: VerdictRow; verdicts: Row[] }) {
  const sentences = verdicts
    .map((verdict) => ({
      state: String(verdict.state),
      sentence: verdictSentence(
        typeof verdict.reads === 'string' ? verdict.reads : '',
        { value: nullableNumber(verdict.value), target: nullableNumber(verdict.target) },
        row.format,
        row.unit,
      ),
    }))
    // A row bound to something other than a verdict resolves to rows with no wording on them. There
    // is no sentence to read, so it draws none rather than a run of empty lines.
    .filter((read) => read.sentence !== '');

  return (
    <div>
      <div className="text-[11px] font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400">
        {row.label}
      </div>
      {sentences.map((read) => (
        <p key={read.state} className="text-[13px] leading-relaxed text-zinc-700 dark:text-zinc-200">
          {read.sentence}
        </p>
      ))}
    </div>
  );
}
