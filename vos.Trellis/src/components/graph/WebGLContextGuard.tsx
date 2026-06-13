import { useEffect, useRef } from 'react';
import { useSigma } from '@react-sigma/core';

/**
 * Renderless component that watches for WebGL context loss on all canvases
 * inside the Sigma container and attempts recovery.
 *
 * Safari is aggressive about reclaiming WebGL contexts when multiple are
 * active (Sigma uses 3). When a context is lost, Sigma cannot recover
 * automatically (sigma.js #1321). This guard:
 *
 * 1. Listens for `webglcontextlost` on all canvas elements in the container
 * 2. Calls `preventDefault()` to signal the browser we want to recover
 * 3. On `webglcontextrestored`, triggers `sigma.refresh()` to re-render
 * 4. Uses MutationObserver to also guard dynamically-added canvases
 *
 * Must be rendered as a child of <SigmaContainer>.
 */
export function WebGLContextGuard() {
  const sigma = useSigma();
  const recoveryCountRef = useRef(0);
  const recoveryTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    const container = sigma.getContainer();
    if (!container) return;

    const watched = new Set<HTMLCanvasElement>();

    /**
     * Rebuild Sigma's rendering state without destroying graph data.
     *
     * After WebGL context restore, the old programs/buffers are invalid.
     * We export the graph, clear Sigma (which also clears the graphology
     * graph), re-import the snapshot, then refresh to recreate all WebGL
     * resources from the current graph data.
     */
    const rebuildSigma = () => {
      try {
        const graph = sigma.getGraph();
        const snapshot = graph.export();

        // Clear Sigma's rendering state + graph
        sigma.clear();

        // Re-import the saved graph data
        graph.import(snapshot);

        // Force a full refresh — recreates WebGL programs and buffers
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

      recoveryCountRef.current += 1;
      console.warn(
        `[WebGLContextGuard] WebGL context lost on canvas (recovery attempt #${recoveryCountRef.current})`,
      );

      // Safari doesn't always fire 'webglcontextrestored', so set a
      // fallback timer that forces a rebuild if the event never arrives.
      if (recoveryTimerRef.current) clearTimeout(recoveryTimerRef.current);
      recoveryTimerRef.current = setTimeout(() => {
        console.warn('[WebGLContextGuard] contextrestored not received after 3 s — forcing rebuild');
        requestAnimationFrame(() => {
          requestAnimationFrame(() => {
            rebuildSigma();
          });
        });
        recoveryTimerRef.current = null;
      }, 3000);
    };

    const handleContextRestored = () => {
      console.info('[WebGLContextGuard] WebGL context restored — rebuilding sigma renderer');

      // Cancel the fallback timer — normal recovery path
      if (recoveryTimerRef.current) {
        clearTimeout(recoveryTimerRef.current);
        recoveryTimerRef.current = null;
      }

      // Multiple RAF to ensure context is fully restored before we attempt to render
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

    // Watch all existing canvases (Sigma's 3 canvases)
    container.querySelectorAll('canvas').forEach(watchCanvas);

    // Watch for dynamically-added canvases
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
      if (recoveryTimerRef.current) {
        clearTimeout(recoveryTimerRef.current);
        recoveryTimerRef.current = null;
      }
    };
  }, [sigma]);

  return null;
}
