import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import { GripVertical, Plus } from 'lucide-react';
import { GridLayout } from 'react-grid-layout';
import 'react-grid-layout/css/styles.css';
import { GRID_COLUMNS, type DashboardSection, type DashboardSpecification, type Placement, type Widget } from '../../types/dashboard';
import type { ResolveContext } from '../../api/dashboardApi';
import { useElementWidth } from '../../hooks/useElementWidth';
import { DEFAULT_SIZE, GRID_GAP, GRID_ROW_HEIGHT } from '../../utils/gridLayout';
import { unboundSlots } from '../../utils/designSpec';
import { layoutItemsOf, placementsFromLayout, type DesignSelection, type GridItem } from '../../utils/designEdits';
import { WidgetRenderer } from '../dashboard/widgets/WidgetRenderer';

interface Props {
  specification: DashboardSpecification;
  selection: DesignSelection | null;
  dragging: Widget['type'] | null;
  context: ResolveContext;
  onSelect: (selection: DesignSelection) => void;
  onPlacements: (section: number, placements: Placement[]) => void;
  onDrop: (section: number, kind: Widget['type'], placement: Placement) => void;
  onAddSection: () => void;
}

const WIDTH_BEFORE_MEASURED = 1200;

/** The class the grid drags a widget by — its header, so a table's own scrolling and a button on a
 *  card keep working under the pointer. */
const DRAG_HANDLE = 'design-handle';

/**
 * The page as it is designed: every section a grid its widgets are moved and resized on, each widget
 * drawing live off the model, and the place a new widget is dropped.
 */
export function DesignCanvas({ specification, selection, dragging, context, onSelect, onPlacements, onDrop, onAddSection }: Props) {
  const { t } = useTranslation();
  return (
    <div className="flex-1 min-w-0 overflow-auto px-6 pb-10">
      {specification.sections.map((section, index) => (
        <DesignSection
          key={index}
          section={section}
          index={index}
          selection={selection}
          dragging={dragging}
          context={context}
          onSelect={onSelect}
          onPlacements={onPlacements}
          onDrop={onDrop}
        />
      ))}
      <button
        type="button"
        onClick={onAddSection}
        className="mt-6 inline-flex items-center gap-1 rounded-md border border-dashed border-zinc-300 px-3 py-1.5 text-xs text-zinc-600 hover:border-zinc-400 hover:text-zinc-900 dark:border-zinc-600 dark:text-zinc-400 dark:hover:text-zinc-100"
      >
        <Plus size={13} />
        {t('design.canvas.addSection')}
      </button>
    </div>
  );
}

function DesignSection({
  section,
  index,
  selection,
  dragging,
  context,
  onSelect,
  onPlacements,
  onDrop,
}: {
  section: DashboardSection;
  index: number;
  selection: DesignSelection | null;
  dragging: Widget['type'] | null;
  context: ResolveContext;
  onSelect: (selection: DesignSelection) => void;
  onPlacements: (section: number, placements: Placement[]) => void;
  onDrop: (section: number, kind: Widget['type'], placement: Placement) => void;
}) {
  const { t } = useTranslation();
  const [measured, width] = useElementWidth();
  const selected = selection?.on === 'section' && selection.section === index;
  const title = section.title ?? t('design.canvas.untitledSection');
  const droppingSize = dragging ? DEFAULT_SIZE[dragging] : { width: 1, height: 1 };

  return (
    <section className="mb-2">
      <div className="flex items-center gap-3 mt-6 mb-3">
        <button
          type="button"
          aria-label={t('design.canvas.selectSection', { title })}
          onClick={() => onSelect({ on: 'section', section: index })}
          className={clsx(
            'text-[12px] uppercase tracking-wider font-bold rounded px-1 -mx-1',
            selected ? 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-300' : 'text-zinc-400 dark:text-zinc-500 hover:text-zinc-700 dark:hover:text-zinc-200',
            !section.title && 'italic',
          )}
        >
          {title}
        </button>
        {section.hint && <span className="text-[11px] text-zinc-400 dark:text-zinc-500">{section.hint}</span>}
        <div className="flex-1 h-px bg-zinc-200 dark:bg-zinc-800" />
      </div>
      <div ref={measured} className={clsx('relative rounded-lg', dragging && 'outline-dashed outline-1 outline-blue-300')}>
        <GridLayout
          width={width || WIDTH_BEFORE_MEASURED}
          layout={layoutItemsOf(section)}
          gridConfig={{ cols: GRID_COLUMNS, rowHeight: GRID_ROW_HEIGHT, margin: [GRID_GAP, GRID_GAP], containerPadding: [0, 0] }}
          dragConfig={{ handle: `.${DRAG_HANDLE}` }}
          resizeConfig={{ handles: ['se'] }}
          dropConfig={{ enabled: dragging !== null, defaultItem: { w: droppingSize.width, h: droppingSize.height } }}
          onLayoutChange={(layout) => onPlacements(index, placementsFromLayout(layout as GridItem[], section.widgets.length))}
          onDrop={(_layout, item) => {
            if (item && dragging) onDrop(index, dragging, { column: item.x, row: item.y, width: item.w, height: item.h });
          }}
          className={clsx(section.widgets.length === 0 && 'min-h-24')}
        >
          {section.widgets.map((widget, at) => (
            <div key={String(at)}>
              <DesignWidget
                widget={widget}
                context={context}
                selected={selection?.on === 'widget' && selection.section === index && selection.widget === at}
                onSelect={() => onSelect({ on: 'widget', section: index, widget: at })}
              />
            </div>
          ))}
        </GridLayout>
        {section.widgets.length === 0 && (
          <p className="pointer-events-none absolute inset-0 flex items-center justify-center text-xs text-zinc-400 dark:text-zinc-500">
            {t('design.canvas.emptySection')}
          </p>
        )}
      </div>
    </section>
  );
}

function DesignWidget({
  widget,
  context,
  selected,
  onSelect,
}: {
  widget: Widget;
  context: ResolveContext;
  selected: boolean;
  onSelect: () => void;
}) {
  const { t } = useTranslation();
  const title = widget.title ?? t('design.canvas.untitledWidget');
  const waitingFor = unboundSlots(widget);

  return (
    <div
      className={clsx(
        'h-full flex flex-col rounded-lg border bg-zinc-50/60 dark:bg-zinc-900/40',
        selected ? 'border-blue-500 ring-2 ring-blue-500/40' : 'border-zinc-200 dark:border-zinc-700',
      )}
    >
      <div className={`${DRAG_HANDLE} flex items-center gap-1 px-1.5 py-0.5 cursor-move text-[10px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400`}>
        <GripVertical size={11} className="flex-shrink-0" />
        <button
          type="button"
          aria-label={t('design.canvas.selectWidget', { title })}
          onClick={onSelect}
          className="min-w-0 flex-1 truncate text-left hover:text-zinc-900 dark:hover:text-zinc-100"
        >
          {t(`design.palette.kind.${widget.type}`)} · {title}
        </button>
      </div>
      <div className="flex-1 min-h-0 overflow-auto px-1 pb-1">
        {waitingFor.length > 0 ? (
          <p className="h-full flex items-center justify-center text-xs text-zinc-400 dark:text-zinc-500">
            {t('design.canvas.waitingFor', { slots: waitingFor.join(', ') })}
          </p>
        ) : (
          <WidgetRenderer widget={widget} context={context} />
        )}
      </div>
    </div>
  );
}
