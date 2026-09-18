import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { X } from 'lucide-react';
import type { ComposedColumn, Composition, PropertyFilter } from '../types/dashboard';
import { WHOLE_MODEL } from '../types/subscription';
import { brokerModelReads } from '../api/brokerModelReads';
import { dashboardPages, dashboardWriteContext } from '../api/dashboardPages';
import { edgeKey, edgesFrom, kindsOffered, propertiesOf, type EdgeCandidate, type PropertyCandidate } from '../api/modelDeclaration';
import { useDashboards, useModelIndex, useResolveContext } from '../hooks/useDashboard';
import { useModelIndexAt } from '../hooks/useModelIndexAt';
import { useSubscription } from '../hooks/useSse';
import { useStatesOf } from '../hooks/useStatesOf';
import { toast } from '../components/common/toastStore';
import { useDetailWindows } from '../components/dashboard/detail/DetailWindowManager';
import { DataTable } from '../components/dashboard/widgets/DataTable';
import { WidgetCard } from '../components/dashboard/widgets/WidgetCard';
import { formatPropertyValue } from '../utils/formatters';
import { columnKey, hopsOf, pageOf, tableOf, unresolvedNames } from '../utils/composer';

/**
 * Composing a table from a kind of Thing.
 *
 * Everything offered here is read off the model the console holds — the properties a kind
 * declares, the links its instances carry, the states it derives — so a person chooses from the
 * model's own words and never types a name blind. What the choices become is the same roster
 * binding a seeded page carries, drawn by the same table, so the search and the card a row opens
 * come with it — and kept, when asked, as the same Dashboard Thing a seeded page is, so it stands
 * in the sidebar for everyone who opens the console.
 */

const OPERATORS: PropertyFilter['op'][] = ['=', '!=', '>', '>=', '<', '<='];

/** A row's card shows what it holds and every edge it sits on: no page names the relations, since
 *  the rows behind one composition can be of any kind. */
const ROW_CARD = { relations: [] };

const selectClass =
  'rounded border border-zinc-300 bg-white px-2 py-1 text-sm text-zinc-800 dark:border-zinc-600 dark:bg-zinc-800 dark:text-zinc-100';
const offerClass =
  'w-full text-left rounded px-2 py-1 text-sm text-zinc-700 hover:bg-zinc-200 dark:text-zinc-300 dark:hover:bg-zinc-700';
const captionClass = 'ml-2 text-xs text-zinc-500 dark:text-zinc-400';
const headingClass = 'text-xs font-medium uppercase tracking-wide text-zinc-500';

/** The instant a moment names, or nothing while the field is empty or half-typed. */
function instantOf(moment: string): string | undefined {
  const chosen = new Date(moment);
  return moment && !Number.isNaN(chosen.getTime()) ? chosen.toISOString() : undefined;
}

