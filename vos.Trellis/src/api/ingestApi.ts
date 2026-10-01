import { apiClient } from './client';
import i18n from '../i18n';

// A count is null when the ingest succeeded and the service could not read it off what the ingest tool
// printed. It is not nought: the model was written.
export interface IngestResult {
  success: boolean;
  thingsCreated: number | null;
  thingsUpdated: number | null;
  relationshipsCreated: number | null;
  error?: string | null;
}

export type IngestMode = 'merge' | 'new-model';

// Read at call time (not module load) so it is configurable and testable.
const ingestUrl = () => (import.meta.env.VITE_INGEST_URL as string | undefined) || '';

export const ingestApi = {
  /** Whether an ingestion service URL is configured — drives whether the upload UI is offered. */
  configured: () => ingestUrl().length > 0,

  upload: async (file: File, name: string, mode: IngestMode): Promise<IngestResult> => {
    const base = ingestUrl();
    if (!base) throw new Error(i18n.t('ifcUpload.serviceNotConfigured', { setting: 'VITE_INGEST_URL' }));

    const token = await apiClient.ensureToken();
    const form = new FormData();
    form.append('file', file);
    form.append('name', name);
    form.append('mode', mode);

    const response = await fetch(`${base.replace(/\/$/, '')}/ingest`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}` },
      body: form,
    });
    return response.json();
  },
};
