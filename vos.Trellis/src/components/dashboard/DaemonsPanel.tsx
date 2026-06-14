import type { DaemonInfo } from '../../types/mycelium';
import { Badge } from '../common/Badge';
import { formatRelativeTime } from '../../utils/formatters';
import { Square } from 'lucide-react';

interface Props {
  daemons: DaemonInfo[];
  onStop: (key: string) => void;
}

export function DaemonsPanel({ daemons, onStop }: Props) {
  return (
    <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
      <h3 className="text-sm font-semibold text-zinc-500 dark:text-zinc-400 mb-3">Daemons</h3>
      {daemons.length === 0 && <p className="text-xs text-zinc-500">No daemons tracked</p>}
      <div className="space-y-2">
        {daemons.map((d) => (
          <div key={d.Key} className="flex items-center justify-between py-2 px-3 bg-zinc-50 dark:bg-zinc-900 rounded-md border border-zinc-200 dark:border-zinc-700">
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-2">
                <span className="text-sm font-mono">{d.Key}</span>
                <Badge label={d.IsRunning ? 'Running' : 'Stopped'} color={d.IsRunning ? 'green' : 'red'} dot />
                {d.IsExternal && <Badge label="External" color="purple" />}
              </div>
              <div className="flex gap-4 text-xs text-zinc-500 mt-1">
                {d.ProcessId && <span>PID: {d.ProcessId}</span>}
                {d.LastContactTime && <span>Last: {formatRelativeTime(d.LastContactTime)}</span>}
                {d.ConsecutiveFailures > 0 && (
                  <span className="text-amber-400">Failures: {d.ConsecutiveFailures}</span>
                )}
              </div>
            </div>
            {d.IsRunning && (
              <button onClick={() => onStop(d.Key)} className="p-1 text-red-400 hover:text-red-300" title="Stop daemon">
                <Square size={14} />
              </button>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
