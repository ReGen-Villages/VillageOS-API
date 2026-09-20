import { useTranslation } from 'react-i18next';
import { Plus } from 'lucide-react';
import type { Binding, ComputedColumn, PropertyFilter, RelationStep, ScopeReference } from '../../types/dashboard';
import { BINDING_SCHEMAS, FILTER_OPERATORS, STEP_DIRECTIONS, kindsForShape, type BindingShape, type FieldSpecification } from '../../utils/widgetSchema';
import type { EdgeCandidate } from '../../api/modelDeclaration';
import { rowKindOf } from '../../utils/rowKind';
import { edgesByPredicate, kindReachedBy } from './designOffers';
import type { BindingContext } from './bindingContext';
import { ChoiceField, Field, TextField } from './DesignFields';
import { OfferedField } from './OfferedField';
import { FieldControl } from './FieldControls';
import { labelFor, nestedClass, offeredFor, smallButtonClass, withKey } from './fieldSupport';
import { Removable } from './Removable';

/** The kind a Thing reference names: the row inside a column, the compared Thing at the top of a
 *  page, or a Thing by name. */
function thingKind(thing: string | undefined, context: BindingContext): string | undefined {
  if (!thing || thing === '$scope') return context.rowKind ?? context.offers.compareKind;
  return context.offers.kindOfThing(thing);
}

/** The kind a binding's words are offered for: the kind it reduces or lists, the Thing it reads, or
 *  what its path has reached. */
function kindOf(binding: Binding, context: BindingContext): string | undefined {
  switch (binding.kind) {
    case 'stateCount':
    case 'stateList':
    case 'thingList':
    case 'aggregate':
    case 'timeseries':
      return binding.archetype;
    case 'property':
    case 'stateOf':
      return thingKind(binding.thing, context);
    case 'related':
    case 'verdict':
    case 'working':
      return kindReachedBy(thingKind(binding.thing, context), binding.via ?? [], context.offers);
    case 'compareEntities':
      return context.offers.compareKind;
    default:
      return context.rowKind ?? context.offers.compareKind;
  }
}

/**
 * One control wherever the contract takes a binding: the kind chosen from the list the resolver
 * answers — the kinds that fit the slot first — what it costs said beneath, and its fields offered
 * from the model's own words. It nests: a ratio holds two of these, a column worked out per row holds
 * one, and each is this same control.
 */
export function BindingEditor({
  label, value, shape, context, onChange,
}: { label: string; value: Binding | undefined; shape: BindingShape; context: BindingContext; onChange: (value: Binding | undefined) => void }) {
  const { t } = useTranslation();
  const kind = value ? kindOf(value, context) : undefined;
  const write = (key: string, next: unknown) => {
    if (!value) return;
    onChange(withKey(value as unknown as Record<string, unknown>, key, next) as unknown as Binding);
  };

  return (
    <Field title={label}>
      {({ id, describedBy }) => (
        <>
          <select
            id={id}
            aria-describedby={describedBy}
            value={value?.kind ?? ''}
            onChange={(event) => onChange(event.target.value ? ({ kind: event.target.value } as Binding) : undefined)}
            className="mt-1 w-full rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 px-2 py-1 text-sm text-zinc-900 dark:text-zinc-100"
          >
            <option value="">{t('design.binding.none')}</option>
            {kindsForShape(shape).map((offered) => (
              <option key={offered} value={offered}>
                {t(`design.bindingKind.${offered}`)}
              </option>
            ))}
          </select>
          {value && (
            <div className={nestedClass}>
              <p className="text-[11px] text-zinc-500 dark:text-zinc-400">{t(`design.bindingCost.${value.kind}`)}</p>
              {BINDING_SCHEMAS[value.kind].map((field) => (
                <BindingPart key={field.key} field={field} binding={value} kind={kind} context={context} onWrite={write} />
              ))}
            </div>
          )}
        </>
      )}
    </Field>
  );
}

function BindingPart({
  field, binding, kind, context, onWrite,
}: { field: FieldSpecification; binding: Binding; kind?: string; context: BindingContext; onWrite: (key: string, next: unknown) => void }) {
  const { t } = useTranslation();
  const held = (binding as unknown as Record<string, unknown>)[field.key];
  const label = labelFor('bindingField', field.key, t);
  switch (field.kind) {
    case 'scope':
      return <ScopeEditor label={label} value={held as ScopeReference | undefined} context={context} onChange={(next) => onWrite(field.key, next)} />;
    case 'steps':
      return (
        <StepsEditor
          label={label}
          value={(held as RelationStep[] | undefined) ?? []}
          start={thingKind((binding as { thing?: string }).thing, context)}
          context={context}
          onChange={(next) => onWrite(field.key, next.length ? next : undefined)}
        />
      );
    case 'filters':
      return <FiltersEditor label={label} value={(held as PropertyFilter[] | undefined) ?? []} kind={kind} context={context} onChange={(next) => onWrite(field.key, next)} />;
    case 'computed':
      return (
        <ComputedEditor
          label={label}
          value={(held as ComputedColumn[] | undefined) ?? []}
          context={{ ...context, rowKind: rowKindOf(binding, context.offers.compareKind) }}
          onChange={(next) => onWrite(field.key, next)}
        />
      );
    default:
      return <FieldControl field={field} value={held} family="bindingField" context={context} kind={kind} onChange={(next) => onWrite(field.key, next)} />;
  }
}

