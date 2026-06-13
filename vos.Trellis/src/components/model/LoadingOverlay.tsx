interface LoadingOverlayProps {
  stage: string;
  progress: number;
}

/**
 * Full-viewer overlay that shows the Fragments load stage + percent.
 * Stays mounted until FragmentsModels reports stage='done'.
 */
export function LoadingOverlay({ stage, progress }: LoadingOverlayProps) {
  const pct = Math.round(Math.min(Math.max(progress, 0), 1) * 100);
  return (
    <div
      role="status"
      aria-label={`Loading model: ${stage}`}
      data-testid="fragments-loading-overlay"
      className="absolute inset-0 flex items-center justify-center bg-zinc-950/80 pointer-events-none"
    >
      <div className="w-80 px-6 py-5 rounded-lg bg-zinc-900 border border-zinc-700 text-center">
        <p className="text-sm text-zinc-300 mb-3">
          Loading model — <span className="text-zinc-400">{stage}</span>
        </p>
        <div className="w-full h-2 rounded bg-zinc-800 overflow-hidden">
          <div
            className="h-full bg-blue-500 transition-all"
            style={{ width: `${pct}%` }}
            data-testid="fragments-loading-bar"
          />
        </div>
        <p className="text-xs text-zinc-500 mt-2">{pct}%</p>
      </div>
    </div>
  );
}
