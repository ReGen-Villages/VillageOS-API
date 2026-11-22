export type ReadPosition =
  | { readonly kind: 'position'; readonly latitude: number; readonly longitude: number }
  | { readonly kind: 'notCoordinates' };

export function positionFromText(_text: string): ReadPosition {
  throw new Error('not built yet');
}
