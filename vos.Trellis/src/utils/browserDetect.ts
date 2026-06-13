/**
 * Browser detection utilities for feature gating.
 *
 * Safari has a strict WebGL context limit (~4). Sigma uses 3 contexts,
 * so Three.js 3D rendering requires at least 1 more — only safe on
 * Chrome/Firefox (~16 limit).
 */

export function isSafari(): boolean {
  if (typeof navigator === 'undefined') return false;
  const ua = navigator.userAgent;
  return /Safari/.test(ua) && !/Chrome/.test(ua) && !/Chromium/.test(ua);
}

/**
 * Returns true if the browser can support the additional WebGL context(s)
 * needed for Three.js 3D rendering alongside Sigma.
 */
export function canSupport3D(): boolean {
  return !isSafari();
}
