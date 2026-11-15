import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { ActionWidget, AskedValue } from '../types/dashboard';
import { asRows, type ResolveContext, type Row } from '../api/dashboardApi';
import { postToEndpoint, type EndpointAnswer } from '../api/dashboardWrites';
import { PROPERTY_NAME_WORDING } from '../api/propertyNames';
import { useBindings } from '../hooks/useBinding';
import { useAskedOptions } from '../hooks/useAskedOptions';
import { wordingIn, wordsInLanguage } from '../i18n/termWording';
import { tendrilWord } from '../i18n/words';
import { AskedFields } from './AskedFields';
import { actionRequest, asksOf, complete, nameOf, offeredChoices, routeFor, type Entered, type OfferedChoice } from './writeRequest';

/**
 * The one widget that acts on what it lists. Every other widget reports what the model holds;
 * this records what somebody decided about it.
 *
 * A press posts the row, the choice and whatever the row was asked for to the route the choice or
 * the specification names, or relates the row to the Thing a choice the model offered stands for.
 * It sends no actor: the session the request travels under is the only answer to who is asking,
 * and a body naming a person could name somebody else. What the write summons is not decided here
 * either: the write lays down a relationship or a Fact and the model decides what that wakes, which
 * is why nothing on this page knows a handler's name.
 */
