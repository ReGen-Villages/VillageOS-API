import { useState } from 'react';
import type { ActionChoice, ActionWidget } from '../../../types/dashboard';
import { asRows, type ResolveContext, type Row } from '../../../api/dashboardApi';
import { postToEndpoint } from '../../../api/dashboardWrites';
import { useBindings } from '../../../hooks/useDashboard';
import { useAskedOptions } from '../../../hooks/useAskedOptions';
import { WidgetCard } from './WidgetCard';
import { AskedFields } from './AskedFields';
import { actionRequest, complete, nameOf, type Entered } from './writeRequest';

/**
 * The one widget that acts on what it lists. Every other widget reports what the model holds;
 * this records what somebody decided about it.
 *
 * A press posts the row, the choice and whatever the row was asked for to the endpoint the spec
 * names. It sends no actor: the session the request travels under is the only answer to who is
 * asking, and a body naming a person could name somebody else. What the write summons is not
 * decided here either: the endpoint lays down an edge or a Fact and the model decides what that
 * wakes, which is why nothing on this page knows a handler's name.
 */
export function ActionList({ widget, ctx }: { widget: ActionWidget; ctx: ResolveContext }) {
  const [results] = useBindings([widget.rows], ctx);
  const rows = asRows(results?.value);
  const asks = widget.asks ?? [];
  const options = useAskedOptions(asks, ctx);

  const [refusal, setRefusal] = useState('');
  // Which row is mid-write. A second press before the first answers would record the decision
  // twice against the same row.
  const [writing, setWriting] = useState<string | null>(null);
  const [decided, setDecided] = useState<Record<string, string>>({});
  const [entered, setEntered] = useState<Record<string, Entered>>({});

  async function press(row: Row, choice: ActionChoice) {
    if (writing) return;
    const id = String(row.id ?? '');
    setWriting(id);
    setRefusal('');
    try {
      const answer = await postToEndpoint(ctx.reads, widget.writes.via, actionRequest(widget, row, choice, entered[id] ?? {}));
      if (answer.error) setRefusal(answer.error);
      else setDecided((was) => ({ ...was, [id]: answer.said ?? choice.label }));
    } finally {
      setWriting(null);
    }
  }

  return (
    <WidgetCard title={widget.title} hint={widget.hint}>
      {refusal && <p className="mt-2 text-sm text-red-600 dark:text-red-400">{refusal}</p>}
      <div className="mt-2 flex flex-col gap-3">
        {rows.map((row) => {
          const id = String(row.id ?? '');
          const already = decided[id];
          const asked = entered[id] ?? {};
          return (
            <div key={id} className="flex flex-wrap items-center gap-2" data-decided={Boolean(already)}>
              <span className="min-w-40 text-sm text-zinc-900 dark:text-zinc-100">{nameOf(widget, row)}</span>
              <span className="flex-1 text-sm text-zinc-500 dark:text-zinc-400">
                {(widget.shows ?? [])
                  .map((key) => row[key])
                  .filter((value) => value !== null && value !== undefined && value !== '')
                  .map((value) => String(value))
                  .join(' · ')}
              </span>
              {already ? (
                <span className="text-sm text-zinc-500 dark:text-zinc-400">{already}</span>
              ) : (
                <>
                  {asks.length > 0 && (
                    <AskedFields
                      fields={asks}
                      options={options}
                      entered={asked}
                      onChange={(next) => setEntered((was) => ({ ...was, [id]: next }))}
                      disabled={writing !== null}
                      inline
                    />
                  )}
                  {widget.writes.choices.map((choice) => (
                    <button
                      key={choice.label}
                      type="button"
                      disabled={writing !== null || !complete(asks, asked)}
                      onClick={() => void press(row, choice)}
                      className="rounded px-3 py-1.5 text-sm bg-emerald-600 text-white hover:bg-emerald-700 disabled:opacity-50"
                    >
                      {choice.label}
                    </button>
                  ))}
                </>
              )}
            </div>
          );
        })}
      </div>
    </WidgetCard>
  );
}
