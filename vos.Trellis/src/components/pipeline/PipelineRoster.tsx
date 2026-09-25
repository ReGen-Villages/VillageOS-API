import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { Plus } from 'lucide-react';
import { SidePanel } from '../common/SidePanel';

export interface PipelineListing {
  id: string;
  name: string;
  nodeCount: number;
}

interface Props {
  pipelines: PipelineListing[];
  openedPipelineId: string | null;
  onOpen: (pipelineId: string) => void;
  onCreate: (name: string) => void;
}

const ROSTER_BOUNDS = { initial: 208, min: 160, max: 480 };

/** Every pipeline the model holds, and the field that names a new one. */
export function PipelineRoster({ pipelines, openedPipelineId, onOpen, onCreate }: Props) {
  const { t } = useTranslation();
  const [newName, setNewName] = useState('');

  const create = () => {
    const name = newName.trim();
    if (name === '') return;
    setNewName('');
    onCreate(name);
  };

  return (
    <SidePanel
      side="left"
      bounds={ROSTER_BOUNDS}
      name={t('pipeline.roster.title')}
      labels={{ expand: t('pipeline.roster.expand'), collapse: t('pipeline.roster.collapse'), resize: t('pipeline.roster.resize') }}
    >
      <ul className="flex-1 overflow-y-auto p-2 space-y-0.5">
        {pipelines.length === 0 && <li className="px-2.5 text-xs text-zinc-400">{t('pipeline.roster.none')}</li>}
        {pipelines.map((pipeline) => (
          <li key={pipeline.id}>
            <button
              type="button"
              onClick={() => onOpen(pipeline.id)}
              aria-current={pipeline.id === openedPipelineId ? 'true' : undefined}
              className={clsx(
                'w-full text-left rounded-md px-2.5 py-2 text-sm',
                pipeline.id === openedPipelineId
                  ? 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300'
                  : 'text-zinc-700 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-800',
              )}
            >
              <span className="block break-words">{pipeline.name}</span>
              <span className="block text-xs text-zinc-500 dark:text-zinc-400">{t('pipeline.roster.nodes', { count: pipeline.nodeCount })}</span>
            </button>
          </li>
        ))}
      </ul>
      <div className="p-2 border-t border-zinc-200 dark:border-zinc-700 flex gap-1.5">
        <input
          value={newName}
          onChange={(event) => setNewName(event.target.value)}
          onKeyDown={(event) => { if (event.key === 'Enter') create(); }}
          aria-label={t('pipeline.roster.newPipeline')}
          placeholder={t('pipeline.roster.newPipeline')}
          className="flex-1 min-w-0 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 px-2 py-1 text-sm text-zinc-900 dark:text-zinc-100"
        />
        <button
          type="button"
          onClick={create}
          aria-label={t('pipeline.roster.addPipeline')}
          className="rounded bg-zinc-100 dark:bg-zinc-800 px-2 text-zinc-700 dark:text-zinc-200 hover:bg-zinc-200 dark:hover:bg-zinc-700"
        >
          <Plus size={15} />
        </button>
      </div>
    </SidePanel>
  );
}
