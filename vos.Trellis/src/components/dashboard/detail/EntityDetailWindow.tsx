/**
 * One floating, draggable, resizable detail window for a single Thing. Generic: what it shows
 * is driven by the model's {@link DetailSpec} — title/subtitle property, property groups, the
 * ordered relations to surface, and the state-change history. Several can be open at once (see
 * DetailWindowManager). Clicking a related Thing opens another window.
 */
import { useCallback, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { X, GripHorizontal, LayoutGrid } from 'lucide-react';
import type { Entered, ModelIndex } from '@tendril-ui/tendril';
import { AskedFields, effectiveProperties } from '@tendril-ui/tendril';
import { formatGuid, formatPropertyValue, formatTimestamp } from '../../../utils/formatters';
import { useNumberDisplaySettings } from '../../../hooks/useNumberDisplaySettings';
import type { DeclaredPropertyTypes } from '../../../hooks/useDeclaredPropertyTypes';
import type { NumberDisplaySettings } from '../../../utils/guiSettings';
import { badgeTone } from '../widgets/format';
import type { DetailSpecification } from '@tendril-ui/tendril';
import type { StateHistoryCoverage } from '@tendril-ui/tendril';
import { useEntityDetail } from './useEntityDetail';
import { DecisionExplanations } from './DecisionExplanations';
import { decisionsOn } from './decisionExplanation';
import type { ResolvedRelation } from './entityDetail';
import { isCallState, mayBeRetried, type ServiceCall } from './serviceHandling';
import { relationshipApi } from '../../../api/relationshipApi';
import { ApiError } from '../../../api/client';

interface Props {
  modelIndex: ModelIndex;
  thingId: string;
  detail: DetailSpecification;
  nonce?: number;
  offset: number;
  index: number;
  total: number;
  spreadTick: number;
  zIndex: number;
  onClose: () => void;
  onFocus: () => void;
  onSpread: () => void;
  openDetail: (thingId: string) => void;
  /** Resolved properties per Thing id, for formatting a value to what the platform says it holds.
   * Null while the read is in flight — values format by shape until it lands. */
  declaredTypes: DeclaredPropertyTypes | null;
  mayChangeData: boolean;
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

/** The ranges the history can never show, so an absent change reads as "not kept" rather than "never happened". */
function CoverageNote({ coverage }: { coverage: StateHistoryCoverage }) {
  const { t } = useTranslation();
  if (coverage.RangesKeepingNoHistory.length === 0) return null;
  return (
    <div className="mt-1.5 text-[10.5px] text-zinc-400 dark:text-zinc-500">
      {t('entityDetail.keepsNoHistory', { ranges: coverage.RangesKeepingNoHistory.join(', ') })}
    </div>
  );
}

const REASON = 'reason';

function retryRefusal(error: unknown, t: TFunction): string {
  if (error instanceof ApiError && error.status === 409) return t('entityDetail.retry.notFailedOrBlocked');
  if (error instanceof ApiError && error.status === 403) return t('entityDetail.retry.mayNotChange');
  return t('entityDetail.retry.refused', { reason: error instanceof Error ? error.message : String(error) });
}

/** Reads the record again after any answer to a retry, so the card shows the state the platform holds, not a guessed one. */
function ServiceCallRow({
  call,
  mayRetry,
  openDetail,
  readCallsAgain,
}: {
  call: ServiceCall;
  mayRetry: boolean;
  openDetail: (thingId: string) => void;
  readCallsAgain: () => void;
}) {
  const { t } = useTranslation();
  const [askingWhy, setAskingWhy] = useState(false);
  const [entered, setEntered] = useState<Entered>({});
  const [retrying, setRetrying] = useState(false);
  const [refusal, setRefusal] = useState<string | null>(null);

  const askWhy = () => {
    setEntered({});
    setAskingWhy(true);
  };

  const retry = async () => {
    const reason = String(entered[REASON] ?? '').trim();
    setRetrying(true);
    setRefusal(null);
    try {
      await relationshipApi.retry(call.relationshipId, reason || undefined);
    } catch (error) {
      setRefusal(retryRefusal(error, t));
    } finally {
      setRetrying(false);
      setAskingWhy(false);
      readCallsAgain();
    }
  };

  return (
    <li className="flex gap-2 text-[11.5px]">
      <span className="text-zinc-400 dark:text-zinc-500 font-mono whitespace-nowrap flex-shrink-0 w-[62px]">
        {call.at ? formatTimestamp(call.at) : '—'}
      </span>
      <span className="min-w-0 flex-1">
        <span className="flex flex-wrap items-center gap-x-1.5 gap-y-1">
          {call.serviceId ? (
            <button
              onClick={() => openDetail(call.serviceId!)}
              className="font-semibold text-blue-600 dark:text-blue-400 hover:underline"
            >
              {call.serviceName}
            </button>
          ) : (
            <span className="font-semibold text-zinc-700 dark:text-zinc-200">{call.serviceName}</span>
          )}
          <span className="text-zinc-400 dark:text-zinc-500 truncate">{call.connectionName}</span>
          {call.state && (
            <span className={`text-[10px] font-bold px-1.5 py-0.5 rounded-full ${badgeTone(call.state)}`}>
              {isCallState(call.state) ? t(`entityDetail.callState.${call.state}`) : call.state}
            </span>
          )}
          {mayRetry && mayBeRetried(call) && !askingWhy && (
            <button
              type="button"
              onClick={askWhy}
              aria-label={t('entityDetail.retry.title', { service: call.serviceName })}
              title={t('entityDetail.retry.title', { service: call.serviceName })}
              className="text-[10px] font-semibold text-blue-600 dark:text-blue-400 hover:underline"
            >
              {t('entityDetail.retry.button')}
            </button>
          )}
        </span>
        {call.error && (
          <span className="block text-[11px] text-red-600 dark:text-red-400 break-words">{call.error}</span>
        )}
        {askingWhy && (
          <span className="mt-1 flex flex-wrap items-center gap-1.5">
            <AskedFields
              fields={[{ key: REASON, label: t('entityDetail.retry.reason'), optional: true }]}
              options={[[]]}
              entered={entered}
              onChange={setEntered}
              disabled={retrying}
              inline
            />
            <button
              type="button"
              onClick={retry}
              disabled={retrying}
              className="text-[10px] font-semibold text-blue-600 dark:text-blue-400 hover:underline disabled:opacity-50"
            >
              {t('entityDetail.retry.send')}
            </button>
            <button
              type="button"
              onClick={() => setAskingWhy(false)}
              disabled={retrying}
              className="text-[10px] text-zinc-500 hover:underline disabled:opacity-50"
            >
              {t('entityDetail.retry.cancel')}
            </button>
          </span>
        )}
        {refusal && (
          <span role="alert" className="block text-[11px] text-amber-700 dark:text-amber-400 break-words">{refusal}</span>
        )}
      </span>
    </li>
  );
}

/** Every service the platform ran on this Thing, oldest first, each with the connection it was
 *  reached through and how that service call ended. A service is a Thing, so its name opens its card. */
function ServiceCalls({
  calls,
  mayRetry,
  openDetail,
  readCallsAgain,
}: {
  calls: ServiceCall[];
  mayRetry: boolean;
  openDetail: (thingId: string) => void;
  readCallsAgain: () => void;
}) {
  const { t } = useTranslation();
  return (
    <>
      <ol className="mt-1 space-y-1.5">
        {calls.map((call) => (
          <ServiceCallRow
            key={call.relationshipId}
            call={call}
            mayRetry={mayRetry}
            openDetail={openDetail}
            readCallsAgain={readCallsAgain}
          />
        ))}
      </ol>
      {calls.some((call) => !call.at) && (
        <div className="mt-1.5 text-[10.5px] text-zinc-400 dark:text-zinc-500">
          {t('entityDetail.serviceCallsSinceModelLoad')}
        </div>
      )}
    </>
  );
}

/** The configured relations, rendered as ordered groups; each relationship names its subject and target,
 *  shows the related Thing's chosen properties and derived states, and nests its own relations. */
function RelationGroups({
  relations,
  statesById,
  openDetail,
  declaredTypes,
  numbers,
}: {
  relations: ResolvedRelation[];
  statesById: Map<string, string[]>;
  openDetail: (thingId: string) => void;
  declaredTypes: DeclaredPropertyTypes | null;
  numbers: NumberDisplaySettings;
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
                            <div className="text-[11px] font-mono text-zinc-700 dark:text-zinc-200 break-words">
                              {formatPropertyValue(value, declaredTypes?.[edge.thingId]?.[key]?.Type, numbers)}
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </button>
                  {edge.children.length > 0 && (
                    <div className="ml-3 mb-1.5 mr-1.5 pl-2 border-l border-zinc-200 dark:border-zinc-700">
                      <RelationGroups relations={edge.children} statesById={statesById} openDetail={openDetail} declaredTypes={declaredTypes} numbers={numbers} />
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

export function EntityDetailWindow({ modelIndex, thingId, detail, nonce, offset, index, total, spreadTick, zIndex, onClose, onFocus, onSpread, openDetail, declaredTypes, mayChangeData }: Props) {
  const { t } = useTranslation();
  const { loading, root, relations, statesById, stateChanges, coverage, calls, readCallsAgain } = useEntityDetail(modelIndex, thingId, detail, nonce);
  const decisions = useMemo(() => decisionsOn(thingId, modelIndex), [thingId, modelIndex]);
  const numbers = useNumberDisplaySettings();

  const props = root ? effectiveProperties(root, modelIndex) : {};
  const title = (detail.titleProperty && (props[detail.titleProperty] as string)) || root?.Name || formatGuid(thingId);
  const subtitle = detail.subtitleProperty ? (props[detail.subtitleProperty] as string) : undefined;

  const groups = detail.propertyGroups?.length
    ? detail.propertyGroups.map((g) => ({ label: g.label, entries: g.keys.filter((k) => k in props).map((k) => [k, props[k]] as const) }))
    : [{ label: t('entityDetail.properties'), entries: Object.entries(props) }];

  const [position, setPosition] = useState({ x: 120 + offset * 28, y: 90 + offset * 28 });
  // Re-tile when the spread button is clicked, using React's "adjust state during render"
  // pattern (a guarded render-phase update) rather than an effect: spreadTick only advances on
  // an explicit click, and starts at 0 so the initial cascade above is kept until then.
  const [appliedSpread, setAppliedSpread] = useState(0);
  if (spreadTick !== appliedSpread) {
    setAppliedSpread(spreadTick);
    if (spreadTick > 0) setPosition(tiledPosition(index));
  }
  const drag = useRef<{ dx: number; dy: number } | null>(null);
  const onPointerDown = useCallback(
    (e: React.PointerEvent) => {
      onFocus();
      drag.current = { dx: e.clientX - position.x, dy: e.clientY - position.y };
      (e.target as HTMLElement).setPointerCapture(e.pointerId);
    },
    [position, onFocus],
  );
  const onPointerMove = useCallback((e: React.PointerEvent) => {
    if (!drag.current) return;
    setPosition({ x: Math.max(0, e.clientX - drag.current.dx), y: Math.max(0, e.clientY - drag.current.dy) });
  }, []);
  const onPointerUp = useCallback((e: React.PointerEvent) => {
    drag.current = null;
    (e.target as HTMLElement).releasePointerCapture(e.pointerId);
  }, []);

  return (
    <div
      onPointerDown={onFocus}
      className="fixed flex flex-col rounded-xl border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 shadow-2xl overflow-hidden resize"
      style={{ left: position.x, top: position.y, zIndex, width: 460, height: 560, minWidth: 320, minHeight: 260, maxWidth: '95vw', maxHeight: '90vh' }}
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
                    <div className="text-[11.5px] text-zinc-700 dark:text-zinc-200 font-mono break-words">
                      {value === null || value === undefined ? '—' : formatPropertyValue(value, declaredTypes?.[thingId]?.[key]?.Type, numbers)}
                    </div>
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
            <RelationGroups relations={relations} statesById={statesById} openDetail={openDetail} declaredTypes={declaredTypes} numbers={numbers} />
          </section>
        )}

        {calls.length > 0 && (
          <section>
            <SectionTitle>{t('entityDetail.handledBy')}</SectionTitle>
            <ServiceCalls calls={calls} mayRetry={mayChangeData} openDetail={openDetail} readCallsAgain={readCallsAgain} />
          </section>
        )}

        {decisions.length > 0 && (
          <section>
            <SectionTitle>{t('entityDetail.decisions')}</SectionTitle>
            <DecisionExplanations decisions={decisions} openDetail={openDetail} />
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
              <div className="text-[11px] text-zinc-400">
                {t(coverage.RangesKeepingHistory.length === 0 ? 'entityDetail.noRangeKeepsHistory' : 'entityDetail.noStateChanges')}
              </div>
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