/** What an offered link says in a list: which way it runs from the kind, what it reaches, and how
 *  many such links the model holds. */
function captionOf(edges: EdgeCandidate[]): string {
  const ends = new Set(edges.map((edge) => edge.direction));
  const arrow = ends.size !== 1 ? '↔' : ends.has('in') ? '←' : '→';
  const reaches = [...new Set(edges.map((edge) => edge.reaches))].join(', ');
  return `${arrow} ${reaches} · ${edges.reduce((held, edge) => held + edge.count, 0)}`;
}

/**
 * The link from the compared Thing to what a binding counts, so the switcher narrows it. A
 * relationship in the model runs one way, so what is chosen here is which end of it the compared
 * Thing stands on; the offers know that already, so picking one says it, and the end is asked for
 * only where it is open — a predicate running both ways from the kind, or one the model holds no
 * link under.
 */
function ScopeEditor({ label, value, context, onChange }: { label: string; value: ScopeReference | undefined; context: BindingContext; onChange: (value: ScopeReference | undefined) => void }) {
  const { t } = useTranslation();
  const compared = context.offers.compareKind;
  const carried = edgesByPredicate(compared ? context.offers.edgesFrom(compared) : []);
  const chosen = value ? carried.get(value.viaPredicate) : undefined;
  const ends = new Set(chosen?.map((edge) => edge.direction));
  const running = (chosen ?? []).filter((edge) => edge.direction === (value?.direction ?? 'out'));
  const settled = ends.size === 1 && running.length > 0;
  const write = (predicate: string, direction: string) =>
    onChange(predicate ? { viaPredicate: predicate, ...(direction === 'in' ? { direction: 'in' as const } : {}) } : undefined);
  return (
    <Field title={label}>
      {() => (
        <div className={nestedClass}>
          <OfferedField
            label={t('design.scope.viaPredicate')}
            value={value?.viaPredicate ?? ''}
            mono
            offered={[...carried].map(([predicate, edges]) => ({ value: predicate, caption: captionOf(edges) }))}
            onCommit={(typed) => {
              const predicate = typed.trim();
              const picked = carried.get(predicate) ?? [];
              const one = new Set(picked.map((edge) => edge.direction));
              write(predicate, one.size === 1 ? [...one][0] : value?.direction ?? '');
            }}
          />
          {value && (
            <p className="text-[11px] text-zinc-600 dark:text-zinc-300">
              {running.length === 0
                ? t('design.scope.readsUnknown', { predicate: value.viaPredicate })
                : t('design.scope.reads', { kind: compared, predicate: value.viaPredicate, reaches: [...new Set(running.map((edge) => edge.reaches))].join(', ') })}
            </p>
          )}
          {value && !settled && (
            <ChoiceField label={t('design.scope.direction')} value={value.direction ?? ''} options={STEP_DIRECTIONS} emptyLabel="out" onCommit={(direction) => write(value.viaPredicate, direction)} />
          )}
        </div>
      )}
    </Field>
  );
}

/** A path built a hop at a time: each hop offered from the links the kind reached so far carries, the
 *  kind it reaches said beneath, and what the whole path costs per row. */
function StepsEditor({
  label, value, start, context, onChange,
}: { label: string; value: RelationStep[]; start: string | undefined; context: BindingContext; onChange: (value: RelationStep[]) => void }) {
  const { t } = useTranslation();
  const reached = kindReachedBy(start, value, context.offers);
  const writeStep = (index: number, next: RelationStep) => onChange(value.map((held, at) => (at === index ? next : held)));
  return (
    <Field title={label}>
      {() => (
        <div className={nestedClass}>
          {value.map((step, index) => {
            const before = kindReachedBy(start, value.slice(0, index), context.offers);
            const after = kindReachedBy(start, value.slice(0, index + 1), context.offers);
            const carried = edgesByPredicate(before ? context.offers.edgesFrom(before) : []);
            const set = (key: keyof RelationStep, next: unknown) => writeStep(index, withKey(step as unknown as Record<string, unknown>, key, next) as unknown as RelationStep);
            return (
              <Removable key={index} name={step.predicate || t('design.list.item', { position: index + 1 })} onRemove={() => onChange(value.filter((_, at) => at !== index))}>
                <OfferedField
                  label={t('design.steps.predicate')}
                  value={step.predicate}
                  mono
                  offered={[...carried].map(([predicate, edges]) => ({ value: predicate, caption: captionOf(edges) }))}
                  onCommit={(predicate) => {
                    const matching = carried.get(predicate) ?? [];
                    const one = matching.length === 1 ? matching[0] : undefined;
                    writeStep(index, {
                      ...step,
                      predicate,
                      ...(one ? { direction: one.direction === 'in' ? ('in' as const) : undefined, archetype: one.reaches } : {}),
                    });
                  }}
                />
                <ChoiceField label={t('design.steps.direction')} value={step.direction ?? ''} options={STEP_DIRECTIONS} emptyLabel="out" onCommit={(direction) => set('direction', direction || undefined)} />
                <OfferedField label={t('design.steps.archetype')} value={step.archetype ?? ''} mono offered={context.offers.kinds.map((kind) => ({ value: kind }))} onCommit={(archetype) => set('archetype', archetype || undefined)} />
                <OfferedField label={t('design.steps.inState')} value={step.inState ?? ''} mono offered={offeredFor('state', context, after, t)} onCommit={(state) => set('inState', state || undefined)} />
                <OfferedField label={t('design.steps.notInState')} value={step.notInState ?? ''} mono offered={offeredFor('state', context, after, t)} onCommit={(state) => set('notInState', state || undefined)} />
              </Removable>
            );
          })}
          <p className="flex flex-wrap gap-x-2 text-[11px] text-zinc-500 dark:text-zinc-400">
            {value.length > 0 && <span>{reached ? t('design.steps.reaches', { kind: reached }) : t('design.steps.reachesNothingKnown')}</span>}
            <span>{t('design.steps.hops', { count: value.length })}</span>
          </p>
          <button type="button" onClick={() => onChange([...value, { predicate: '' }])} className={smallButtonClass}>
            <Plus size={11} />
            {t('design.steps.add')}
          </button>
        </div>
      )}
    </Field>
  );
}

