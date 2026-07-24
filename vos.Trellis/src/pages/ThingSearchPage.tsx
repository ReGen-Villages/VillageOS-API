import { useState, useMemo, useCallback, useRef, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useModelStore } from '../stores/modelStore';
import { useUiStore } from '../stores/uiStore';
import { toast } from '../components/common/Toast';
import {
  buildThingSearchIndex,
  searchThings,
  buildThingSearchMarkdown,
  PREVIEW_PROPS,
} from '../utils/thingSearch';
import clsx from 'clsx';

const PAGE_SIZE = 100;

export function ThingSearchPage() {
  const { t } = useTranslation();
  const [inputValue, setInputValue] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');
  const [visibleCount, setVisibleCount] = useState(PAGE_SIZE);
  const debounceRef = useRef<ReturnType<typeof setTimeout>>(undefined);

  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  const selectNode = useUiStore((s) => s.selectNode);
  const navigate = useNavigate();

  const onInputChange = useCallback((value: string) => {
    setInputValue(value);
    clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setDebouncedQuery(value);
      setVisibleCount(PAGE_SIZE);
    }, 250);
  }, []);

  useEffect(() => () => clearTimeout(debounceRef.current), []);

  const index = useMemo(
    () => buildThingSearchIndex(things, relationships),
    [things, relationships],
  );

  const results = useMemo(
    () => searchThings(debouncedQuery, things, index),
    [debouncedQuery, things, index],
  );

  const visibleResults = useMemo(() => results.slice(0, visibleCount), [results, visibleCount]);
  const hasMore = visibleCount < results.length;

  const goToGraph = useCallback(
    (id: string) => {
      selectNode(id);
      navigate('/graph');
    },
    [selectNode, navigate],
  );

  const copyAsMarkdown = useCallback(() => {
    if (results.length === 0) return;
    navigator.clipboard.writeText(buildThingSearchMarkdown(debouncedQuery, results)).then(
      () => toast.success(t('common.copiedToClipboard')),
      () => toast.error(t('common.copyFailed')),
    );
  }, [results, debouncedQuery]);

  const downloadAsMarkdown = useCallback(() => {
    if (results.length === 0) return;
    const md = buildThingSearchMarkdown(debouncedQuery, results);
    const blob = new Blob([md], { type: 'text/markdown' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `thing-search-${debouncedQuery.trim().replace(/\s+/g, '-')}.md`;
    a.click();
    URL.revokeObjectURL(url);
  }, [results, debouncedQuery]);

  return (
    <div className="h-full overflow-auto p-6">
      <h2 className="text-xl font-bold mb-4">{t('thingSearch.title')}</h2>

      <div className="space-y-4">
        <p className="text-xs text-zinc-500">
          {t('thingSearch.intro')}
        </p>

        <input
          type="text"
          value={inputValue}
          onChange={(e) => onInputChange(e.target.value)}
          placeholder={t('thingSearch.placeholder')}
          autoFocus
          className="w-full px-4 py-2 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500 placeholder-zinc-400"
        />

        {debouncedQuery.trim().length > 0 && (
          <div className="flex items-center justify-between">
            <p className="text-xs text-zinc-500">
              {t('thingSearch.foundCount', { count: results.length })}
              {hasMore && ` (${t('common.showingFirst', { count: visibleCount })})`}
            </p>
            {results.length > 0 && (
              <div className="flex gap-2">
                <button
                  onClick={copyAsMarkdown}
                  className="px-3 py-1 text-xs rounded-md border border-zinc-600 text-zinc-400 hover:text-zinc-200 hover:border-zinc-500 transition-colors"
                >
                  {t('common.copyAsMarkdown')}
                </button>
                <button
                  onClick={downloadAsMarkdown}
                  className="px-3 py-1 text-xs rounded-md border border-zinc-600 text-zinc-400 hover:text-zinc-200 hover:border-zinc-500 transition-colors"
                >
                  {t('common.downloadAsMarkdown')}
                </button>
              </div>
            )}
          </div>
        )}

        {debouncedQuery.trim().length > 0 && (
          <div className="space-y-2">
            {visibleResults.map((m) => (
              <div
                key={m.id}
                className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 px-4 py-3 flex items-start gap-3"
              >
                <div className="flex-1 min-w-0">
                  <div className="flex items-baseline gap-2 flex-wrap">
                    <button
                      onClick={() => goToGraph(m.id)}
                      className="text-sm font-medium text-blue-400 hover:underline"
                      title={t('thingSearch.openInGraph')}
                    >
                      {m.name}
                    </button>
                    {m.typeName && (
                      <span
                        className={clsx(
                          'flex-shrink-0 px-1.5 py-0.5 rounded text-[10px] font-medium',
                          'bg-emerald-900/30 text-emerald-400',
                        )}
                        title={t('thingSearch.typeLabel', { type: m.typeName })}
                      >
                        {m.typeName}
                      </span>
                    )}
                  </div>

                  {m.previewProps.length > 0 && (
                    <div className="mt-1.5 flex flex-wrap gap-x-4 gap-y-0.5">
                      {m.previewProps.map((p) => (
                        <span key={p.key} className="text-xs text-zinc-500">
                          <span className="text-amber-400 font-mono">{p.key}</span>
                          <span className="mx-1 text-zinc-600">=</span>
                          <span className="text-zinc-400">{p.formatted}</span>
                        </span>
                      ))}
                      {m.ownPropertyCount > PREVIEW_PROPS && (
                        <span className="text-xs text-zinc-600">
                          {t('thingSearch.moreProps', { count: m.ownPropertyCount - PREVIEW_PROPS })}
                        </span>
                      )}
                    </div>
                  )}
                </div>

                <div className="flex-shrink-0 flex gap-3 text-xs text-zinc-500 pt-0.5">
                  <span title={t('thingSearch.ownProperties')}>
                    <span className="text-zinc-600">{t('thingSearch.propsAbbrev')} </span>
                    <span className="text-zinc-400">{m.ownPropertyCount}</span>
                  </span>
                  <span title={t('thingSearch.relationships')}>
                    <span className="text-zinc-600">{t('thingSearch.relsAbbrev')} </span>
                    <span className="text-zinc-400">{m.relationshipCount}</span>
                  </span>
                </div>
              </div>
            ))}

            {results.length === 0 && (
              <p className="text-sm text-zinc-500">{t('thingSearch.noMatch', { query: debouncedQuery })}</p>
            )}

            {hasMore && (
              <button
                onClick={() => setVisibleCount((c) => c + PAGE_SIZE)}
                className="w-full py-2 text-sm text-blue-400 hover:text-blue-300 border border-zinc-700 rounded-md"
              >
                {t('common.showMore', { count: results.length - visibleCount })}
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
