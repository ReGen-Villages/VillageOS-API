import i18n from '../i18n';
import { ROLE, type PipelineModel } from './model';

export interface RelateChoices {
  predicates: { id: string; name: string }[];
  readingOf: (thingId: string) => { name: string; state?: string; service?: string; refusal?: string };
}

export function relateChoicesOf(model: PipelineModel, language: string): RelateChoices {
  return {
    predicates: model.relatablePredicates()
      .map((predicate) => ({ id: predicate.Id, name: model.labelOf(predicate.Id, language) }))
      .sort((a, b) => a.name.localeCompare(b.name)),
    readingOf: (thingId) => {
      const name = model.labelOf(thingId, language);
      const reading = model.relatingOf(thingId);
      return reading.accepted
        ? { name, state: model.labelOf(reading.stateId, language), service: model.labelOf(reading.serviceId, language) }
        : { name, refusal: relateRefusalReason(model, thingId, language) };
    },
  };
}

export function relateRefusalReason(model: PipelineModel, standsForId: string, language: string): string | undefined {
  const reading = model.relatingOf(standsForId);
  if (reading.accepted) return undefined;
  const name = model.labelOf(standsForId, language);
  const notifiesId = model.boundTo(ROLE.NotifiedConnection);
  if (!notifiesId) return i18n.t('pipeline.relate.modelNamesNoNotifiedConnection', { name, lng: language });
  const words = {
    name,
    notifies: model.labelOf(notifiesId, language),
    connection: reading.connectionId ? model.labelOf(reading.connectionId, language) : '',
    lng: language,
  };
  return i18n.t(`pipeline.relate.${reading.refusal}`, words);
}

export function relatingLine(model: PipelineModel, standsForId: string | undefined, language: string): string {
  if (!standsForId) return i18n.t('pipeline.relatesAlongNothing', { lng: language });
  const reading = model.relatingOf(standsForId);
  if (!reading.accepted) return relateRefusalReason(model, standsForId, language)!;
  return i18n.t('pipeline.relatingAlong', {
    predicate: model.labelOf(standsForId, language), state: model.labelOf(reading.stateId, language), lng: language,
  });
}