/** The comparisons that narrow a roster or a reduction, each a property, an operator and a value typed
 *  as the property is. */
function FiltersEditor({
  label, value, kind, context, onChange,
}: { label: string; value: PropertyFilter[]; kind?: string; context: BindingContext; onChange: (value: PropertyFilter[] | undefined) => void }) {
  const { t } = useTranslation();
  const properties = kind ? context.offers.propertiesOf(kind) : [];
  const write = (filters: PropertyFilter[]) => onChange(filters.length ? filters : undefined);
  const typed = (property: string, text: string): unknown => {
    const numeric = properties.find((candidate) => candidate.name === property)?.numeric;
    const parse = (word: string) => (numeric && word.trim() !== '' && !Number.isNaN(Number(word)) ? Number(word) : word.trim());
    return text.includes(',') ? text.split(',').map(parse) : parse(text);
  };
  return (
    <Field title={label}>
      {() => (
        <div className={nestedClass}>
          {value.map((filter, index) => {
            const set = (next: Partial<PropertyFilter>) => write(value.map((held, at) => (at === index ? { ...held, ...next } : held)));
            return (
              <Removable key={index} name={filter.property || t('design.list.item', { position: index + 1 })} onRemove={() => write(value.filter((_, at) => at !== index))}>
                <OfferedField label={t('design.filters.property')} value={filter.property} mono offered={offeredFor('property', context, kind, t)} onCommit={(property) => set({ property })} />
                <ChoiceField label={t('design.filters.operator')} value={filter.op} options={FILTER_OPERATORS} emptyLabel="=" onCommit={(op) => set({ op: (op || '=') as PropertyFilter['op'] })} />
                <TextField label={t('design.filters.value')} value={Array.isArray(filter.value) ? filter.value.join(',') : String(filter.value ?? '')} mono onCommit={(text) => set({ value: typed(filter.property, text) })} />
              </Removable>
            );
          })}
          <button type="button" onClick={() => write([...value, { property: '', op: '=', value: '' }])} className={smallButtonClass}>
            <Plus size={11} />
            {t('design.filters.add')}
          </button>
        </div>
      )}
    </Field>
  );
}

/** Columns worked out per row: a key each lands on, and a binding resolved with the row as `$scope`. */
function ComputedEditor({
  label, value, context, onChange,
}: { label: string; value: ComputedColumn[]; context: BindingContext; onChange: (value: ComputedColumn[] | undefined) => void }) {
  const { t } = useTranslation();
  const write = (columns: ComputedColumn[]) => onChange(columns.length ? columns : undefined);
  return (
    <Field title={label}>
      {() => (
        <div className={nestedClass}>
          {value.map((column, index) => (
            <Removable key={index} name={column.key || t('design.list.item', { position: index + 1 })} onRemove={() => write(value.filter((_, at) => at !== index))}>
              <TextField label={t('design.widgetField.key')} value={column.key} mono onCommit={(key) => write(value.map((held, at) => (at === index ? { ...held, key } : held)))} />
              <BindingEditor
                label={t('design.widgetField.value')}
                value={column.value}
                shape="number"
                context={context}
                onChange={(binding) => write(value.map((held, at) => (at === index ? { ...held, value: binding ?? { kind: 'const', value: 0 } } : held)))}
              />
            </Removable>
          ))}
          <button type="button" onClick={() => write([...value, { key: '', value: { kind: 'const', value: 0 } }])} className={smallButtonClass}>
            <Plus size={11} />
            {t('design.computed.add')}
          </button>
        </div>
      )}
    </Field>
  );
}
