import type { EndpointServiceInfo } from '../../types/mycelium';
import { formatMs, formatRelativeTime } from '../../utils/formatters';
import { Globe } from 'lucide-react';

interface Props {
  endpoints: EndpointServiceInfo[];
}

export function EndpointServicesPanel({ endpoints }: Props) {
  return (
    <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
      <h3 className="text-sm font-semibold text-zinc-500 dark:text-zinc-400 mb-3">Endpoint Services</h3>
      {endpoints.length === 0 && <p className="text-xs text-zinc-500">No endpoint services registered</p>}
      <div className="space-y-3">
        {endpoints.map((ep) => (
          <div key={ep.Subdomain} className="p-3 bg-zinc-50 dark:bg-zinc-900 rounded-md border border-zinc-200 dark:border-zinc-700">
            <div className="flex items-center justify-between mb-2">
              <div className="flex items-center gap-2">
                <Globe size={14} className="text-teal-400" />
                <span className="font-medium text-sm">{ep.Name}</span>
              </div>
              <span className="text-xs font-mono text-zinc-500">/api/endpoints/{ep.Subdomain}</span>
            </div>
            <div className="grid grid-cols-4 gap-2 text-xs text-zinc-500">
              <div>
                <span className="block text-zinc-400">Requests</span>
                <span className="font-mono text-zinc-300">{ep.Stats.RequestCount}</span>
              </div>
              <div>
                <span className="block text-zinc-400">Avg Time</span>
                <span className="font-mono text-zinc-300">{formatMs(ep.Stats.AverageResponseMs)}</span>
              </div>
              <div>
                <span className="block text-zinc-400">Errors</span>
                <span className={`font-mono ${ep.Stats.ErrorCount > 0 ? 'text-red-400' : 'text-zinc-300'}`}>
                  {ep.Stats.ErrorCount}
                </span>
              </div>
              <div>
                <span className="block text-zinc-400">Last Req</span>
                <span className="text-zinc-300">
                  {ep.Stats.LastRequestUtc ? formatRelativeTime(ep.Stats.LastRequestUtc) : '—'}
                </span>
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
