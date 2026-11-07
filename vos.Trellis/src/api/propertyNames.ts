/**
 * How a model names a property in words a reader knows: a Thing of the archetype the property-name role
 * binds, saying which property it names and its words by language, and reaching the archetype whose members
 * hold that property through the predicate. The platform's seed validation states the same predicate name,
 * and nothing else keeps the two in step.
 */
import { LAND_INTAKE_ROLE } from './landIntakeRoles';

export const PROPERTY_NAME_ROLE = LAND_INTAKE_ROLE.PropertyName;
export const NAMED_PROPERTY = 'namedProperty';
export const PROPERTY_NAME_WORDING = 'wording';
export const NAMES_PROPERTY_OF_PREDICATE = 'namesPropertyOf';
