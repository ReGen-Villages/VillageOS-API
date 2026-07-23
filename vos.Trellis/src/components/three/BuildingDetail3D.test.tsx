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
vi.mock('@react-three/drei', () => ({
  OrbitControls: () => null,
}));

// A sentinel geometry value that parses; anything else yields no mesh, driving
// the "Unable to parse 3D geometry" early return.
vi.mock('../../utils/geometryDispatcher', () => ({
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
    // Empty first: the component takes the early-return branch.
    const { rerender, queryByTestId, getByText } = render(<BuildingDetail3D geometryValue={null} />);
    expect(getByText('Unable to parse 3D geometry.')).toBeTruthy();

    // Geometry arrives: the populated branch renders extra hooks. Before the
    // fix this threw "Rendered more hooks than during the previous render".
    rerender(<BuildingDetail3D geometryValue="VALID" />);
    expect(queryByTestId('canvas')).toBeTruthy();
  });
});
