import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import type { Binding, HeatmapWidget } from '../../../types/dashboard';
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

const { Heatmap } = await import('./Heatmap');

/** What the canvas was asked to paint: jsdom draws nothing, so the drawing calls are recorded. */
const painted: { fillStyle: string; x: number; y: number; width: number; height: number }[] = [];
let fillStyle = '';

beforeEach(() => {
  painted.length = 0;
  Object.defineProperty(HTMLCanvasElement.prototype, 'getContext', {
    configurable: true,
    value: () => ({
      set fillStyle(style: string) { fillStyle = style; },
      get fillStyle() { return fillStyle; },
      fillRect: (x: number, y: number, width: number, height: number) => painted.push({ fillStyle, x, y, width, height }),
      clearRect: () => undefined,
      strokeRect: () => undefined,
      scale: () => undefined,
      setTransform: () => undefined,
    }),
  });
});

const TEN_YEARS = 10 * 365 * 86_400;

/** Every hour of every day of the year, warm in the afternoon and in the southern summer. */
function hourByDay(): { key: string; value: number }[] {
  const rows: { key: string; value: number }[] = [];
  for (let day = 1; day <= 366; day++) {
    for (let hour = 0; hour < 24; hour++) {
      const season = Math.cos(((day - 1) / 366) * 2 * Math.PI);
      const diurnal = -Math.cos(((hour - 2) / 24) * 2 * Math.PI);
      rows.push({ key: `${hour},${day}`, value: 15 + 5 * season + 6 * diurnal });
    }
  }
  return rows;
}

const HOUR_BY_DAY = hourByDay();

function bound(binding: Binding, result: BindingResult): Binding {
  values.set(JSON.stringify(binding), result);
  return binding;
}

const widget: HeatmapWidget = {
  type: 'heatmap',
  title: 'Daily temperature',
  value: bound(
    { kind: 'history', property: 'temperature', windowSeconds: TEN_YEARS, steps: [{ fold: 'hourOfDay,dayOfYear', function: 'Average' }] },
    HOUR_BY_DAY,
  ),
  sun: {
    latitude: bound({ kind: 'property', thing: '$scope', property: 'latitude' }, -25.83),
    longitude: bound({ kind: 'property', thing: '$scope', property: 'longitude' }, 28.17),
    utcOffsetSeconds: bound({ kind: 'property', thing: '$scope', property: 'utcOffsetSeconds' }, 7200),
  },
  unit: '°C',
  format: 'decimal1',
};

function draw(drawn: HeatmapWidget = widget) {
  return render(<Heatmap widget={drawn} context={{} as ResolveContext} />);
}

describe('the hour-by-day heatmap', () => {
  it('paints one cell per group on the canvas, sized to the grid', () => {
    draw();

    expect(painted).toHaveLength(366 * 24);
    expect(new Set(painted.map((cell) => cell.width.toFixed(3))).size).toBe(1);
    expect(new Set(painted.map((cell) => cell.x.toFixed(3))).size).toBe(366);
    expect(new Set(painted.map((cell) => cell.y.toFixed(3))).size).toBe(24);
  });

  it('colours the coldest cell at the light end of the ramp and the warmest at the dark end', () => {
    draw();

    const coldest = HOUR_BY_DAY.reduce((low, row) => (row.value < low.value ? row : low));
    const warmest = HOUR_BY_DAY.reduce((high, row) => (row.value > high.value ? row : high));
    const cellOf = (key: string) => {
      const [hour, day] = key.split(',').map(Number);
      return painted[(day - 1) * 24 + hour];
    };
    expect(cellOf(coldest.key).fillStyle).toBe('#cde2fb');
    expect(cellOf(warmest.key).fillStyle).toBe('#0d366b');
  });

  it('draws the sunrise and sunset curves from the coordinates the model states', () => {
    const { container } = draw();

    const sunrise = container.querySelector('path[data-curve="sunrise"]')!;
    const sunset = container.querySelector('path[data-curve="sunset"]')!;
    expect(sunrise).not.toBeNull();
    // Near Pretoria in January the sun rises before six and sets after seven, local time.
    const risesAt = Number(sunrise.getAttribute('data-first-hour'));
    const setsAt = Number(sunset.getAttribute('data-first-hour'));
    expect(risesAt).toBeGreaterThan(5);
    expect(risesAt).toBeLessThan(6);
    expect(setsAt).toBeGreaterThan(19);
    expect(setsAt).toBeLessThan(20);
    expect(screen.getByText('Sunrise')).toBeInTheDocument();
  });

  it('draws no curves where the spec binds no coordinates', () => {
    const { container } = draw({ ...widget, sun: undefined });

    expect(container.querySelector('path[data-curve]')).toBeNull();
  });

  it('reads the cell under the pointer, and walks the grid from the keyboard', () => {
    const { container } = draw();
    const canvas = container.querySelector('canvas')!;
    Object.defineProperty(canvas, 'getBoundingClientRect', { value: () => ({ left: 0, top: 0, width: 366, height: 240 }) });

    fireEvent.mouseMove(canvas, { clientX: 0.5, clientY: 145 });
    let tooltip = screen.getByRole('tooltip');
    expect(within(tooltip).getByText(/Jan 1\b|1 Jan/)).toBeInTheDocument();
    expect(within(tooltip).getByText(/14:00/)).toBeInTheDocument();

    fireEvent.keyDown(canvas, { key: 'ArrowRight' });
    fireEvent.keyDown(canvas, { key: 'ArrowUp' });
    tooltip = screen.getByRole('tooltip');
    expect(within(tooltip).getByText(/Jan 2\b|2 Jan/)).toBeInTheDocument();
    expect(within(tooltip).getByText(/13:00/)).toBeInTheDocument();
    expect(within(tooltip).getByText(/°C/)).toBeInTheDocument();
  });

  it('carries a scale legend from the floor to the ceiling, and the window it draws', () => {
    draw({ ...widget, floor: 0, ceiling: 30 });

    expect(screen.getByText('0.0 °C')).toBeInTheDocument();
    expect(screen.getByText('30.0 °C')).toBeInTheDocument();
    expect(screen.getByText('last 10 years')).toBeInTheDocument();
  });

  it('names the warmest and coldest hours for a reader who cannot see the grid', () => {
    draw();

    const name = screen.getByRole('img').getAttribute('aria-label')!;
    expect(name).toMatch(/Daily temperature/);
    expect(name).toMatch(/26\.0 °C/);
    expect(name).toMatch(/4\.0 °C/);
  });

  it('paints nothing where the platform answered nothing', () => {
    draw({ ...widget, value: { kind: 'const', value: 0 } });

    expect(painted).toHaveLength(0);
  });
});
