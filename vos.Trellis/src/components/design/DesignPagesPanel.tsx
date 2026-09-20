import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { Plus } from 'lucide-react';
import type { DashboardDescriptor, DashboardSpecification } from '../../types/dashboard';
import { isKeptPage } from '../../utils/designSpec';

/** A page the model holds whose specification could be read; one that could not is not offered,
 *  since there is nothing to lay out. */
export type DesignablePage = DashboardDescriptor & { specification: DashboardSpecification };

interface Props {
  pages: DesignablePage[];
  openedId: string | null;
  onOpen: (page: DesignablePage) => void;
  onStart: (name: string) => void;
}

/** Every page the model holds — the seed's marked as such — and the field that names a new one. */
export function DesignPagesPanel({ pages, openedId, onOpen, onStart }: Props) {
  const { t } = useTranslation();
  const [name, setName] = useState('');

  const start = () => {
    const trimmed = name.trim();
    if (trimmed === '') return;
    setName('');
    onStart(trimmed);
  };

  return (
    <aside className="w-56 flex-shrink-0 flex flex-col border-r border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-900">
      <h2 className="px-3 pt-3 pb-1 text-[11px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400">{t('design.pages.title')}</h2>
      <ul className="flex-1 overflow-y-auto p-2 space-y-0.5">
        {pages.map((page) => (
          <li key={page.id}>
            <button
              type="button"
              aria-label={t('design.pages.open', { name: page.specification.title })}
              onClick={() => onOpen(page)}
              className={clsx(
                'w-full text-left rounded-md px-2.5 py-2 text-sm',
                page.id === openedId
                  ? 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300'
                  : 'text-zinc-700 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-800',
              )}
            >
              <span className="block break-words">{page.specification.title}</span>
              <span className="block text-[10px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400">
                {isKeptPage(page.specification) ? t('design.pages.kept') : t('design.pages.seeded')}
              </span>
            </button>
          </li>
        ))}
      </ul>
      <div className="p-2 border-t border-zinc-200 dark:border-zinc-700 flex gap-1.5">
        <input
          value={name}
          onChange={(event) => setName(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') start();
          }}
          aria-label={t('design.pages.newPage')}
          placeholder={t('design.pages.newPage')}
          className="flex-1 min-w-0 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 px-2 py-1 text-sm text-zinc-900 dark:text-zinc-100"
        />
        <button
          type="button"
          onClick={start}
          aria-label={t('design.pages.start', { name: name.trim() })}
          className="rounded bg-zinc-100 dark:bg-zinc-800 px-2 text-zinc-700 dark:text-zinc-200 hover:bg-zinc-200 dark:hover:bg-zinc-700"
        >
          <Plus size={15} />
        </button>
      </div>
    </aside>
  );
}
