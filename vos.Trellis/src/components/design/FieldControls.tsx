import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus, X } from 'lucide-react';
import type { Binding, TableColumn } from '../../types/dashboard';
import { COLUMN_FIELDS, type FieldSpecification } from '../../utils/widgetSchema';
import { ChoiceField, Field, NumberField, SwitchField, TextField } from './DesignFields';
import { OfferedField, type Offered } from './OfferedField';
import { labelFor, nestedClass, offeredFor, smallButtonClass, withKey, type FieldFamily } from './fieldSupport';
import { Removable } from './Removable';
import { BindingEditor } from './BindingEditor';
import type { BindingContext } from './bindingContext';
import { DetailEditor } from './DetailEditor';
import { AskedEditor, ActionWritesEditor, FormWritesEditor, PreviewEditor } from './WritesEditors';

interface Props {
  field: FieldSpecification;
  value: unknown;
  /** Undefined takes the key away, so a specification never carries an empty value the renderer
   *  would draw. */
  onChange: (value: unknown) => void;
  family: FieldFamily;
  context: BindingContext;
  /** The kind the field's words are offered for: a property of it, a state it derives. */
  kind?: string;
}

/** One field of a widget or a binding, drawn by the control its schema names. */
export function FieldControl({ field, value, onChange, family, context, kind }: Props) {
  const { t } = useTranslation();
  const label = labelFor(family, field.key, t);

  if (field.derived) {
    return (
      <p className="text-xs text-zinc-500 dark:text-zinc-400">
        <span className="font-semibold">{label}</span> — {t('design.derivedField')}
      </p>
    );
  }

  switch (field.kind) {
    case 'text':
      return field.offers ? (
        <OfferedField label={label} value={asText(value)} mono={field.mono} offered={offeredFor(field.offers, context, kind, t)} onCommit={(text) => onChange(text || undefined)} />
      ) : (
        <TextField label={label} value={asText(value)} mono={field.mono} onCommit={(text) => onChange(text || undefined)} />
      );
    case 'number':
      return <NumberField label={label} value={value} onCommit={onChange} />;
    case 'boolean':
      return <SwitchField label={label} value={value === true} onCommit={(on) => onChange(on ? true : undefined)} />;
    case 'choice':
      return <ChoiceField label={label} value={asText(value)} options={field.options} emptyLabel="—" onCommit={(chosen) => onChange(chosen || undefined)} />;
    case 'binding':
      return <BindingEditor label={label} value={value as Binding | undefined} shape={field.shape} context={context} onChange={onChange} />;
    case 'bound':
      return <BoundField label={label} value={value} context={context} onChange={onChange} />;
    case 'strings':
      return <StringsField label={label} value={asStrings(value)} offered={offeredFor(field.offers, context, kind, t)} onChange={(held) => onChange(held.length ? held : undefined)} />;
    case 'numberPair':
      return <NumberPairField label={label} value={value} onChange={onChange} />;
    case 'json':
      return <JsonField label={label} value={value} onChange={onChange} />;
    case 'columns':
      return <ColumnsEditor label={label} value={(value as TableColumn[] | undefined) ?? []} context={context} kind={kind} onChange={onChange} />;
    case 'list':
      return <ListEditor label={label} fields={field.of} value={(value as Record<string, unknown>[] | undefined) ?? []} family={family} context={context} kind={kind} onChange={onChange} />;
    case 'group':
      return <GroupEditor label={label} fields={field.of} value={(value as Record<string, unknown> | undefined) ?? {}} family={family} context={context} kind={kind} onChange={onChange} />;
    case 'detail':
      return <DetailEditor label={label} value={value as never} context={context} kind={kind} onChange={onChange} />;
    case 'asked':
      return <AskedEditor label={label} value={value as never} context={context} onChange={onChange} />;
    case 'actionWrites':
      return <ActionWritesEditor label={label} value={value as never} context={context} onChange={onChange} />;
    case 'formWrites':
      return <FormWritesEditor label={label} value={value as never} context={context} onChange={onChange} />;
    case 'preview':
      return <PreviewEditor label={label} value={value as never} onChange={onChange} />;
    case 'scope':
    case 'steps':
    case 'filters':
    case 'computed':
      // Parts of a binding, drawn by the binding editor beside the binding they belong to.
      return null;
  }
}

