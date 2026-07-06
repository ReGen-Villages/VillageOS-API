import type { RegisteredService, EndpointServiceInfo, HealthStatus } from '../../types/mycelium';
import { Badge } from '../common/Badge';
import { formatMs, formatRelativeTime } from '../../utils/formatters';
import { Play, Square, Workflow, Globe, Trash2 } from 'lucide-react';

interface Props {
  services: RegisteredService[];
  endpoints: EndpointServiceInfo[];
  onStart: (id: string) => void;
  onStop: (id: string) => void;
  /** Retract a connection from the model (only offered for rows with a model Thing id). */
  onDelete?: (thingId: string, name: string) => void;
}

const healthColor: Record<HealthStatus, 'green' | 'yellow' | 'red' | 'gray'> = {
  Healthy: 'green',
  Unhealthy: 'yellow',
  Unreachable: 'red',
  Unknown: 'gray',
};

// A connection of either trigger kind, normalized to one row shape so the panel
// renders them uniformly — graph (predicate) and http (endpoint) are the same
// kind of thing, distinguished only by how requests reach them.
interface ServiceRow {
  key: string;
  name: string;
  trigger: 'graph' | 'http';
  routeLabel: string;
  requests: number;
  avgMs: number;
  lastReqUtc?: string;
  errors?: number;
  health?: string;
  running?: boolean;
  isExternal?: boolean;
  processId?: number;
  lastContactTime?: string;
  failureCount?: number;
  controlId?: string;
  deleteThingId?: string;
}

function fromService(svc: RegisteredService): ServiceRow {
  return {
    key: `svc:${svc.HandlerId}`,
    name: svc.ServiceName,
    trigger: 'graph',
    routeLabel: 'predicate',
    requests: svc.Stats.RequestsForwarded,
    avgMs: svc.Stats.AverageResponseMilliseconds,
    lastReqUtc: svc.Stats.LastRequestUtc,
    health: svc.HealthStatus,
    running: svc.IsRunning,
    isExternal: svc.IsExternal,
    processId: svc.ProcessId,
    lastContactTime: svc.LastContactTime,
    failureCount: svc.FailureCount,
    controlId: svc.HandlerId,
  };
}

function fromEndpoint(ep: EndpointServiceInfo): ServiceRow {
  return {
    key: `ep:${ep.Subdomain}`,
    name: ep.Name,
    trigger: 'http',
    routeLabel: `/api/endpoints/${ep.Subdomain}`,
    requests: ep.Stats.RequestCount,
    avgMs: ep.Stats.AverageResponseMs,
    lastReqUtc: ep.Stats.LastRequestUtc,
    errors: ep.Stats.ErrorCount,
    deleteThingId: ep.ObjectId,
  };
}

// Base cells: Requests, Avg Time, Last Req. Errors (endpoints), Last Contact and
// PID (graph services) are added conditionally. Literal class strings so Tailwind
// keeps them in the build.
function statGridCols(row: ServiceRow): string {
  const cols =
    3 +
    (row.errors !== undefined ? 1 : 0) +
    (row.lastContactTime ? 1 : 0) +
    (row.processId ? 1 : 0);
  switch (cols) {
    case 6: return 'grid-cols-6';
    case 5: return 'grid-cols-5';
    case 4: return 'grid-cols-4';
    default: return 'grid-cols-3';
  }
}

export function ServicesPanel({ services, endpoints, onStart, onStop, onDelete }: Props) {
  const rows = [...services.map(fromService), ...endpoints.map(fromEndpoint)];

  return (
    <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
      <h3 className="text-sm font-semibold text-zinc-500 dark:text-zinc-400 mb-3">Services</h3>
      {rows.length === 0 && <p className="text-xs text-zinc-500">No services registered</p>}
      <div className="space-y-3">
        {rows.map((row) => (
          <div key={row.key} className="p-3 bg-zinc-50 dark:bg-zinc-900 rounded-md border border-zinc-200 dark:border-zinc-700">
            <div className="flex items-center justify-between mb-2">
              <div className="flex items-center gap-2 min-w-0">
                {row.trigger === 'http'
                  ? <Globe size={14} className="text-teal-400 flex-shrink-0" />
                  : <Workflow size={14} className="text-blue-400 flex-shrink-0" />}
                <span className="font-medium text-sm truncate">{row.name}</span>
                <span className="text-xs font-mono text-zinc-500 truncate">{row.routeLabel}</span>
              </div>
              <div className="flex items-center gap-2 flex-shrink-0">
                {row.health && (
                  <Badge label={row.health} color={healthColor[row.health as HealthStatus] || 'gray'} dot />
                )}
                {row.running !== undefined && (
                  <Badge label={row.running ? 'Running' : 'Stopped'} color={row.running ? 'green' : 'red'} dot />
                )}
                {row.isExternal && <Badge label="External" color="purple" />}
                {row.controlId && (row.running
                  ? <button onClick={() => onStop(row.controlId!)} className="p-1 text-red-400 hover:text-red-300" title="Stop"><Square size={14} /></button>
                  : <button onClick={() => onStart(row.controlId!)} className="p-1 text-emerald-400 hover:text-emerald-300" title="Start"><Play size={14} /></button>
                )}
                {row.deleteThingId && onDelete && (
                  <button onClick={() => onDelete(row.deleteThingId!, row.name)} className="p-1 text-zinc-400 hover:text-red-400" title="Delete (retract from model)"><Trash2 size={14} /></button>
                )}
              </div>
            </div>
            <div className={`grid ${statGridCols(row)} gap-2 text-xs text-zinc-500`}>
              <div>
                <span className="block text-zinc-400">Requests</span>
                <span className="font-mono text-zinc-300">{row.requests}</span>
              </div>
              <div>
                <span className="block text-zinc-400">Avg Time</span>
                <span className="font-mono text-zinc-300">{formatMs(row.avgMs)}</span>
              </div>
              {row.errors !== undefined && (
                <div>
                  <span className="block text-zinc-400">Errors</span>
                  <span className={`font-mono ${row.errors > 0 ? 'text-red-400' : 'text-zinc-300'}`}>{row.errors}</span>
                </div>
              )}
              <div>
                <span className="block text-zinc-400">Last Req</span>
                <span className="text-zinc-300">{row.lastReqUtc ? formatRelativeTime(row.lastReqUtc) : '—'}</span>
              </div>
              {row.lastContactTime && (
                <div>
                  <span className="block text-zinc-400">Last Contact</span>
                  <span className="text-zinc-300">{formatRelativeTime(row.lastContactTime)}</span>
                </div>
              )}
              {row.processId && (
                <div>
                  <span className="block text-zinc-400">PID</span>
                  <span className="font-mono text-zinc-300">{row.processId}</span>
                </div>
              )}
            </div>
            {row.failureCount !== undefined && row.failureCount > 0 && (
              <div className="mt-1 text-xs text-amber-400">Failures: {row.failureCount}</div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
