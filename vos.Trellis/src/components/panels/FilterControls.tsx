import { useTranslation } from 'react-i18next';
import { Eye, EyeOff, X } from 'lucide-react';

interface FilterControlsProperties {
  search: string;
  onSearch: (search: string) => void;
  placeholder: string;
  onShowAll: () => void;
  onHideAll: () => void;
  hidden: number;
}

export function FilterControls({ search, onSearch, placeholder, onShowAll, onHideAll, hidden }: FilterControlsProperties) {
  const { t } = useTranslation();
  return (
    <>
      <div className="px-3 py-2 flex items-center gap-2 flex-shrink-0">
        <input
          type="search"
          value={search}
          onChange={(e) => onSearch(e.target.value)}
          placeholder={placeholder}
          className="flex-1 bg-zinc-900/60 border border-zinc-700 rounded px-2 py-1 text-xs text-zinc-200 placeholder-zinc-500 focus:outline-none focus:border-zinc-500"
        />
        {search && (
          <button
            type="button"
            onClick={() => onSearch('')}
            className="text-zinc-400 hover:text-zinc-200"
            aria-label={t('graph.filter.clearSearch')}
          >
            <X size={14} />
          </button>
        )}
      </div>

      <div className="px-3 pb-2 flex items-center gap-2 flex-shrink-0">
        <button
          type="button"
          onClick={onShowAll}
          className="flex items-center gap-1 px-2 py-1 rounded bg-zinc-700/50 hover:bg-zinc-600/50 text-zinc-200"
        >
          <Eye size={12} /> {t('graph.filter.all')}
        </button>
        <button
          type="button"
          onClick={onHideAll}
          className="flex items-center gap-1 px-2 py-1 rounded bg-zinc-700/50 hover:bg-zinc-600/50 text-zinc-200"
        >
          <EyeOff size={12} /> {t('graph.filter.none')}
        </button>
        {hidden > 0 && (
          <span className="ml-auto text-zinc-400 text-[10px]">
            {t('graph.filter.hidden', { count: hidden })}
          </span>
        )}
      </div>
    </>
  );
}
