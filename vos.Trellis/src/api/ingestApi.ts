import { apiClient } from './client';

export interface IngestResult {
  success: boolean;
  thingsCreated: number;
  thingsUpdated: number;
  relationshipsCreated: number;
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
    if (!base) throw new Error('Ingestion service URL is not configured (set VITE_INGEST_URL).');

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
