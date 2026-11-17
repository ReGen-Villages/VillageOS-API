import { useTranslation } from 'react-i18next';
import { SlidersHorizontal } from 'lucide-react';

interface RunParametersBarProperties {
  parameterKeys: string[];
  values: Record<string, string>;
  onChange: (key: string, value: string) => void;
}

export function RunParametersBar({ parameterKeys, values, onChange }: RunParametersBarProperties) {
  const { t } = useTranslation();
  if (parameterKeys.length === 0) return null;
  return (
    <div className="flex items-center gap-3 px-2 py-1 border-b border-zinc-200 dark:border-zinc-700 text-xs">
      <span className="text-zinc-500 flex items-center gap-1"><SlidersHorizontal size={12} /> {t('pipeline.parameters')}</span>
      {parameterKeys.map((k) => (
        <label key={k} className="flex items-center gap-1">
          <span className="font-mono text-zinc-600 dark:text-zinc-300">{k}</span>
          <input
            value={values[k] ?? ''}
            onChange={(e) => onChange(k, e.target.value)}
            className="w-28 px-1 py-0.5 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800"
          />
        </label>
      ))}
    </div>
  );
}
