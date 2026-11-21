import { apiClient } from './client';
import i18n from '../i18n';

// A count is null when the ingest succeeded but the service could not read the count from the tool's
// output; it is not nought, because the model was written.
interface IngestResult {
  success: boolean;
  thingsCreated: number | null;
  thingsUpdated: number | null;
  relationshipsCreated: number | null;
  error?: string | null;
}

type IngestMode = 'merge' | 'new-model';

// Read at call time so a test can set it.
const ingestUrl = () => (import.meta.env.VITE_INGEST_URL as string | undefined) || '';

export const ingestApi = {
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
