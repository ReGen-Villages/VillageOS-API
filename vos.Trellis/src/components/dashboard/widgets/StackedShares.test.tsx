import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, fireEvent, within, act } from '@testing-library/react';
import type { Binding, StackedSharesWidget } from '@tendril-ui/tendril';
import type { ResolveContext } from '@tendril-ui/tendril';
import i18n from '../../../i18n';

import { values } from '../../../testBindingValues';

vi.mock('@tendril-ui/tendril', async (importOriginal) =>
  (await import('../../../testBindingValues')).withAnsweredBindings(importOriginal));

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
  // write one into the specification; a colour the model does not answer leaves the class out rather than
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

  it('draws an annual column after the months from each class\'s share of the whole window', () => {
    const annual = (label: string, share: number): Binding => {
      const binding: Binding = { kind: 'history', property: label, windowSeconds: TEN_YEARS, steps: [{ fold: 'all', function: 'ShareWithin' }] };
      values.set(JSON.stringify(binding), [{ key: 'all', value: share }]);
      return binding;
    };
    const { container } = draw({
      ...widget,
      classes: widget.classes.map((entry, at) => ({ ...entry, annual: annual(entry.label, [0.25, 0.35, 0.4][at]) })),
    });

    const column = container.querySelector('[data-month="all"]')!;
    const heights = [...column.querySelectorAll('rect[data-class]')].map((segment) => Number(segment.getAttribute('height')));
    expect(heights[0] / heights[2]).toBeCloseTo(0.25 / 0.4, 5);
    expect(Number(column.querySelector('rect[data-class]')!.getAttribute('x')))
      .toBeGreaterThan(Number(container.querySelector('[data-month="12"] rect[data-class]')!.getAttribute('x')));
    expect(within(column as HTMLElement).getByText('Annual')).toBeInTheDocument();
    const rows = within(screen.getByRole('table')).getAllByRole('row');
    expect(rows).toHaveLength(14);
    expect(rows[13].textContent).toBe('Annual25%35%40%');
  });

  it('draws no annual column where no class binds one', () => {
    const { container } = draw();

    expect(container.querySelector('[data-month="all"]')).toBeNull();
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

describe('the stacked percentage bar from hours counted under each group Thing', () => {
  const groups: Binding = { kind: 'thingList', archetype: 'DesignStrategyGroup' };
  values.set(JSON.stringify(groups), [
    { id: 'G-COOLING', order: 7, wording: 'Cooling system needed', colour: '#c0392b' },
    { id: 'G-COMFORT', order: 1, wording: 'Comfortable as it is', colour: '#27ae60' },
    { id: 'G-UNSEEN', order: 4, wording: 'Heating needed', colour: '#2980b9' },
  ]);
  const byMonth: Binding = { kind: 'history', property: 'designStrategyGroup', windowSeconds: TEN_YEARS, steps: [{ fold: 'monthOfYear,value', function: 'Count' }] };
  values.set(JSON.stringify(byMonth), [
    { key: '1,g-comfort', value: 300 }, { key: '1,g-cooling', value: 100 },
    { key: '7,g-comfort', value: 50 }, { key: '7,g-cooling', value: 450 }, { key: '7,g-elsewhere', value: 500 },
  ]);
  const wholeWindow: Binding = { kind: 'history', property: 'designStrategyGroup', windowSeconds: TEN_YEARS, steps: [{ fold: 'value', function: 'Count' }] };
  values.set(JSON.stringify(wholeWindow), [{ key: 'g-comfort', value: 350 }, { key: 'g-cooling', value: 550 }]);

  const counted: StackedSharesWidget = {
    type: 'stackedShares',
    title: 'Comfort strategies',
    classes: [],
    counted: { counts: byMonth, annual: wholeWindow, groups, orderKey: 'order', labelKey: 'wording', colourKey: 'colour' },
  };

  it('stacks each month from its groups\' shares of the hours counted, in the groups\' order, coloured by each group', () => {
    const { container } = draw(counted);

    const january = container.querySelector('[data-month="1"]')!;
    const segments = [...january.querySelectorAll('rect[data-class]')];
    expect(segments.map((segment) => segment.getAttribute('fill'))).toEqual(['#27ae60', '#c0392b']);
    const heights = segments.map((segment) => Number(segment.getAttribute('height')));
    expect(heights[0] / heights[1]).toBeCloseTo(300 / 100, 5);
  });

  it('leaves out hours counted under a group the model does not declare', () => {
    draw(counted);

    fireEvent.focus(screen.getByRole('img', { name: /^Jul/ }));

    expect(within(screen.getByRole('tooltip')).getByText('90%')).toBeInTheDocument();
  });

  it('draws the annual column from the whole window\'s counts, and lists only the groups counted', () => {
    const { container } = draw(counted);

    const annual = [...container.querySelector('[data-month="all"]')!.querySelectorAll('rect[data-class]')];
    expect(Number(annual[0].getAttribute('height')) / Number(annual[1].getAttribute('height'))).toBeCloseTo(350 / 550, 5);
    expect(within(screen.getByRole('list')).getAllByRole('listitem').map((item) => item.textContent))
      .toEqual(['Comfortable as it is', 'Cooling system needed']);
  });

  describe('on a page in another language', () => {
    function wordedGroups(archetype: string, comfort: string | undefined, cooling: string | undefined): Binding {
      const binding: Binding = { kind: 'thingList', archetype };
      values.set(JSON.stringify(binding), [
        { id: 'G-COOLING', order: 7, name: 'cooling-system-needed', wording: cooling, colour: '#c0392b' },
        { id: 'G-COMFORT', order: 1, name: 'comfortable-as-it-is', wording: comfort, colour: '#27ae60' },
      ]);
      return binding;
    }

    async function drawIn(language: string, groupsBinding: Binding) {
      await act(() => i18n.changeLanguage(language));
      return draw({ ...counted, counted: { ...counted.counted!, groups: groupsBinding, labelKey: 'name', wordingKey: 'wording' } });
    }

    const legend = () => within(screen.getByRole('list')).getAllByRole('listitem').map((item) => item.textContent);

    afterEach(async () => {
      await act(() => i18n.changeLanguage('en'));
    });

    it('labels each group in the legend, the tooltip and the table with its words in the page language', async () => {
      await drawIn('de', wordedGroups('GermanGroups',
        JSON.stringify({ en: 'Comfortable as it is', de: 'Behaglich ohne Maßnahmen' }),
        JSON.stringify({ en: 'Cooling system needed', de: 'Kühlanlage nötig' })));

      expect(legend()).toEqual(['Behaglich ohne Maßnahmen', 'Kühlanlage nötig']);
      expect(within(screen.getByRole('table')).getAllByRole('columnheader').map((header) => header.textContent)).toContain('Kühlanlage nötig');
      fireEvent.focus(screen.getByRole('img', { name: /^Jan/ }));
      expect(within(screen.getByRole('tooltip')).getByText('Behaglich ohne Maßnahmen')).toBeInTheDocument();
    });

    it('falls back to the English words, then to the label column, where the page language has none', async () => {
      await drawIn('de', wordedGroups('EnglishOnlyGroups', JSON.stringify({ en: 'Comfortable as it is' }), undefined));

      expect(legend()).toEqual(['Comfortable as it is', 'cooling-system-needed']);
    });

    it('reads a wording column that is not words by language as no wording, and shows the label column', async () => {
      await drawIn('de', wordedGroups('MalformedGroups', '{"de": ', JSON.stringify(['Kühlanlage nötig'])));

      expect(legend()).toEqual(['comfortable-as-it-is', 'cooling-system-needed']);
    });
  });
});
