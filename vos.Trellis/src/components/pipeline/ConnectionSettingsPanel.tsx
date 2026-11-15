import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import type { VosThing } from '@tendril-ui/tendril';
import { thingApi } from '../../api/thingApi';
import { modelApi } from '../../api/modelApi';
import type { CallSetting } from '../../api/callSettingsApi';
import { useConnectionSettings } from '../../hooks/useConnectionSettings';
import type { PipelineModel } from '../../pipeline/model';
import { COMPLETES_WHEN, completionChoiceFragment, completionChoices } from '../../pipeline/connectionSettings';
import { EditablePropertyList } from '../panels/EditablePropertyList';
import { toast } from '../common/toastStore';

interface Props {
  model: PipelineModel;
  connection: VosThing;
  mayChange: boolean;
  onClose: () => void;
}

function WhereFrom({ setting }: { setting: CallSetting }) {
  const { t } = useTranslation();
  return <span>{t(`pipeline.connectionSettings.source.${setting.source}`, { name: setting.from?.name ?? '' })}</span>;
}

export function ConnectionSettingsPanel({ model, connection, mayChange, onClose }: Props) {
  const { t, i18n } = useTranslation();
  const connectionId = connection.Id;
  const reading = useConnectionSettings(model, connectionId);
  const choices = useMemo(() => completionChoices(model, i18n.language), [model, i18n.language]);
  const title = t('pipeline.connectionSettings.title', { connection: connection.Name });

  const removeOwnValue = async (name: string) => {
    try {
      await thingApi.deleteProperty(connectionId, name);
      toast.success(t('pipeline.connectionSettings.removeDone', { name }));
    } catch (error) {
      toast.error(error instanceof Error ? error.message : t('pipeline.connectionSettings.removeFailed', { name }));
    }
  };

  const choose = async (memberId: string) => {
    const fragment = completionChoiceFragment(model, connectionId, memberId);
    if (!fragment) {
      toast.error(t('pipeline.connectionSettings.choiceUnbound'));
      return;
    }
    try {
      await modelApi.applyFragment(JSON.stringify(fragment));
    } catch (error) {
      toast.error(t('pipeline.connectionSettings.choiceRefused', { reason: error instanceof Error ? error.message : String(error) }));
    }
  };

  const completesWhen = reading.kind === 'read' ? reading.completesWhen : undefined;
  const chosenLabel = (memberId: unknown) => choices.find((choice) => choice.id === memberId)?.label ?? String(memberId ?? '');

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
      {reading.kind === 'reading' && <p className="text-zinc-500">{t('pipeline.connectionSettings.reading')}</p>}
      {reading.kind === 'failed' && (
        <p role="alert" className="text-red-600 dark:text-red-400">
          {reading.status === 404 ? t('pipeline.connectionSettings.readNoConnection')
            : reading.status === 403 ? t('pipeline.connectionSettings.readMayNotRead')
            : t('pipeline.connectionSettings.readFailed', { reason: reading.reason })}
        </p>
      )}
      {reading.kind === 'read' && (
        <ul>
          {completesWhen && (
            <li aria-label={COMPLETES_WHEN} className="py-1 border-b border-zinc-200 dark:border-zinc-700">
              <span className="block text-zinc-500">{t('pipeline.connectionSettings.completesWhen')}</span>
              {mayChange ? (
                <select
                  aria-label={t('pipeline.connectionSettings.completesWhen')}
                  value={String(completesWhen.value ?? '')}
                  onChange={(event) => choose(event.target.value)}
                  className="w-full rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 px-1 py-0.5"
                >
                  {!choices.some((choice) => choice.id === completesWhen.value) && (
                    <option value={String(completesWhen.value ?? '')}>{chosenLabel(completesWhen.value)}</option>
                  )}
                  {choices.map((choice) => <option key={choice.id} value={choice.id}>{choice.label}</option>)}
                </select>
              ) : (
                <span className="block font-mono">{chosenLabel(completesWhen.value)}</span>
              )}
              <div className="text-[10px] text-zinc-500"><WhereFrom setting={completesWhen} /></div>
            </li>
          )}
          {reading.values.map((line) => (
            <li key={line.name} aria-label={line.name} className="py-1 border-b border-zinc-200 dark:border-zinc-700 last:border-b-0">
              <EditablePropertyList
                properties={[{ name: line.name, value: line.value, type: line.type ?? '' }]}
                entityId={connectionId}
                entityType="thing"
                editMode={mayChange && !!line.type}
                showAddRow={false}
              />
              <div className="flex items-center justify-between gap-2 text-[10px] text-zinc-500">
                <WhereFrom setting={line} />
                {mayChange && line.source === 'connection' && (
                  <button
                    type="button"
                    onClick={() => removeOwnValue(line.name)}
                    aria-label={t('pipeline.connectionSettings.removeTitle', { name: line.name })}
                    title={t('pipeline.connectionSettings.removeTitle', { name: line.name })}
                    className="text-blue-600 dark:text-blue-400 hover:underline"
                  >
                    {t('pipeline.connectionSettings.remove')}
                  </button>
                )}
              </div>
              {line.type === null && <p className="text-[10px] text-zinc-500">{t('pipeline.connectionSettings.typeUnknown')}</p>}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
