import type { Binding, DashboardSpecification } from '../types/dashboard';
import type { PropertyCandidate } from '../api/modelDeclaration';
import { unboundSlots } from './designSpec';
import { BINDING_KINDS } from './widgetSchema';
import { rowKindOf, rowsBindingOf } from './rowKind';

/**
 * What the Design page refuses a page for, and what it warns about.
 *
 * Nothing checks a specification written in a browser the way the platform's seed validation checks
 * a seeded one, so the rules it holds a seeded page to are carried here and judged before the page is
 * written. A refusal stops the write; a warning does not. Each finding says which widget it is about.
 */

export const DESIGN_FINDING_CODES = [
  'namesAnUnknownKind',
  'namesAnUnknownPredicate',
  'namesAnUnknownState',
  'namesAnUnknownProperty',
  'sortKeyNoColumnCarries',
  'searchKeyNoRowCarries',
  'tableWithoutARowCap',
  'pageWithoutAnIcon',
  'iconAlreadyTaken',
  'nameCarriesDashboard',
  'comparedPropertyIsText',
  'columnOnAnUnboundedValue',
  'stateBindingWithoutScope',
  'widgetBoundToNothing',
] as const;

export type DesignFindingCode = (typeof DESIGN_FINDING_CODES)[number];

export interface DesignFinding {
  severity: 'refusal' | 'warning';
  code: DesignFindingCode;
  section?: number;
  widget?: number;
  /** The word the finding is about, where there is one. */
  named?: string;
}

export interface DesignCheckContext {
  isKind: (name: string) => boolean;
  isPredicate: (name: string) => boolean;
  /** The states a kind derives, or undefined where they are not known yet — nothing is refused on a
   *  state until the kind's states have been read. */
  statesOf: (kind: string) => string[] | undefined;
  /** What a kind declares, or undefined where the kind is not known. */
  propertiesOf: (kind: string) => PropertyCandidate[] | undefined;
  /** The icons the other pages ask for. */
  iconsTaken: Set<string>;
  compareKind?: string;
}

/** A text value longer than this cannot be given a column without it deciding the page's width. */
const UNBOUNDED_TEXT = 80;

/** The sidebar drops this word from a page's name, so a name made of it alone is a blank entry. */
const WORD_THE_NAVIGATION_DROPS = 'Dashboard';

type Where = { section?: number; widget?: number };

function kindOf(binding: Binding, context: DesignCheckContext, rowKind: string | undefined): string | undefined {
  if ('archetype' in binding && binding.archetype) return binding.archetype;
  if (binding.kind === 'compareEntities') return context.compareKind;
  if ('thing' in binding && binding.thing && binding.thing !== '$scope') return undefined;
  return rowKind ?? context.compareKind;
}

/** The kind whose states a binding names, given the kind the binding itself is about. A binding that
 *  selects a state's members is about whatever holds the state, and the archetype it names is the
 *  only thing that says what that is: a scope reaches a kind the specification never writes down,
 *  and the kind a page compares is not that kind, so neither stands in for it. */
function kindHoldingTheStates(binding: Binding, kind: string | undefined): string | undefined {
  const selectsMembersOfTheState = binding.kind === 'stateCount' || binding.kind === 'stateList';
  return selectsMembersOfTheState ? binding.archetype : kind;
}

interface HeldBinding {
  binding: Binding;
  rowKind?: string;
  underOptions: boolean;
}

/** Every binding a widget holds, however deep, with whether it lists what a field picks from and the
 *  kind of the row it is worked out for. */
function bindingsOf(node: unknown, rowKind: string | undefined, underOptions: boolean, found: HeldBinding[]): void {
  if (Array.isArray(node)) {
    for (const entry of node) bindingsOf(entry, rowKind, underOptions, found);
    return;
  }
  if (!node || typeof node !== 'object') return;
  const held = node as Record<string, unknown>;
  const isBinding = typeof held.kind === 'string' && (BINDING_KINDS as string[]).includes(held.kind);
  if (isBinding) {
    const binding = held as unknown as Binding;
    found.push({ binding, rowKind, underOptions });
    const rowKindWithin = binding.kind === 'stateList' || binding.kind === 'thingList' ? binding.archetype : binding.kind === 'compareEntities' ? undefined : rowKind;
    for (const [key, value] of Object.entries(held)) {
      if (key === 'computed') bindingsOf(value, rowKindWithin, underOptions, found);
      else bindingsOf(value, rowKind, underOptions, found);
    }
    return;
  }
  for (const [key, value] of Object.entries(held)) bindingsOf(value, rowKind, underOptions || key === 'options', found);
}

function namedStatesOf(binding: Binding): string[] {
  const named: string[] = [];
  if ('state' in binding && binding.state) named.push(binding.state);
  if ('excludeState' in binding && binding.excludeState) named.push(binding.excludeState);
  if ('inState' in binding && binding.inState) named.push(binding.inState);
  if (binding.kind === 'stateOf') named.push(...binding.states);
  if (binding.kind === 'verdict') named.push(...binding.states.map((candidate) => candidate.state));
  return named;
}

