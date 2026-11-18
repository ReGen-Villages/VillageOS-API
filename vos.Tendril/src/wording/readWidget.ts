import { asNumber, asRows, resolveBinding, type BindingResult, type ResolveContext } from '../api/dashboardApi';
import { slotsOf } from '../api/pageSelector';
import { BASE_LANGUAGE } from '../i18n/languages';
import type { TableColumn, Widget } from '../types/dashboard';
import { figureText, originLines, leverSentence, verdictReadings, workingRuns, type OriginLine, type WorkingRun } from './figureReadings';
import { formatNumberIn } from './formatNumber';

interface Titled {
  title?: string;
}

export type WidgetReading =
  | (Titled & { type: 'kpi'; value: BindingResult; worded: string; unit?: string; origin: OriginLine[] })
  | (Titled & {
      type: 'verdict';
      rows: { label: string; verdicts: { state: string; sentence: string; levers: string[] }[] }[];
    })
  | (Titled & {
      type: 'working';
      rows: { label: string; value: number | null; worded: string; unit?: string; runs: WorkingRun[] }[];
    })
  | (Titled & { type: Widget['type']; columns?: TableColumn[]; slots: { kind: string; result: BindingResult }[] });

/**
 * One widget read out for a reader with no screen. A widget that shows a figure comes back worded as
 * the console words it, from the same functions. Any other widget draws a chart or a table, which no
 * sentence stands for, so it comes back as what each of its bindings resolved to, with the columns a
 * table declares.
 */
export async function readWidget(widget: Widget, context: ResolveContext): Promise<WidgetReading> {
  const language = context.language ?? BASE_LANGUAGE;
  const titled: Titled = widget.title !== undefined ? { title: widget.title } : {};
  switch (widget.type) {
    case 'kpi': {
      const value = await resolveBinding(widget.value, context);
      const origin = widget.origin ? asRows(await resolveBinding(widget.origin, context)) : [];
      const isAWord = typeof value === 'string' && value !== '';
      return {
        ...titled,
        type: 'kpi',
        value,
        worded: figureText(value, widget.format, language),
        ...(widget.unit && !isAWord ? { unit: widget.unit } : {}),
        origin: originLines(origin),
      };
    }
    case 'verdict': {
      const rows = [];
      for (const row of widget.rows) {
        const verdicts = verdictReadings(row, asRows(await resolveBinding(row.verdicts, context)), language);
        rows.push({
          label: row.label,
          verdicts: verdicts.map((verdict) => ({ state: verdict.state, sentence: verdict.sentence, levers: verdict.levers.map(leverSentence) })),
        });
      }
      return { ...titled, type: 'verdict', rows };
    }
    case 'working': {
      const rows = [];
      for (const row of widget.rows) {
        const value = asNumber(await resolveBinding(row.value, context));
        rows.push({
          label: row.label,
          value,
          worded: formatNumberIn(value, row.format, language),
          ...(row.unit ? { unit: row.unit } : {}),
          runs: workingRuns(asRows(await resolveBinding(row.working, context)), language),
        });
      }
      return { ...titled, type: 'working', rows };
    }
    default: {
      const slots = [];
      for (const binding of slotsOf(widget)) slots.push({ kind: binding.kind, result: await resolveBinding(binding, context) });
      return { ...titled, type: widget.type, ...('columns' in widget && widget.columns ? { columns: widget.columns } : {}), slots };
    }
  }
}
