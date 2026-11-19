import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { Binding, BoxPlotWidget, BoxSeries } from '@tendril-ui/tendril';
import type { ResolveContext } from '@tendril-ui/tendril';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { AxisUnit, ChartBands, ChartTooltip, HistoryWindow, SeriesTable, ValueGridlines } from './chartChrome';
import { frameOf, PLOT_HEIGHT } from './chartFrame';
import { monthSlots } from './chartScale';
import { windowOf } from './historySeries';
import { formatNumber } from './format';
import { useMonthlyFigures, type Period } from './useMonthlyFigures';

/** The five figures from the bottom up, the order the table reads them in. */
export const PARTS = ['whiskerLow', 'boxLow', 'median', 'boxHigh', 'whiskerHigh'] as const satisfies readonly (keyof BoxSeries)[];
type Part = (typeof PARTS)[number];

const WIDEST_BOX = 24;
const CAP_SHARE = 0.5;

/** The percentile a part's binding asks for, where its last step states one as a number. */
function percentileOf(binding: Binding | undefined): number | null {
  if (binding?.kind !== 'history') return null;
  const last = binding.steps.at(-1);
  return last?.function === 'Percentile' && typeof last.percentile === 'number' ? last.percentile : null;
}

export function BoxPlot({ widget, context }: { widget: BoxPlotWidget; context: ResolveContext }) {
  const { t } = useTranslation();
  const [measure, measuredWidth] = useElementWidth();
  const [active, setActive] = useState<number | null>(null);

  const { periods, bands, domain } = useMonthlyFigures(PARTS, widget, context);
  const { width, height, plotTop, plotBottom, plotLeft, plotRight, y, ticks } = frameOf(measuredWidth, domain);
  const { slotWidth, centreOf } = monthSlots(plotLeft, plotRight, !!widget.annual);
  const boxWidth = Math.min(WIDEST_BOX, slotWidth * 0.6);
  const figure = (value: number | undefined) =>
    value === undefined ? '—' : `${formatNumber(value, widget.format)}${widget.unit ? ` ${widget.unit}` : ''}`;
  const label = (part: Part) => {
    const percentile = percentileOf(widget.months[part]);
    return percentile === null ? t(`widgets.boxPlot.${part}`) : t('widgets.boxPlot.percentile', { value: percentile });
  };
  const topDown = [...PARTS].reverse();
  const readOut = (period: Period<Part>) =>
    `${period.name}: ${topDown.filter((part) => period.figures[part] !== undefined)
      .map((part) => `${label(part)} ${figure(period.figures[part])}`).join(', ')}`;
  const shown = active === null ? undefined : periods.find((period) => period.at === active);

  return (
    <WidgetCard title={widget.title} hint={widget.hint} right={<HistoryWindow window={windowOf(widget.months.median)} />}>
      <div ref={measure} className="relative mt-2">
        <svg
          width={width}
          height={height}
          viewBox={`0 0 ${width} ${height}`}
          role="img"
          aria-label={widget.title ?? ''}
          className="block max-w-full"
        >
          <ChartBands bands={bands} domain={domain} y={y} left={plotLeft} right={plotRight} />
          <ValueGridlines ticks={ticks} y={y} left={plotLeft} right={plotRight} />
          <AxisUnit unit={widget.unit} left={plotLeft} plotBottom={plotBottom} />
          {widget.annual && (
            <line
              x1={plotLeft + slotWidth * 12} x2={plotLeft + slotWidth * 12}
              y1={plotTop} y2={plotBottom} stroke="var(--chart-axis)" strokeWidth={1}
            />
          )}
          {periods.map((period) => {
            const centre = centreOf(period.at);
            const left = centre - boxWidth / 2;
            const { whiskerLow, boxLow, median, boxHigh, whiskerHigh } = period.figures;
            const cap = (boxWidth * CAP_SHARE) / 2;
            return (
              <g
                key={period.key}
                data-month={period.key}
                role="img"
                tabIndex={0}
                aria-label={readOut(period)}
                onFocus={() => setActive(period.at)}
                onBlur={() => setActive(null)}
                onMouseEnter={() => setActive(period.at)}
                onMouseLeave={() => setActive(null)}
                className="outline-none"
              >
                <rect x={left - 6} y={plotTop} width={boxWidth + 12} height={PLOT_HEIGHT} fill="transparent" />
                {whiskerLow !== undefined && whiskerHigh !== undefined && (
                  <>
                    <line data-mark="whisker" x1={centre} x2={centre} y1={y(whiskerHigh)} y2={y(whiskerLow)} stroke="var(--chart-ink-muted)" strokeWidth={1.5} />
                    <line x1={centre - cap} x2={centre + cap} y1={y(whiskerHigh)} y2={y(whiskerHigh)} stroke="var(--chart-ink-muted)" strokeWidth={1.5} />
                    <line x1={centre - cap} x2={centre + cap} y1={y(whiskerLow)} y2={y(whiskerLow)} stroke="var(--chart-ink-muted)" strokeWidth={1.5} />
                  </>
                )}
                {boxLow !== undefined && boxHigh !== undefined && (
                  <rect
                    data-mark="box"
                    x={left}
                    y={y(boxHigh)}
                    width={boxWidth}
                    height={Math.max(0, y(boxLow) - y(boxHigh))}
                    fill="var(--chart-cool-near)"
                    stroke="var(--chart-ink)"
                    strokeWidth={1}
                  />
                )}
                {median !== undefined && (
                  <line data-mark="median" x1={left} x2={left + boxWidth} y1={y(median)} y2={y(median)} stroke="var(--chart-ink)" strokeWidth={2} />
                )}
                <text x={centre} y={plotBottom + 15} fontSize={10} textAnchor="middle" fill="var(--chart-ink-muted)">
                  {period.name}
                </text>
                {active === period.at && (
                  <rect x={left - 3} y={plotTop} width={boxWidth + 6} height={PLOT_HEIGHT} fill="none" stroke="var(--chart-ink-muted)" strokeWidth={1} rx={3} />
                )}
              </g>
            );
          })}
        </svg>
        {shown && (
          <ChartTooltip left={centreOf(shown.at)} top={plotTop} width={width} title={shown.name}>
            {topDown.filter((part) => shown.figures[part] !== undefined).map((part) => (
              <div key={part} className="flex items-baseline gap-2">
                <span className="font-semibold tabular-nums">{figure(shown.figures[part])}</span>
                <span className="text-zinc-500 dark:text-zinc-400">{label(part)}</span>
              </div>
            ))}
          </ChartTooltip>
        )}
      </div>
      <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-zinc-500 dark:text-zinc-400">
        <li className="inline-flex items-center gap-1.5">
          <svg width={12} height={12} aria-hidden="true">
            <line x1={6} x2={6} y1={0} y2={12} stroke="var(--chart-ink-muted)" strokeWidth={1.5} />
          </svg>
          {`${label('whiskerLow')} – ${label('whiskerHigh')}`}
        </li>
        <li className="inline-flex items-center gap-1.5">
          <i className="inline-block h-2.5 w-2.5 rounded-sm border border-[var(--chart-ink)]" style={{ background: 'var(--chart-cool-near)' }} aria-hidden="true" />
          {`${label('boxLow')} – ${label('boxHigh')}`}
        </li>
        <li className="inline-flex items-center gap-1.5">
          <svg width={12} height={12} aria-hidden="true">
            <line x1={0} x2={12} y1={6} y2={6} stroke="var(--chart-ink)" strokeWidth={2} />
          </svg>
          {label('median')}
        </li>
      </ul>
      <SeriesTable
        caption={widget.title ?? ''}
        columns={PARTS.map(label)}
        rows={periods.map((period) => ({ name: period.name, cells: PARTS.map((part) => figure(period.figures[part])) }))}
      />
    </WidgetCard>
  );
}
