import { apiClient } from './client';
import { FeedbackRefusedError, type FeedbackReport } from '../feedback/feedbackPanel';

const SWITCHED_OFF = 'off';

/** The relay is routed on this host by default, so a report crosses no origin. A deployment that runs
 *  it elsewhere names its address; one that runs none says `off`. Read at call time so a test can set it. */
const relayAddress = () => (import.meta.env.VITE_FEEDBACK_URL as string | undefined) || '/feedback';

interface RelayRefusal {
  code?: string;
  values?: Record<string, unknown>;
}

export const feedbackApi = {
  offered: () => relayAddress() !== SWITCHED_OFF,

  send: async (report: FeedbackReport): Promise<{ reference: number }> => {
    const token = await apiClient.ensureToken();
    const response = await fetch(`${relayAddress().replace(/\/$/, '')}/reports`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
      body: JSON.stringify(report),
    });
    if (!response.ok) {
      const refusal: RelayRefusal = await response.json().catch(() => ({}));
      throw new FeedbackRefusedError(refusal.code ?? '', refusal.values ?? {});
    }
    return response.json();
  },
};
