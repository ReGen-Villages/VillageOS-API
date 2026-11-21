import { useEffect, useRef } from 'react';
import { useSigma } from '@react-sigma/core';

/**
 * Safari takes back WebGL contexts when several are active, and Sigma uses several. Sigma cannot recover a lost
 * one by itself (sigma.js issue 1321), so this asks the browser to restore the context of every canvas in the
 * container, including canvases added later, and redraws once it is restored.
 */
export function WebGLContextGuard() {
  const sigma = useSigma();
  const recoveryCountReference = useRef(0);
  const recoveryTimerReference = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    const container = sigma.getContainer();
    if (!container) return;

    const watched = new Set<HTMLCanvasElement>();

    /** After a restore the old drawing programs and buffers are invalid. Clearing Sigma clears the graph too, so
     *  the graph is copied out first and imported again before the redraw. */
    const rebuildSigma = () => {
      try {
        const graph = sigma.getGraph();
        const snapshot = graph.export();

        sigma.clear();

        graph.import(snapshot);

        sigma.refresh();

        console.info('[WebGLContextGuard] Sigma rebuild complete');
      } catch (err) {
        console.warn('[WebGLContextGuard] sigma rebuild failed:', err);
      }
    };

    const handleContextLost = (event: Event) => {
      // preventDefault() signals the browser that we intend to handle
      // recovery — without it, the browser will NOT attempt to restore
      // the context and 'webglcontextrestored' will never fire.
      event.preventDefault();

      recoveryCountReference.current += 1;
      console.warn(
        `[WebGLContextGuard] WebGL context lost on canvas (recovery attempt #${recoveryCountReference.current})`,
      );

      // Safari doesn't always fire 'webglcontextrestored', so set a
      // fallback timer that forces a rebuild if the event never arrives.
      if (recoveryTimerReference.current) clearTimeout(recoveryTimerReference.current);
      recoveryTimerReference.current = setTimeout(() => {
        console.warn('[WebGLContextGuard] contextrestored not received after 3 s — forcing rebuild');
        requestAnimationFrame(() => {
          requestAnimationFrame(() => {
            rebuildSigma();
          });
        });
        recoveryTimerReference.current = null;
      }, 3000);
    };

    const handleContextRestored = () => {
      console.info('[WebGLContextGuard] WebGL context restored — rebuilding sigma renderer');

      if (recoveryTimerReference.current) {
        clearTimeout(recoveryTimerReference.current);
        recoveryTimerReference.current = null;
      }

      // Waits more than one frame so the context is fully restored before anything is drawn.
      requestAnimationFrame(() => {
        requestAnimationFrame(() => {
          rebuildSigma();
        });
      });
    };

    function watchCanvas(canvas: HTMLCanvasElement) {
      if (watched.has(canvas)) return;
      watched.add(canvas);
      canvas.addEventListener('webglcontextlost', handleContextLost);
      canvas.addEventListener('webglcontextrestored', handleContextRestored);
    }

    function unwatchCanvas(canvas: HTMLCanvasElement) {
      if (!watched.has(canvas)) return;
      watched.delete(canvas);
      canvas.removeEventListener('webglcontextlost', handleContextLost);
      canvas.removeEventListener('webglcontextrestored', handleContextRestored);
    }

    container.querySelectorAll('canvas').forEach(watchCanvas);

    const observer = new MutationObserver((mutations) => {
      for (const mutation of mutations) {
        for (const node of mutation.addedNodes) {
          if (node instanceof HTMLCanvasElement) {
            watchCanvas(node);
          } else if (node instanceof HTMLElement) {
            node.querySelectorAll('canvas').forEach(watchCanvas);
          }
        }
        for (const node of mutation.removedNodes) {
          if (node instanceof HTMLCanvasElement) {
            unwatchCanvas(node);
          }
        }
      }
    });
    observer.observe(container, { childList: true, subtree: true });

    return () => {
      observer.disconnect();
      watched.forEach(unwatchCanvas);
      if (recoveryTimerReference.current) {
        clearTimeout(recoveryTimerReference.current);
        recoveryTimerReference.current = null;
      }
    };
  }, [sigma]);

  return null;
}
