import { useTranslation } from 'react-i18next';
import { Plus } from 'lucide-react';
import type { ActionChoice, ActionRecords, AskedValue, FormWidget } from '../../types/dashboard';
import { ChoiceField, Field, SwitchField, TextField } from './DesignFields';
import { OfferedField } from './OfferedField';
import { BindingEditor } from './BindingEditor';
import type { BindingContext } from './bindingContext';
import { StringsField } from './FieldControls';
import { nestedClass, offeredFor, smallButtonClass, withKey } from './fieldSupport';
import { Removable } from './Removable';
import { rowKindOf } from '../../utils/rowKind';

/**
 * The two widgets that write, wired from the panel: what a press or a form asks for first, and what
 * it writes — the route chosen by subdomain from what the platform registers, because a press naming
 * anything else reaches nothing and says so only when pressed.
 */

const ASKED_KINDS = ['text', 'number', 'datetime', 'secret', 'choice', 'multichoice'] as const;

/** The values a person supplies before pressing — typed, or chosen by name from a roster. */
export function AskedEditor({
  label, value, context, onChange,
}: { label: string; value: AskedValue[] | undefined; context: BindingContext; onChange: (value: AskedValue[] | undefined) => void }) {
  const { t } = useTranslation();
  const held = value ?? [];
  const write = (fields: AskedValue[]) => onChange(fields.length ? fields : undefined);
  return (
    <Field title={label}>
      {() => (
        <div className={nestedClass}>
          {held.map((field, index) => {
            const set = (key: keyof AskedValue, next: unknown) => write(held.map((each, at) => (at === index ? (withKey(each as unknown as Record<string, unknown>, key, next) as unknown as AskedValue) : each)));
            const picked = field.kind === 'choice' || field.kind === 'multichoice';
            return (
              <Removable key={index} name={field.key || t('design.list.item', { position: index + 1 })} onRemove={() => write(held.filter((_, at) => at !== index))}>
                <TextField label={t('design.asked.key')} value={field.key} mono onCommit={(key) => set('key', key)} />
                <TextField label={t('design.asked.label')} value={field.label} onCommit={(text) => set('label', text)} />
                <ChoiceField label={t('design.asked.kind')} value={field.kind ?? ''} options={ASKED_KINDS} emptyLabel="text" onCommit={(kind) => set('kind', kind || undefined)} />
                {picked && (
                  <>
                    <BindingEditor label={t('design.asked.options')} value={field.options} shape="rows" context={context} onChange={(options) => set('options', options)} />
                    <StringsField label={t('design.asked.shows')} value={field.shows ?? []} offered={offeredFor('property', context, rowKindOf(field.options, context.offers.compareKind), t)} onChange={(shows) => set('shows', shows.length ? shows : undefined)} />
                  </>
                )}
                <SwitchField label={t('design.asked.optional')} value={field.optional === true} onCommit={(on) => set('optional', on ? true : undefined)} />
              </Removable>
            );
          })}
          <button type="button" onClick={() => write([...held, { key: '', label: '' }])} className={smallButtonClass}>
            <Plus size={11} />
            {t('design.asked.add')}
          </button>
        </div>
      )}
    </Field>
  );
}

/** What pressing a choice on an action list writes: the route, the archetype minted and its predicate
 *  to the row, and the choices with what each names. */
