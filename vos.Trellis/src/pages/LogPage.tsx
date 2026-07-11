import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Terminal, Pause, Play, Trash2 } from 'lucide-react';
import { useLogTail } from '../hooks/useLogTail';

/** Live tail of the Mycelium broker log, streamed over SSE. */
export function LogPage() {
  const { lines, connected, clear } = useLogTail();
  const [autoScroll, setAutoScroll] = useState(true);
  const scrollRef = useRef<HTMLDivElement>(null);

  // Stick to the bottom as new lines arrive, unless the user paused auto-scroll.
  useLayoutEffect(() => {
    if (!autoScroll) return;
    const el = scrollRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [lines, autoScroll]);

  // If the user scrolls up, pause auto-scroll; re-stick when they return to the bottom.
  useEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    const onScroll = () => {
      const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 40;
      setAutoScroll(atBottom);
    };
    el.addEventListener('scroll', onScroll);
    return () => el.removeEventListener('scroll', onScroll);
  }, []);

  return (
    <div className="h-full flex flex-col">
      <div className="flex items-center justify-between px-6 pt-6 pb-4 flex-shrink-0">
        <div className="flex items-center gap-3">
          <Terminal className="w-6 h-6 text-zinc-500" />
          <h2 className="text-xl font-bold">Broker Log</h2>
          <span className="text-xs text-zinc-500">{lines.length} lines</span>
        </div>
        <div className="flex items-center gap-4 text-xs">
          <div className="flex items-center gap-1.5">
            <span className={`w-2 h-2 rounded-full ${connected ? 'bg-emerald-500' : 'bg-red-500'}`} />
            <span className="text-zinc-500">{connected ? 'Streaming' : 'Reconnecting'}</span>
          </div>
          <button
            onClick={() => setAutoScroll((v) => !v)}
            title={autoScroll ? 'Pause auto-scroll' : 'Resume auto-scroll'}
            className="flex items-center gap-1.5 p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
          >
            {autoScroll ? <Pause size={14} /> : <Play size={14} />}
            {autoScroll ? 'Pause' : 'Resume'}
          </button>
          <button
            onClick={clear}
            title="Clear the view"
            className="flex items-center gap-1.5 p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
          >
            <Trash2 size={14} />
            Clear
          </button>
        </div>
      </div>

      <div className="flex-1 min-h-0 px-6 pb-6">
        <div
          ref={scrollRef}
          className="h-full overflow-auto rounded-md bg-zinc-950 border border-zinc-800 p-3 font-mono text-xs leading-relaxed text-zinc-300"
        >
          {lines.length === 0 ? (
            <div className="text-zinc-600">Waiting for log output…</div>
          ) : (
            lines.map((line, i) => (
              <div key={i} className="whitespace-pre-wrap break-all">
                {line || ' '}
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
}
