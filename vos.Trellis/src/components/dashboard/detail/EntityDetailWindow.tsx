/**
 * One floating, draggable, resizable detail window for a single Thing. Generic: what it shows
 * is driven by the model's {@link DetailSpec} — title/subtitle property, property groups, the
 * ordered relations to surface, and the state-change history. Several can be open at once (see
 * DetailWindowManager). Clicking a related Thing opens another window.
 */
import { useCallback, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { X, GripHorizontal, LayoutGrid } from 'lucide-react';
import type { ModelIndex } from '../../../api/dashboardApi';
import { effectiveProperties } from '../../../utils/propertyMapper';
import { formatDateTime, formatGuid, formatPropertyValue, formatTimestamp } from '../../../utils/formatters';
import { badgeTone } from '../widgets/format';
import type { DetailSpec } from '../../../types/dashboard';
import type { StateHistoryCoverage } from '../../../types/vos';
import { useEntityDetail } from './useEntityDetail';
import type { ResolvedRelation } from './entityDetail';

interface Props {
  idx: ModelIndex;
  thingId: string;
  detail: DetailSpec;
  nonce?: number;
  offset: number;
  /** This window's position among the open set, and the total, for tiling on spread. */
  index: number;
  total: number;
  /** Bumped by the manager when any window's spread button is clicked; re-tiles this window. */
  spreadTick: number;
  zIndex: number;
  onClose: () => void;
  onFocus: () => void;
  onSpread: () => void;
  openDetail: (thingId: string) => void;
}

const WINDOW_WIDTH = 460;
const WINDOW_HEIGHT = 560;

/** Tile the open windows edge-to-edge across the viewport: as many columns as fit, wrapping to rows. */
function tiledPosition(index: number): { x: number; y: number } {
  const gap = 16;
  const top = 80;
  const columns = Math.max(1, Math.floor((window.innerWidth - gap) / (WINDOW_WIDTH + gap)));
  const column = index % columns;
  const row = Math.floor(index / columns);
  return { x: gap + column * (WINDOW_WIDTH + gap), y: top + row * (WINDOW_HEIGHT + gap) };
}

function StatePills({ states }: { states: string[] }) {
  const { t } = useTranslation();
  if (!states.length) return <span className="text-[11px] text-zinc-400 dark:text-zinc-500">{t('entityDetail.noDerivedStates')}</span>;
  return (
    <div className="flex flex-wrap gap-1">
      {states.map((s) => (
        <span key={s} className={`text-[10px] font-bold px-1.5 py-0.5 rounded-full ${badgeTone(s)}`}>
          {s}
        </span>
      ))}
    </div>
  );
}

/** How far back the history reaches. Shown so an empty or short list reads as "not retained"
 *  rather than "never happened" — in-memory history only starts when the engine loaded the model. */
function CoverageNote({ coverage }: { coverage: StateHistoryCoverage }) {
  const { t } = useTranslation();
  if (coverage.Source !== 'in-memory') return null;
  return (
    <div className="mt-1.5 text-[10.5px] text-zinc-400 dark:text-zinc-500">
      {t('entityDetail.inMemoryHistory', { time: formatDateTime(coverage.From) })}
    </div>
  );
}

/** The configured relations, rendered as ordered groups; each edge names its subject and target,
 *  shows the related Thing's chosen properties and derived states, and nests its own relations. */
function RelationGroups({
  relations,
  statesById,
  openDetail,
}: {
  relations: ResolvedRelation[];
  statesById: Map<string, string[]>;
  openDetail: (thingId: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <div className="space-y-2.5">
      {relations.map((group) => (
        <div key={group.label}>
          <div className="text-[10.5px] font-semibold uppercase tracking-wide text-zinc-400 dark:text-zinc-500 mb-1">
            {group.label} ({group.edges.length})
          </div>
          {group.edges.length === 0 ? (
            <div className="text-[11px] text-zinc-400">{t('entityDetail.none')}</div>
          ) : (
            <div className="space-y-1.5">
              {group.edges.map((edge) => (
                <div key={edge.thingId} className="rounded-md border border-zinc-200 dark:border-zinc-700">
                  <button
                    onClick={() => openDetail(edge.thingId)}
                    className="block w-full text-left px-2 py-1.5 hover:bg-zinc-50 dark:hover:bg-zinc-800 transition-colors"
                  >
                    <div className="text-[10px] uppercase tracking-wide text-zinc-400 dark:text-zinc-500 truncate">
                      {edge.subjectName} <span className="mx-0.5">{group.predicate} →</span> {edge.targetName}
                    </div>
                    <div className="text-[12px] font-semibold text-blue-600 dark:text-blue-400 truncate">{edge.relatedName}</div>
                    <div className="mt-1">
                      <StatePills states={statesById.get(edge.thingId) ?? []} />
                    </div>
                    {edge.properties.length > 0 && (
                      <div className="mt-1 grid grid-cols-[minmax(0,130px)_1fr] gap-x-2 gap-y-0.5">
                        {edge.properties.map(([key, value]) => (
                          <div key={key} className="contents">
                            <div className="text-[11px] text-zinc-400 dark:text-zinc-500 truncate" title={key}>{key}</div>
                            <div className="text-[11px] font-mono text-zinc-700 dark:text-zinc-200 break-words">{formatPropertyValue(value)}</div>
                          </div>
                        ))}
                      </div>
                    )}
                  </button>
                  {edge.children.length > 0 && (
                    <div className="ml-3 mb-1.5 mr-1.5 pl-2 border-l border-zinc-200 dark:border-zinc-700">
                      <RelationGroups relations={edge.children} statesById={statesById} openDetail={openDetail} />
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      ))}
    </div>
  );
}

export function EntityDetailWindow({ idx, thingId, detail, nonce, offset, index, total, spreadTick, zIndex, onClose, onFocus, onSpread, openDetail }: Props) {
  const { t } = useTranslation();
  const { loading, root, relations, statesById, stateChanges, coverage } = useEntityDetail(idx, thingId, detail, nonce);

  const props = root ? effectiveProperties(root) : {};
  const title = (detail.titleProperty && (props[detail.titleProperty] as string)) || root?.Name || formatGuid(thingId);
  const subtitle = detail.subtitleProperty ? (props[detail.subtitleProperty] as string) : undefined;

  const groups = detail.propertyGroups?.length
    ? detail.propertyGroups.map((g) => ({ label: g.label, entries: g.keys.filter((k) => k in props).map((k) => [k, props[k]] as const) }))
    : [{ label: t('entityDetail.properties'), entries: Object.entries(props) }];

  // ── Drag & layout ─────────────────────────────────────────────────────────
  const [pos, setPos] = useState({ x: 120 + offset * 28, y: 90 + offset * 28 });
  // Re-tile when the spread button is clicked, using React's "adjust state during render"
  // pattern (a guarded render-phase update) rather than an effect: spreadTick only advances on
  // an explicit click, and starts at 0 so the initial cascade above is kept until then.
  const [appliedSpread, setAppliedSpread] = useState(0);
  if (spreadTick !== appliedSpread) {
    setAppliedSpread(spreadTick);
    if (spreadTick > 0) setPos(tiledPosition(index));
  }
  const drag = useRef<{ dx: number; dy: number } | null>(null);
  const onPointerDown = useCallback(
    (e: React.PointerEvent) => {
      onFocus();
      drag.current = { dx: e.clientX - pos.x, dy: e.clientY - pos.y };
      (e.target as HTMLElement).setPointerCapture(e.pointerId);
    },
    [pos, onFocus],
  );
  const onPointerMove = useCallback((e: React.PointerEvent) => {
    if (!drag.current) return;
    setPos({ x: Math.max(0, e.clientX - drag.current.dx), y: Math.max(0, e.clientY - drag.current.dy) });
  }, []);
  const onPointerUp = useCallback((e: React.PointerEvent) => {
    drag.current = null;
    (e.target as HTMLElement).releasePointerCapture(e.pointerId);
  }, []);

  return (
    <div
      onPointerDown={onFocus}
      className="fixed flex flex-col rounded-xl border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 shadow-2xl overflow-hidden resize"
      style={{ left: pos.x, top: pos.y, zIndex, width: 460, height: 560, minWidth: 320, minHeight: 260, maxWidth: '95vw', maxHeight: '90vh' }}
    >
      {/* Title bar (drag handle) */}
      <div
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        className="flex items-center gap-2 px-3 py-2 border-b border-zinc-200 dark:border-zinc-700 bg-zinc-50 dark:bg-zinc-800 cursor-move select-none flex-shrink-0"
      >
        <GripHorizontal size={14} className="text-zinc-400 flex-shrink-0" />
        <div className="min-w-0 flex-1">
          <div className="text-sm font-bold text-zinc-900 dark:text-white truncate">{title}</div>
          <div className="text-[10.5px] text-zinc-400 dark:text-zinc-500 truncate">
            {subtitle ? `${subtitle} · ` : ''}
            {root?.Name ?? formatGuid(thingId)}
          </div>
        </div>
        {total > 1 && (
          <button
            onPointerDown={(e) => e.stopPropagation()}
            onClick={onSpread}
            className="text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 flex-shrink-0"
            title={t('entityDetail.spread')}
          >
            <LayoutGrid size={15} />
          </button>
        )}
        <button
          onPointerDown={(e) => e.stopPropagation()}
          onClick={onClose}
          className="text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 flex-shrink-0"
          title={t('entityDetail.close')}
        >
          <X size={16} />
        </button>
      </div>

      <div className="flex-1 overflow-auto p-3 space-y-4 text-sm">
        {/* Derived states of the root */}
        <section>
          <SectionTitle>{t('entityDetail.derivedStates')}</SectionTitle>
          <StatePills states={statesById.get(thingId) ?? []} />
        </section>

        {/* Details */}
        <section className="space-y-2">
          <SectionTitle>{t('entityDetail.details')}</SectionTitle>
          {groups.map((group) => (
            <div key={group.label}>
              {detail.propertyGroups?.length ? (
                <div className="text-[10.5px] font-semibold uppercase tracking-wide text-zinc-400 dark:text-zinc-500 mb-1">{group.label}</div>
              ) : null}
              <div className="grid grid-cols-[minmax(0,140px)_1fr] gap-x-3 gap-y-0.5">
                {group.entries.length === 0 && <div className="text-[11px] text-zinc-400 col-span-2">{t('entityDetail.noProperties')}</div>}
                {group.entries.map(([key, value]) => (
                  <div key={key} className="contents">
                    <div className="text-[11.5px] text-zinc-400 dark:text-zinc-500 truncate" title={key}>{key}</div>
                    <div className="text-[11.5px] text-zinc-700 dark:text-zinc-200 font-mono break-words">{String(value ?? '—')}</div>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </section>

        {/* Configured relations, in the model's declared order */}
        {relations.length > 0 && (
          <section>
            <SectionTitle>{t('entityDetail.relations')}</SectionTitle>
            <RelationGroups relations={relations} statesById={statesById} openDetail={openDetail} />
          </section>
        )}

        {/* Handling history — the root's own derived-state changes, oldest first */}
        {detail.history?.enabled !== false && (
          <section>
            <SectionTitle>
              {t('entityDetail.handlingHistory')} {loading ? `· ${t('entityDetail.loading')}` : coverage ? `· ${stateChanges.length}` : ''}
            </SectionTitle>
            {!loading && !coverage && (
              <div className="text-[11px] text-zinc-400">{t('entityDetail.stateHistoryUnavailable')}</div>
            )}
            {coverage && stateChanges.length === 0 && (
              <div className="text-[11px] text-zinc-400">{t('entityDetail.noStateChanges')}</div>
            )}
            {stateChanges.length > 0 && (
              <ol className="mt-1 space-y-1.5">
                {stateChanges.map((change, i) => (
                  <li key={`${change.at}-${i}`} className="flex gap-2 text-[11.5px]">
                    <span className="text-zinc-400 dark:text-zinc-500 font-mono whitespace-nowrap flex-shrink-0 w-[62px]">
                      {formatTimestamp(change.at)}
                    </span>
                    <span className="min-w-0 flex flex-wrap items-center gap-1">
                      {change.entered.map((s) => (
                        <span key={`entered-${s}`} className={`text-[10px] font-bold px-1.5 py-0.5 rounded-full ${badgeTone(s)}`}>
                          + {s}
                        </span>
                      ))}
                      {change.exited.map((s) => (
                        <span
                          key={`exited-${s}`}
                          className="text-[10px] px-1.5 py-0.5 rounded-full line-through bg-zinc-100 text-zinc-500 dark:bg-zinc-800 dark:text-zinc-500"
                        >
                          {s}
                        </span>
                      ))}
                    </span>
                  </li>
                ))}
              </ol>
            )}
            {coverage && <CoverageNote coverage={coverage} />}
          </section>
        )}
      </div>
    </div>
  );
}

function SectionTitle({ children }: { children: React.ReactNode }) {
  return <h4 className="text-[11px] font-bold uppercase tracking-wide text-zinc-500 dark:text-zinc-400 mb-1.5">{children}</h4>;
}
