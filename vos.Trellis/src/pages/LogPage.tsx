import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Terminal, Pause, Play, Trash2, Download, HardDriveDownload } from 'lucide-react';
import { useLogTail } from '../hooks/useLogTail';
import { fetchFullLog } from '../api/logsApi';
import { snapshotBlob, snapshotFileName, triggerDownload } from '../utils/logDownload';
import { toast } from '../components/common/Toast';

/** Live tail of the Mycelium broker log — or a service daemon's log when ?service=<key> is set.
 *  Keyed by service so switching sources remounts the view with fresh state. */
export function LogPage() {
  const [params] = useSearchParams();
  const service = params.get('service') ?? undefined;
  return <LogView key={service ?? 'broker'} service={service} />;
}

function LogView({ service }: { service?: string }) {
  const { lines, connected, clear } = useLogTail(service);
  const title = service ? `${service} log` : 'Broker Log';
  const [autoScroll, setAutoScroll] = useState(true);
  const [downloadingFull, setDownloadingFull] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);

  const downloadSnapshot = () =>
    triggerDownload(snapshotBlob(lines), snapshotFileName(service, new Date()));

  const downloadFullLog = async () => {
    setDownloadingFull(true);
    try {
      const { blob, fileName } = await fetchFullLog(service);
      triggerDownload(blob, fileName);
    } catch {
      toast.error('Could not download the full log.');
    } finally {
      setDownloadingFull(false);
    }
  };

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
          <h2 className="text-xl font-bold capitalize">{title}</h2>
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
            onClick={downloadSnapshot}
            disabled={lines.length === 0}
            title="Download the lines currently in view"
            className="flex items-center gap-1.5 p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors disabled:opacity-40 disabled:hover:bg-transparent disabled:hover:text-zinc-500"
          >
            <Download size={14} />
            Snapshot
          </button>
          <button
            onClick={downloadFullLog}
            disabled={downloadingFull}
            title="Download the whole log file from the broker"
            className="flex items-center gap-1.5 p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors disabled:opacity-40 disabled:hover:bg-transparent disabled:hover:text-zinc-500"
          >
            <HardDriveDownload size={14} />
            {downloadingFull ? 'Downloading…' : 'Full log'}
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
