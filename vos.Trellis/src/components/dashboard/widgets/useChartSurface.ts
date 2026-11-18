import { useEffect, useState } from 'react';
import type { Surface } from './chartRamp';

function surfaceOf(): Surface {
  return document.documentElement.classList.contains('dark') ? 'dark' : 'light';
}

/** The surface a ramp is painted on. The theme is a class on the document, and a ramp colour is worked
 *  out rather than read from a token, so the class is watched and the chart repainted when it changes. */
export function useChartSurface(): Surface {
  const [surface, setSurface] = useState<Surface>(surfaceOf);
  useEffect(() => {
    const observer = new MutationObserver(() => setSurface(surfaceOf()));
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
    return () => observer.disconnect();
  }, []);
  return surface;
}
