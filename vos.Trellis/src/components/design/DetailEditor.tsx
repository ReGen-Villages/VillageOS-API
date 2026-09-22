import { useTranslation } from 'react-i18next';
import { Plus } from 'lucide-react';
import type { DetailSpecification, RelationSpecification } from '../../types/dashboard';
import { STEP_DIRECTIONS } from '../../utils/widgetSchema';
import { kindReachedBy } from './designOffers';
import type { BindingContext } from './bindingContext';
import { ChoiceField, Field, SwitchField, TextField } from './DesignFields';
import { OfferedField } from './OfferedField';
import { StringsField } from './FieldControls';
import { offeredFor, smallButtonClass, withKey } from './fieldSupport';
import { Removable } from './Removable';

/**
 * The card a row opens: what titles it, its groups of properties, and the relations it follows from
 * the Thing — each of which may follow further relations in turn, so the editor nests as the card does.
 */
export function DetailEditor({
  label, value, context, kind, onChange,
}: { label: string; value: DetailSpecification | undefined; context: BindingContext; kind?: string; onChange: (value: unknown) => void }) {
  const { t } = useTranslation();
  const held = value ?? {};
  const write = (key: keyof DetailSpecification, next: unknown) => {
    const written = withKey(held as Record<string, unknown>, key, next);
    onChange(Object.keys(written).length ? written : undefined);
  };
  const properties = offeredFor('property', context, kind, t);
  const groups = held.propertyGroups ?? [];

  return (
    <Field title={label}>
      {() => (
        <div className="mt-1 space-y-2 rounded-md border border-zinc-200 dark:border-zinc-700 p-2">
          <OfferedField label={t('design.detail.titleProperty')} value={held.titleProperty ?? ''} mono offered={properties} onCommit={(text) => write('titleProperty', text || undefined)} />
          <OfferedField label={t('design.detail.subtitleProperty')} value={held.subtitleProperty ?? ''} mono offered={properties} onCommit={(text) => write('subtitleProperty', text || undefined)} />
          <Field title={t('design.detail.propertyGroups')}>
            {() => (
              <div className="mt-1 space-y-2">
                {groups.map((group, index) => (
                  <Removable key={index} name={group.label || t('design.list.item', { position: index + 1 })} onRemove={() => write('propertyGroups', dropAt(groups, index).length ? dropAt(groups, index) : undefined)}>
                    <TextField label={t('design.widgetField.label')} value={group.label} onCommit={(text) => write('propertyGroups', replaceAt(groups, index, { ...group, label: text }))} />
                    <StringsField label={t('design.detail.keys')} value={group.keys} offered={properties} onChange={(keys) => write('propertyGroups', replaceAt(groups, index, { ...group, keys }))} />
                  </Removable>
                ))}
                <button type="button" onClick={() => write('propertyGroups', [...groups, { label: '', keys: [] }])} className={smallButtonClass}>
                  <Plus size={11} />
                  {t('design.detail.addGroup')}
                </button>
              </div>
            )}
          </Field>
          <RelationsEditor value={held.relations ?? []} context={context} kind={kind} onChange={(relations) => write('relations', relations.length ? relations : undefined)} />
          <SwitchField label={t('design.detail.history')} value={held.history?.enabled === true} onCommit={(on) => write('history', on ? { enabled: true } : undefined)} />
        </div>
      )}
    </Field>
  );
}

function RelationsEditor({ value, context, kind, onChange }: { value: RelationSpecification[]; context: BindingContext; kind?: string; onChange: (value: RelationSpecification[]) => void }) {
  const { t } = useTranslation();
  const edges = kind ? context.offers.edgesFrom(kind) : [];
  return (
    <Field title={t('design.detail.relations')}>
      {() => (
        <div className="mt-1 space-y-2">
          {value.map((relation, index) => {
            const reached = kindReachedBy(kind, [relation], context.offers);
            const set = (key: keyof RelationSpecification, next: unknown) => onChange(replaceAt(value, index, withKey(relation as unknown as Record<string, unknown>, key, next) as unknown as RelationSpecification));
            return (
              <Removable key={index} name={relation.predicate || t('design.list.item', { position: index + 1 })} onRemove={() => onChange(dropAt(value, index))}>
                <OfferedField
                  label={t('design.detail.predicate')}
                  value={relation.predicate}
                  mono
                  offered={edges.map((edge) => ({ value: edge.predicate, caption: `${edge.direction === 'in' ? '←' : '→'} ${edge.reaches}` }))}
                  onCommit={(predicate) => set('predicate', predicate)}
                />
                <ChoiceField label={t('design.detail.direction')} value={relation.direction ?? ''} options={STEP_DIRECTIONS} emptyLabel="out" onCommit={(direction) => set('direction', direction || undefined)} />
                <OfferedField label={t('design.detail.archetype')} value={relation.archetype ?? ''} mono offered={context.offers.kinds.map((each) => ({ value: each }))} onCommit={(archetype) => set('archetype', archetype || undefined)} />
                <TextField label={t('design.detail.relationLabel')} value={relation.label ?? ''} onCommit={(text) => set('label', text || undefined)} />
                <SwitchField label={t('design.detail.allProperties')} value={relation.properties === '*'} onCommit={(all) => set('properties', all ? '*' : undefined)} />
                {relation.properties !== '*' && (
                  <StringsField label={t('design.detail.properties')} value={Array.isArray(relation.properties) ? relation.properties : []} offered={offeredFor('property', context, reached, t)} onChange={(properties) => set('properties', properties.length ? properties : undefined)} />
                )}
                <SwitchField label={t('design.detail.inline')} value={relation.inline === true} onCommit={(on) => set('inline', on ? true : undefined)} />
                <RelationsEditor value={relation.relations ?? []} context={context} kind={reached} onChange={(relations) => set('relations', relations.length ? relations : undefined)} />
              </Removable>
            );
          })}
          <button type="button" onClick={() => onChange([...value, { predicate: '' }])} className={smallButtonClass}>
            <Plus size={11} />
            {t('design.detail.addRelation')}
          </button>
        </div>
      )}
    </Field>
  );
}

function replaceAt<T>(items: T[], index: number, next: T): T[] {
  return items.map((held, at) => (at === index ? next : held));
}

function dropAt<T>(items: T[], index: number): T[] {
  return items.filter((_, at) => at !== index);
}
