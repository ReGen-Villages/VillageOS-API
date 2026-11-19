import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { StackedSharesCounted, StackedSharesWidget } from '@tendril-ui/tendril';
import type { BindingResult, ResolveContext, Row } from '@tendril-ui/tendril';
import { asRows, nullableNumber, useBindings } from '@tendril-ui/tendril';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { ChartTooltip, HistoryWindow, SeriesTable, ValueGridlines } from './chartChrome';
import { frameOf, PLOT_HEIGHT } from './chartFrame';
import { ANNUAL, monthSlots } from './chartScale';
import { groupsByKey, monthNames, rowLabel, windowOf } from './historySeries';
import { formatNumber } from './format';

const WIDEST_BAR = 28;
/** The axis reads in quarters: 0%, 25%, 50%, 75%, 100%. */
const AXIS_TICKS = [0, 0.25, 0.5, 0.75, 1];
/** A thin ring in the surface colour is the gap between stacked segments. */
const SURFACE_GAP = 1;
const BINDINGS_PER_CLASS = 3;

type DrawnClass = { label: string; colour: string; groups: Map<string, number>; annual: number | undefined };

/** Each declared group's share of the hours counted under a declared group, a month at a time and over the
 *  whole window, in the groups' order; a group nothing was counted under is left out. */
function countedClasses(
  counted: StackedSharesCounted, rows: Row[], byMonth: Map<string, number>, wholeWindow: Map<string, number>, language: string,
): DrawnClass[] {
  const groups = rows
    .map((row) => ({
      id: typeof row.id === 'string' ? row.id.toLowerCase() : '',
      order: nullableNumber(row[counted.orderKey]) ?? Infinity,
      label: rowLabel(row, counted.labelKey, counted.wordingKey, language),
      colour: String(row[counted.colourKey] ?? ''),
    }))
    .filter((group) => group.id !== '')
    .sort((one, other) => one.order - other.order);
  const declared = new Set(groups.map((group) => group.id));
  const monthTotals = new Map<string, number>();
  const hours = new Map<string, Map<string, number>>();
  for (const [key, count] of byMonth) {
    const month = key.slice(0, key.indexOf(','));
    const group = key.slice(key.indexOf(',') + 1).toLowerCase();
    if (!declared.has(group)) continue;
    monthTotals.set(month, (monthTotals.get(month) ?? 0) + count);
    const ofGroup = hours.get(group) ?? new Map<string, number>();
    hours.set(group, ofGroup.set(month, count));
  }
  const wholeTotal = [...wholeWindow].reduce((sum, [key, count]) => sum + (declared.has(key.toLowerCase()) ? count : 0), 0);
  const wholeOf = (id: string) => [...wholeWindow].find(([key]) => key.toLowerCase() === id)?.[1];
  return groups
    .filter((group) => hours.has(group.id))
    .map((group) => {
      const whole = wholeOf(group.id);
      return {
        label: group.label,
        colour: group.colour,
        groups: new Map([...hours.get(group.id)!].map(([month, count]) => [month, count / monthTotals.get(month)!])),
        annual: whole === undefined || wholeTotal === 0 ? undefined : whole / wholeTotal,
      };
    });
}

