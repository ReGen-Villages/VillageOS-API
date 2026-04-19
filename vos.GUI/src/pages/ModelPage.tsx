import { lazy, Suspense, useEffect, useState } from 'react';
import { apiClient } from '../api/client';
import { useAuth } from '../hooks/useAuth';

const FragmentsViewer = lazy(() =>
  import('../components/model/FragmentsViewer').then((m) => ({ default: m.FragmentsViewer })),
);

type ViewerState =
  | { status: 'loading' }
  | { status: 'empty' }
  | { status: 'error'; message: string }
  | { status: 'ready'; bytes: ArrayBuffer };

/**
 * Model page — renders the IFC-derived Fragments artifact produced by
 * vos.Tools.IfcIngest. Fetches bytes from the broker's JWT-scoped endpoint
 * and hands them to the Fragments viewer. If the artifact is not present
 * (broker returns 404), the original placeholder is shown.
 */
export function ModelPage() {
  const { modelId } = useAuth();
  const [state, setState] = useState<ViewerState>({ status: 'loading' });

  // Re-fetch whenever the JWT-scoped model changes (e.g. via /api/auth/switch-model).
  // Without a modelId dep, switching models leaves the previous .frag on screen.
  useEffect(() => {
    let cancelled = false;
    setState({ status: 'loading' });
    apiClient
      .getBytes('/api/model/fragments')
      .then((bytes) => {
        if (cancelled) return;
        if (bytes === null) setState({ status: 'empty' });
        else setState({ status: 'ready', bytes });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setState({
          status: 'error',
          message: err instanceof Error ? err.message : 'Failed to load model',
        });
      });
    return () => {
      cancelled = true;
    };
  }, [modelId]);

  return (
    <div className="h-full flex flex-col p-6">
      <header className="mb-6">
        <h1 className="text-2xl font-bold text-zinc-900 dark:text-white">Model</h1>
        <p className="text-sm text-zinc-600 dark:text-zinc-400">
          3D viewer for the IFC-derived Fragments artifact.
        </p>
      </header>

      {state.status === 'ready' ? (
        <div className="flex-1 rounded-lg overflow-hidden border border-zinc-200 dark:border-zinc-700">
          <Suspense fallback={<LoadingPlaceholder />}>
            <FragmentsViewer fragmentsBytes={state.bytes} />
          </Suspense>
        </div>
      ) : state.status === 'loading' ? (
        <LoadingPlaceholder />
      ) : state.status === 'error' ? (
        <ErrorPlaceholder message={state.message} />
      ) : (
        <EmptyPlaceholder />
      )}
    </div>
  );
}

function LoadingPlaceholder() {
  return (
    <div
      role="status"
      aria-label="Loading model"
      data-testid="model-viewer-loading"
      className="flex-1 rounded-lg border-2 border-dashed border-zinc-300 dark:border-zinc-700 flex items-center justify-center text-sm text-zinc-500 dark:text-zinc-400"
    >
      Loading model…
    </div>
  );
}

function EmptyPlaceholder() {
  return (
    <div
      role="region"
      aria-label="Fragments viewer placeholder"
      data-testid="model-viewer-placeholder"
      className="flex-1 rounded-lg border-2 border-dashed border-zinc-300 dark:border-zinc-700 flex flex-col items-center justify-center text-center p-8"
    >
      <p className="text-lg font-medium text-zinc-700 dark:text-zinc-300">
        No Fragments artifact loaded for this model.
      </p>
      <p className="text-sm text-zinc-500 dark:text-zinc-500 mt-2 max-w-md">
        Ingest an IFC file via <code className="font-mono">vos.Tools.IfcIngest</code> to
        produce the <code className="font-mono">.frag</code> and mapping sidecar served
        to this page.
      </p>
    </div>
  );
}

function ErrorPlaceholder({ message }: { message: string }) {
  return (
    <div
      role="alert"
      data-testid="model-viewer-error"
      className="flex-1 rounded-lg border-2 border-dashed border-red-400 dark:border-red-600 flex flex-col items-center justify-center text-center p-8"
    >
      <p className="text-lg font-medium text-red-700 dark:text-red-400">
        Failed to load the Fragments artifact.
      </p>
      <p className="text-sm text-zinc-500 dark:text-zinc-400 mt-2 max-w-md break-words">{message}</p>
    </div>
  );
}
