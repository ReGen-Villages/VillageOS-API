import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { StackedSharesWidget } from '../../../types/dashboard';
import type { BindingResult, ResolveContext } from '../../../api/dashboardApi';
import { useBindings } from '../../../hooks/useDashboard';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { ChartTooltip, HistoryWindow, SeriesTable } from './chartChrome';
import { linearScale } from './chartScale';
import { groupsByKey, monthNames, windowOf } from './historySeries';
import { formatNumber } from './format';

const PLOT_HEIGHT = 220;
const TOP_GUTTER = 8;
const AXIS_BAND = 22;
const LEFT_GUTTER = 40;
const RIGHT_GUTTER = 8;
const FALLBACK_WIDTH = 480;
const WIDEST_BAR = 28;
/** The axis reads in quarters: 0%, 25%, 50%, 75%, 100%. */
const AXIS_TICKS = [0, 0.25, 0.5, 0.75, 1];
/** A thin ring in the surface colour is the gap between stacked segments. */
const SURFACE_GAP = 1;

export function StackedShares({ widget, ctx }: { widget: StackedSharesWidget; ctx: ResolveContext }) {
  const { i18n } = useTranslation();
  const [measure, measuredWidth] = useElementWidth();
  const [active, setActive] = useState<number | null>(null);

  const resolved = useBindings(
    widget.classes.flatMap((entry) => [entry.share, typeof entry.colour === 'object' ? entry.colour : undefined]), ctx);
  const classes = widget.classes
    .map((entry, at) => ({
      label: entry.label,
      colour: typeof entry.colour === 'object' ? asText(resolved[at * 2 + 1]?.value ?? null) : entry.colour,
      groups: groupsByKey(resolved[at * 2]?.value ?? null),
    }))
    .filter((entry): entry is { label: string; colour: string; groups: Map<string, number> } =>
      entry.groups.size > 0 && entry.colour !== null);

  const names = monthNames(i18n.language);
  const months = names
    .map((name, at) => ({
      name,
      at,
      shares: classes
        .map((entry) => ({ label: entry.label, colour: entry.colour, share: entry.groups.get(String(at + 1)) }))
        .filter((entry): entry is { label: string; colour: string; share: number } => entry.share !== undefined),
    }))
    .filter((month) => month.shares.length > 0);

  const width = measuredWidth || FALLBACK_WIDTH;
  const plotTop = TOP_GUTTER;
  const plotBottom = TOP_GUTTER + PLOT_HEIGHT;
  const y = linearScale([0, 1], [plotBottom, plotTop]);
  const slotWidth = (width - LEFT_GUTTER - RIGHT_GUTTER) / 12;
  const barWidth = Math.min(WIDEST_BAR, slotWidth * 0.6);
  const centreOf = (at: number) => LEFT_GUTTER + slotWidth * (at + 0.5);
  const figure = (share: number | undefined) => share === undefined ? '—' : formatNumber(share, 'percent');
  const readOut = (month: (typeof months)[number]) =>
    `${month.name}: ${month.shares.map((entry) => `${entry.label} ${figure(entry.share)}`).join(', ')}`;
  const shown = active === null ? undefined : months.find((month) => month.at === active);

  return (
    <WidgetCard title={widget.title} hint={widget.hint} right={<HistoryWindow window={windowOf(widget.classes[0]?.share)} />}>
      <div ref={measure} className="relative mt-2">
        <svg
          width={width}
          height={plotBottom + AXIS_BAND}
          viewBox={`0 0 ${width} ${plotBottom + AXIS_BAND}`}
          role="img"
          aria-label={widget.title ?? ''}
          className="block max-w-full"
        >
          {AXIS_TICKS.map((tick) => (
            <g key={tick}>
              <line x1={LEFT_GUTTER} x2={width - RIGHT_GUTTER} y1={y(tick)} y2={y(tick)} stroke="var(--chart-grid)" strokeWidth={1} />
              <text x={LEFT_GUTTER - 6} y={y(tick) + 3.5} fontSize={10} textAnchor="end" fill="var(--chart-ink-muted)" className="tabular-nums">
                {formatNumber(tick, 'percent')}
              </text>
            </g>
          ))}
          {months.map((month) => {
            const centre = centreOf(month.at);
            const left = centre - barWidth / 2;
            // Segments stack from the axis upward in the order the spec lists the classes, so the
            // first class sits at the bottom of every bar and a reader can follow it across months.
            let stacked = 0;
            return (
              <g
                key={month.at}
                data-month={String(month.at + 1)}
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
