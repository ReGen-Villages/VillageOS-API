/** Asked before a 3D drawing is mounted, since a browser that gives no context leaves that drawing blank. */
export function canDrawIn3D(): boolean {
  const context = document.createElement('canvas').getContext('webgl2');
  if (!context) return false;
  // A browser keeps only so many contexts, so this one is given back at once.
  context.getExtension('WEBGL_lose_context')?.loseContext();
  return true;
}
