import { useTranslation } from 'react-i18next';
import type { Binding, RangeBand, ResolveContext } from '@tendril-ui/tendril';
import { asNumber, useBindings } from '@tendril-ui/tendril';
import { ANNUAL, paddedDomain } from './chartScale';
import { groupsByKey, monthNames } from './historySeries';

type MonthlyWidget<Part extends string> = {
  months: Record<Part, Binding>;
  annual?: Record<Part, Binding>;
  bands?: RangeBand[];
  floor?: number;
  ceiling?: number;
};

export type Period<Part extends string> = {
  name: string;
  at: number;
  key: string;
  figures: Partial<Record<Part, number>>;
};

function figuresOf<Part extends string>(parts: readonly Part[], series: Map<string, number>[], key: string | null) {
  const figures: Partial<Record<Part, number>> = {};
  parts.forEach((part, at) => {
    const value = key === null ? series[at].values().next().value : series[at].get(key);
    if (value !== undefined) figures[part] = value;
  });
  return figures;
}

export function useMonthlyFigures<Part extends string>(
  parts: readonly Part[], widget: MonthlyWidget<Part>, context: ResolveContext,
) {
  const { t, i18n } = useTranslation();
  const monthBindings = parts.map((part) => widget.months[part]);
  const annualBindings = widget.annual ? parts.map((part) => widget.annual![part]) : [];
  const bandBindings = (widget.bands ?? []).flatMap((band) => [band.from, band.to]);
  const resolved = useBindings([...monthBindings, ...annualBindings, ...bandBindings], context);
  const monthSeries = resolved.slice(0, parts.length).map((state) => groupsByKey(state.value));
  const annualSeries = resolved.slice(parts.length, parts.length + annualBindings.length).map((state) => groupsByKey(state.value));
  const bandBounds = resolved.slice(parts.length + annualBindings.length).map((state) => asNumber(state.value));

  const periods: Period<Part>[] = [
    ...monthNames(i18n.language).map((name, month) =>
      ({ name, at: month, key: String(month + 1), figures: figuresOf(parts, monthSeries, String(month + 1)) })),
    ...(widget.annual
      ? [{ name: t('widgets.history.annual'), at: ANNUAL, key: 'annual', figures: figuresOf(parts, annualSeries, null) }]
      : []),
  ].filter((period) => Object.keys(period.figures).length > 0);

  const bands = (widget.bands ?? []).map((band, at) => ({
    label: band.label, colour: band.colour, from: bandBounds[at * 2], to: bandBounds[at * 2 + 1],
  }));

  const drawn = [
    ...periods.flatMap((period) => Object.values<number | undefined>(period.figures)),
    ...bands.flatMap((band) => [band.from, band.to]),
  ].filter((value): value is number => value !== undefined && value !== null);
  const domain = paddedDomain(drawn.length ? [Math.min(...drawn), Math.max(...drawn)] : [0, 1], widget.floor, widget.ceiling);

  return { periods, bands, domain };
}
