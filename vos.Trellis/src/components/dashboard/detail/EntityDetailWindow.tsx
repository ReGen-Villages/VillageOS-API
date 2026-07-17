/**
 * One floating, draggable, resizable detail window for a single Thing. Generic: what it shows
 * is driven by the model's {@link DetailSpec} (title/subtitle property, property groups,
 * involved-things traversal, movement predicates). Several can be open at once (see
 * DetailWindowManager). Clicking an involved Thing opens another window.
 */
import { useCallback, useRef, useState } from 'react';
import { X, GripHorizontal } from 'lucide-react';
import type { ModelIndex } from '../../../api/dashboardApi';
import { effectiveProperties } from '../../../utils/propertyMapper';
import { formatDateTime, formatGuid, formatPropertyValue, formatTimestamp } from '../../../utils/formatters';
import { badgeTone } from '../widgets/format';
import type { DetailSpec } from '../../../types/dashboard';
import type { StateHistoryCoverage, StateTransition } from '../../../types/vos';
import { useEntityDetail } from './useEntityDetail';

interface Props {
  idx: ModelIndex;
  thingId: string;
  detail: DetailSpec;
  nonce?: number;
  offset: number;
  zIndex: number;
  onClose: () => void;
  onFocus: () => void;
  openDetail: (thingId: string) => void;
}

