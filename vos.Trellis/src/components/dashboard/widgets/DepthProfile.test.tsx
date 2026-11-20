import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import type { Binding, DepthProfileWidget } from '@tendril-ui/tendril';
import type { ResolveContext } from '@tendril-ui/tendril';

import { values } from '../../../testBindingValues';

vi.mock('@tendril-ui/tendril', async (importOriginal) =>
  (await import('../../../testBindingValues')).withAnsweredBindings(importOriginal));

const { DepthProfile } = await import('./DepthProfile');

const horizons: Binding = { kind: 'thingList', archetype: 'SoilHorizon', scope: { viaPredicate: 'has' } };
/** Six layers, listed out of order so the chart has to sort them by their top. */
values.set(JSON.stringify(horizons), [
  { name: '30–60 cm', topCentimetres: 30, sandPercent: 78, siltPercent: 12, clayPercent: 10, pH: 8.4 },
  { name: '0–5 cm', topCentimetres: 0, sandPercent: 88, siltPercent: 7, clayPercent: 5, pH: 8.1 },
  { name: '5–15 cm', topCentimetres: 5, sandPercent: 86, siltPercent: 8, clayPercent: 6, pH: 8.2 },
  { name: '15–30 cm', topCentimetres: 15, sandPercent: 82, siltPercent: 10, clayPercent: 8, pH: 8.3 },
  { name: '60–100 cm', topCentimetres: 60, sandPercent: 75, siltPercent: 13, clayPercent: 12 },
  { name: '100–200 cm', topCentimetres: 100, sandPercent: 72, siltPercent: 14, clayPercent: 14, pH: 8.5 },
]);

const texture: DepthProfileWidget = {
  type: 'depthProfile',
  title: 'Soil texture',
  rows: horizons,
  labelKey: 'name',
  orderKey: 'topCentimetres',
  parts: [
    { label: 'Sand', key: 'sandPercent', colour: '#e0b973' },
    { label: 'Silt', key: 'siltPercent', colour: '#a68a64' },
    { label: 'Clay', key: 'clayPercent', colour: '#7f5539' },
  ],
  unit: '%',
};

const acidity: DepthProfileWidget = {
  type: 'depthProfile',
  title: 'Soil pH',
  rows: horizons,
  labelKey: 'name',
  orderKey: 'topCentimetres',
  parts: [{ label: 'pH in water', key: 'pH', colour: '#6b7f4e' }],
  format: 'decimal1',
  floor: 4,
  ceiling: 9,
};

function draw(drawn: DepthProfileWidget) {
  return render(<DepthProfile widget={drawn} context={{} as ResolveContext} />);
}

describe('the depth profile', () => {
  it('draws a row per layer, top down by the order key', () => {
    const { container } = draw(texture);

    expect([...container.querySelectorAll('[data-layer]')].map((row) => row.getAttribute('data-layer')))
      .toEqual(['0–5 cm', '5–15 cm', '15–30 cm', '30–60 cm', '60–100 cm', '100–200 cm']);
  });

  it('stacks the parts across a row, each sized by its value and in its colour', () => {
    const { container } = draw(texture);

    const top = container.querySelector('[data-layer="0–5 cm"]')!;
    const segments = [...top.querySelectorAll('rect[data-part]')];
    expect(segments.map((segment) => segment.getAttribute('fill'))).toEqual(['#e0b973', '#a68a64', '#7f5539']);
    const widths = segments.map((segment) => Number(segment.getAttribute('width')));
    expect(widths[0] / widths[2]).toBeCloseTo(88 / 5, 5);
    expect(Number(segments[1].getAttribute('x'))).toBeCloseTo(Number(segments[0].getAttribute('x')) + widths[0], 5);
  });

  it('draws a single part as a bar from the floor, and leaves out a layer that has no value for it', () => {
    const { container } = draw(acidity);

    const rows = [...container.querySelectorAll('[data-layer]')];
    expect(rows.map((row) => row.getAttribute('data-layer'))).not.toContain('60–100 cm');
    const widthOf = (layer: string) => Number(container.querySelector(`[data-layer="${layer}"] rect[data-part]`)!.getAttribute('width'));
    expect(widthOf('100–200 cm') / widthOf('0–5 cm')).toBeCloseTo((8.5 - 4) / (8.1 - 4), 5);
  });

  it('reads every part of a layer on focus', () => {
    draw(texture);

    fireEvent.focus(screen.getByRole('img', { name: /^0–5 cm/ }));

    const tooltip = within(screen.getByRole('tooltip'));
    expect(tooltip.getByText('88 %')).toBeInTheDocument();
    expect(tooltip.getByText('Clay')).toBeInTheDocument();
  });

  it('carries a legend of the parts and a table twin of every layer', () => {
    draw(texture);

    expect(within(screen.getByRole('list')).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['Sand', 'Silt', 'Clay']);
    const rows = within(screen.getByRole('table')).getAllByRole('row');
    expect(rows).toHaveLength(7);
    expect(rows[1].textContent).toBe('0–5 cm88 %7 %5 %');
  });

  it('draws nothing where the rows are not answered', () => {
    const { container } = draw({ ...texture, rows: { kind: 'const', value: 0 } });

    expect(container.querySelectorAll('[data-layer]')).toHaveLength(0);
  });
});
