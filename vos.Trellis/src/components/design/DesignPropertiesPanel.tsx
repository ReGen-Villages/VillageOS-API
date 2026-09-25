import { useTranslation } from 'react-i18next';
import { ArrowDown, ArrowUp, Trash2, X } from 'lucide-react';
import type { Binding, CompareConfiguration, DashboardSpecification, Widget } from '../../types/dashboard';
import { nextPlacement } from '../../utils/designSpec';
import { WIDGET_SCHEMAS } from '../../utils/widgetSchema';
import { rowKindOf, rowsBindingOf } from '../../utils/rowKind';
import type { BindingContext } from './bindingContext';
import { FieldControl } from './FieldControls';
import { DetailEditor } from './DetailEditor';
import { OfferedField } from './OfferedField';
import { ChoiceField, NumberField, TextField } from './DesignFields';
import { withKey } from './fieldSupport';
import {
  type DesignSelection,
  withPageWritten,
  withSectionMoved,
  withSectionRemoved,
  withSectionWritten,
  withWidgetMoved,
  withWidgetRemoved,
  withWidgetReplaced,
} from '../../utils/designEdits';

interface Props {
  specification: DashboardSpecification;
  selection: DesignSelection;
  context: BindingContext;
  onEdit: (change: (specification: DashboardSpecification) => DashboardSpecification) => void;
  onSelect: (selection: DesignSelection | null) => void;
}

const buttonClass =
  'inline-flex items-center gap-1 rounded-md border border-zinc-200 bg-white px-2 py-1 text-xs font-semibold text-zinc-600 hover:text-zinc-900 disabled:opacity-40 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-400 dark:hover:text-zinc-100';

/** What the selection declares — the page, a section or a widget — and where it is changed. */
export function DesignPropertiesPanel({ specification, selection, context, onEdit, onSelect }: Props) {
  const { t } = useTranslation();
  if (selection.on === 'translations') return null;
  const heading =
    selection.on === 'page'
      ? t('design.properties.page')
      : selection.on === 'section'
        ? t('design.properties.section')
        : t(`design.palette.kind.${specification.sections[selection.section].widgets[selection.widget].type}`);

  return (
    <aside className="w-full max-h-[45dvh] md:w-72 md:max-h-none flex-shrink-0 flex flex-col border-t md:border-t-0 md:border-l border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-900">
      <div className="flex items-center justify-between px-3 pt-3 pb-1">
        <h2 className="text-[11px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400">{heading}</h2>
        <button type="button" aria-label={t('design.properties.close')} onClick={() => onSelect(null)} className="text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200">
          <X size={14} />
        </button>
      </div>
      <div className="flex-1 overflow-y-auto p-3 space-y-3">
        {selection.on === 'page' && <PageFields specification={specification} context={context} onEdit={onEdit} />}
        {selection.on === 'section' && <SectionFields specification={specification} section={selection.section} onEdit={onEdit} onSelect={onSelect} />}
        {selection.on === 'widget' && <WidgetFields specification={specification} section={selection.section} widget={selection.widget} context={context} onEdit={onEdit} onSelect={onSelect} />}
      </div>
    </aside>
  );
}

function PageFields({ specification, context, onEdit }: { specification: DashboardSpecification; context: BindingContext; onEdit: Props['onEdit'] }) {
  const { t } = useTranslation();
  const compare = (patch: Partial<CompareConfiguration>) =>
    onEdit((held) => {
      const { label = '', archetype = '' } = { ...held.compare, ...patch };
      return { ...held, compare: label && archetype ? { label, archetype } : undefined } as DashboardSpecification;
    });
  return (
    <>
      <TextField label={t('design.properties.title')} value={specification.title} onCommit={(title) => { if (title.trim()) onEdit((held) => withPageWritten(held, { title: title.trim() })); }} />
      <TextField label={t('design.properties.subtitle')} value={specification.subtitle ?? ''} onCommit={(subtitle) => onEdit((held) => withPageWritten(held, { subtitle }))} />
      <NumberField label={t('design.properties.refreshSeconds')} value={specification.refreshSeconds} onCommit={(seconds) => onEdit((held) => withPageWritten(held, { refreshSeconds: seconds ?? 0 }))} />
      <TextField label={t('design.properties.icon')} value={specification.icon ?? ''} mono onCommit={(icon) => onEdit((held) => withPageWritten(held, { icon }))} />
      <TextField label={t('design.properties.compareLabel')} value={specification.compare?.label ?? ''} onCommit={(label) => compare({ label: label.trim() })} />
      <OfferedField label={t('design.properties.compareArchetype')} value={specification.compare?.archetype ?? ''} mono offered={context.offers.kinds.map((value) => ({ value }))} onCommit={(archetype) => compare({ archetype: archetype.trim() })} />
      <DetailEditor label={t('design.properties.detail')} value={specification.detail} context={context} kind={specification.compare?.archetype} onChange={(detail) => onEdit((held) => withKey(held as unknown as Record<string, unknown>, 'detail', detail) as unknown as DashboardSpecification)} />
    </>
  );
}

