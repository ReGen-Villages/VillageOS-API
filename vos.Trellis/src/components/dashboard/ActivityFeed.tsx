import { useEffect, useRef, useState, useCallback } from 'react';
import { Pause, Play, PanelRightClose, Search, X } from 'lucide-react';
import type { ActivityEvent } from '../../types/mycelium';
import { formatTimestamp } from '../../utils/formatters';

const typeColors: Record<string, string> = {
  ThingCreated: 'text-emerald-400',
  ThingDeleted: 'text-red-400',
  RelationshipCreated: 'text-blue-400',
  RelationshipDeleted: 'text-red-400',
  PropertyChanged: 'text-amber-400',
  PropertyDeleted: 'text-red-400',
  ServiceHealthChanged: 'text-purple-400',
  DaemonStatusChanged: 'text-cyan-400',
  EndpointServiceRequestCompleted: 'text-teal-400',
  ModelChanged: 'text-indigo-400',
  ModelCleared: 'text-red-400',
};

/** Category groups for the filter chips. */
const categories = [
  { label: 'Model', types: ['ModelChanged', 'ModelCleared'] },
  { label: 'Things', types: ['ThingCreated', 'ThingDeleted'] },
  { label: 'Rels', types: ['RelationshipCreated', 'RelationshipDeleted'] },
  { label: 'Props', types: ['PropertyChanged', 'PropertyDeleted'] },
  { label: 'Services', types: ['ServiceHealthChanged', 'DaemonStatusChanged', 'DaemonStarted', 'DaemonStartFailed', 'EndpointServiceRequestCompleted'] },
] as const;

type CategoryLabel = (typeof categories)[number]['label'];

const STORAGE_KEY_HEIGHT = 'vos-activity-feed-height';
const MIN_HEIGHT = 120;

function loadHeight(): number | null {
  const stored = localStorage.getItem(STORAGE_KEY_HEIGHT);
  if (stored) {
    const n = parseInt(stored, 10);
    if (!isNaN(n) && n >= MIN_HEIGHT) return n;
  }
  return null; // null = fill parent
}

interface Props {
  events: ActivityEvent[];
  onCollapse: () => void;
}

