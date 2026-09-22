// Safari has a strict WebGL context limit (~4). Sigma uses 3 contexts, so
// Three.js 3D rendering needs 1 more — only safe on Chrome/Firefox (~16 limit).

export function isSafari(): boolean {
  if (typeof navigator === 'undefined') return false;
  const ua = navigator.userAgent;
  return /Safari/.test(ua) && !/Chrome/.test(ua) && !/Chromium/.test(ua);
}

export function canSupport3D(): boolean {
  return !isSafari();
}
