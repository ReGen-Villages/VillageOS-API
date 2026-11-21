import { useTranslation } from 'react-i18next';
import { triggerDownload } from '../../utils/logDownload';
import { toast } from './toastStore';

const BUTTON_CLASSES = 'px-3 py-1 text-xs rounded-md border border-zinc-600 text-zinc-400 hover:text-zinc-200 hover:border-zinc-500 transition-colors';

export function MarkdownExportButtons({ markdown, fileName }: { markdown: () => string; fileName: string }) {
  const { t } = useTranslation();
  const copy = () => navigator.clipboard.writeText(markdown()).then(
    () => toast.success(t('common.copiedToClipboard')),
    () => toast.error(t('common.copyFailed')),
  );
  const download = () => triggerDownload(new Blob([markdown()], { type: 'text/markdown' }), fileName);
  return (
    <div className="flex gap-2">
      <button type="button" onClick={copy} className={BUTTON_CLASSES}>{t('common.copyAsMarkdown')}</button>
      <button type="button" onClick={download} className={BUTTON_CLASSES}>{t('common.downloadAsMarkdown')}</button>
    </div>
  );
}