function StatePills({ states }: { states: string[] }) {
  if (!states.length) return <span className="text-[11px] text-zinc-400 dark:text-zinc-500">no derived states</span>;
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

/** How far back the history reaches. Shown so an empty or short timeline reads as "not retained"
 *  rather than "never happened" — in-memory history only starts when the engine loaded the model. */
function CoverageNote({ coverage }: { coverage: StateHistoryCoverage }) {
  if (coverage.Source !== 'in-memory') return null;
  return (
    <div className="mt-1.5 text-[10.5px] text-zinc-400 dark:text-zinc-500">
      In-memory history — since {formatDateTime(coverage.From)}. Earlier transitions are not retained.
    </div>
  );
}

function TransitionRow({ transition }: { transition: StateTransition }) {
  return (
    <li className="flex gap-2 text-[11.5px]">
      <span className="text-zinc-400 dark:text-zinc-500 font-mono whitespace-nowrap flex-shrink-0 w-[70px]">
        {formatTimestamp(transition.At)}
      </span>
      <span className="min-w-0">
        <span className="flex flex-wrap items-center gap-1">
          {transition.Entered.map((s) => (
            <span key={`entered-${s}`} className={`text-[10px] font-bold px-1.5 py-0.5 rounded-full ${badgeTone(s)}`}>
              + {s}
            </span>
          ))}
          {transition.Exited.map((s) => (
            <span
              key={`exited-${s}`}
              className="text-[10px] px-1.5 py-0.5 rounded-full line-through bg-zinc-100 text-zinc-500 dark:bg-zinc-800 dark:text-zinc-500"
            >
              {s}
            </span>
          ))}
        </span>
        {transition.TriggeringProperty && (
          <span className="block text-zinc-400 dark:text-zinc-500 font-mono">
            {transition.TriggeringProperty}: {formatPropertyValue(transition.OldValue)} → {formatPropertyValue(transition.NewValue)}
          </span>
        )}
      </span>
    </li>
  );
}

export function EntityDetailWindow({ idx, thingId, detail, nonce, offset, zIndex, onClose, onFocus, openDetail }: Props) {
  const { loading, root, involvedIds, statesById, timeline, stateHistory } = useEntityDetail(idx, thingId, detail, nonce);

  const props = root ? effectiveProperties(root) : {};
  const title = (detail.titleProperty && (props[detail.titleProperty] as string)) || root?.Name || formatGuid(thingId);
  const subtitle = detail.subtitleProperty ? (props[detail.subtitleProperty] as string) : undefined;

  const groups = detail.propertyGroups?.length
    ? detail.propertyGroups.map((g) => ({ label: g.label, entries: g.keys.filter((k) => k in props).map((k) => [k, props[k]] as const) }))
    : [{ label: 'Properties', entries: Object.entries(props) }];

  // ── Drag ────────────────────────────────────────────────────────────────
  const [pos, setPos] = useState({ x: 120 + offset * 28, y: 90 + offset * 28 });
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
        <button onClick={onClose} className="text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 flex-shrink-0" title="Close">
          <X size={16} />
        </button>
      </div>

      <div className="flex-1 overflow-auto p-3 space-y-4 text-sm">
        {/* Derived states of the root */}
        <section>
          <SectionTitle>Derived states</SectionTitle>
          <StatePills states={statesById.get(thingId) ?? []} />
        </section>

        {/* How the root reached those states, oldest-first like the handling history below */}
        <section>
          <SectionTitle>
            State transitions {loading ? '· loading…' : stateHistory ? `· ${stateHistory.Transitions.length}` : ''}
          </SectionTitle>
          {!loading && !stateHistory && (
            <div className="text-[11px] text-zinc-400">State history unavailable — no active reactive engine for this model.</div>
          )}
          {stateHistory && stateHistory.Transitions.length === 0 && (
            <div className="text-[11px] text-zinc-400">No transitions recorded.</div>
          )}
          {stateHistory && stateHistory.Transitions.length > 0 && (
            <ol className="mt-1 space-y-1.5">
              {stateHistory.Transitions.map((transition, i) => (
                <TransitionRow key={`${transition.At}-${i}`} transition={transition} />
              ))}
            </ol>
          )}
          {stateHistory && <CoverageNote coverage={stateHistory.Coverage} />}
        </section>

        {/* Details */}
        <section className="space-y-2">
          <SectionTitle>Details</SectionTitle>
          {groups.map((group) => (
            <div key={group.label}>
              {detail.propertyGroups?.length ? (
                <div className="text-[10.5px] font-semibold uppercase tracking-wide text-zinc-400 dark:text-zinc-500 mb-1">{group.label}</div>
              ) : null}
              <div className="grid grid-cols-[minmax(0,140px)_1fr] gap-x-3 gap-y-0.5">
                {group.entries.length === 0 && <div className="text-[11px] text-zinc-400 col-span-2">No properties.</div>}
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

        {/* Involved things, each with its own derived states */}
        <section className="space-y-1.5">
          <SectionTitle>Things involved ({involvedIds.length})</SectionTitle>
          {involvedIds.length === 0 && <div className="text-[11px] text-zinc-400">Nothing linked.</div>}
          {involvedIds.map((id) => {
            const t = idx.byId.get(id);
            return (
              <button
                key={id}
                onClick={() => openDetail(id)}
                className="block w-full text-left rounded-md border border-zinc-200 dark:border-zinc-700 px-2 py-1.5 hover:bg-zinc-50 dark:hover:bg-zinc-800 transition-colors"
              >
                <div className="text-[12px] font-semibold text-blue-600 dark:text-blue-400 truncate">{t?.Name ?? formatGuid(id)}</div>
                <div className="mt-1">
                  <StatePills states={statesById.get(id) ?? []} />
                </div>
              </button>
            );
          })}
        </section>

        {/* Handling history */}
        {detail.history?.enabled !== false && (
          <section>
            <SectionTitle>Handling history {loading ? '· loading…' : `· ${timeline.length}`}</SectionTitle>
            {!loading && timeline.length === 0 && <div className="text-[11px] text-zinc-400">No recorded history.</div>}
            <ol className="mt-1 space-y-1.5">
              {timeline.map((event, i) => (
                <li key={i} className="flex gap-2 text-[11.5px]">
                  <span className="text-zinc-400 dark:text-zinc-500 font-mono whitespace-nowrap flex-shrink-0 w-[70px]">
                    {event.time ? formatTimestamp(event.time) : '—'}
                  </span>
                  <span
                    className={`flex-shrink-0 w-1.5 rounded-full mt-1 mb-1 ${event.kind === 'movement' ? 'bg-blue-400' : 'bg-amber-400'}`}
                    aria-hidden
                  />
                  <span className="min-w-0">
                    <span className="text-zinc-700 dark:text-zinc-200">{event.label}</span>
                    <span className="text-zinc-400 dark:text-zinc-500">
                      {'  '}
                      {event.thingName}
                      {event.author ? ` · ${event.author}` : ''}
                    </span>
                  </span>
                </li>
              ))}
            </ol>
          </section>
        )}
      </div>
    </div>
  );
}

function SectionTitle({ children }: { children: React.ReactNode }) {
  return <h4 className="text-[11px] font-bold uppercase tracking-wide text-zinc-500 dark:text-zinc-400 mb-1.5">{children}</h4>;
}