export function ActionList({ widget, context }: { widget: ActionWidget; context: ResolveContext }) {
  const { i18n } = useTranslation();
  const [results, drawnResults, decidedResults] = useBindings(
    [widget.rows, widget.writes.choicesFrom?.from, widget.decided?.rows],
    context,
  );
  const rows = asRows(results?.value);
  const drawn = asRows(drawnResults?.value);
  const decidedRows = asRows(decidedResults?.value);

  const offered = useMemo(
    () => offeredChoices(widget, drawn, (row) => wordsInLanguage(wordingIn(row[PROPERTY_NAME_WORDING]) ?? undefined, i18n.language) ?? String(row.name ?? '')),
    [widget, drawn, i18n.language],
  );
  const widgetAsks = useMemo(() => widget.asks ?? [], [widget.asks]);
  const everyAsk = useMemo(
    () => [...widgetAsks, ...widget.writes.choices.flatMap((choice) => choice.asks ?? [])],
    [widgetAsks, widget.writes.choices],
  );
  const everyOption = useAskedOptions(everyAsk, context);
  const optionsFor = (fields: AskedValue[]) => fields.map((field) => everyOption[everyAsk.indexOf(field)] ?? []);

  const [refusal, setRefusal] = useState('');
  // Which row is mid-write. A second press before the first answers would record the decision
  // twice against the same row.
  const [writing, setWriting] = useState<string | null>(null);
  const [decided, setDecided] = useState<Record<string, string>>({});
  const [entered, setEntered] = useState<Record<string, Entered>>({});
  const [asking, setAsking] = useState<{ rowId: string; offered: OfferedChoice } | null>(null);
  const [showingDecided, setShowingDecided] = useState(false);

  async function write(row: Row, choice: OfferedChoice, asked: Entered): Promise<EndpointAnswer> {
    const route = routeFor(widget, choice);
    if (route) return postToEndpoint(context.reads, route, actionRequest(widget, row, choice.choice, asked, route));
    const predicateId = context.index.predicateNameToId.get(widget.writes.choicesFrom?.predicate ?? '');
    if (!predicateId || !context.reads.relate) return { error: tendrilWord('cannotRelate', { predicate: widget.writes.choicesFrom?.predicate ?? '' }) };
    try {
      await context.reads.relate(String(row.id), predicateId, choice.drawnId!);
      return {};
    } catch (refused) {
      return { error: refused instanceof Error ? refused.message : String(refused) };
    }
  }

  async function press(row: Row, choice: OfferedChoice) {
    if (writing) return;
    const id = String(row.id ?? '');
    setWriting(id);
    setRefusal('');
    try {
      const answer = await write(row, choice, entered[id] ?? {});
      if (answer.error) {
        setRefusal(answer.error);
        return;
      }
      if (widget.writes.stamps && context.reads.stampTime) await context.reads.stampTime(id, widget.writes.stamps, new Date());
      setDecided((was) => ({ ...was, [id]: answer.said ?? choice.choice.label }));
      setAsking(null);
      context.wrote?.();
    } catch (refused) {
      setRefusal(refused instanceof Error ? refused.message : String(refused));
    } finally {
      setWriting(null);
    }
  }

  function fieldsFor(rowId: string, fields: AskedValue[]) {
    return (
      <AskedFields
        fields={fields}
        options={optionsFor(fields)}
        entered={entered[rowId] ?? {}}
        onChange={(next) => setEntered((was) => ({ ...was, [rowId]: next }))}
        disabled={writing !== null}
        inline
      />
    );
  }

  const button = (key: string, label: string, disabled: boolean, onClick: () => void) => (
    <button
      key={key}
      type="button"
      disabled={disabled}
      onClick={onClick}
      className="rounded px-3 py-1.5 text-sm bg-emerald-600 text-white hover:bg-emerald-700 disabled:opacity-50"
    >
      {label}
    </button>
  );

  const shown = (row: Row) =>
    (widget.shows ?? [])
      .map((key) => row[key])
      .filter((value) => value !== null && value !== undefined && value !== '')
      .map((value) => String(value))
      .join(' · ');

  return (
    <>
      {refusal && <p className="mt-2 text-sm text-red-600 dark:text-red-400">{refusal}</p>}
      {widget.decided && (
        <label className="mt-2 flex items-center gap-2 text-sm text-zinc-600 dark:text-zinc-300">
          <input type="checkbox" checked={showingDecided} onChange={(event) => setShowingDecided(event.target.checked)} />
          {tendrilWord('showDecided')}
        </label>
      )}
      <div className="mt-2 flex flex-col gap-3">
        {rows.map((row) => {
          const id = String(row.id ?? '');
          const already = decided[id];
          const asked = entered[id] ?? {};
          const pressable = !already || widget.writes.repeatable;
          const askingHere = asking?.rowId === id ? asking.offered : null;
          return (
            <div key={id} className="flex flex-wrap items-center gap-2" data-decided={Boolean(already)}>
              <span className="min-w-40 text-sm text-zinc-900 dark:text-zinc-100">{nameOf(widget, row)}</span>
              <span className="flex-1 text-sm text-zinc-500 dark:text-zinc-400">{shown(row)}</span>
              {already && <span className="text-sm text-zinc-500 dark:text-zinc-400">{already}</span>}
              {pressable && askingHere && (
                <>
                  {fieldsFor(id, asksOf(widget, askingHere.choice))}
                  {button('confirm', askingHere.choice.label, writing !== null || !complete(asksOf(widget, askingHere.choice), asked), () => void press(row, askingHere))}
                  <button type="button" onClick={() => setAsking(null)} className="rounded px-3 py-1.5 text-sm text-zinc-600 dark:text-zinc-300">
                    {tendrilWord('cancel')}
                  </button>
                </>
              )}
              {pressable && !askingHere && (
                <>
                  {widgetAsks.length > 0 && fieldsFor(id, widgetAsks)}
                  {offered.map((choice) => {
                    const ownAsks = choice.choice.asks;
                    const key = choice.drawnId ?? choice.choice.label;
                    if (ownAsks) return button(key, choice.choice.label, writing !== null, () => setAsking({ rowId: id, offered: choice }));
                    return button(key, choice.choice.label, writing !== null || !complete(widgetAsks, asked), () => void press(row, choice));
                  })}
                </>
              )}
            </div>
          );
        })}
        {showingDecided &&
          decidedRows.map((row) => (
            <div key={String(row.id ?? '')} className="flex flex-wrap items-center gap-2" data-decided>
              <span className="min-w-40 text-sm text-zinc-900 dark:text-zinc-100">{nameOf(widget, row)}</span>
              <span className="flex-1 text-sm text-zinc-500 dark:text-zinc-400">{shown(row)}</span>
              <span className="text-sm text-zinc-500 dark:text-zinc-400">{String(row[widget.decided!.decision] ?? '')}</span>
            </div>
          ))}
      </div>
    </>
  );
}
