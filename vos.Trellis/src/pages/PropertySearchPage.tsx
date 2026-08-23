import { useState, useMemo, useCallback, useRef, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useModelStore } from '../stores/modelStore';
import { useSubscription } from '../hooks/useSse';
import { WHOLE_MODEL } from '../types/subscription';
import { useUiStore } from '../stores/uiStore';
import { temporalApi } from '../api/temporalApi';
import { toast } from '../components/common/toastStore';
import { formatDateTime, formatPropertyValue } from '../utils/formatters';
import { useNumberDisplaySettings } from '../hooks/useNumberDisplaySettings';
import { useDeclaredPropertyTypes } from '../hooks/useDeclaredPropertyTypes';
import type { PropertyVersionsResponse } from '../types/vos';
import { searchProperties, type PropertyMatch } from './propertySearch';
import clsx from 'clsx';

/** Max rows rendered at once to keep the DOM lightweight. */
const PAGE_SIZE = 100;

/** Return a ReactNode with the first occurrence of `query` highlighted in yellow. */
function highlightMatch(text: string, query: string) {
  const idx = text.toLowerCase().indexOf(query.toLowerCase());
  if (idx === -1) return text;
  return (
    <>
      {text.slice(0, idx)}
      <span className="bg-amber-500/30 text-amber-200 rounded px-0.5">{text.slice(idx, idx + query.length)}</span>
      {text.slice(idx + query.length)}
    </>
  );
}

type SearchMode = 'name' | 'value';

