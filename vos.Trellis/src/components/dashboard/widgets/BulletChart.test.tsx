import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { Binding, BulletWidget } from '../../../types/dashboard';
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

const { BulletChart } = await import('./BulletChart');

function bind(name: string, value: BindingResult): Binding {
  const binding = { kind: 'property', thing: name, property: name } as unknown as Binding;
  values.set(JSON.stringify(binding), value);
  return binding;
}

function bar(label: string): HTMLElement {
  // The coloured value bar is the div whose inline width is a percentage of the track.
  const row = screen.getByText(label).closest('.grid') as HTMLElement;
  const bars = Array.from(row.querySelectorAll<HTMLElement>('div[style*="width"]'));
  return bars[bars.length - 1];
}

describe('BulletChart up-good self-sufficiency rows', () => {
  it('draws a value past target within the track (max) and tones it good, not over-the-edge', () => {
    const widget: BulletWidget = {
      type: 'bullet',
      rows: [
        {
          label: 'Energy — net positive',
          value: bind('pctOfConsumption', 112),
          max: 200,
          target: 100,
          band: [100, 200],
          direction: 'up-good',
          format: 'pct100',
        },
      ],
    };

    render(<BulletChart widget={widget} ctx={{} as ResolveContext} />);

    // The real percentage is shown, not a 0..1 fraction.
    expect(screen.getByText('112%')).toBeInTheDocument();
    // 112 of a 200 track is 56% wide — not clamped to the right edge.
    expect(parseFloat(bar('Energy — net positive').style.width)).toBeCloseTo(56, 5);
    // At/above target on an up-good row is healthy (green), never the utilization "over" red.
    expect(bar('Energy — net positive').style.background).toContain('--good');
  });

  it('tones an up-good value below its band critical', () => {
    const widget: BulletWidget = {
      type: 'bullet',
      rows: [
        {
          label: 'Water — days of supply',
          value: bind('daysOfSupply', 6),
          max: 28,
          target: 14,
          band: [14, 28],
          direction: 'up-good',
          format: 'decimal1',
        },
      ],
    };

    render(<BulletChart widget={widget} ctx={{} as ResolveContext} />);

    expect(bar('Water — days of supply').style.background).toContain('--crit');
  });

  it('never lets a band whose upper bound exceeds max bleed past the track', () => {
    const widget: BulletWidget = {
      type: 'bullet',
      rows: [
        // A misconfigured/legacy row: default max=1 with a band that runs to 2.0.
        { label: 'Water — resilience', value: bind('daysOfSupply', 0), target: 1, band: [1, 2] },
      ],
    };

    render(<BulletChart widget={widget} ctx={{} as ResolveContext} />);

    const row = screen.getByText('Water — resilience').closest('.grid') as HTMLElement;
    const bandEl = Array.from(row.querySelectorAll<HTMLElement>('div[style*="color-mix"]'))[0];
    const left = parseFloat(bandEl.style.left);
    const width = parseFloat(bandEl.style.width);
    // Right edge stays within the track — no overflow into the label column.
    expect(left + width).toBeLessThanOrEqual(100);
  });

  it('keeps utilization (down-good, default) semantics: past the band is critical', () => {
    const widget: BulletWidget = {
      type: 'bullet',
      rows: [
        {
          label: 'Cube utilization',
          value: bind('cube_utilization', 0.95),
          target: 0.8,
          band: [0.75, 0.9],
          format: 'percent',
        },
      ],
    };

    render(<BulletChart widget={widget} ctx={{} as ResolveContext} />);

    expect(bar('Cube utilization').style.background).toContain('--crit');
  });
});
