import type { ReactNode } from 'react';
import { describe, it, expect, vi } from 'vitest';
import { render } from '@testing-library/react';
import BuildingDetail3D from './BuildingDetail3D';

// The 3D stack needs a real GPU; stub the fiber/drei surface so the component
// mounts in jsdom and we can exercise its hook order, not its rendering.
vi.mock('@react-three/fiber', () => ({
  Canvas: ({ children }: { children: ReactNode }) => <div data-testid="canvas">{children}</div>,
  useFrame: () => {},
}));

const orbitControlsProps: Record<string, unknown> = {};
vi.mock('@react-three/drei', () => ({
  OrbitControls: (props: Record<string, unknown>) => {
    Object.assign(orbitControlsProps, props);
    return null;
  },
}));

// A sentinel geometry value that parses; anything else yields no mesh, driving
// the early return for geometry that cannot be read.
vi.mock('../../utils/geometryRouter', () => ({
  parseSolidMesh: (value: unknown) =>
    value === 'VALID'
      ? {
          positions: new Float32Array([0, 0, 0, 1, 0, 0, 0, 0, 1]),
          indices: new Uint16Array([0, 1, 2]),
          normals: new Float32Array([0, 1, 0, 0, 1, 0, 0, 1, 0]),
          height: 1,
          localCenter: [0, 0] as [number, number],
        }
      : null,
}));

describe('BuildingDetail3D', () => {
  it('keeps a stable hook order when geometry appears after an empty render', () => {
    const { rerender, queryByTestId, getByText } = render(<BuildingDetail3D geometryValue={null} />);
    expect(getByText('The 3D geometry could not be read.')).toBeTruthy();

    // The populated branch renders more hooks, which throws "Rendered more hooks than during the previous
    // render" if any of them sits below the early return.
    rerender(<BuildingDetail3D geometryValue="VALID" />);
    expect(queryByTestId('canvas')).toBeTruthy();
  });

  // The field guide says the 3D tab orbits and zooms but does not pan, so this keeps the guide true.
  it('disables panning', () => {
    render(<BuildingDetail3D geometryValue="VALID" />);
    expect(orbitControlsProps.enablePan).toBe(false);
  });
});
