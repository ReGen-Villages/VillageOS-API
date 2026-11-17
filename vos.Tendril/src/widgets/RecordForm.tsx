import { useState, type FormEvent } from 'react';
import type { FormWidget } from '../types/dashboard';
import type { ResolveContext } from '../api/dashboardApi';
import { postToEndpoint, type EndpointAnswer } from '../api/dashboardWrites';
import { useAskedOptions } from '../hooks/useAskedOptions';
import { AskedFields } from './AskedFields';
import { ACTION_CLASS, FIELD_BORDER_CLASS, LABEL_CLASS, MUTED_CLASS, REFUSAL_CLASS } from './roleClasses';
import { complete, formRequest, type Entered } from './writeRequest';

/**
 * The widget that records something nothing on the page lists yet. An action list decides about a
 * row it was handed; this one mints the row.
 *
 * It posts what was filled in to the endpoint the specification names, under the act's name, and nothing
 * else: no actor, and no Thing built here. The endpoint lays down what the model already
 * understands, so what the write summons is the model's to decide from the relationships it now holds.
 */
export function RecordForm({ widget, context }: { widget: FormWidget; context: ResolveContext }) {
  const options = useAskedOptions(widget.fields, context);

  const [entered, setEntered] = useState<Entered>({});
  const [refusal, setRefusal] = useState('');
  const [taken, setTaken] = useState('');
  const [covered, setCovered] = useState('');
  const [writing, setWriting] = useState(false);
  const ready = complete(widget.fields, entered);

  /** What the endpoint says about the fields as they stand, under whichever of the form's two acts
   *  is being made. The wording is the endpoint's own: it knows what a choice covers and what a
   *  press took, and a client that reworded either would be guessing at half of it. */
  async function ask(act: string): Promise<EndpointAnswer> {
    setWriting(true);
    setRefusal('');
    try {
      return await postToEndpoint(context.reads, widget.writes.via, formRequest(widget, entered, act), context.wordRefusal);
    } finally {
      setWriting(false);
    }
  }

  async function look() {
    if (writing || !widget.preview) return;
    setTaken('');
    setCovered('');
    const answer = await ask(widget.preview.act);
    if (answer.error) setRefusal(answer.error);
    else setCovered(answer.said ?? '');
  }

  /** What a look said is about the choice as it stood, so editing any field takes it off the
   *  screen. Left up, it would read as a sentence about the choice on screen. */
  function fill(next: Entered) {
    setEntered(next);
    setCovered('');
  }

  async function record() {
    if (writing) return;
    setTaken('');
    setCovered('');
    const answer = await ask(widget.writes.act);
    // A refusal keeps what was typed, so a mistyped digit costs the rest of the form nothing.
    // A press the endpoint took clears it: the next one is about something else.
    if (answer.error) setRefusal(answer.error);
    else {
      setTaken(answer.said ?? widget.submit);
      setEntered({});
      context.wrote?.();
    }
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    if (ready) void record();
  }

  return (
    <form className="mt-2 flex flex-col gap-2" onSubmit={submit}>
      <AskedFields fields={widget.fields} options={options} entered={entered} onChange={fill} disabled={writing} />
      {refusal && <p className={`text-sm ${REFUSAL_CLASS}`}>{refusal}</p>}
      {covered && <p className={`text-sm ${MUTED_CLASS}`}>{covered}</p>}
      {taken && <p className={`text-sm ${MUTED_CLASS}`}>{taken}</p>}
      <div className="flex items-center gap-2">
        {widget.preview && (
          <button
            type="button"
            disabled={writing}
            onClick={() => void look()}
            className={`rounded border ${FIELD_BORDER_CLASS} px-3 py-1.5 text-sm ${LABEL_CLASS} disabled:opacity-50`}
          >
            {widget.preview.label}
          </button>
        )}
        <button
          type="submit"
          disabled={writing || !ready}
          className={`rounded px-3 py-1.5 text-sm ${ACTION_CLASS} disabled:opacity-50`}
        >
          {widget.submit}
        </button>
      </div>
    </form>
  );
}
