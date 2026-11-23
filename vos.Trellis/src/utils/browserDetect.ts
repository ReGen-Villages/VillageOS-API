// Safari allows few WebGL contexts and Sigma takes most of them, so the 3D view's extra one is safe only on
// Chrome and Firefox.

export function isSafari(): boolean {
  if (typeof navigator === 'undefined') return false;
  const ua = navigator.userAgent;
  return /Safari/.test(ua) && !/Chrome/.test(ua) && !/Chromium/.test(ua);
}

export function canSupport3D(): boolean {
  return !isSafari();
}
