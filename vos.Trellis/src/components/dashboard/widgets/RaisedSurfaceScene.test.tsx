import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import * as THREE from 'three';
import type { RaisedSurface } from './surfaceGeometry';

// Drawing needs a graphics card, so these stand-ins show what the scene asks of it.
vi.mock('@react-three/fiber', () => ({
  Canvas: ({ camera, children }: { camera: unknown; children: ReactNode }) => (
    <div data-testid="canvas" data-camera={JSON.stringify(camera)}>{children}</div>
  ),
}));
vi.mock('@react-three/drei', () => ({
  OrbitControls: ({ target }: { target: unknown }) => <div data-testid="controls" data-target={JSON.stringify(target)} />,
}));

const { default: RaisedSurfaceScene } = await import('./RaisedSurfaceScene');

const surface: RaisedSurface = {
  positions: new Float32Array([0, 0, 0, 10, 0, 0, 0, 2, -10]),
  indices: new Uint32Array([0, 1, 2]),
  shares: new Float32Array([0, 0, 1]),
  width: 10, depth: 20, boxHeight: 2, lowest: 0, highest: 1,
};
const colours = ['#ff0000', '#00ff00', '#0000ff'];

beforeEach(() => {
  // The scene's own elements, such as a mesh, are not the page's, and React says so for each.
  vi.spyOn(console, 'error').mockImplementation(() => {});
});

afterEach(() => vi.restoreAllMocks());

describe('the raised surface\'s scene', () => {
  it('draws the surface\'s points and triangles, each point in its own colour', () => {
    const attributes = vi.spyOn(THREE.BufferGeometry.prototype, 'setAttribute');
    const index = vi.spyOn(THREE.BufferGeometry.prototype, 'setIndex');
    render(<RaisedSurfaceScene surface={surface} colours={colours} />);

    const given = (name: string) => Array.from((attributes.mock.calls.find(([called]) => called === name)![1] as THREE.BufferAttribute).array);
    expect(given('position')).toEqual([0, 0, 0, 10, 0, 0, 0, 2, -10]);
    expect(given('color')).toEqual([1, 0, 0, 0, 1, 0, 0, 0, 1]);
    expect(Array.from((index.mock.calls[0][0] as THREE.BufferAttribute).array)).toEqual([0, 1, 2]);
  });

  it('is seen from a distance of its larger side, looking at half its height', () => {
    render(<RaisedSurfaceScene surface={surface} colours={colours} />);

    expect(JSON.parse(screen.getByTestId('canvas').dataset.camera!)).toEqual({ position: [14, 16, 18], fov: 40, near: 0.02, far: 200 });
    expect(JSON.parse(screen.getByTestId('controls').dataset.target!)).toEqual([0, 1, 0]);
  });

  it('gives back what it drew when the surface changes and when it goes', () => {
    const disposed = vi.spyOn(THREE.BufferGeometry.prototype, 'dispose');
    const { rerender, unmount } = render(<RaisedSurfaceScene surface={surface} colours={colours} />);
    expect(disposed).not.toHaveBeenCalled();

    rerender(<RaisedSurfaceScene surface={surface} colours={['#0000ff', '#00ff00', '#ff0000']} />);
    expect(disposed).toHaveBeenCalledTimes(1);

    unmount();
    expect(disposed).toHaveBeenCalledTimes(2);
  });
});
