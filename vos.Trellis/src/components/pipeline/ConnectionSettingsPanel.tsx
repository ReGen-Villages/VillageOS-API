import { useTranslation } from 'react-i18next';
import type { VosThing } from '@tendril-ui/tendril';
import { thingApi } from '../../api/thingApi';
import { useConnectionSettings, type ConnectionSettingLine } from '../../hooks/useConnectionSettings';
import type { PipelineModel } from '../../pipeline/model';
import { EditablePropertyList } from '../panels/EditablePropertyList';
import { toast } from '../common/toastStore';

interface Props {
  model: PipelineModel;
  connection: VosThing;
  mayChange: boolean;
  onClose: () => void;
}

export function ConnectionSettingsPanel({ model, connection, mayChange, onClose }: Props) {
  const { t } = useTranslation();
  const connectionId = connection.Id;
  const lines = useConnectionSettings(model, connectionId);
  const title = t('pipeline.connectionSettings.title', { connection: connection.Name });

  const putBack = async (name: string, archetype: string) => {
    try {
      await thingApi.deleteProperty(connectionId, name);
      toast.success(t('pipeline.connectionSettings.putBackDone', { name, archetype }));
    } catch (error) {
      toast.error(error instanceof Error ? error.message : t('pipeline.connectionSettings.putBackFailed', { name }));
    }
  };

  const whereFrom = (line: ConnectionSettingLine) =>
    line.setOnTheConnection ? t('pipeline.connectionSettings.setHere')
      : line.inherited?.from ? t('pipeline.connectionSettings.inheritedFrom', { archetype: line.inherited.from })
      : t('pipeline.connectionSettings.inherited');

  return (
    <section
      aria-label={title}
      className="absolute top-2 left-2 w-72 max-h-[calc(100%-1rem)] overflow-y-auto bg-white dark:bg-zinc-800 border border-zinc-300 dark:border-zinc-600 rounded shadow-lg p-2 text-xs z-10"
    >
      <div className="font-semibold mb-1.5 flex items-center justify-between gap-2">
        <span className="truncate">{title}</span>
        <button onClick={onClose} aria-label={t('pipeline.closeInspector')} className="text-zinc-400 hover:text-zinc-600">×</button>
      </div>
      {!mayChange && <p className="mb-1.5 text-zinc-500">{t('pipeline.connectionSettings.readOnly')}</p>}
      {lines.length === 0 && <p className="text-zinc-500">{t('pipeline.connectionSettings.none')}</p>}
      <ul>
        {lines.map((line) => (
          <li key={line.name} aria-label={line.name} className="py-1 border-b border-zinc-200 dark:border-zinc-700 last:border-b-0">
            <EditablePropertyList
              properties={[{ name: line.name, value: line.value, type: line.type ?? '' }]}
              entityId={connectionId}
              entityType="thing"
              editMode={mayChange && !!line.type}
              showAddRow={false}
            />
            <div className="flex items-center justify-between gap-2 text-[10px] text-zinc-500">
              <span>{whereFrom(line)}</span>
              {mayChange && line.setOnTheConnection && line.inherited && (
                <button
                  type="button"
                  onClick={() => putBack(line.name, line.inherited!.from ?? '')}
                  aria-label={t('pipeline.connectionSettings.putBackTitle', { archetype: line.inherited.from ?? '' })}
                  className="text-blue-600 dark:text-blue-400 hover:underline"
                >
                  {t('pipeline.connectionSettings.putBack')}
                </button>
              )}
            </div>
            {line.type === null && <p className="text-[10px] text-zinc-500">{t('pipeline.connectionSettings.typeUnknown')}</p>}
          </li>
        ))}
      </ul>
    </section>
  );
}