export function ComposerPage() {
  const { t } = useTranslation();
  useSubscription(WHOLE_MODEL);
  const modelIndex = useModelIndex();
  const pages = useDashboards();
  const writeContext = useMemo(() => dashboardWriteContext(modelIndex), [modelIndex]);

  const [kind, setKind] = useState('');
  const [columns, setColumns] = useState<ComposedColumn[]>([]);
  const [inState, setInState] = useState('');
  const [whereProperty, setWhereProperty] = useState('');
  const [whereOperator, setWhereOperator] = useState<PropertyFilter['op']>('=');
  const [whereValue, setWhereValue] = useState('');
  const [sortKey, setSortKey] = useState('');
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');
  const [pageName, setPageName] = useState('');
  const [keeping, setKeeping] = useState(false);
  const [moment, setMoment] = useState('');

  const instant = useMemo(() => instantOf(moment), [moment]);
  const stood = useModelIndexAt(instant, modelIndex);
  const ctx = useResolveContext(stood.index ?? modelIndex, null, undefined, brokerModelReads);
  const readingTheMoment = instant !== undefined && stood.index === null && !stood.failed;
  const { openDetail, windows } = useDetailWindows(modelIndex, ROW_CARD);

  const kinds = useMemo(() => kindsOffered(modelIndex), [modelIndex]);
  const properties = useMemo(() => (kind ? propertiesOf(kind, modelIndex) : []), [kind, modelIndex]);
  const edges = useMemo(() => (kind ? edgesFrom(kind, modelIndex) : []), [kind, modelIndex]);
  const states = useStatesOf(kind, modelIndex);

  /** A moment carries no state, so choosing one takes the state choices off the composition rather
   *  than leaving columns nothing can answer — the sort with them when it named the column that
   *  went. */
  const chooseMoment = (chosen: string) => {
    setMoment(chosen);
    if (!instantOf(chosen)) return;
    const dropped = columns.filter((c) => c.source === 'state');
    setColumns((held) => held.filter((c) => c.source !== 'state'));
    if (dropped.some((c) => columnKey(c) === sortKey)) setSortKey('');
    setInState('');
  };

  const chooseKind = (chosen: string) => {
    setKind(chosen);
    setColumns([]);
    setInState('');
    setWhereProperty('');
    setWhereValue('');
    setSortKey('');
  };

  const add = (column: ComposedColumn) =>
    setColumns((held) => (held.some((c) => columnKey(c) === columnKey(column)) ? held : [...held, column]));
  const remove = (key: string) => setColumns((held) => held.filter((c) => columnKey(c) !== key));
  const replace = (key: string, column: ComposedColumn) =>
    setColumns((held) => held.map((c) => (columnKey(c) === key ? column : c)));

  const where: PropertyFilter | null = useMemo(() => {
    if (!whereProperty || whereValue === '') return null;
    const numeric = properties.find((p) => p.name === whereProperty)?.numeric;
    const parsed = numeric ? Number(whereValue) : whereValue;
    return { property: whereProperty, op: whereOperator, value: parsed };
  }, [whereProperty, whereOperator, whereValue, properties]);

  const composition: Composition | null = useMemo(() => {
    if (!kind) return null;
    return {
      kind,
      columns,
      inState: inState || undefined,
      where: where ? [where] : undefined,
      sortKey: sortKey || undefined,
      sortDir,
      moment: instant,
    };
  }, [kind, columns, inState, where, sortKey, sortDir, instant]);

  const table = useMemo(
    () => (composition ? tableOf(composition, t('composer.tableTitle', { kind: composition.kind })) : null),
    [composition, t],
  );

  const name = pageName.trim();
  const nameTaken = pages.some((page) => page.name === name || page.spec?.title === name);

  const keep = async () => {
    if (!composition || !writeContext || !name || nameTaken) return;
    const unresolved = unresolvedNames(composition, modelIndex, new Set(states));
    if (unresolved.length) {
      toast.error(t('composer.unresolved', { names: unresolved.join(', ') }));
      return;
    }
    setKeeping(true);
    try {
      await dashboardPages.keep(name, pageOf(composition, name), writeContext);
      toast.success(t('composer.kept', { name }));
      setPageName('');
    } catch (error) {
      toast.error(error instanceof Error ? error.message : t('composer.keepFailed'));
    } finally {
      setKeeping(false);
    }
  };

  // What a path column can go on to, read once per choice rather than once per render: the link
  // reading walks every relationship the model holds, and a column list redraws far more often
  // than a column is added.
  const reachedVocabulary = useMemo(() => {
    const reached = new Set(columns.flatMap((c) => (c.source === 'path' ? [c.steps[c.steps.length - 1]?.archetype ?? ''] : [])));
    reached.delete('');
    return new Map([...reached].map((k) => [k, { properties: propertiesOf(k, modelIndex), edges: edgesFrom(k, modelIndex) }]));
  }, [columns, modelIndex]);

  const edgeWord = (edge: EdgeCandidate) =>
    edge.direction === 'in'
      ? t('composer.reachesIn', { predicate: edge.predicate, kind: edge.reaches })
      : t('composer.reachesOut', { predicate: edge.predicate, kind: edge.reaches });

  return (
    <div className="flex h-full min-h-0">
      <aside className="w-80 flex-shrink-0 overflow-y-auto border-r border-zinc-200 p-4 space-y-5 dark:border-zinc-700">
        <div>
          <h1 className="text-base font-semibold text-zinc-900 dark:text-zinc-100">{t('composer.title')}</h1>
          <p className="mt-1 text-xs text-zinc-500 dark:text-zinc-400">{t('composer.intro')}</p>
        </div>

        <label className="block text-sm">
          <span className={`block ${headingClass}`}>{t('composer.kind')}</span>
          <select className={`${selectClass} mt-1 w-full`} value={kind} onChange={(e) => chooseKind(e.target.value)}>
            <option value="">{t('composer.kindPlaceholder')}</option>
            {kinds.map((k) => <option key={k} value={k}>{k}</option>)}
          </select>
        </label>

        {kind && (
          <>
            <section>
              <h2 className={headingClass}>{t('composer.properties')}</h2>
              <ul className="mt-1">
                {properties.map((p) => (
                  <li key={p.name}>
                    <button type="button" className={offerClass} onClick={() => add({ source: 'property', name: p.name, numeric: p.numeric })}>
                      {p.name}
                      <span className={captionClass}>
                        {p.declaredBy !== kind ? t('composer.declaredBy', { kind: p.declaredBy }) : ''}
                        {p.example !== undefined && p.example !== '' ? ` ${t('composer.example', { value: formatPropertyValue(p.example) })}` : ''}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </section>

            <section>
              <h2 className={headingClass}>{t('composer.edges')}</h2>
              <ul className="mt-1">
                {edges.map((edge) => (
                  <li key={edgeKey(edge)}>
                    <button
                      type="button"
                      className={offerClass}
                      onClick={() => add({
                        source: 'path',
                        steps: [{ predicate: edge.predicate, direction: edge.direction, archetype: edge.reaches }],
                        label: edgeWord(edge),
                      })}
                    >
                      {edgeWord(edge)}
                      <span className={captionClass}>{t('composer.edgeCount', { count: edge.count })}</span>
                    </button>
                  </li>
                ))}
              </ul>
            </section>

            <section>
              <h2 className={headingClass}>{t('composer.states')}</h2>
              <ul className="mt-1">
                {states.map((state) => (
                  <li key={state}>
                    <button
                      type="button"
                      className={offerClass}
                      disabled={instant !== undefined}
                      onClick={() => add({ source: 'state', states: [state] })}
                    >
                      {state}
                    </button>
                  </li>
                ))}
              </ul>
            </section>

            <section>
              <h2 className={headingClass}>{t('composer.filter')}</h2>
              <label className="mt-1 block text-sm">
                <span className="text-xs text-zinc-500">{t('composer.inState')}</span>
                <select
                  className={`${selectClass} mt-1 w-full`}
                  value={inState}
                  disabled={instant !== undefined}
                  onChange={(e) => setInState(e.target.value)}
                >
                  <option value="">{t('composer.everyRow')}</option>
                  {states.map((state) => <option key={state} value={state}>{state}</option>)}
                </select>
              </label>
              <div className="mt-2 flex flex-wrap items-center gap-1 text-sm">
                <label className="flex items-center gap-1">
                  <span className="text-xs text-zinc-500">{t('composer.where')}</span>
                  <select className={selectClass} value={whereProperty} onChange={(e) => setWhereProperty(e.target.value)}>
                    <option value="">—</option>
                    {properties.map((p) => <option key={p.name} value={p.name}>{p.name}</option>)}
                  </select>
                </label>
                <label className="flex items-center gap-1">
                  <span className="sr-only">{t('composer.operator')}</span>
                  <select className={selectClass} value={whereOperator} onChange={(e) => setWhereOperator(e.target.value as PropertyFilter['op'])}>
                    {OPERATORS.map((op) => <option key={op} value={op}>{op}</option>)}
                  </select>
                </label>
                <label className="flex items-center gap-1">
                  <span className="sr-only">{t('composer.value')}</span>
                  <input
                    className={`${selectClass} w-24`}
                    value={whereValue}
                    placeholder={t('composer.valuePlaceholder')}
                    onChange={(e) => setWhereValue(e.target.value)}
                  />
                </label>
              </div>
            </section>

            <section>
              <h2 className={headingClass}>{t('composer.moment')}</h2>
              <label className="mt-1 block text-sm">
                <span className="sr-only">{t('composer.moment')}</span>
                <input
                  type="datetime-local"
                  className={`${selectClass} w-full`}
                  value={moment}
                  onChange={(e) => chooseMoment(e.target.value)}
                />
              </label>
              <p className="mt-1 text-xs text-zinc-500 dark:text-zinc-400">
                {instant === undefined ? t('composer.momentIntro') : t('composer.momentLeavesStates')}
              </p>
            </section>

            <section>
              <h2 className={headingClass}>{t('composer.sort')}</h2>
              <div className="mt-1 flex items-center gap-1 text-sm">
                <label className="flex-1">
                  <span className="sr-only">{t('composer.sort')}</span>
                  <select className={`${selectClass} w-full`} value={sortKey} onChange={(e) => setSortKey(e.target.value)}>
                    <option value="">{t('composer.noSort')}</option>
                    {table?.columns.map((c) => <option key={c.key} value={c.key}>{c.label}</option>)}
                  </select>
                </label>
                <button
                  type="button"
                  className={`${selectClass} whitespace-nowrap`}
                  onClick={() => setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'))}
                >
                  {sortDir === 'asc' ? t('composer.ascending') : t('composer.descending')}
                </button>
              </div>
            </section>

            <section>
              <h2 className={headingClass}>{t('composer.keep')}</h2>
              {writeContext ? (
                <>
                  <label className="mt-1 block text-sm">
                    <span className="text-xs text-zinc-500">{t('composer.pageName')}</span>
                    <input
                      className={`${selectClass} mt-1 w-full`}
                      value={pageName}
                      placeholder={t('composer.pageNamePlaceholder')}
                      onChange={(e) => setPageName(e.target.value)}
                    />
                  </label>
                  {nameTaken && (
                    <p className="mt-1 text-xs text-amber-700 dark:text-amber-400">{t('composer.nameTaken', { name })}</p>
                  )}
                  <button
                    type="button"
                    className={`${selectClass} mt-2 disabled:opacity-50`}
                    disabled={!name || nameTaken || keeping}
                    onClick={() => void keep()}
                  >
                    {t('composer.keepAsPage')}
                  </button>
                </>
              ) : (
                <p className="mt-1 text-xs text-zinc-500 dark:text-zinc-400">{t('composer.noPageKind')}</p>
              )}
            </section>
          </>
        )}
      </aside>

      <main className="flex-1 min-w-0 overflow-auto p-4 space-y-4">
        {!kind && <p className="text-sm text-zinc-500 dark:text-zinc-400">{t('composer.chooseAKind')}</p>}
        {kind && (
          <section>
            <h2 className={headingClass}>{t('composer.columns')}</h2>
            {columns.length === 0 && <p className="mt-1 text-sm text-zinc-500 dark:text-zinc-400">{t('composer.noColumns')}</p>}
            <ul className="mt-1 flex flex-wrap gap-2">
              {columns.map((column) => {
                const key = columnKey(column);
                const reached = column.source === 'path' ? column.steps[column.steps.length - 1]?.archetype : undefined;
                return (
                  <ColumnChip
                    key={key}
                    column={column}
                    onward={reached ? reachedVocabulary.get(reached) : undefined}
                    edgeWord={edgeWord}
                    onReplace={(next) => replace(key, next)}
                    onRemove={() => remove(key)}
                  />
                );
              })}
            </ul>
          </section>
        )}

        {table && instant !== undefined && (
          <p className="text-xs text-zinc-500 dark:text-zinc-400">
            {stood.failed
              ? t('composer.momentRefused', { instant })
              : readingTheMoment
                ? t('composer.momentReading', { instant })
                : t('composer.momentStood', { instant })}
          </p>
        )}

        {table && !readingTheMoment && !stood.failed && (
          <WidgetCard title={table.title}>
            <DataTable
              columns={table.columns}
              rowsBinding={table.rows}
              ctx={ctx}
              sortKey={table.sortKey}
              sortDir={table.sortDir}
              visibleRows={table.visibleRows}
              onRowClick={openDetail ? (row) => openDetail(String(row.id)) : undefined}
            />
          </WidgetCard>
        )}
      </main>
      {windows}
    </div>
  );
}

/** What a path column can go on to from the kind it reached: the properties to read off it, and
 *  the links to follow further. */
interface Onward {
  properties: PropertyCandidate[];
  edges: EdgeCandidate[];
}

/** One chosen column: what it shows, what it costs per row, and — for a path — the two ways it can
 *  go on from the kind it reached. */
function ColumnChip({
  column,
  onward,
  edgeWord,
  onReplace,
  onRemove,
}: {
  column: ComposedColumn;
  onward?: Onward;
  edgeWord: (edge: EdgeCandidate) => string;
  onReplace: (next: ComposedColumn) => void;
  onRemove: () => void;
}) {
  const { t } = useTranslation();
  const hops = hopsOf(column);
  const reached = column.source === 'path' ? column.steps[column.steps.length - 1]?.archetype : undefined;
  const shown = column.source === 'state' ? column.states.join(', ') : column.source === 'property' ? column.name : column.label;
  return (
    <li className="flex flex-wrap items-center gap-1 rounded border border-zinc-300 px-2 py-1 text-sm dark:border-zinc-600">
      <span>{shown}</span>
      {hops > 0 && <span className={captionClass}>{t('composer.hops', { count: hops })}</span>}
      {column.source === 'path' && reached && onward && (
        <>
          <select
            className={selectClass}
            value={column.property ?? ''}
            aria-label={t('composer.thenProperty', { kind: reached })}
            onChange={(e) => onReplace({
              ...column,
              property: e.target.value || undefined,
              label: e.target.value ? `${column.label} · ${e.target.value}` : column.label,
            })}
          >
            <option value="">{t('composer.itsName')}</option>
            {onward.properties.map((p) => <option key={p.name} value={p.name}>{p.name}</option>)}
          </select>
          <select
            className={selectClass}
            value=""
            aria-label={t('composer.thenAlong')}
            onChange={(e) => {
              const edge = onward.edges.find((c) => edgeKey(c) === e.target.value);
              if (!edge) return;
              onReplace({
                source: 'path',
                steps: [...column.steps, { predicate: edge.predicate, direction: edge.direction, archetype: edge.reaches }],
                label: `${column.label} · ${edgeWord(edge)}`,
              });
            }}
          >
            <option value="">{t('composer.thenAlong')}</option>
            {onward.edges.map((edge) => <option key={edgeKey(edge)} value={edgeKey(edge)}>{edgeWord(edge)}</option>)}
          </select>
        </>
      )}
      <button type="button" className="rounded p-0.5 text-zinc-500 hover:bg-zinc-200 dark:hover:bg-zinc-700" aria-label={t('composer.remove')} onClick={onRemove}>
        <X size={14} />
      </button>
    </li>
  );
}