function asText(value: unknown): string {
  return value === undefined || value === null ? '' : String(value);
}

function asStrings(value: unknown): string[] {
  return Array.isArray(value) ? value.map(String) : [];
}

/** A number, or a binding onto the model's own value: a number typed stays a number; the binding is
 *  opened when the field is switched over. */
function BoundField({ label, value, context, onChange }: { label: string; value: unknown; context: BindingContext; onChange: (value: unknown) => void }) {
  const { t } = useTranslation();
  const isBinding = typeof value === 'object' && value !== null;
  const [bound, setBound] = useState(isBinding);
  return (
    <div>
      {bound || isBinding ? (
        <BindingEditor label={label} value={isBinding ? (value as Binding) : undefined} shape="number" context={context} onChange={onChange} />
      ) : (
        <NumberField label={label} value={value} onCommit={onChange} />
      )}
      <button type="button" className={`${smallButtonClass} mt-1`} onClick={() => { setBound(!bound); onChange(undefined); }}>
        {bound || isBinding ? t('design.bound.asNumber') : t('design.bound.asBinding')}
      </button>
    </div>
  );
}

/** A list of words: each drawn as a chip that can be taken away, and a field that adds one. */
export function StringsField({ label, value, offered, onChange }: { label: string; value: string[]; offered: Offered[]; onChange: (value: string[]) => void }) {
  const { t } = useTranslation();
  const [adding, setAdding] = useState(0);
  return (
    <div>
      <OfferedField
        key={adding}
        label={label}
        value=""
        offered={offered.filter((option) => !value.includes(option.value))}
        mono
        onCommit={(word) => {
          const trimmed = word.trim();
          if (trimmed && !value.includes(trimmed)) onChange([...value, trimmed]);
          setAdding((n) => n + 1);
        }}
      />
      {value.length > 0 && (
        <ul className="mt-1 flex flex-wrap gap-1">
          {value.map((word) => (
            <li key={word} className="inline-flex items-center gap-1 rounded border border-zinc-200 bg-zinc-50 px-1.5 py-0.5 font-mono text-[11px] text-zinc-700 dark:border-zinc-700 dark:bg-zinc-800/60 dark:text-zinc-300">
              {word}
              <button type="button" aria-label={`${t('design.list.remove')} ${word}`} onClick={() => onChange(value.filter((held) => held !== word))} className="text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200">
                <X size={11} />
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

/** Two numbers written as one pair once both are there; half a pair is held here, not on the
 *  specification. */
function NumberPairField({ label, value, onChange }: { label: string; value: unknown; onChange: (value: unknown) => void }) {
  const { t } = useTranslation();
  const pair = Array.isArray(value) && value.length === 2 ? (value as [number, number]) : undefined;
  const [held, setHeld] = useState<[number | undefined, number | undefined]>(pair ?? [undefined, undefined]);
  const write = (low: number | undefined, high: number | undefined) => {
    setHeld([low, high]);
    onChange(low === undefined || high === undefined ? undefined : [low, high]);
  };
  return (
    <Field title={label}>
      {() => (
        <div className="mt-1 grid grid-cols-2 gap-2">
          <NumberField label={t('design.pair.low')} value={held[0]} onCommit={(low) => write(low, held[1])} />
          <NumberField label={t('design.pair.high')} value={held[1]} onCommit={(high) => write(held[0], high)} />
        </div>
      )}
    </Field>
  );
}

function JsonField({ label, value, onChange }: { label: string; value: unknown; onChange: (value: unknown) => void }) {
  const { t } = useTranslation();
  const [text, setText] = useState(value === undefined ? '' : JSON.stringify(value, null, 2));
  const [invalid, setInvalid] = useState(false);
  return (
    <Field title={label}>
      {({ id }) => (
        <>
          <textarea
            id={id}
            value={text}
            rows={4}
            onChange={(event) => setText(event.target.value)}
            onBlur={() => {
              if (text.trim() === '') {
                setInvalid(false);
                onChange(undefined);
                return;
              }
              try {
                onChange(JSON.parse(text));
                setInvalid(false);
              } catch {
                setInvalid(true);
              }
            }}
            className="mt-1 w-full rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 px-2 py-1 font-mono text-xs text-zinc-900 dark:text-zinc-100"
          />
          {invalid && <p className="mt-1 text-xs text-amber-700 dark:text-amber-400">{t('design.json.invalid')}</p>}
        </>
      )}
    </Field>
  );
}

/** A list of like items — stages, buckets, metrics, rows — each edited by its own fields, taken away
 *  one at a time, and added empty at the end. */
function ListEditor({
  label, fields, value, family, context, kind, onChange,
}: { label: string; fields: FieldSpecification[]; value: Record<string, unknown>[]; family: FieldFamily; context: BindingContext; kind?: string; onChange: (value: unknown) => void }) {
  const { t } = useTranslation();
  const write = (items: Record<string, unknown>[]) => onChange(items.length ? items : undefined);
  return (
    <Field title={label}>
      {() => (
        <div className="mt-1 space-y-2">
          {value.map((item, index) => (
            <Removable key={index} name={String(item.label ?? item.key ?? item.state ?? t('design.list.item', { position: index + 1 }))} onRemove={() => write(value.filter((_, at) => at !== index))}>
              {fields.map((field) => (
                <FieldControl
                  key={field.key}
                  field={field}
                  value={item[field.key]}
                  family={family}
                  context={context}
                  kind={kind}
                  onChange={(next) => write(value.map((held, at) => (at === index ? withKey(held, field.key, next) : held)))}
                />
              ))}
            </Removable>
          ))}
          <button type="button" onClick={() => write([...value, {}])} className={smallButtonClass}>
            <Plus size={11} />
            {t('design.list.add')}
          </button>
        </div>
      )}
    </Field>
  );
}

/** Several fields that stand as one object on the specification — a range's seven series, a side of a
 *  diverging bar — written key by key, and taken away whole when every key is. */
function GroupEditor({
  label, fields, value, family, context, kind, onChange,
}: { label: string; fields: FieldSpecification[]; value: Record<string, unknown>; family: FieldFamily; context: BindingContext; kind?: string; onChange: (value: unknown) => void }) {
  return (
    <Field title={label}>
      {() => (
        <div className={nestedClass}>
          {fields.map((field) => (
            <FieldControl
              key={field.key}
              field={field}
              value={value[field.key]}
              family={family}
              context={context}
              kind={kind}
              onChange={(next) => {
                const written = withKey(value, field.key, next);
                onChange(Object.keys(written).length ? written : undefined);
              }}
            />
          ))}
        </div>
      )}
    </Field>
  );
}

function ColumnsEditor({ label, value, context, kind, onChange }: { label: string; value: TableColumn[]; context: BindingContext; kind?: string; onChange: (value: unknown) => void }) {
  const { t } = useTranslation();
  const write = (columns: TableColumn[]) => onChange(columns.length ? columns : undefined);
  return (
    <Field title={label}>
      {() => (
        <div className="mt-1 space-y-2">
          {value.map((column, index) => (
            <Removable key={index} name={column.key || t('design.list.item', { position: index + 1 })} onRemove={() => write(value.filter((_, at) => at !== index))}>
              {COLUMN_FIELDS.map((field) => (
                <FieldControl
                  key={field.key}
                  field={field}
                  value={(column as unknown as Record<string, unknown>)[field.key]}
                  family="widgetField"
                  context={context}
                  kind={kind}
                  onChange={(next) => write(value.map((held, at) => (at === index ? (withKey(held as unknown as Record<string, unknown>, field.key, next) as unknown as TableColumn) : held)))}
                />
              ))}
            </Removable>
          ))}
          <button type="button" onClick={() => write([...value, { key: '', label: '' }])} className={smallButtonClass}>
            <Plus size={11} />
            {t('design.columns.add')}
          </button>
        </div>
      )}
    </Field>
  );
}
