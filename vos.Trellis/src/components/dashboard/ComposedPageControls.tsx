import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import type { ModelIndex } from '../../api/dashboardApi';
import { dashboardPages, dashboardWriteContext } from '../../api/dashboardPages';
import type { DashboardDescriptor } from '../../types/dashboard';
import { toast } from '../common/toastStore';

const buttonClass =
  'rounded border border-zinc-300 bg-white px-2 py-1 text-xs text-zinc-700 hover:bg-zinc-100 dark:border-zinc-600 dark:bg-zinc-800 dark:text-zinc-200 dark:hover:bg-zinc-700';

/** Rename and Remove for a page the console kept, and nothing for a seeded one: a page the seed
 *  wrote is the seed's to change, and the composition a kept page carries is what tells the two
 *  apart. A rename rewrites the title and keeps the Thing's name, so the address stays. */
export function ComposedPageControls({ dashboard, index }: { dashboard: DashboardDescriptor; index: ModelIndex }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [renaming, setRenaming] = useState(false);
  const [title, setTitle] = useState(dashboard.specification?.title ?? '');
  const [busy, setBusy] = useState(false);

  if (!dashboard.specification?.composed) return null;
  const specification = dashboard.specification;

  const rename = async () => {
    const next = title.trim();
    if (!next) return;
    setBusy(true);
    try {
      await dashboardPages.retitle(dashboard.id, specification, next);
      setRenaming(false);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : t('composer.renameFailed'));
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    const context = dashboardWriteContext(index);
    if (!context || !window.confirm(t('composer.removeConfirm', { name: specification.title }))) return;
    setBusy(true);
    try {
      await dashboardPages.remove(dashboard.id, context);
      navigate('/operations');
    } catch (error) {
      toast.error(error instanceof Error ? error.message : t('composer.removeFailed'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex items-center gap-2">
      {renaming ? (
        <>
          <label className="flex items-center gap-1 text-xs">
            <span className="sr-only">{t('composer.newTitle')}</span>
            <input
              className="rounded border border-zinc-300 bg-white px-2 py-1 text-xs dark:border-zinc-600 dark:bg-zinc-800"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
            />
          </label>
          <button type="button" className={buttonClass} disabled={busy} onClick={() => void rename()}>{t('composer.save')}</button>
          <button type="button" className={buttonClass} onClick={() => setRenaming(false)}>{t('composer.cancel')}</button>
        </>
      ) : (
        <button type="button" className={buttonClass} onClick={() => setRenaming(true)}>{t('composer.rename')}</button>
      )}
      <button type="button" className={buttonClass} disabled={busy} onClick={() => void remove()}>{t('composer.removePage')}</button>
    </div>
  );
}
