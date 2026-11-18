/**
 * What a figure reads as, in words: the one place a KPI card, a verdict list and a working list take
 * their text from, whether a browser draws it or a program with no screen reads it out.
 *
 * Every word of a verdict, an origin and a lever is the model's. These put the figures into that
 * wording and write the numbers the way the language does, and add nothing of their own.
 */
import { nullableNumber, type BindingResult, type Lever, type Row } from '../api/dashboardApi';
import { wordsInLanguage } from '../i18n/termWording';
import type { NumberFormat, VerdictCandidate, VerdictRow } from '../types/dashboard';
import { ORIGIN_KINDS, type OriginKind } from '../types/vos';
import { formatNumberIn } from './formatNumber';
import { originSentence } from './originSentence';
import { verdictSentence } from './verdictSentence';

/** A figure as a card shows it: the word a binding resolved to, or the number in the widget's format. */
export function figureText(value: BindingResult, format: NumberFormat | undefined, language: string): string {
  if (typeof value === 'string' && value !== '') return value;
  return formatNumberIn(typeof value === 'number' ? value : null, format, language);
}

export interface OriginLine {
  origin: OriginKind;
  sentence: string;
}

/** Where a figure came from, one line per Thing an origin binding read. A row carrying no origin and
 *  an origin the page description left unworded say nothing, rather than a blank line that would
 *  read as an answer. */
export function originLines(rows: Row[]): OriginLine[] {
  return rows
    .filter((row) => ORIGIN_KINDS.includes(row.origin as OriginKind) && typeof row.reads === 'string')
    .map((row) => ({
      origin: row.origin as OriginKind,
      sentence: originSentence(row.reads as string, {
        source: typeof row.source === 'string' ? row.source : null,
        resolvedAt: typeof row.resolvedAt === 'string' ? row.resolvedAt : null,
      }),
    }))
    .filter((line) => line.sentence !== '');
}

/** One input a verdict offers to change, with the state's wording split around where the input is
 *  named. `declaredName` is the name the model declares for the input in the reader's language; an
 *  input with none is named by the model's own word for it, and by its members' kind where each
 *  member holds it. `after` is absent where the wording names no input. */
export interface LeverReading {
  lever: Lever;
  before: string;
  after?: string;
  declaredName?: string;
}

export interface VerdictReading {
  state: string;
  sentence: string;
  levers: LeverReading[];
}

/** Each verdict a row's binding resolved to, as its sentence and the inputs it offers. A row bound
 *  to something other than a verdict resolves to rows with no wording on them, and reads as nothing. */
export function verdictReadings(row: VerdictRow, verdicts: Row[], language: string): VerdictReading[] {
  const candidates: VerdictCandidate[] = row.verdicts.kind === 'verdict' ? row.verdicts.states : [];
  return verdicts
    .map((verdict) => {
      const offered = Array.isArray(verdict.levers) ? (verdict.levers as Lever[]) : [];
      const wording = candidates.find((candidate) => candidate.state === verdict.state)?.levers;
      return {
        state: String(verdict.state),
        sentence: verdictSentence(
          typeof verdict.reads === 'string' ? verdict.reads : '',
          { value: nullableNumber(verdict.value), target: nullableNumber(verdict.target) },
          language,
          row.format,
          row.unit,
        ),
        levers: wording
          ? offered.map((lever) => leverReading(lever.direction === 'raise' ? wording.raise : wording.lower, lever, language))
          : [],
      };
    })
    .filter((reading) => reading.sentence !== '');
}

function leverReading(wording: string, lever: Lever, language: string): LeverReading {
  const [before, after] = wording.split('{term}', 2);
  const declaredName = wordsInLanguage(lever.words, language);
  return {
    lever,
    before,
    ...(after !== undefined ? { after } : {}),
    ...(declaredName !== undefined ? { declaredName } : {}),
  };
}

/** A lever as one line of text, for a reader with no way to set the input's name apart. */
export function leverSentence(reading: LeverReading): string {
  if (reading.after === undefined) return reading.before;
  const named = reading.declaredName
    ?? (reading.lever.memberArchetype ? `${reading.lever.term} · ${reading.lever.memberArchetype}` : reading.lever.term);
  return `${reading.before}${named}${reading.after}`;
}

/** One input a figure was worked out from: its value on the Thing computing the figure, or the kind
 *  of the members that each hold it where the model reduces over members. */
export interface WorkingInput {
  term: string;
  value?: string;
  memberKind?: string;
}

export interface WorkingRun {
  formula: string | null;
  inputs: WorkingInput[];
}

/** A working binding's rows as runs of consecutive inputs sharing one formula. A walk reaching
 *  several Things computing the figure returns each one's rows in turn, and a formula accounts only
 *  for the rows it produced. An input is written plainly, not in the figure's format: the page
 *  description declares that format for the figure, and its inputs are rarely in the same terms. */
export function workingRuns(inputs: Row[], language: string): WorkingRun[] {
  const runs: WorkingRun[] = [];
  for (const input of inputs) {
    const formula = typeof input.formula === 'string' ? input.formula : null;
    const read: WorkingInput = 'memberArchetype' in input
      ? { term: String(input.term), memberKind: input.memberArchetype ? String(input.memberArchetype) : '' }
      : { term: String(input.term), value: formatNumberIn(input.value as number | null, undefined, language) };
    const current = runs[runs.length - 1];
    if (current && current.formula === formula) current.inputs.push(read);
    else runs.push({ formula, inputs: [read] });
  }
  return runs;
}
