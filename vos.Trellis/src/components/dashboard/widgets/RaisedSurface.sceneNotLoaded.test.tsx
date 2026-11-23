import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { ResolveContext } from '@tendril-ui/tendril';

vi.mock('@tendril-ui/tendril', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tendril-ui/tendril')>()),
  useBindings: () => [{
    loading: false,
    error: false,
    value: {
      originLatitude: 25, originLongitude: 55, cellSizeMetres: 10, latitudeStepDegrees: 0.0001, longitudeStepDegrees: 0.0001,
      rows: 2, columns: 2, values: [100, 100.5, 101, 101.2],
    },
  }],
}));

vi.mock('./RaisedSurfaceScene', () => {
  throw new Error('the scene could not be loaded');
});

const { RaisedSurface } = await import('./RaisedSurface');

beforeEach(() => {
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({ getExtension: () => null } as never);
  vi.spyOn(console, 'error').mockImplementation(() => {});
});

afterEach(() => vi.restoreAllMocks());

describe('a raised surface whose scene could not be loaded', () => {
  it('says so in place of the drawing, and leaves the figures, the table and the rest of the page', async () => {
    render(
      <>
        <p>the rest of the page</p>
        <RaisedSurface widget={{ type: 'raisedSurface', title: 'Surface', surface: { kind: 'value', property: 'levels' }, unit: 'm' }} context={{} as ResolveContext} />
      </>,
    );

    expect(await screen.findByText('The 3D drawing could not be shown here.')).toBeTruthy();
    expect(screen.queryByRole('img')).toBeNull();
    expect(screen.getByText('100 m')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Table' })).toBeTruthy();
    expect(screen.getByText('the rest of the page')).toBeTruthy();
  });
});
