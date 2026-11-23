import { apiClient } from './client';

export const CALL_SETTING_SOURCES = ['connection', 'service', 'serviceArchetype', 'builtIn'] as const;

type CallSettingSource = (typeof CALL_SETTING_SOURCES)[number];

export interface CallSetting {
  value: unknown;
  source: CallSettingSource;
  from: { id: string; name: string } | null;
}

export type CallSettings = Record<string, CallSetting>;

/** The platform works out where each setting comes from, so the console never holds a second copy of that order. */
export const callSettingsApi = {
  get: (connectionId: string, signal?: AbortSignal) =>
    apiClient.get<CallSettings>(`/api/connections/${connectionId}/call-settings`, signal),
};
