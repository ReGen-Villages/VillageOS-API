import { useTranslation } from 'react-i18next';
import { MousePointerClick } from 'lucide-react';

interface EmptyCanvasHintProperties {
  hasServices: boolean;
}

export function EmptyCanvasHint({ hasServices }: EmptyCanvasHintProperties) {
  const { t } = useTranslation();
  return (
    <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
      <div className="text-center text-sm text-zinc-500 dark:text-zinc-400 max-w-xs">
        {hasServices ? (
          <>
            <MousePointerClick className="mx-auto mb-2 text-zinc-400" size={28} />
            <p className="font-medium text-zinc-600 dark:text-zinc-300">{t('pipeline.startTitle')}</p>
            <p className="mt-1">{t('pipeline.startBody')}</p>
          </>
        ) : (
          <>
            <p className="font-medium text-zinc-600 dark:text-zinc-300">{t('pipeline.noServicesTitle')}</p>
            <p className="mt-1">{t('pipeline.noServicesBody')}</p>
          </>
        )}
      </div>
    </div>
  );
}