export function StackedShares({ widget, context }: { widget: StackedSharesWidget; context: ResolveContext }) {
  const { t, i18n } = useTranslation();
  const [measure, measuredWidth] = useElementWidth();
  const [active, setActive] = useState<number | null>(null);

  const listed = widget.counted ? [] : widget.classes;
  const resolved = useBindings([
    ...listed.flatMap((entry) => [entry.share, typeof entry.colour === 'object' ? entry.colour : undefined, entry.annual]),
    widget.counted?.counts, widget.counted?.annual, widget.counted?.groups,
  ], context);
  const countedAt = listed.length * BINDINGS_PER_CLASS;
  const classes: DrawnClass[] = widget.counted
    ? countedClasses(
      widget.counted, asRows(resolved[countedAt + 2]?.value ?? null),
      groupsByKey(resolved[countedAt]?.value ?? null), groupsByKey(resolved[countedAt + 1]?.value ?? null), i18n.language)
    : listed
      .map((entry, at) => ({
        label: entry.label,
        colour: typeof entry.colour === 'object' ? asText(resolved[at * BINDINGS_PER_CLASS + 1]?.value ?? null) : entry.colour,
        groups: groupsByKey(resolved[at * BINDINGS_PER_CLASS]?.value ?? null),
        annual: groupsByKey(resolved[at * BINDINGS_PER_CLASS + 2]?.value ?? null).values().next().value,
      }))
      .filter((entry): entry is DrawnClass => entry.groups.size > 0 && entry.colour !== null);

  const sharesOf = (shareOf: (entry: (typeof classes)[number]) => number | undefined) => classes
    .map((entry) => ({ label: entry.label, colour: entry.colour, share: shareOf(entry) }))
    .filter((entry): entry is { label: string; colour: string; share: number } => entry.share !== undefined);
  const names = monthNames(i18n.language);
  const hasAnnual = classes.some((entry) => entry.annual !== undefined);
  const months = [
    ...names.map((name, at) => ({ name, at, key: String(at + 1), shares: sharesOf((entry) => entry.groups.get(String(at + 1))) })),
    ...(hasAnnual ? [{ name: t('widgets.history.annual'), at: ANNUAL, key: 'all', shares: sharesOf((entry) => entry.annual) }] : []),
  ].filter((month) => month.shares.length > 0);

  const { width, height, plotTop, plotBottom, plotLeft, plotRight, y } = frameOf(measuredWidth, [0, 1]);
  const { slotWidth, centreOf } = monthSlots(plotLeft, plotRight, hasAnnual);
  const barWidth = Math.min(WIDEST_BAR, slotWidth * 0.6);
  const figure = (share: number | undefined) => share === undefined ? '—' : formatNumber(share, 'percent');
  const readOut = (month: (typeof months)[number]) =>
    `${month.name}: ${month.shares.map((entry) => `${entry.label} ${figure(entry.share)}`).join(', ')}`;
  const shown = active === null ? undefined : months.find((month) => month.at === active);

  return (
    <WidgetCard title={widget.title} hint={widget.hint} right={<HistoryWindow window={windowOf(widget.classes[0]?.share)} />}>
      <div ref={measure} className="relative mt-2">
        <svg
          width={width}
          height={height}
          viewBox={`0 0 ${width} ${height}`}
          role="img"
          aria-label={widget.title ?? ''}
          className="block max-w-full"
        >
          <ValueGridlines ticks={AXIS_TICKS} y={y} left={plotLeft} right={plotRight} format="percent" />
          {months.map((month) => {
            const centre = centreOf(month.at);
            const left = centre - barWidth / 2;
            // Segments stack from the axis upward in the order the specification lists the classes, so the
            // first class sits at the bottom of every bar and a reader can follow it across months.
            let stacked = 0;
            return (
              <g
                key={month.at}
                data-month={month.key}
                role="img"
                tabIndex={0}
                aria-label={readOut(month)}
                onFocus={() => setActive(month.at)}
                onBlur={() => setActive(null)}
                onMouseEnter={() => setActive(month.at)}
                onMouseLeave={() => setActive(null)}
                className="outline-none"
              >
                <rect x={left - 6} y={plotTop} width={barWidth + 12} height={PLOT_HEIGHT} fill="transparent" />
                {month.shares.map((entry) => {
                  const bottom = stacked;
                  stacked += entry.share;
                  return (
                    <rect
                      key={entry.label}
                      data-class={entry.label}
                      x={left}
                      y={y(stacked)}
                      width={barWidth}
                      height={Math.max(0, y(bottom) - y(stacked))}
                      fill={entry.colour}
                      stroke="var(--card-bg)"
                      strokeWidth={SURFACE_GAP}
                    />
                  );
                })}
                <text x={centre} y={plotBottom + 15} fontSize={10} textAnchor="middle" fill="var(--chart-ink-muted)">
                  {month.name}
                </text>
                {active === month.at && (
                  <rect x={left - 3} y={plotTop} width={barWidth + 6} height={PLOT_HEIGHT} fill="none" stroke="var(--chart-ink-muted)" strokeWidth={1} rx={3} />
                )}
              </g>
            );
          })}
        </svg>
        {shown && (
          <ChartTooltip left={centreOf(shown.at)} top={plotTop} width={width} title={shown.name}>
            {[...shown.shares].reverse().map((entry) => (
              <div key={entry.label} className="flex items-baseline gap-2">
                <ClassSwatch colour={entry.colour} />
                <span className="font-semibold tabular-nums">{figure(entry.share)}</span>
                <span className="text-zinc-500 dark:text-zinc-400">{entry.label}</span>
              </div>
            ))}
          </ChartTooltip>
        )}
      </div>
      <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-zinc-500 dark:text-zinc-400">
        {classes.map((entry) => (
          <li key={entry.label} className="inline-flex items-center gap-1.5">
            <ClassSwatch colour={entry.colour} />
            {entry.label}
          </li>
        ))}
      </ul>
      <SeriesTable
        caption={widget.title ?? ''}
        columns={classes.map((entry) => entry.label)}
        rows={months.map((month) => ({
          name: month.name,
          cells: classes.map((entry) => figure(month.shares.find((share) => share.label === entry.label)?.share)),
        }))}
      />
    </WidgetCard>
  );
}

/** A colour bound to the model arrives as text; anything else is no colour, and the class is left out
 *  rather than painted in a colour the model did not give it. */
function asText(value: BindingResult): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

function ClassSwatch({ colour }: { colour: string }) {
  return <i className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: colour }} aria-hidden="true" />;
}
