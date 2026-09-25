import { apiClient } from './client';
import { unwrapProperties } from '../utils/propertyMapper';
import type { TemporalSnapshot, VosThing } from '../types/vos';

/** The project model a promotion carried the group into. Promoting the same group twice answers with
 *  the same model, because the server derives its identifier rather than generating one. */
export interface PromotionResult {
  modelId: string;
  modelName: string;
}

/** Counts returned by POST /api/model/fragment. */
export interface FragmentResult {
  thingsCreated: number;
  thingsUpdated: number;
  relationshipsCreated: number;
  things: VosThing[];
}

export const modelApi = {
  get: () => apiClient.getText('/api/model'),

  getAtTime: async (timestamp: string, signal?: AbortSignal) => {
    const data = await apiClient.get<TemporalSnapshot>(
      `/api/model?timestamp=${encodeURIComponent(timestamp)}`,
      signal,
    );
    return {
      ...data,
      Things: data.Things.map((t) => ({ ...t, Properties: unwrapProperties(t.Properties) })),
      Relationships: data.Relationships.map((r) => ({ ...r, Properties: unwrapProperties(r.Properties) })),
    } as TemporalSnapshot;
  },

  set: (modelJson: string) => {
    const parsed = JSON.parse(modelJson);
    return apiClient.action('replace the whole model', () => apiClient.post<unknown>('/api/model', parsed));
  },

  // Upsert a fragment ({ Name, Things, Relationships }) into the live model. Idempotent: re-applying
  // the same fragment neither duplicates nor errors; created Things/relationships animate over SSE. Contrast
  // `set`, which replaces the whole model.
  applyFragment: (fragmentJson: string) => {
    const parsed = JSON.parse(fragmentJson);
    return apiClient.action(
      `apply fragment "${parsed.Name ?? 'unnamed'}"`,
      () => apiClient.post<FragmentResult>('/api/model/fragment', parsed),
    );
  },

  // Carry a group out of this model into a project model built for it from a template. The
  // token names the SOURCE, the opposite way round from a fragment: the receiving model does not
  // exist when the call begins. `followedPredicateNames` says what belongs with the root — this
  // model's own vocabulary, so the caller names it rather than the platform assuming it.
  promote: (
    rootThingId: string,
    followedPredicateNames: readonly string[],
    template: string,
    projectName: string,
  ) =>
    apiClient.action(`promote Thing ${rootThingId} into project "${projectName}"`, () =>
      apiClient.post<PromotionResult>('/api/model/promote', {
        RootThingId: rootThingId,
        FollowedPredicateNames: followedPredicateNames,
        Template: template,
        ProjectName: projectName,
      }),
    ),

  clear: () => apiClient.action('clear the model', () => apiClient.del<{ message: string }>('/api/model')),
};
