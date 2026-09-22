import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import type { Binding, StackedSharesWidget } from '../../../types/dashboard';
import type { BindingResult, ResolveContext } from '../../../api/dashboardApi';

const values = new Map<string, BindingResult>();

vi.mock('../../../hooks/useDashboard', () => ({
  useBindings: (bindings: (Binding | undefined)[]) =>
    bindings.map((binding) => ({
      loading: false,
      error: false,
      value: binding ? (values.get(JSON.stringify(binding)) ?? null) : null,
    })),
}));

const { StackedShares } = await import('./StackedShares');

const TEN_YEARS = 10 * 365 * 86_400;

/** Three classes whose shares sum to one every month: a cold class that grows in winter, a warm one
 *  that grows in summer, and the rest in between. */
const CLASSES = [
  { label: 'cold', colour: '#38bdf8', from: undefined, to: 9, share: (month: number) => 0.1 + 0.4 * (1 + Math.cos(((month - 1) / 12) * 2 * Math.PI)) / 2 },
  { label: 'comfortable', colour: '#65a30d', from: 9, to: 26, share: (month: number) => 0.4 - 0.1 * Math.cos(((month - 1) / 12) * 2 * Math.PI) },
  { label: 'hot', colour: '#dc2626', from: 26, to: undefined, share: (month: number) => 1 - (0.1 + 0.4 * (1 + Math.cos(((month - 1) / 12) * 2 * Math.PI)) / 2) - (0.4 - 0.1 * Math.cos(((month - 1) / 12) * 2 * Math.PI)) },
];

function shareBinding(from: number | undefined, to: number | undefined, share: (month: number) => number): Binding {
  const binding: Binding = {
    kind: 'history', property: 'apparentTemperature', windowSeconds: TEN_YEARS,
    steps: [{ fold: 'monthOfYear', function: 'ShareWithin', ...(from !== undefined ? { from } : {}), ...(to !== undefined ? { to } : {}) }],
  };
  values.set(JSON.stringify(binding), Array.from({ length: 12 }, (_, at) => ({ key: String(at + 1), value: share(at + 1) })));
  return binding;
}

const widget: StackedSharesWidget = {
  type: 'stackedShares',
  title: 'Thermal stress',
  classes: CLASSES.map((entry) => ({ label: entry.label, colour: entry.colour, share: shareBinding(entry.from, entry.to, entry.share) })),
};

function draw(drawn: StackedSharesWidget = widget) {
  return render(<StackedShares widget={drawn} context={{} as ResolveContext} />);
}

describe('the stacked percentage bar', () => {
  it('draws twelve bars of stacked segments, each coloured by its class and sized by its share', () => {
    const { container } = draw();

    expect(container.querySelectorAll('[data-month]')).toHaveLength(12);
    const january = container.querySelector('[data-month="1"]')!;
    const segments = [...january.querySelectorAll('rect[data-class]')];
    expect(segments.map((segment) => segment.getAttribute('fill'))).toEqual(['#38bdf8', '#65a30d', '#dc2626']);
    const heights = segments.map((segment) => Number(segment.getAttribute('height')));
    // January: cold 0.5, comfortable 0.3, hot 0.2 of the same bar.
    expect(heights[0] / heights[1]).toBeCloseTo(0.5 / 0.3, 5);
    expect(heights[2] / heights[1]).toBeCloseTo(0.2 / 0.3, 5);
  });

  it('stacks the classes in the order listed, the first at the bottom', () => {
    const { container } = draw();

    const july = container.querySelector('[data-month="7"]')!;
    const bottoms = [...july.querySelectorAll('rect[data-class]')]
      .map((segment) => Number(segment.getAttribute('y')) + Number(segment.getAttribute('height')));
    expect(bottoms[0]).toBeGreaterThan(bottoms[1]);
    expect(bottoms[1]).toBeGreaterThan(bottoms[2]);
  });

  it('labels the axis in whole percentages', () => {
    draw();

    expect(screen.getByText('0%')).toBeInTheDocument();
    expect(screen.getByText('100%')).toBeInTheDocument();
  });

  it('reads every class of a month on focus, as a percentage', () => {
    draw();

    fireEvent.focus(screen.getByRole('img', { name: /^Jan/ }));

    const tooltip = screen.getByRole('tooltip');
    expect(within(tooltip).getByText('50%')).toBeInTheDocument();
    expect(within(tooltip).getByText('cold')).toBeInTheDocument();
    expect(within(tooltip).getByText('20%')).toBeInTheDocument();
  });

  it('opens the readout for the bar under the pointer and closes it when the pointer or the focus leaves', () => {
    draw();

    fireEvent.mouseEnter(screen.getByRole('img', { name: /^Jul/ }));
    expect(within(screen.getByRole('tooltip')).getByText('10%')).toBeInTheDocument();

    fireEvent.mouseLeave(screen.getByRole('img', { name: /^Jul/ }));
    expect(screen.queryByRole('tooltip')).toBeNull();

    fireEvent.focus(screen.getByRole('img', { name: /^Jul/ }));
    fireEvent.blur(screen.getByRole('img', { name: /^Jul/ }));
    expect(screen.queryByRole('tooltip')).toBeNull();
  });

  it('carries a legend of the classes, the window, and a table twin of every month', () => {
    draw();

    const legend = screen.getByRole('list');
    expect(within(legend).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['cold', 'comfortable', 'hot']);
    expect(screen.getByText('last 10 years')).toBeInTheDocument();
    expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(13);
  });

  // The classes are the model's Things, so a class may bind its colour to the Thing's own rather than
  // write one into the spec; a colour the model does not answer leaves the class out rather than
  // painting it in one the model never gave.
  it('paints a class in the colour the model answers, and leaves out one the model gives no colour', () => {
    const coloured: Binding = { kind: 'property', thing: 'No thermal stress', property: 'colour' };
    const uncoloured: Binding = { kind: 'property', thing: 'Slight cold stress', property: 'colour' };
    values.set(JSON.stringify(coloured), '#B7E4C7');
    const { container } = draw({
      ...widget,
      classes: [
        { label: 'no stress', colour: coloured, share: widget.classes[1].share },
        { label: 'slight cold', colour: uncoloured, share: widget.classes[0].share },
      ],
    });

    const january = container.querySelector('[data-month="1"]')!;
    expect([...january.querySelectorAll('rect[data-class]')].map((segment) => segment.getAttribute('fill'))).toEqual(['#B7E4C7']);
    expect(screen.queryByText('slight cold')).toBeNull();
  });

  it('leaves out a month no class was answered for, and a class the platform answered nothing for', () => {
    const { container } = draw({
      ...widget,
      classes: [
        { label: 'never', colour: '#000000', share: { kind: 'const', value: 0 } },
        { label: 'only summer', colour: '#dc2626', share: (() => {
          const binding: Binding = { kind: 'history', property: 'x', windowSeconds: TEN_YEARS, steps: [{ fold: 'monthOfYear', function: 'ShareWithin', from: 30 }] };
          values.set(JSON.stringify(binding), [{ key: '6', value: 1 }, { key: '7', value: 1 }]);
          return binding;
        })() },
      ],
    });

    expect(container.querySelectorAll('[data-month]')).toHaveLength(2);
    expect(screen.queryByText('never')).toBeNull();
  });
});
