/** Test double for `ResizeObserver`, for components and hooks that size themselves to a measured
 *  element. jsdom performs no layout, so nothing is ever measured until a test reports a size:
 *  `report` pushes one to every observer, as both a `contentRect` and the observed element's
 *  bounding rectangle, so it serves callers that read either. */
export function installResizeObserverDouble(): {
  observed: HTMLElement[];
  report: (size: { width?: number; height?: number }) => void;
  restore: () => void;
} {
  const original = globalThis.ResizeObserver;
  const observed: HTMLElement[] = [];
  const callbacks: ResizeObserverCallback[] = [];

  class ResizeObserverDouble {
    constructor(callback: ResizeObserverCallback) {
      callbacks.push(callback);
    }
    observe(element: Element) {
      observed.push(element as HTMLElement);
    }
    unobserve() {}
    disconnect() {}
  }
  globalThis.ResizeObserver = ResizeObserverDouble as unknown as typeof ResizeObserver;

  return {
    observed,
    report({ width = 0, height = 0 }) {
      const entries = observed.map((element) => {
        element.getBoundingClientRect = () => ({ width, height }) as DOMRect;
        return { target: element, contentRect: { width, height } } as unknown as ResizeObserverEntry;
      });
      for (const callback of callbacks) callback(entries, {} as ResizeObserver);
    },
    restore() {
      globalThis.ResizeObserver = original;
    },
  };
}
