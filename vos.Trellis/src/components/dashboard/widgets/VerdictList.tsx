import { useTranslation } from 'react-i18next';
import type { VerdictRow, VerdictWidget } from '@tendril-ui/tendril';
import type { LeverReading, ResolveContext, Row } from '@tendril-ui/tendril';
import { asRows, verdictReadings } from '@tendril-ui/tendril';
import { useBindings } from '@tendril-ui/tendril';
import { WidgetCard } from './WidgetCard';

/**
 * Each judged quantity read as a sentence: what was measured, what it was judged against, and the
 * verdict in words.
 *
 * Every word of every verdict comes from the model — Trellis supplies the layout and the number
 * formatting and nothing else. A balance the model returned no verdict for is left unsaid rather
 * than filled with a dash: the client has no wording of its own to put there, and a placeholder
 * would read as an answer.
 */
export function VerdictList({ widget, context }: { widget: VerdictWidget; context: ResolveContext }) {
  const results = useBindings(
    widget.rows.map((r) => r.verdicts),
    context,
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
  const { i18n } = useTranslation();
  const readings = verdictReadings(row, verdicts, i18n.language);

  return (
    <div>
      <div className="text-[11px] font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400">
        {row.label}
      </div>
      {readings.map((read) => (
        <div key={read.state}>
          <p className="text-[13px] leading-relaxed text-zinc-700 dark:text-zinc-200">
            {read.sentence}
          </p>
          {read.levers.map((reading) => (
            <p key={`${reading.lever.term} ${reading.lever.memberArchetype ?? ''}`}
               className="text-xs leading-relaxed text-zinc-500 dark:text-zinc-400">
              <LeverLine reading={reading} />
            </p>
          ))}
        </div>
      ))}
    </div>
  );
}

/** The state's wording around the input's name. A name the model declares needs no kind beside it,
 *  because it was declared for the property on that kind; an input with none is drawn by the model's
 *  word, the way the working widget draws it, with the members' kind beside an input each member holds. */
function LeverLine({ reading }: { reading: LeverReading }) {
  const { lever, before, after, declaredName } = reading;
  const name = declaredName !== undefined ? (
    <span className="text-zinc-600 dark:text-zinc-300">{declaredName}</span>
  ) : (
    <>
      <span className="font-mono text-zinc-600 dark:text-zinc-300">{lever.term}</span>
      {lever.memberArchetype && (
        <span className="font-mono text-zinc-400 dark:text-zinc-500"> · {lever.memberArchetype}</span>
      )}
    </>
  );
  return after === undefined ? <>{before}</> : <>{before}{name}{after}</>;
}
