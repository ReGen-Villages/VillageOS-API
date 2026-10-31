import { useTranslation } from 'react-i18next';

export interface WirePaths {
  fromPath?: string;
  toPath?: string;
  transform?: string;
}

interface Props {
  wireId: string;
  fromPortName: string;
  toPortName: string;
  paths: WirePaths;
  onSetPath: (wireId: string, which: keyof WirePaths, value: string) => void;
  /** Success, fail and each choice of the node the wire leaves, named for display. */
  results: { id: string; name: string }[];
  takenOn: string[];
  onSetTakenOn: (wireId: string, takenOn: string[]) => void;
  onClose: () => void;
}

const FIELD = 'flex-1 px-1 py-0.5 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900';

/** The selected wire: what it is taken on, the field it picks from the upstream value, where it lands it, and a transform on the way. */
export function WireInspector({ wireId, fromPortName, toPortName, paths, onSetPath, results, takenOn, onSetTakenOn, onClose }: Props) {
  const { t } = useTranslation();
  const toggle = (id: string) =>
    onSetTakenOn(wireId, takenOn.includes(id) ? takenOn.filter((taken) => taken !== id) : [...takenOn, id]);
  return (
    <div className="absolute top-2 right-2 w-64 bg-white dark:bg-zinc-800 border border-zinc-300 dark:border-zinc-600 rounded shadow-lg p-2 text-xs z-10">
      <div className="font-semibold mb-1.5 flex items-center justify-between gap-2">
        <span className="truncate">{t('pipeline.wireLabel', { from: fromPortName, to: toPortName })}</span>
        <button onClick={onClose} aria-label={t('pipeline.closeInspector')} className="text-zinc-400 hover:text-zinc-600">×</button>
      </div>
      <div className="text-zinc-400 mb-1">{t('pipeline.takenOnLabel')}</div>
      <div className="flex flex-wrap gap-x-3 gap-y-0.5 mb-2">
        {results.map((result) => (
          <label key={result.id} className="flex items-center gap-1 text-zinc-600 dark:text-zinc-300">
            <input
              type="checkbox"
              checked={takenOn.includes(result.id)}
              disabled={takenOn.length === 1 && takenOn[0] === result.id}
              onChange={() => toggle(result.id)}
            />
            {result.name}
          </label>
        ))}
      </div>
      <div className="text-zinc-400 mb-1">{t('pipeline.mapField')}</div>
      <label className="flex items-center gap-1 mb-1">
        <span className="w-16 truncate text-zinc-600 dark:text-zinc-300">{t('pipeline.fromPath')}</span>
        <input aria-label={t('pipeline.wireFromPath')} placeholder="e.g. user.id" value={paths.fromPath ?? ''} onChange={(e) => onSetPath(wireId, 'fromPath', e.target.value)} className={FIELD} />
      </label>
      <label className="flex items-center gap-1">
        <span className="w-16 truncate text-zinc-600 dark:text-zinc-300">{t('pipeline.toPath')}</span>
        <input aria-label={t('pipeline.wireToPath')} placeholder="e.g. a" value={paths.toPath ?? ''} onChange={(e) => onSetPath(wireId, 'toPath', e.target.value)} className={FIELD} />
      </label>
      <div className="text-zinc-400 mt-2 mb-1">{t('pipeline.transformLabel')}</div>
      <textarea
        aria-label={t('pipeline.wireTransform')}
        placeholder='e.g. {"name": firstName & " " & lastName}'
        value={paths.transform ?? ''}
        onChange={(e) => onSetPath(wireId, 'transform', e.target.value)}
        rows={2}
        className="w-full px-1 py-0.5 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 font-mono"
      />
    </div>
  );
}