export function PropertySearchPage() {
  useSubscription(WHOLE_MODEL);
  const { t } = useTranslation();
  const [inputValue, setInputValue] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');
  const [visibleCount, setVisibleCount] = useState(PAGE_SIZE);
  const [searchMode, setSearchMode] = useState<SearchMode>('name');
  const [historyTarget, setHistoryTarget] = useState<PropertyMatch | null>(null);
  const [versions, setVersions] = useState<PropertyVersionsResponse | null>(null);
  const [loadingHistory, setLoadingHistory] = useState(false);
  const debounceRef = useRef<ReturnType<typeof setTimeout>>(undefined);

  const numbers = useNumberDisplaySettings();
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  const selectNode = useUiStore((s) => s.selectNode);
  const navigate = useNavigate();

  // A Thing's inherited property values are resolved by the broker (walking the is-chain); the client
  // store only carries own + stored overrides, so search must read the server-resolved effective set.
  // A read-only snapshot — Property Search is not a live surface — and the same read the panels make
  // for the declared types, which is why it comes from the one hook rather than a second fetch.
  const effectiveProps = useDeclaredPropertyTypes();

  // Debounce: update the actual search query 250ms after the user stops typing
  const onInputChange = useCallback((value: string) => {
    setInputValue(value);
    clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setDebouncedQuery(value);
      setVisibleCount(PAGE_SIZE);
      setHistoryTarget(null);
      setVersions(null);
    }, 250);
  }, []);

  useEffect(() => () => clearTimeout(debounceRef.current), []);

  // Build a name lookup for relationship detail display
  const thingNames = useMemo(() => {
    const map = new Map<string, string>();
    for (const t of things) map.set(t.Id, t.Name);
    return map;
  }, [things]);

  // Search — runs only when the query, mode, resolved properties, or relationships change
  const results = useMemo(
    () => searchProperties({ effectiveProps, relationships, thingNames, query: debouncedQuery, mode: searchMode }),
    [debouncedQuery, searchMode, effectiveProps, relationships, thingNames],
  );

  // Group results by property name, but only materialize what we'll render
  const { grouped, shownTotal } = useMemo(() => {
    const map = new Map<string, PropertyMatch[]>();
    let count = 0;
    for (const m of results) {
      if (count >= visibleCount) break;
      const arr = map.get(m.propertyName) ?? [];
      arr.push(m);
      map.set(m.propertyName, arr);
      count++;
    }
    return { grouped: map, shownTotal: count };
  }, [results, visibleCount]);

  const hasMore = shownTotal < results.length;

  const loadHistory = useCallback(async (match: PropertyMatch) => {
    if (match.ownerType !== 'thing') {
      toast.error(t('propertySearch.historyThingsOnly'));
      return;
    }
    setHistoryTarget(match);
    setVersions(null);
    setLoadingHistory(true);
    try {
      const data = await temporalApi.getPropertyVersions(match.ownerId, match.propertyName);
      setVersions(data);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('propertySearch.loadHistoryFailed'));
    } finally {
      setLoadingHistory(false);
    }
  }, [t]);

  const buildMarkdown = useCallback(() => {
    const g = new Map<string, PropertyMatch[]>();
    for (const m of results) {
      const arr = g.get(m.propertyName) ?? [];
      arr.push(m);
      g.set(m.propertyName, arr);
    }
    const lines: string[] = [`# Property Search (by ${searchMode}): "${debouncedQuery}"`, ''];
    for (const [propName, matches] of g) {
      lines.push(`## ${propName}`, '');
      lines.push('| Type | Owner | Inherited From | Value |', '|------|-------|----------------|-------|');
      for (const m of matches) {
        const type = m.ownerType === 'thing' ? 'Thing' : 'Rel';
        const owner = m.ownerType === 'thing' ? m.ownerName : (m.ownerDetail ?? m.ownerName);
        const via = m.inheritedFrom ?? '';
        const val = formatPropertyValue(m.value, m.declaredType, numbers).replace(/\|/g, '\\|');
        lines.push(`| ${type} | ${owner} | ${via} | ${val} |`);
      }
      lines.push('');
    }
    return lines.join('\n');
  }, [results, debouncedQuery, searchMode, numbers]);

  const copyAsMarkdown = useCallback(() => {
    if (results.length === 0) return;
    navigator.clipboard.writeText(buildMarkdown()).then(
      () => toast.success(t('common.copiedToClipboard')),
      () => toast.error(t('common.copyFailed')),
    );
  }, [results, buildMarkdown, t]);

  const downloadAsMarkdown = useCallback(() => {
    if (results.length === 0) return;
    const blob = new Blob([buildMarkdown()], { type: 'text/markdown' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `property-search-${debouncedQuery.trim().replace(/\s+/g, '-')}.md`;
    a.click();
    URL.revokeObjectURL(url);
  }, [results, debouncedQuery, buildMarkdown]);

  const goToGraph = useCallback((match: PropertyMatch) => {
    if (match.ownerType === 'thing') {
      selectNode(match.ownerId);
      navigate('/graph');
    }
  }, [selectNode, navigate]);

  return (
    <div className="h-full overflow-auto p-6">
      <h2 className="text-xl font-bold mb-4">{t('propertySearch.title')}</h2>

      <div className="space-y-4">
        <p className="text-xs text-zinc-500">
          {searchMode === 'name' ? t('propertySearch.introByName') : t('propertySearch.introByValue')}
        </p>

        {/* Search mode toggle */}
        <div className="flex gap-1 text-xs">
          <button
            onClick={() => setSearchMode('name')}
            className={clsx(
              'px-3 py-1 rounded-md border transition-colors',
              searchMode === 'name'
                ? 'border-blue-500 bg-blue-500/20 text-blue-300'
                : 'border-zinc-600 text-zinc-400 hover:text-zinc-200 hover:border-zinc-500',
            )}
          >
            {t('propertySearch.byName')}
          </button>
          <button
            onClick={() => setSearchMode('value')}
            className={clsx(
              'px-3 py-1 rounded-md border transition-colors',
              searchMode === 'value'
                ? 'border-blue-500 bg-blue-500/20 text-blue-300'
                : 'border-zinc-600 text-zinc-400 hover:text-zinc-200 hover:border-zinc-500',
            )}
          >
            {t('propertySearch.byValue')}
          </button>
        </div>

        {/* Search input — uncontrolled from search perspective, updates immediately */}
        <input
          type="text"
          value={inputValue}
          onChange={(e) => onInputChange(e.target.value)}
          placeholder={searchMode === 'name'
            ? t('propertySearch.namePlaceholder')
            : t('propertySearch.valuePlaceholder')}
          autoFocus
          className="w-full px-4 py-2 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 focus:outline-none focus:ring-2 focus:ring-blue-500 placeholder-zinc-400"
        />

        {/* Result count + copy button */}
        {debouncedQuery.trim().length > 0 && (
          <div className="flex items-center justify-between">
            <p className="text-xs text-zinc-500">
              {t('propertySearch.matchCount', { count: results.length })} {t('propertySearch.acrossNames', { count: grouped.size })}
              {hasMore && ` (${t('common.showingFirst', { count: shownTotal })})`}
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

        {/* Grouped results */}
        {debouncedQuery.trim().length > 0 && (
          <div className="space-y-4">
            {[...grouped.entries()].map(([propName, matches]) => (
              <div
                key={propName}
                className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4"
              >
                <h3 className="text-sm font-semibold text-amber-400 mb-2 font-mono">{propName}</h3>
                <div className="space-y-1.5">
                  {matches.map((m, i) => (
                    <div
                      key={`${m.ownerId}-${i}`}
                      className="flex items-baseline gap-2 text-xs group"
                    >
                      {/* Owner type badge */}
                      <span
                        className={clsx(
                          'flex-shrink-0 px-1.5 py-0.5 rounded text-[10px] font-medium uppercase',
                          m.ownerType === 'thing'
                            ? 'bg-blue-900/30 text-blue-400'
                            : 'bg-purple-900/30 text-purple-400',
                        )}
                      >
                        {m.ownerType === 'thing' ? 'T' : 'R'}
                      </span>

                      {/* Owner name (clickable for things) */}
                      {m.ownerType === 'thing' ? (
                        <button
                          onClick={() => goToGraph(m)}
                          className="text-blue-400 hover:underline flex-shrink-0"
                          title={t('propertySearch.openInGraph')}
                        >
                          {m.ownerName}
                        </button>
                      ) : (
                        <span className="text-purple-300 flex-shrink-0" title={m.ownerDetail}>
                          {m.ownerDetail}
                        </span>
                      )}

                      {/* Inherited badge */}
                      {m.inheritedFrom && (
                        <span className="text-[10px] text-emerald-500 flex-shrink-0" title={t('propertySearch.inheritedFrom', { source: m.inheritedFrom })}>
                          {t('propertySearch.via', { source: m.inheritedFrom })}
                        </span>
                      )}

                      {/* Value */}
                      <span className="text-zinc-400 mx-1">=</span>
                      <span className="text-zinc-300 truncate" title={formatPropertyValue(m.value, m.declaredType, numbers)}>
                        {searchMode === 'value' && debouncedQuery.trim().length > 0
                          ? highlightMatch(formatPropertyValue(m.value, m.declaredType, numbers), debouncedQuery.trim())
                          : formatPropertyValue(m.value, m.declaredType, numbers)}
                      </span>

                      {/* History button (things only) */}
                      {m.ownerType === 'thing' && (
                        <button
                          onClick={() => loadHistory(m)}
                          className="ml-auto text-zinc-600 hover:text-zinc-300 opacity-0 group-hover:opacity-100 transition-opacity flex-shrink-0"
                          title={t('propertySearch.viewHistory')}
                        >
                          {t('propertySearch.historyLink')}
                        </button>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            ))}

            {results.length === 0 && (
              <p className="text-sm text-zinc-500">{t('propertySearch.noMatch', { query: debouncedQuery })}</p>
            )}

            {hasMore && (
              <button
                onClick={() => setVisibleCount((c) => c + PAGE_SIZE)}
                className="w-full py-2 text-sm text-blue-400 hover:text-blue-300 border border-zinc-700 rounded-md"
              >
                {t('common.showMore', { count: results.length - shownTotal })}
              </button>
            )}
          </div>
        )}

        {/* History panel */}
        {historyTarget && (
          <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4 mt-4">
            <h3 className="text-sm font-semibold mb-2">
              {t('propertySearch.historyOf')} <span className="text-amber-400 font-mono">{historyTarget.propertyName}</span>
              {' '}{t('temporal.on')}{' '}
              <span className="text-blue-400">{historyTarget.ownerName}</span>
            </h3>
            {loadingHistory ? (
              <p className="text-xs text-zinc-500">{t('common.loading')}</p>
            ) : versions ? (
              <div className="space-y-1">
                {versions.Versions.length > 0 ? (
                  versions.Versions.map((v, i) => (
                    <div key={i} className="flex items-baseline gap-3 text-xs">
                      <span className="font-mono text-zinc-500 flex-shrink-0">
                        {formatDateTime(v.Timestamp)}
                      </span>
                      <span className="text-zinc-300">{formatPropertyValue(v.Value, historyTarget?.declaredType, numbers)}</span>
                    </div>
                  ))
                ) : (
                  <p className="text-xs text-zinc-500">{t('propertySearch.noHistory')}</p>
                )}
              </div>
            ) : null}
          </div>
        )}
      </div>
    </div>
  );
}
