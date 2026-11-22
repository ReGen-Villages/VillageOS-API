import type { BoundaryPoint } from '../utils/parcelGeometry';

export interface BorderDrawing {
  readonly mode: 'drawing' | 'editing' | 'done';
  readonly corners: readonly BoundaryPoint[];
}

export type DrawingStep =
  | { readonly kind: 'accepted'; readonly drawing: BorderDrawing }
  | { readonly kind: 'crosses' }
  | { readonly kind: 'tooFewCorners' };

const notBuilt = (): never => {
  throw new Error('not built yet');
};

export const startDrawing = (): BorderDrawing => notBuilt();
export const addCorner = (_drawing: BorderDrawing, _corner: BoundaryPoint): DrawingStep => notBuilt();
export const undoCorner = (_drawing: BorderDrawing): BorderDrawing => notBuilt();
export const finishDrawing = (_drawing: BorderDrawing): DrawingStep => notBuilt();
export const closeOnFirstCorner = (_drawing: BorderDrawing): DrawingStep => notBuilt();
export const startEditing = (_drawing: BorderDrawing): BorderDrawing => notBuilt();
export const moveCorner = (_drawing: BorderDrawing, _index: number, _to: BoundaryPoint): DrawingStep => notBuilt();
export const addCornerOnEdge = (_drawing: BorderDrawing, _edge: number, _corner: BoundaryPoint): DrawingStep => notBuilt();
export const finishEditing = (_drawing: BorderDrawing): BorderDrawing => notBuilt();
