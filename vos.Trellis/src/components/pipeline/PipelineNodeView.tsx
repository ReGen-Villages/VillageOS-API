import { Handle, Position, type NodeProps } from '@xyflow/react';
import clsx from 'clsx';
import type { PortInfo } from '../../pipeline/model';

export interface PipelineNodeData {
  label: string;
  connectionId: string;
  subdomain: string;
  ports: PortInfo[];
  status?: string;
  [key: string]: unknown;
}

const STATUS_RING: Record<string, string> = {
  running: 'ring-2 ring-blue-400 animate-pulse',
  succeeded: 'ring-2 ring-green-500',
  failed: 'ring-2 ring-red-500',
  skipped: 'ring-2 ring-zinc-400 opacity-60',
};

/** A pipeline DAG node: one Handle per typed port (inputs on the left, outputs on the right). */
export function PipelineNodeView({ data }: NodeProps) {
  const d = data as PipelineNodeData;
  const inputs = d.ports.filter((p) => p.direction === 'in');
  const outputs = d.ports.filter((p) => p.direction === 'out');

  return (
    <div
      className={clsx(
        'rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 shadow-sm min-w-[180px]',
        d.status && STATUS_RING[d.status],
      )}
    >
      <div className="px-3 py-1.5 border-b border-zinc-200 dark:border-zinc-700 text-sm font-semibold text-zinc-900 dark:text-white flex items-center justify-between gap-2">
        <span className="truncate">{d.label}</span>
        <span className="text-[10px] font-mono text-zinc-400">{d.subdomain}</span>
      </div>
      <div className="flex justify-between gap-4 py-2 text-xs">
        <div className="flex flex-col gap-2">
          {inputs.map((p) => (
            <div key={p.portName} className="relative pl-3 text-zinc-600 dark:text-zinc-300">
              <Handle
                type="target"
                position={Position.Left}
                id={p.portName}
                className="!w-2.5 !h-2.5 !bg-blue-500"
              />
              {p.portName}
              {p.required && <span className="text-red-400">*</span>}
            </div>
          ))}
        </div>
        <div className="flex flex-col gap-2 items-end">
          {outputs.map((p) => (
            <div key={p.portName} className="relative pr-3 text-zinc-600 dark:text-zinc-300">
              {p.portName}
              <Handle
                type="source"
                position={Position.Right}
                id={p.portName}
                className="!w-2.5 !h-2.5 !bg-green-500"
              />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