function SectionFields({ specification, section, onEdit, onSelect }: { specification: DashboardSpecification; section: number; onEdit: Props['onEdit']; onSelect: Props['onSelect'] }) {
  const { t } = useTranslation();
  const held = specification.sections[section];
  const move = (direction: 'earlier' | 'later') => {
    onEdit((current) => withSectionMoved(current, section, direction));
    onSelect({ on: 'section', section: direction === 'earlier' ? section - 1 : section + 1 });
  };
  return (
    <>
      <TextField label={t('design.properties.title')} value={held.title ?? ''} onCommit={(title) => onEdit((current) => withSectionWritten(current, section, { title }))} />
      <TextField label={t('design.properties.hint')} value={held.hint ?? ''} onCommit={(hint) => onEdit((current) => withSectionWritten(current, section, { hint }))} />
      <div className="flex flex-wrap gap-2">
        <button type="button" className={buttonClass} disabled={section === 0} onClick={() => move('earlier')}>
          <ArrowUp size={12} />
          {t('design.properties.earlier')}
        </button>
        <button type="button" className={buttonClass} disabled={section === specification.sections.length - 1} onClick={() => move('later')}>
          <ArrowDown size={12} />
          {t('design.properties.later')}
        </button>
        <button
          type="button"
          className={buttonClass}
          onClick={() => {
            onEdit((current) => withSectionRemoved(current, section));
            onSelect(null);
          }}
        >
          <Trash2 size={12} />
          {t('design.properties.removeSection')}
        </button>
      </div>
    </>
  );
}

/** The keys a row carries: the properties of its kind, and the columns worked out per row. */
function rowKeysOf(rows: Binding | undefined, rowKind: string | undefined, context: BindingContext): string[] {
  const keys = new Set<string>();
  if (rowKind) for (const property of context.offers.propertiesOf(rowKind)) keys.add(property.name);
  if (rows && 'computed' in rows) for (const column of rows.computed ?? []) keys.add(column.key);
  return [...keys].sort((a, b) => a.localeCompare(b));
}

function WidgetFields({
  specification,
  section,
  widget,
  context,
  onEdit,
  onSelect,
}: {
  specification: DashboardSpecification;
  section: number;
  widget: number;
  context: BindingContext;
  onEdit: Props['onEdit'];
  onSelect: Props['onSelect'];
}) {
  const { t } = useTranslation();
  const held = specification.sections[section].widgets[widget];
  const rows = rowsBindingOf(held);
  const rowKind = rowKindOf(rows, context.offers.compareKind);
  const widgetContext: BindingContext = { ...context, rowKind, rowKeys: rowKeysOf(rows, rowKind, context) };
  const write = (key: string, value: unknown) =>
    onEdit((current) => {
      const standing = current.sections[section].widgets[widget] as unknown as Record<string, unknown>;
      return withWidgetReplaced(current, section, widget, withKey(standing, key, value) as unknown as Widget);
    });
  const moveTo = (target: number) => {
    const size = held.placement ?? { width: 1, height: 1 };
    const placement = nextPlacement(specification.sections[target], size);
    onEdit((current) => withWidgetMoved(current, { section, widget }, target, placement));
    onSelect({ on: 'widget', section: target, widget: specification.sections[target].widgets.length });
  };
  return (
    <>
      {WIDGET_SCHEMAS[held.type].map((field) => (
        <FieldControl
          key={field.key}
          field={field}
          value={(held as unknown as Record<string, unknown>)[field.key]}
          family="widgetField"
          context={widgetContext}
          kind={rowKind}
          onChange={(value) => write(field.key, value)}
        />
      ))}
      {specification.sections.length > 1 && (
        <ChoiceField
          label={t('design.properties.inSection')}
          value={String(section)}
          options={specification.sections.map((_, index) => String(index))}
          emptyLabel=""
          onCommit={(chosen) => { if (chosen) moveTo(Number(chosen)); }}
        />
      )}
      <button
        type="button"
        className={buttonClass}
        onClick={() => {
          onEdit((current) => withWidgetRemoved(current, section, widget));
          onSelect(null);
        }}
      >
        <Trash2 size={12} />
        {t('design.properties.removeWidget')}
      </button>
    </>
  );
}
