import { enclosesLand, type BoundaryPoint } from '../../utils/parcelGeometry';

export type BorderEdit =
  | { readonly kind: 'place'; readonly corner: BoundaryPoint }
  | { readonly kind: 'close' }
  | { readonly kind: 'move'; readonly index: number; readonly to: BoundaryPoint }
  | { readonly kind: 'insert'; readonly side: number; readonly corner: BoundaryPoint };

/** For a page that takes every corner where it is put, with no rule against crossing. */
export function freehandBoundary(boundary: readonly BoundaryPoint[], edit: BorderEdit): readonly BoundaryPoint[] {
  switch (edit.kind) {
    case 'place':
      return [...boundary, edit.corner];
    case 'move':
      return boundary.map((corner, index) => (index === edit.index ? edit.to : corner));
    case 'insert':
      return [...boundary.slice(0, edit.side + 1), edit.corner, ...boundary.slice(edit.side + 1)];
    case 'close':
      return boundary;
  }
}

/** Side `n` runs from corner `n` to the next, and the last side closes back to the first corner. */
export function sideMidpoints(boundary: readonly BoundaryPoint[]): BoundaryPoint[] {
  if (!enclosesLand(boundary)) return [];
  return boundary.map((corner, index) => {
    const next = boundary[(index + 1) % boundary.length];
    return { latitude: (corner.latitude + next.latitude) / 2, longitude: (corner.longitude + next.longitude) / 2 };
  });
}
