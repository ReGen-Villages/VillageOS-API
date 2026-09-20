import { useTranslation } from 'react-i18next';
import { ArrowDown, ArrowUp, Trash2, X } from 'lucide-react';
import type { DashboardSpec } from '../../types/dashboard';
import { nextPlacement } from '../../utils/designSpec';
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
  spec: DashboardSpec;
  selection: DesignSelection;
  onEdit: (change: (spec: DashboardSpec) => DashboardSpec) => void;
  onSelect: (selection: DesignSelection | null) => void;
}

const fieldClass =
  'w-full rounded border border-zinc-300 bg-white px-2 py-1 text-sm text-zinc-900 dark:border-zinc-600 dark:bg-zinc-800 dark:text-white';
const labelClass = 'block text-[11px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400 mb-1';
const buttonClass =
  'inline-flex items-center gap-1 rounded-md border border-zinc-200 bg-white px-2 py-1 text-xs font-semibold text-zinc-600 hover:text-zinc-900 disabled:opacity-40 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-400 dark:hover:text-zinc-100';

/** What the selection declares — the page, a section or a widget — and where it is changed. */
export function DesignPropertiesPanel({ spec, selection, onEdit, onSelect }: Props) {
  const { t } = useTranslation();
  const heading =
    selection.on === 'page'
      ? t('design.properties.page')
      : selection.on === 'section'
        ? t('design.properties.section')
        : t(`design.palette.kind.${spec.sections[selection.section].widgets[selection.widget].type}`);

  return (
    <aside className="w-72 flex-shrink-0 flex flex-col border-l border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-900">
      <div className="flex items-center justify-between px-3 pt-3 pb-1">
        <h2 className="text-[11px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400">{heading}</h2>
        <button type="button" aria-label={t('design.properties.close')} onClick={() => onSelect(null)} className="text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200">
          <X size={14} />
        </button>
      </div>
      <div className="flex-1 overflow-y-auto p-3 space-y-3">
        {selection.on === 'page' && <PageFields spec={spec} onEdit={onEdit} />}
        {selection.on === 'section' && <SectionFields spec={spec} section={selection.section} onEdit={onEdit} onSelect={onSelect} />}
        {selection.on === 'widget' && <WidgetFields spec={spec} section={selection.section} widget={selection.widget} onEdit={onEdit} onSelect={onSelect} />}
      </div>
    </aside>
  );
}

function PageFields({ spec, onEdit }: { spec: DashboardSpec; onEdit: Props['onEdit'] }) {
  const { t } = useTranslation();
  return (
    <>
      <label className="block">
        <span className={labelClass}>{t('design.properties.title')}</span>
        <input className={fieldClass} value={spec.title} onChange={(event) => onEdit((held) => withPageWritten(held, { title: event.target.value }))} />
      </label>
      <label className="block">
        <span className={labelClass}>{t('design.properties.subtitle')}</span>
        <input className={fieldClass} value={spec.subtitle ?? ''} onChange={(event) => onEdit((held) => withPageWritten(held, { subtitle: event.target.value }))} />
      </label>
      <label className="block">
        <span className={labelClass}>{t('design.properties.refreshSeconds')}</span>
        <input
          className={fieldClass}
          type="number"
          min={0}
          value={spec.refreshSeconds ?? 0}
          onChange={(event) => onEdit((held) => withPageWritten(held, { refreshSeconds: Number(event.target.value) }))}
        />
      </label>
    </>
  );
}

function SectionFields({ spec, section, onEdit, onSelect }: { spec: DashboardSpec; section: number; onEdit: Props['onEdit']; onSelect: Props['onSelect'] }) {
  const { t } = useTranslation();
  const held = spec.sections[section];
  const move = (direction: 'earlier' | 'later') => {
    onEdit((current) => withSectionMoved(current, section, direction));
    onSelect({ on: 'section', section: direction === 'earlier' ? section - 1 : section + 1 });
  };
  return (
    <>
      <label className="block">
        <span className={labelClass}>{t('design.properties.title')}</span>
        <input className={fieldClass} value={held.title ?? ''} onChange={(event) => onEdit((current) => withSectionWritten(current, section, { title: event.target.value }))} />
      </label>
      <label className="block">
        <span className={labelClass}>{t('design.properties.hint')}</span>
        <input className={fieldClass} value={held.hint ?? ''} onChange={(event) => onEdit((current) => withSectionWritten(current, section, { hint: event.target.value }))} />
      </label>
      <div className="flex flex-wrap gap-2">
        <button type="button" className={buttonClass} disabled={section === 0} onClick={() => move('earlier')}>
          <ArrowUp size={12} />
          {t('design.properties.earlier')}
        </button>
        <button type="button" className={buttonClass} disabled={section === spec.sections.length - 1} onClick={() => move('later')}>
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

function WidgetFields({
  spec,
  section,
  widget,
  onEdit,
  onSelect,
}: {
  spec: DashboardSpec;
  section: number;
  widget: number;
  onEdit: Props['onEdit'];
  onSelect: Props['onSelect'];
}) {
  const { t } = useTranslation();
  const held = spec.sections[section].widgets[widget];
  const moveTo = (target: number) => {
    const size = held.placement ?? { width: 1, height: 1 };
    const placement = nextPlacement(spec.sections[target], size);
    onEdit((current) => withWidgetMoved(current, { section, widget }, target, placement));
    onSelect({ on: 'widget', section: target, widget: spec.sections[target].widgets.length });
  };
  return (
    <>
      <label className="block">
        <span className={labelClass}>{t('design.properties.title')}</span>
        <input
          className={fieldClass}
          value={held.title ?? ''}
          onChange={(event) => onEdit((current) => withWidgetReplaced(current, section, widget, { ...held, title: event.target.value }))}
        />
      </label>
      {spec.sections.length > 1 && (
        <label className="block">
          <span className={labelClass}>{t('design.properties.inSection')}</span>
          <select className={fieldClass} value={section} onChange={(event) => moveTo(Number(event.target.value))}>
            {spec.sections.map((candidate, index) => (
              <option key={index} value={index}>
                {candidate.title ?? t('design.canvas.untitledSection')}
              </option>
            ))}
          </select>
        </label>
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