export function ActivityFeed({ events, onCollapse }: Props) {
  const listRef = useRef<HTMLDivElement>(null);
  const [paused, setPaused] = useState(false);
  const snapshotRef = useRef<ActivityEvent[]>([]);
  const pausedAtLengthRef = useRef(0);
  const [enabledCategories, setEnabledCategories] = useState<Set<CategoryLabel>>(
    () => new Set(categories.map((c) => c.label)),
  );
  const [searchText, setSearchText] = useState('');
  const [height, setHeight] = useState<number | null>(loadHeight);
  const containerRef = useRef<HTMLDivElement>(null);
  const dragging = useRef(false);
  const startY = useRef(0);
  const startHeight = useRef(0);

  // Build the set of enabled event types from the active category chips
  const enabledTypes: Set<string> = new Set(
    categories
      .filter((c) => enabledCategories.has(c.label))
      .flatMap((c) => c.types),
  );

  // Snapshot events when pausing
  const togglePause = useCallback(() => {
    setPaused((prev) => {
      if (!prev) {
        // Entering pause — snapshot current events
        snapshotRef.current = [...events];
        pausedAtLengthRef.current = events.length;
      }
      return !prev;
    });
  }, [events]);

  const toggleCategory = useCallback((label: CategoryLabel) => {
    setEnabledCategories((prev) => {
      const next = new Set(prev);
      if (next.has(label)) next.delete(label);
      else next.add(label);
      return next;
    });
  }, []);

  const displayEvents = paused ? snapshotRef.current : events;
  const searchLower = searchText.toLowerCase();
  const filteredEvents = displayEvents.filter(
    (e) => enabledTypes.has(e.Type) && (!searchText || e.Description.toLowerCase().includes(searchLower)),
  );
  const missedCount = paused ? Math.max(0, events.length - pausedAtLengthRef.current) : 0;

  // Auto-scroll only when not paused. Scroll the list element itself rather
  // than scrollIntoView, which also scrolls every scrollable ancestor (the
  // outer dashboard content region) and drags the other widgets up when the
  // feed fills.
  useEffect(() => {
    if (!paused) {
      const list = listRef.current;
      if (list) list.scrollTo({ top: list.scrollHeight, behavior: 'smooth' });
    }
  }, [events.length, paused]);

  // ── Resize drag handlers ──────────────────────────────────────────
  const onPointerDown = useCallback(
    (e: React.PointerEvent) => {
      e.preventDefault();
      dragging.current = true;
      startY.current = e.clientY;
      // If no explicit height yet, measure the current rendered height
      startHeight.current = height ?? containerRef.current?.offsetHeight ?? 400;
      (e.target as HTMLElement).setPointerCapture(e.pointerId);
    },
    [height],
  );

  const onPointerMove = useCallback((e: React.PointerEvent) => {
    if (!dragging.current) return;
    // Dragging up increases height
    const delta = startY.current - e.clientY;
    const newHeight = Math.max(MIN_HEIGHT, startHeight.current + delta);
    setHeight(newHeight);
  }, []);

  const onPointerUp = useCallback(() => {
    if (dragging.current) {
      dragging.current = false;
      setHeight((h) => {
        if (h !== null) localStorage.setItem(STORAGE_KEY_HEIGHT, String(h));
        return h;
      });
    }
  }, []);

  // Prevent text selection while dragging
  useEffect(() => {
    const prevent = (e: Event) => {
      if (dragging.current) e.preventDefault();
    };
    document.addEventListener('selectstart', prevent);
    return () => document.removeEventListener('selectstart', prevent);
  }, []);

  return (
    <div
      ref={containerRef}
      className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 flex flex-col overflow-hidden"
      style={{ height: height ?? 'calc(100vh - 7.5rem)' }}
    >
      {/* Resize handle */}
      <div
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        className="h-1.5 cursor-row-resize flex-shrink-0 group relative"
      >
        <div className="absolute -top-1 -bottom-1 inset-x-0" />
        <div className="w-full h-px mt-[2px] bg-zinc-700 group-hover:bg-blue-500 group-active:bg-blue-400 transition-colors" />
      </div>

      <div className="p-4 pt-2 flex flex-col flex-1 min-h-0 overflow-hidden">
        {/* Header row */}
        <div className="flex items-center justify-between mb-2">
          <h3 className="text-sm font-semibold text-zinc-500 dark:text-zinc-400">Activity Feed</h3>
          <div className="flex items-center gap-1">
            <button
              onClick={togglePause}
              className={`p-1 rounded transition-colors ${
                paused
                  ? 'bg-amber-500/20 text-amber-400 hover:bg-amber-500/30'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-zinc-700'
              }`}
              title={paused ? 'Resume' : 'Pause'}
            >
              {paused ? <Play size={14} /> : <Pause size={14} />}
            </button>
            <button
              onClick={onCollapse}
              className="p-1 rounded text-zinc-400 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
              title="Collapse panel"
            >
              <PanelRightClose size={14} />
            </button>
          </div>
        </div>

        {/* Paused badge */}
        {paused && missedCount > 0 && (
          <div className="text-xs text-amber-400 mb-1">
            Paused — {missedCount} new event{missedCount !== 1 ? 's' : ''} buffered
          </div>
        )}

        {/* Filter chips */}
        <div className="flex flex-wrap gap-1 mb-2">
          {categories.map((cat) => {
            const on = enabledCategories.has(cat.label);
            return (
              <button
                key={cat.label}
                onClick={() => toggleCategory(cat.label)}
                className={`text-[10px] px-1.5 py-0.5 rounded-full border transition-colors ${
                  on
                    ? 'border-zinc-500 text-zinc-200 bg-zinc-700'
                    : 'border-zinc-700 text-zinc-600 bg-transparent'
                }`}
              >
                {cat.label}
              </button>
            );
          })}
        </div>

        {/* Text filter */}
        <div className="flex items-center gap-1.5 mb-2 bg-zinc-900/50 rounded px-2 py-1">
          <Search size={12} className="text-zinc-500 flex-shrink-0" />
          <input
            value={searchText}
            onChange={(e) => setSearchText(e.target.value)}
            placeholder="Filter events..."
            className="bg-transparent text-xs text-zinc-200 placeholder-zinc-600 focus:outline-none flex-1 min-w-0"
          />
          {searchText && (
            <button
              onClick={() => setSearchText('')}
              className="text-zinc-500 hover:text-zinc-300 transition-colors flex-shrink-0"
            >
              <X size={12} />
            </button>
          )}
        </div>

        {/* Event list */}
        <div ref={listRef} className="flex-1 overflow-auto space-y-1 min-h-0">
          {filteredEvents.length === 0 && <p className="text-xs text-zinc-500">No activity yet</p>}
          {filteredEvents.map((e, i) => (
            <div key={i} className="flex gap-2 text-xs py-0.5">
              <span className="text-zinc-500 font-mono flex-shrink-0">{formatTimestamp(e.Timestamp)}</span>
              <span className={typeColors[e.Type] || 'text-zinc-400'}>{e.Description}</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
