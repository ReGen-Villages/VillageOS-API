import i18n from '../i18n';
import { ROLE, type PipelineModel } from './model';
/** A relate box's choices, each name in the page language: the predicates the orchestrator would accept, and what any
 *  Thing a box stands for reads as, with the state and service it leads to or the orchestrator's reason to refuse it. */
export interface RelateChoices {
  predicates: { id: string; name: string }[];
  readingOf: (thingId: string) => { name: string; state?: string; service?: string; refusal?: string };
}

/** What the inspector offers a relate box. */
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

/** The orchestrator's reason for refusing a relate box standing for this Thing, in the page language; none when it
 *  would accept it. */
export function relateRefusalReason(model: PipelineModel, standsForId: string, language: string): string | undefined {
  const reading = model.relatingOf(standsForId);
  if (reading.accepted) return undefined;
  const notifiesId = model.boundTo(ROLE.NotifiedConnection);
  const words = {
    name: model.labelOf(standsForId, language),
    notifies: notifiesId ? model.labelOf(notifiesId, language) : '',
    connection: reading.connectionId ? model.labelOf(reading.connectionId, language) : '',
    lng: language,
  };
  return i18n.t(`pipeline.relate.${reading.refusal}`, words);
}

/** What a relate box says on the canvas: the predicate it relates along and the state it then waits for, the
 *  orchestrator's reason where it would be refused, or that it stands for nothing yet. */
export function relatingLine(model: PipelineModel, standsForId: string | undefined, language: string): string {
  if (!standsForId) return i18n.t('pipeline.relatesAlongNothing', { lng: language });
  const reading = model.relatingOf(standsForId);
  if (!reading.accepted) return relateRefusalReason(model, standsForId, language)!;
  return i18n.t('pipeline.relatingAlong', {
    predicate: model.labelOf(standsForId, language), state: model.labelOf(reading.stateId, language), lng: language,
  });
}
