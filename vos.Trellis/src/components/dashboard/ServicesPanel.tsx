import type { RegisteredService } from '../../types/mycelium';
import { Badge } from '../common/Badge';
import { formatMs, formatRelativeTime } from '../../utils/formatters';
import { Play, Square } from 'lucide-react';
import type { HealthStatus } from '../../types/mycelium';

interface Props {
  services: RegisteredService[];
  onStart: (id: string) => void;
  onStop: (id: string) => void;
}

const healthColor: Record<HealthStatus, 'green' | 'yellow' | 'red' | 'gray'> = {
  Healthy: 'green',
  Unhealthy: 'yellow',
  Unreachable: 'red',
  Unknown: 'gray',
};

export function ServicesPanel({ services, onStart, onStop }: Props) {
  return (
    <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
      <h3 className="text-sm font-semibold text-zinc-500 dark:text-zinc-400 mb-3">Registered Services</h3>
      {services.length === 0 && <p className="text-xs text-zinc-500">No services registered</p>}
      <div className="space-y-3">
        {services.map((svc) => (
          <div key={svc.HandlerId} className="p-3 bg-zinc-50 dark:bg-zinc-900 rounded-md border border-zinc-200 dark:border-zinc-700">
            <div className="flex items-center justify-between mb-2">
              <span className="font-medium text-sm">{svc.ServiceName}</span>
              <div className="flex items-center gap-2">
                <Badge label={svc.HealthStatus} color={healthColor[svc.HealthStatus as HealthStatus] || 'gray'} dot />
                <Badge label={svc.IsRunning ? 'Running' : 'Stopped'} color={svc.IsRunning ? 'green' : 'red'} dot />
                {svc.IsExternal && <Badge label="External" color="purple" />}
                {svc.IsRunning ? (
                  <button onClick={() => onStop(svc.HandlerId)} className="p-1 text-red-400 hover:text-red-300" title="Stop">
                    <Square size={14} />
                  </button>
                ) : (
                  <button onClick={() => onStart(svc.HandlerId)} className="p-1 text-emerald-400 hover:text-emerald-300" title="Start">
                    <Play size={14} />
                  </button>
                )}
              </div>
            </div>
            <div className="grid grid-cols-3 gap-2 text-xs text-zinc-500">
              <div>
                <span className="block text-zinc-400">Requests</span>
                <span className="font-mono text-zinc-300">{svc.Stats.RequestsForwarded}</span>
              </div>
              <div>
                <span className="block text-zinc-400">Avg Time</span>
                <span className="font-mono text-zinc-300">{formatMs(svc.Stats.AverageResponseMilliseconds)}</span>
              </div>
              <div>
                <span className="block text-zinc-400">Last Req</span>
                <span className="text-zinc-300">
                  {svc.Stats.LastRequestUtc ? formatRelativeTime(svc.Stats.LastRequestUtc) : '—'}
                </span>
              </div>
            </div>
            {(svc.ProcessId || svc.LastContactTime) && (
              <div className="mt-1 flex gap-4 text-xs text-zinc-500">
                {svc.ProcessId && <span>PID: {svc.ProcessId}</span>}
                {svc.LastContactTime && <span>Last: {formatRelativeTime(svc.LastContactTime)}</span>}
              </div>
            )}
            {svc.FailureCount > 0 && (
              <div className="mt-1 text-xs text-amber-400">Failures: {svc.FailureCount}</div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
