import { useState, useRef, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { Expand, Trash2, Loader2, Plus } from 'lucide-react';
import { formatPropertyValue } from '../../utils/formatters';
import { thingApi } from '../../api/thingApi';
import { relationshipApi } from '../../api/relationshipApi';
import { toast } from '../common/toastStore';
import { PROPERTY_TYPES, DEFAULT_PROPERTY_TYPE, asVosTypeName } from '../../utils/constants';
import { useNumberDisplaySettings } from '../../hooks/useNumberDisplaySettings';
import type { NumberDisplaySettings } from '../../utils/guiSettings';
import type { EditableProperty } from './editableProperties';
import { editorForType, rejectionKeyForType, type EditorKind } from './propertyEditing';

/** Max characters before truncating a property value and showing an expand button. */
const VALUE_TRUNCATE_LIMIT = 60;

const TYPE_LABELS = new Map(PROPERTY_TYPES.map((option) => [option.value as string, option.label]));

/** The dropdown's short word for a type, falling back to the name without its prefix so a type the
 *  dropdown does not offer still reads as something rather than as nothing. */
function shortTypeLabel(type: string): string {
  return TYPE_LABELS.get(type) ?? type.replace(/^vos\./, '');
}

/** A date keeps its seconds; the picker's default drops them, which would quietly shorten a
 *  timestamp a user only meant to nudge. */
function inputTypeFor(editor: EditorKind): string {
  if (editor === 'dateTime') return 'datetime-local';
  if (editor === 'wholeNumber' || editor === 'number') return 'number';
  return 'text';
}

interface Props {
  properties: EditableProperty[];
  entityId: string;
  entityType: 'thing' | 'relationship';
  editMode: boolean;
  onSaved?: () => void;
  onDeleteProperty?: (name: string) => void;
  onExpandValue?: (name: string, value: string) => void;
  showAddRow?: boolean;
}

export function EditablePropertyList({
  properties,
  entityId,
  entityType,
  editMode,
  onSaved,
  onDeleteProperty,
  onExpandValue,
  showAddRow = true,
}: Props) {
  const { t } = useTranslation();
  const numbers = useNumberDisplaySettings();
  return (
    <>
      {properties.length === 0 && !editMode && (
        <p className="text-zinc-500 text-xs italic">{t('panels.props.none')}</p>
      )}
      {properties.map(({ name, value, type }) =>
        editMode ? (
          <EditableRow
            key={name}
            name={name}
            value={value}
            declaredType={type}
            entityId={entityId}
            entityType={entityType}
            onSaved={onSaved}
            onDelete={onDeleteProperty ? () => onDeleteProperty(name) : undefined}
          />
        ) : (
          <DisplayRow
            key={name}
            name={name}
            value={value}
            declaredType={type}
            numbers={numbers}
            onExpand={onExpandValue ? (formatted) => onExpandValue(name, formatted) : undefined}
          />
        ),
      )}
      {editMode && showAddRow && (
        <AddPropertyRow entityId={entityId} entityType={entityType} onSaved={onSaved} />
      )}
    </>
  );
}

/** Read-only display row, formatted to what the platform says the property holds. */
function DisplayRow({
  name,
  value,
  declaredType,
  numbers,
  onExpand,
}: {
  name: string;
  value: unknown;
  declaredType: string;
  numbers: NumberDisplaySettings;
  onExpand?: (formatted: string) => void;
}) {
  const { t } = useTranslation();
  const formatted = formatPropertyValue(value, declaredType, numbers);
  const isLong = formatted.length > VALUE_TRUNCATE_LIMIT;

  return (
    <div className="flex items-baseline gap-2 py-1 min-w-0">
      <span className="text-zinc-400 text-xs truncate shrink min-w-[60px]" title={name}>{name}</span>
      <div className="flex items-center gap-1 min-w-0 ml-auto shrink-0">
        <span className="text-[10px] text-zinc-600 dark:text-zinc-500 shrink-0" title={declaredType}>
          {shortTypeLabel(declaredType)}
        </span>
        <span className="text-xs font-mono truncate max-w-[180px]" title={isLong ? undefined : formatted}>
          {isLong ? formatted.slice(0, VALUE_TRUNCATE_LIMIT) + '…' : formatted}
        </span>
        {isLong && onExpand && (
          <button
            onClick={() => onExpand(formatted)}
            className="text-zinc-500 hover:text-zinc-300 shrink-0"
            title={t('panels.props.viewFullValue')}
          >
            <Expand size={12} />
          </button>
        )}
      </div>
    </div>
  );
}

/** Row for adding a brand-new property. Shown at the bottom when edit mode is active. */
function AddPropertyRow({
  entityId,
  entityType,
  onSaved,
}: {
  entityId: string;
  entityType: 'thing' | 'relationship';
  onSaved?: () => void;
}) {
  const { t } = useTranslation();
  const [name, setName] = useState('');
  const [type, setType] = useState(DEFAULT_PROPERTY_TYPE);
  const [value, setValue] = useState('');
  const [saving, setSaving] = useState(false);
  const nameRef = useRef<HTMLInputElement>(null);

  const editor = editorForType(type);
  const canSubmit = name.trim().length > 0 && !saving;

  const submit = useCallback(async () => {
    if (!canSubmit) return;
    const rejection = rejectionKeyForType(value, type);
    if (rejection) {
      toast.error(t(rejection, { name: name.trim(), value }));
      return;
    }
    setSaving(true);
    try {
      const api = entityType === 'thing' ? thingApi : relationshipApi;
      await api.addProperty(entityId, name.trim(), type, value);
      toast.success(t('panels.props.setToast', { name: name.trim(), value }));
      setName('');
      setType(DEFAULT_PROPERTY_TYPE);
      setValue('');
      onSaved?.();
      // Re-focus the name input for quick successive adds
      nameRef.current?.focus();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('panels.props.addFailed'));
    } finally {
      setSaving(false);
    }
  }, [canSubmit, entityType, entityId, name, type, value, onSaved, t]);

  // Choosing a type swaps what the value is entered with, and resets the value: text typed for one
  // type is rarely a value of the next, and carrying it over just fails the check on submit.
  const onTypeChange = useCallback((chosenName: string) => {
    const chosen = asVosTypeName(chosenName);
    if (!chosen) return;
    setType(chosen);
    setValue(editorForType(chosen) === 'checkbox' ? 'false' : '');
  }, []);

  const onKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        submit();
      }
    },
    [submit],
  );

  return (
    <div className="flex items-center gap-1.5 py-1.5 mt-1 border-t border-zinc-700/50">
      <input
        ref={nameRef}
        value={name}
        onChange={(e) => setName(e.target.value)}
        onKeyDown={onKeyDown}
        placeholder={t('panels.props.namePlaceholder')}
        disabled={saving}
        className="w-[72px] px-1.5 py-0.5 text-xs rounded border border-zinc-600 bg-zinc-800 text-zinc-200 placeholder-zinc-600 focus:outline-none focus:ring-1 focus:ring-blue-500"
      />
      <select
        value={type}
        onChange={(e) => onTypeChange(e.target.value)}
        disabled={saving}
        aria-label={t('panels.props.typeLabel')}
        className="px-1 py-0.5 text-xs rounded border border-zinc-600 bg-zinc-800 text-zinc-200 focus:outline-none focus:ring-1 focus:ring-blue-500"
      >
        {PROPERTY_TYPES.map((t) => (
          <option key={t.value} value={t.value}>{t.label}</option>
        ))}
      </select>
      {editor === 'checkbox' ? (
        <input
          type="checkbox"
          checked={value === 'true'}
          onChange={(e) => setValue(String(e.target.checked))}
          onKeyDown={onKeyDown}
          disabled={saving}
          aria-label={t('panels.props.valuePlaceholder')}
          className="flex-1 min-w-0 accent-blue-500"
        />
      ) : (
        <input
          type={inputTypeFor(editor)}
          step={editor === 'wholeNumber' ? 1 : editor === 'number' ? 'any' : undefined}
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={onKeyDown}
          placeholder={t('panels.props.valuePlaceholder')}
          aria-label={t('panels.props.valuePlaceholder')}
          disabled={saving}
          className="flex-1 min-w-0 px-1.5 py-0.5 text-xs font-mono rounded border border-zinc-600 bg-zinc-800 text-zinc-200 placeholder-zinc-600 focus:outline-none focus:ring-1 focus:ring-blue-500"
        />
      )}
      <button
        onClick={submit}
        disabled={!canSubmit}
        className="p-0.5 rounded text-emerald-400 hover:bg-emerald-500/20 disabled:opacity-30 disabled:cursor-default transition-colors flex-shrink-0"
        title={t('panels.props.addProperty')}
      >
        {saving ? <Loader2 size={14} className="animate-spin" /> : <Plus size={14} />}
      </button>
    </div>
  );
}