function namedPropertiesOf(binding: Binding): string[] {
  switch (binding.kind) {
    case 'aggregate':
      return binding.property ? [binding.property] : [];
    case 'property':
    case 'working':
    case 'origin':
    case 'history':
      return binding.property ? [binding.property] : [];
    case 'timeseries':
      return [binding.happenedAt, ...(binding.property ? [binding.property] : [])];
    default:
      return [];
  }
}

export function checkDesign(specification: DashboardSpecification, name: string, context: DesignCheckContext): DesignFinding[] {
  const findings: DesignFinding[] = [];
  const refuse = (code: DesignFindingCode, where: Where, named?: string) => findings.push({ severity: 'refusal', code, ...where, ...(named ? { named } : {}) });
  const warn = (code: DesignFindingCode, where: Where, named?: string) => findings.push({ severity: 'warning', code, ...where, ...(named ? { named } : {}) });

  if (!specification.icon) refuse('pageWithoutAnIcon', {});
  else if (context.iconsTaken.has(specification.icon)) refuse('iconAlreadyTaken', {}, specification.icon);
  if (name.includes(WORD_THE_NAVIGATION_DROPS) || specification.title.includes(WORD_THE_NAVIGATION_DROPS)) refuse('nameCarriesDashboard', {}, name);
  if (specification.compare && !context.isKind(specification.compare.archetype)) refuse('namesAnUnknownKind', {}, specification.compare.archetype);

  specification.sections.forEach((section, sectionIndex) => {
    section.widgets.forEach((widget, widgetIndex) => {
      const where = { section: sectionIndex, widget: widgetIndex };
      const rows = rowsBindingOf(widget);
      const rowKind = rowKindOf(rows, context.compareKind);
      const rowProperties = rowKind ? context.propertiesOf(rowKind) : undefined;

      if (unboundSlots(widget).length > 0) warn('widgetBoundToNothing', where);

      if (widget.type === 'table') {
        if (widget.visibleRows === undefined) refuse('tableWithoutARowCap', where);
        const carried = new Set([
          ...widget.columns.map((column) => column.key),
          ...(rows && 'computed' in rows ? (rows.computed ?? []).map((column) => column.key) : []),
        ]);
        if (widget.sortKey && !carried.has(widget.sortKey)) refuse('sortKeyNoColumnCarries', where, widget.sortKey);
        if (rowProperties) {
          for (const key of widget.searchKeys ?? [])
            if (!carried.has(key) && !rowProperties.some((property) => property.name === key)) refuse('searchKeyNoRowCarries', where, key);
          for (const column of widget.columns) {
            const property = rowProperties.find((candidate) => candidate.name === column.key);
            if (typeof property?.example === 'string' && property.example.length > UNBOUNDED_TEXT) warn('columnOnAnUnboundedValue', where, column.key);
          }
        }
      }

      if (widget.type === 'leaderboard' && widget.entities?.kind === 'compareEntities' && context.compareKind) {
        const compared = context.propertiesOf(context.compareKind);
        for (const key of widget.entities.properties) {
          const property = compared?.find((candidate) => candidate.name === key);
          if (property && !property.numeric && typeof property.example === 'string') refuse('comparedPropertyIsText', where, key);
        }
      }

      const bindings: HeldBinding[] = [];
      bindingsOf(widget, rowKind, false, bindings);
      for (const { binding, rowKind: kindOfRow, underOptions } of bindings) {
        if ('archetype' in binding && binding.archetype && !context.isKind(binding.archetype)) refuse('namesAnUnknownKind', where, binding.archetype);
        if ('scope' in binding && binding.scope && !context.isPredicate(binding.scope.viaPredicate)) refuse('namesAnUnknownPredicate', where, binding.scope.viaPredicate);
        if ('via' in binding) {
          for (const step of binding.via ?? []) {
            if (step.predicate && !context.isPredicate(step.predicate)) refuse('namesAnUnknownPredicate', where, step.predicate);
            if (step.archetype && !context.isKind(step.archetype)) refuse('namesAnUnknownKind', where, step.archetype);
          }
        }
        const kind = kindOf(binding, context, kindOfRow);
        const holdingStates = kindHoldingTheStates(binding, kind);
        const states = holdingStates ? context.statesOf(holdingStates) : undefined;
        if (states) for (const state of namedStatesOf(binding)) if (!states.includes(state)) refuse('namesAnUnknownState', where, state);
        const declared = kind ? context.propertiesOf(kind) : undefined;
        if (declared) for (const property of namedPropertiesOf(binding)) if (!declared.some((candidate) => candidate.name === property)) warn('namesAnUnknownProperty', where, property);
        if (context.compareKind && !underOptions && (binding.kind === 'stateCount' || binding.kind === 'stateList') && !binding.scope) warn('stateBindingWithoutScope', where);
      }
    });
  });
  return findings;
}