export function ActionWritesEditor({
  label, value, context, onChange,
}: { label: string; value: ActionRecords | undefined; context: BindingContext; onChange: (value: ActionRecords) => void }) {
  const { t } = useTranslation();
  const held: ActionRecords = value ?? { via: '', choices: [] };
  const set = (key: keyof ActionRecords, next: unknown) => onChange(withKey(held as unknown as Record<string, unknown>, key, next) as unknown as ActionRecords);
  const writeChoices = (next: ActionChoice[]) => set('choices', next);
  const kinds = context.offers.kinds.map((each) => ({ value: each }));
  const predicates = context.offers.predicates.map((each) => ({ value: each }));

  return (
    <Field title={label}>
      {() => (
        <div className={nestedClass}>
          <OfferedField label={t('design.writes.via')} value={held.via} mono offered={offeredFor('endpoint', context, undefined, t)} onCommit={(via) => set('via', via.trim())} />
          <OfferedField label={t('design.writes.archetype')} value={held.archetype ?? ''} mono offered={kinds} onCommit={(archetype) => set('archetype', archetype.trim() || undefined)} />
          <OfferedField label={t('design.writes.predicate')} value={held.predicate ?? ''} mono offered={predicates} onCommit={(predicate) => set('predicate', predicate.trim() || undefined)} />
          <SwitchField label={t('design.writes.repeatable')} value={held.repeatable === true} onCommit={(on) => set('repeatable', on ? true : undefined)} />
          <Field title={t('design.writes.choices')}>
            {() => (
              <div className={nestedClass}>
                {held.choices.map((choice, index) => {
                  const setChoice = (key: keyof ActionChoice, next: unknown) => writeChoices(held.choices.map((each, at) => (at === index ? (withKey(each as unknown as Record<string, unknown>, key, next) as unknown as ActionChoice) : each)));
                  return (
                    <Removable key={index} name={choice.label || t('design.list.item', { position: index + 1 })} onRemove={() => writeChoices(held.choices.filter((_, at) => at !== index))}>
                      <TextField label={t('design.writes.choiceLabel')} value={choice.label} onCommit={(text) => setChoice('label', text)} />
                      <TextField label={t('design.writes.choiceAct')} value={choice.act ?? ''} mono onCommit={(act) => setChoice('act', act.trim() || undefined)} />
                      <OfferedField label={t('design.writes.choiceTarget')} value={choice.target ?? ''} mono offered={kinds} onCommit={(target) => setChoice('target', target.trim() || undefined)} />
                      <OfferedField label={t('design.writes.choiceViaPredicate')} value={choice.viaPredicate ?? ''} mono offered={predicates} onCommit={(predicate) => setChoice('viaPredicate', predicate.trim() || undefined)} />
                    </Removable>
                  );
                })}
                <button type="button" onClick={() => writeChoices([...held.choices, { label: '' }])} className={smallButtonClass}>
                  <Plus size={11} />
                  {t('design.writes.addChoice')}
                </button>
              </div>
            )}
          </Field>
        </div>
      )}
    </Field>
  );
}

export function FormWritesEditor({
  label, value, context, onChange,
}: { label: string; value: FormWidget['writes'] | undefined; context: BindingContext; onChange: (value: FormWidget['writes']) => void }) {
  const { t } = useTranslation();
  const held: FormWidget['writes'] = value ?? { via: '', act: '' };
  return (
    <Field title={label}>
      {() => (
        <div className={nestedClass}>
          <OfferedField label={t('design.writes.via')} value={held.via} mono offered={offeredFor('endpoint', context, undefined, t)} onCommit={(via) => onChange({ ...held, via: via.trim() })} />
          <TextField label={t('design.writes.act')} value={held.act} mono onCommit={(act) => onChange({ ...held, act: act.trim() })} />
          <OfferedField label={t('design.writes.archetype')} value={held.archetype ?? ''} mono offered={context.offers.kinds.map((each) => ({ value: each }))} onCommit={(archetype) => onChange(withKey(held as unknown as Record<string, unknown>, 'archetype', archetype.trim() || undefined) as unknown as FormWidget['writes'])} />
        </div>
      )}
    </Field>
  );
}

/** A read of the same fields offered beside the press. */
export function PreviewEditor({ label, value, onChange }: { label: string; value: FormWidget['preview'] | undefined; onChange: (value: FormWidget['preview'] | undefined) => void }) {
  const { t } = useTranslation();
  const write = (act: string, text: string) => onChange(act.trim() || text.trim() ? { act: act.trim(), label: text.trim() } : undefined);
  return (
    <Field title={label}>
      {() => (
        <div className={nestedClass}>
          <TextField label={t('design.writes.previewAct')} value={value?.act ?? ''} mono onCommit={(act) => write(act, value?.label ?? '')} />
          <TextField label={t('design.writes.previewLabel')} value={value?.label ?? ''} onCommit={(text) => write(value?.act ?? '', text)} />
        </div>
      )}
    </Field>
  );
}