/** Inline editable row — shows an input immediately. */
function EditableRow({
  name,
  value,
  declaredType,
  entityId,
  entityType,
  onSaved,
  onDelete,
}: {
  name: string;
  value: unknown;
  declaredType: string;
  entityId: string;
  entityType: 'thing' | 'relationship';
  onSaved?: () => void;
  onDelete?: () => void;
}) {
  const { t } = useTranslation();
  const editor = editorForType(declaredType);
  // Deliberately unformatted, unlike the display row. A reading shown to five decimal places is
  // rounded, and an edit box holding the rounded text would save that rounding back over the stored
  // value the moment anything else on the row changed. Editing works on the value, not on its
  // presentation — which is also why a date here is the timestamp the platform stores.
  const formatted = formatPropertyValue(value);
  const asDraft = formatted === '(null)' ? '' : formatted;
  const [draft, setDraft] = useState(asDraft);
  const [saving, setSaving] = useState(false);
  const [dirty, setDirty] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  // The row starts over when the stored value changes underneath it — after this save, or someone
  // else's. Adjusted while rendering rather than in an effect: React re-runs the component before
  // painting, so the old draft is never shown, where an effect would paint it and then correct it.
  const [renderedValue, setRenderedValue] = useState(asDraft);
  if (renderedValue !== asDraft) {
    setRenderedValue(asDraft);
    setDraft(asDraft);
    setDirty(false);
  }

  const onChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setDraft(editor === 'checkbox' ? String(e.target.checked) : e.target.value);
    setDirty(true);
  }, [editor]);

  const save = useCallback(async () => {
    if (saving || !dirty) return;
    const trimmed = draft.trim();
    const original = formatted === '(null)' ? '' : formatted;
    if (trimmed === original) {
      setDirty(false);
      return;
    }
    const type = asVosTypeName(declaredType);
    if (!type) {
      toast.error(t('panels.props.unknownType', { name, type: declaredType }));
      return;
    }
    // Refused here rather than sent for the platform to reject, so the message reaches the user
    // while the field that caused it is still in front of them.
    const rejection = rejectionKeyForType(trimmed, type);
    if (rejection) {
      toast.error(t(rejection, { name, value: trimmed }));
      return;
    }
    setSaving(true);
    try {
      const api = entityType === 'thing' ? thingApi : relationshipApi;
      await api.setProperty(entityId, name, type, trimmed);
      toast.success(t('panels.props.savedToast', { name, value: trimmed }));
      setDirty(false);
      onSaved?.();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('panels.props.saveFailed'));
    } finally {
      setSaving(false);
    }
  }, [saving, dirty, draft, formatted, entityType, entityId, name, declaredType, onSaved, t]);

  const onKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        save();
      }
      if (e.key === 'Escape') {
        const f = formatted === '(null)' ? '' : formatted;
        setDraft(f);
        setDirty(false);
        inputRef.current?.blur();
      }
    },
    [save, formatted],
  );

  return (
    <div className="flex items-center justify-between py-1 gap-2">
      <span className="text-zinc-400 text-xs shrink-0">{name}</span>
      <div className="flex items-center gap-1 min-w-0 flex-1 justify-end">
        {editor === 'readOnly' ? (
          <span className="text-[11px] text-zinc-500 italic truncate" title={t('panels.props.writtenByIngest')}>
            {t('panels.props.writtenByIngest')}
          </span>
        ) : editor === 'checkbox' ? (
          <input
            ref={inputRef}
            type="checkbox"
            checked={draft === 'true'}
            onChange={onChange}
            onKeyDown={onKeyDown}
            onBlur={save}
            disabled={saving}
            aria-label={name}
            className="accent-blue-500"
          />
        ) : (
          <input
            ref={inputRef}
            type={inputTypeFor(editor)}
            step={editor === 'wholeNumber' ? 1 : editor === 'number' ? 'any' : undefined}
            value={draft}
            onChange={onChange}
            onKeyDown={onKeyDown}
            onBlur={save}
            disabled={saving}
            aria-label={name}
            className={`w-full max-w-[180px] px-1.5 py-0.5 text-xs font-mono rounded border bg-zinc-800 text-zinc-200 focus:outline-none focus:ring-1 focus:ring-blue-500 ${
              dirty ? 'border-blue-500' : 'border-zinc-600'
            }`}
          />
        )}
        {saving && <Loader2 size={12} className="text-zinc-500 animate-spin shrink-0" />}
        {onDelete && (
          <button
            onClick={onDelete}
            className="text-red-400 hover:text-red-300 shrink-0"
            title={t('panels.props.deleteProperty')}
          >
            <Trash2 size={12} />
          </button>
        )}
      </div>
    </div>
  );
}
