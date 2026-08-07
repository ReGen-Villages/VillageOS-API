// What the type filter says the 3D scene should show.
//
// Bug #5366: this used to be a bare list of IFC GlobalIds to hide, which cannot
// express "show nothing". A .frag holds every element the IFC had, while the
// model holds Things only for the ones it was given — so a hide-list built from
// Things covers a fraction of the scene, and unchecking every type left the
// model on screen. The empty selection is stated outright instead of inferred.
export type SceneVisibility =
  | { kind: 'everything' }
  | { kind: 'nothing' }
  | { kind: 'everythingExcept'; hiddenIfcGuids: readonly string[] };
