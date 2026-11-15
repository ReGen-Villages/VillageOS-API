import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { thingApi } from '../api/thingApi';
import { callSettingsApi, type CallSetting, type CallSettings } from '../api/callSettingsApi';
import { toast } from '../components/common/toastStore';
import { ROLE, type PipelineModel } from '../pipeline/model';
import { COMPLETES_WHEN, declaredType } from '../pipeline/connectionSettings';

export interface ConnectionSettingLine extends CallSetting {
  name: string;
  /** The type the platform declares; null where it declares none, undefined until it has answered. */
  type: string | null | undefined;
}

export type ConnectionSettingsReading =
  | { kind: 'reading' }
  | { kind: 'failed'; reason: string }
  | { kind: 'read'; completesWhen: CallSetting | undefined; values: ConnectionSettingLine[] };

/** A change to the connection's or its service's own values or choices asks the platform again; a change to an archetype
 *  waits for the panel to be opened again. */
function askAgainWhenChanged(model: PipelineModel, connectionId: string): string {
  const service = model.outgoing(connectionId, 'has').find((held) => model.isOf(held.Id, ROLE.Service));
  const ownOf = (thingId: string) => [model.stated(thingId), model.reachedAlong(thingId, ROLE.CompletesWhen).map((choice) => choice.Id)];
  return JSON.stringify([ownOf(connectionId), service ? ownOf(service.Id) : null]);
}

/** The console's model carries no types, so the platform is asked once for each name not yet asked about. */
export function useConnectionSettings(model: PipelineModel, connectionId: string): ConnectionSettingsReading {
  const { t } = useTranslation();
  const askedFor = askAgainWhenChanged(model, connectionId);
  const [answer, setAnswer] = useState<{ kind: 'answered'; settings: CallSettings } | { kind: 'failed'; reason: string }>();
  const [types, setTypes] = useState<Record<string, string | null>>({});

  useEffect(() => {
    const controller = new AbortController();
    callSettingsApi.get(connectionId, controller.signal).then(
      (settings) => setAnswer({ kind: 'answered', settings }),
      (error) => {
        if (!controller.signal.aborted) setAnswer({ kind: 'failed', reason: error instanceof Error ? error.message : String(error) });
      },
    );
    return () => controller.abort();
  }, [connectionId, askedFor]);

  const settings = answer?.kind === 'answered' ? answer.settings : undefined;
  const unasked = Object.keys(settings ?? {}).filter((name) => name !== COMPLETES_WHEN && !(name in types)).join('\n');

  useEffect(() => {
    if (!unasked) return;
    let cancelled = false;
    const asking = unasked.split('\n');
    thingApi.getEffectiveProperties(connectionId).then(
      (resolved) => {
        if (cancelled) return;
        setTypes((previous) => ({ ...previous, ...Object.fromEntries(asking.map((name) => [name, declaredType(resolved, name)])) }));
      },
      () => { if (!cancelled) toast.error(t('pipeline.connectionSettings.typesFailed')); },
    );
    return () => { cancelled = true; };
  }, [connectionId, unasked, t]);

  return useMemo(() => {
    if (!answer) return { kind: 'reading' };
    if (answer.kind === 'failed') return answer;
    const { [COMPLETES_WHEN]: completesWhen, ...values } = answer.settings;
    return {
      kind: 'read',
      completesWhen,
      values: Object.entries(values).map(([name, setting]) => ({ name, ...setting, type: types[name] })),
    };
  }, [answer, types]);
}
