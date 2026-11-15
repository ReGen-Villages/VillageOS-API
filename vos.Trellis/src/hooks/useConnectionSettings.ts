import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { EffectiveProperty } from '@tendril-ui/tendril';
import { thingApi } from '../api/thingApi';
import { toast } from '../components/common/toastStore';
import type { PipelineModel } from '../pipeline/model';
import { connectionSettings, type ConnectionSetting } from '../pipeline/connectionSettings';

export interface ConnectionSettingLine extends ConnectionSetting {
  /** The type the platform declares; null where it declares none, undefined until it has answered. */
  type: string | null | undefined;
}

/** The platform keys an inherited property by the path of archetypes it came through. */
function declaredType(resolved: Record<string, EffectiveProperty>, name: string): string | null {
  const key = name in resolved ? name : Object.keys(resolved).find((path) => path.endsWith(`.${name}`));
  return key === undefined ? null : resolved[key].Type;
}

/** The console's model carries no types, so the platform is asked once for each name not yet asked about. */
export function useConnectionSettings(model: PipelineModel, connectionId: string): ConnectionSettingLine[] {
  const { t } = useTranslation();
  const settings = useMemo(() => connectionSettings(model, connectionId), [model, connectionId]);
  const [types, setTypes] = useState<Record<string, string | null>>({});
  const unasked = settings.map((setting) => setting.name).filter((name) => !(name in types)).join('\n');

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

  return useMemo(() => settings.map((setting) => ({ ...setting, type: types[setting.name] })), [settings, types]);
}
