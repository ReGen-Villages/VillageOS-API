import { useTranslation } from 'react-i18next';

/** What the viewer is doing while a model loads: its own first step, then the steps the fragments
 *  library reports. */
export const LOADING_STAGES = ['fetchingWorker', 'decompressing', 'parsing', 'generating', 'done'] as const;

export type LoadingStage = (typeof LOADING_STAGES)[number];

interface LoadingOverlayProps {
  stage: LoadingStage;
  progress: number;
}

export function LoadingOverlay({ stage, progress }: LoadingOverlayProps) {
  const { t } = useTranslation();
  const percent = Math.round(Math.min(Math.max(progress, 0), 1) * 100);
  const stageWords = t(`modelPage.stage.${stage}`);
  return (
    <div
      role="status"
      aria-label={t('modelPage.loadingStage', { stage: stageWords })}
      data-testid="fragments-loading-overlay"
      className="absolute inset-0 flex items-center justify-center bg-zinc-950/80 pointer-events-none"
    >
      <div className="w-80 px-6 py-5 rounded-lg bg-zinc-900 border border-zinc-700 text-center">
        <p className="text-sm text-zinc-300 mb-3">
          {t('modelPage.loadingModel')} — <span className="text-zinc-400">{stageWords}</span>
        </p>
        <div className="w-full h-2 rounded bg-zinc-800 overflow-hidden">
          <div
            className="h-full bg-blue-500 transition-all"
            style={{ width: `${percent}%` }}
            data-testid="fragments-loading-bar"
          />
        </div>
        <p className="text-xs text-zinc-500 mt-2">{percent}%</p>
      </div>
    </div>
  );
}
