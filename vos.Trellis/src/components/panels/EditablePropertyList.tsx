import { useState, useRef, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { Expand, Trash2, Loader2, Plus } from 'lucide-react';
import { formatPropertyValue } from '../../utils/formatters';
import { thingApi } from '../../api/thingApi';
import { relationshipApi } from '../../api/relationshipApi';
import { toast } from '../common/toastStore';
import { PROPERTY_TYPES, DEFAULT_PROPERTY_TYPE, asVosTypeName } from '../../utils/constants';
import type { EditableProperty } from './editableProperties';

/** Max characters before truncating a property value and showing an expand button. */
const VALUE_TRUNCATE_LIMIT = 60;

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

/** Read-only display row — same as the original pattern. */
function DisplayRow({
  name,
  value,
  onExpand,
}: {
  name: string;
  value: unknown;
  onExpand?: (formatted: string) => void;
}) {
  const { t } = useTranslation();
  const formatted = formatPropertyValue(value);
  const isLong = formatted.length > VALUE_TRUNCATE_LIMIT;

  return (
    <div className="flex items-baseline gap-2 py-1 min-w-0">
      <span className="text-zinc-400 text-xs truncate shrink min-w-[60px]" title={name}>{name}</span>
      <div className="flex items-center gap-1 min-w-0 ml-auto shrink-0">
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

  const canSubmit = name.trim().length > 0 && !saving;

  const submit = useCallback(async () => {
    if (!canSubmit) return;
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
        onChange={(e) => {
          const chosen = asVosTypeName(e.target.value);
          if (chosen) setType(chosen);
        }}
        disabled={saving}
        className="px-1 py-0.5 text-xs rounded border border-zinc-600 bg-zinc-800 text-zinc-200 focus:outline-none focus:ring-1 focus:ring-blue-500"
      >
        {PROPERTY_TYPES.map((t) => (
          <option key={t.value} value={t.value}>{t.label}</option>
        ))}
      </select>
      <input
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onKeyDown={onKeyDown}
        placeholder={t('panels.props.valuePlaceholder')}
        disabled={saving}
        className="flex-1 min-w-0 px-1.5 py-0.5 text-xs font-mono rounded border border-zinc-600 bg-zinc-800 text-zinc-200 placeholder-zinc-600 focus:outline-none focus:ring-1 focus:ring-blue-500"
      />
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
    setDraft(e.target.value);
    setDirty(true);
  }, []);

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
        <input
          ref={inputRef}
          value={draft}
          onChange={onChange}
          onKeyDown={onKeyDown}
          onBlur={save}
          disabled={saving}
          className={`w-full max-w-[180px] px-1.5 py-0.5 text-xs font-mono rounded border bg-zinc-800 text-zinc-200 focus:outline-none focus:ring-1 focus:ring-blue-500 ${
            dirty ? 'border-blue-500' : 'border-zinc-600'
          }`}
        />
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
