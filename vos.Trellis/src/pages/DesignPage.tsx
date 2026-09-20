import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Languages, Settings2, Trash2 } from 'lucide-react';
import type { DashboardSpec, Placement, Widget } from '../types/dashboard';
import { WHOLE_MODEL } from '../types/subscription';
import { brokerModelReads } from '../api/brokerModelReads';
import { discoverDashboardsFromIndex } from '../api/dashboardApi';
import { dashboardPages, dashboardWriteContext } from '../api/dashboardPages';
import { useModelStore } from '../stores/modelStore';
import { useModelIndex, useResolveContext } from '../hooks/useDashboard';
import { useSubscription } from '../hooks/useSse';
import { useDesign } from '../hooks/useDesign';
import { useStatesByKind } from '../hooks/useStatesByKind';
import { useEndpoints } from '../hooks/useEndpoints';
import { DEFAULT_SIZE } from '../utils/gridLayout';
import { emptyWidget, nextPlacement } from '../utils/designSpec';
import { readyToKeep } from '../utils/rowProperties';
import { checkDesign, refusalsIn, type DesignCheckContext } from '../utils/designFindings';
import { offersFor } from '../components/design/designOffers';
import type { BindingContext } from '../components/design/bindingContext';
import { withPlacements, withSectionAdded, withWidgetAdded } from '../utils/designEdits';
import { ConfirmDialog } from '../components/common/ConfirmDialog';
import { toast } from '../components/common/toastStore';
import { DesignPagesPanel, type DesignablePage } from '../components/design/DesignPagesPanel';
import { DesignPalette } from '../components/design/DesignPalette';
import { DesignCanvas } from '../components/design/DesignCanvas';
import { DesignPropertiesPanel } from '../components/design/DesignPropertiesPanel';
import { DesignFindingsBar } from '../components/design/DesignFindingsBar';
import { TranslationsPanel } from '../components/design/TranslationsPanel';

const DESIGN_PATH = '/design';

/**
 * Designing a page.
 *
 * A page is laid out on a grid from the palette every seeded page was drawn with, and kept as the
 * same Dashboard Thing a seeded page is. The pages the model holds stand on the left; the one opened
 * is edited on the canvas between the palette and the properties of whatever is selected. A seeded
 * page is opened as a copy and never written over; a page the console kept is edited in place. The
 * workbench is keyed on the page it opened, so opening another starts afresh.
 */
export function DesignPage() {
  const { t } = useTranslation();
  const { dashboardKey } = useParams();
  const navigate = useNavigate();
  useSubscription(WHOLE_MODEL);
  const loaded = useModelStore((state) => state.loaded);
  const modelIndex = useModelIndex();
  const pages = useMemo(
    () => discoverDashboardsFromIndex(modelIndex).filter((page): page is DesignablePage => page.spec !== null),
    [modelIndex],
  );
  const [started, setStarted] = useState<string | null>(null);
  const opened = dashboardKey ? pages.find((page) => page.routeKey === dashboardKey) : undefined;

  if (!loaded) return <Centered>{t('design.readingModel')}</Centered>;

  const workbenchKey = opened ? `page:${opened.id}` : started ? `new:${started}` : null;

  return (
    <div className="h-full flex">
      <DesignPagesPanel
        pages={pages}
        openedId={opened?.id ?? null}
        onOpen={(page) => {
          setStarted(null);
          navigate(`${DESIGN_PATH}/${page.routeKey}`);
        }}
        onStart={(name) => {
          setStarted(name);
          navigate(DESIGN_PATH);
        }}
      />
      {workbenchKey ? (
        <DesignWorkbench key={workbenchKey} opened={opened} started={started ?? undefined} pages={pages} />
      ) : (
        <Centered>{t('design.noPage')}</Centered>
      )}
    </div>
  );
}

const headerButtonClass =
  'inline-flex items-center gap-1 rounded-md border border-zinc-200 bg-white px-2 py-1 text-xs font-semibold text-zinc-600 hover:text-zinc-900 disabled:opacity-40 dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-400 dark:hover:text-zinc-100';
const headerFieldClass =
  'rounded-md border border-zinc-300 bg-white px-2 py-1 text-xs text-zinc-900 dark:border-zinc-600 dark:bg-zinc-800 dark:text-white';

function DesignWorkbench({ opened, started, pages }: { opened?: DesignablePage; started?: string; pages: DesignablePage[] }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const modelIndex = useModelIndex();
  const writeContext = useMemo(() => dashboardWriteContext(modelIndex), [modelIndex]);
  const { design, selection, select, edit, kept } = useDesign({ opened, started });
  const { spec, source, dirty } = design;
  const context = useResolveContext(modelIndex, null, spec.compare?.archetype, brokerModelReads);
  const statesOf = useStatesByKind(modelIndex);
  const endpoints = useEndpoints();
  const compareKind = spec.compare?.archetype;
  const offers = useMemo(() => offersFor(modelIndex, compareKind), [modelIndex, compareKind]);
  const bindingContext = useMemo<BindingContext>(
    () => ({ offers, statesOf: (kind) => statesOf(kind) ?? [], endpoints }),
    [offers, statesOf, endpoints],
  );
  const [dragging, setDragging] = useState<Widget['type'] | null>(null);
  const [name, setName] = useState('');
  const [removing, setRemoving] = useState(false);
  const [busy, setBusy] = useState(false);

  const trimmed = name.trim();
  const nameTaken = pages.some((page) => page.name === trimmed || page.spec.title === trimmed);
  const keepsInPlace = !source.seeded && source.id !== null;

  const checkContext = useMemo<DesignCheckContext>(() => {
    const isKind = (kind: string) => {
      const thing = modelIndex.byName.get(kind);
      return !!thing && modelIndex.archetypeIds.has(thing.Id);
    };
    return {
      isKind,
      isPredicate: (predicate) => modelIndex.predicateNameToId.has(predicate),
      statesOf,
      propertiesOf: (kind) => (isKind(kind) ? offers.propertiesOf(kind) : undefined),
      iconsTaken: new Set(pages.filter((page) => page.id !== source.id).map((page) => page.spec.icon).filter((icon): icon is string => !!icon)),
      compareKind,
    };
  }, [modelIndex, offers, statesOf, pages, source.id, compareKind]);
  const findings = useMemo(
    () => checkDesign(spec, keepsInPlace ? source.name : trimmed || spec.title, checkContext),
    [spec, keepsInPlace, source.name, trimmed, checkContext],
  );
  const refused = refusalsIn(findings).length > 0;

  const sectionForNew = () => (selection && selection.on !== 'page' && selection.on !== 'translations' ? selection.section : 0);

  const add = (kind: Widget['type']) => {
    const withRoom = spec.sections.length === 0 ? withSectionAdded(spec) : spec;
    const section = Math.min(sectionForNew(), withRoom.sections.length - 1);
    const at = withRoom.sections[section].widgets.length;
    const placement = nextPlacement(withRoom.sections[section], DEFAULT_SIZE[kind]);
    edit(() => withWidgetAdded(withRoom, section, emptyWidget(kind, t('design.canvas.untitledWidget')), placement));
    select({ on: 'widget', section, widget: at });
  };

  const drop = (section: number, kind: Widget['type'], placement: Placement) => {
    const at = spec.sections[section].widgets.length;
    edit((held) => withWidgetAdded(held, section, emptyWidget(kind, t('design.canvas.untitledWidget')), placement));
    select({ on: 'widget', section, widget: at });
    setDragging(null);
  };

  const failed = (error: unknown) => toast.error(error instanceof Error ? error.message : t('design.toast.failed'));

  const keep = async () => {
    if (!source.id) return;
    setBusy(true);
    try {
      const written = readyToKeep(spec);
      await dashboardPages.write(source.id, written);
      kept(source.id, source.name, written);
      toast.success(t('design.toast.written', { name: spec.title }));
    } catch (error) {
      failed(error);
    } finally {
      setBusy(false);
    }
  };

  const keepAs = async () => {
    if (!writeContext) {
      toast.error(t('design.toast.noPageKind'));
      return;
    }
    if (!trimmed || nameTaken) return;
    setBusy(true);
    try {
      const written: DashboardSpec = readyToKeep({ ...spec, title: trimmed });
      const id = await dashboardPages.keep(trimmed, written, writeContext);
      kept(id, trimmed, written);
      setName('');
      toast.success(t('design.toast.kept', { name: trimmed }));
    } catch (error) {
      failed(error);
    } finally {
      setBusy(false);
    }
  };

  const remove = async () => {
    setRemoving(false);
    if (!source.id || !writeContext) return;
    try {
      await dashboardPages.remove(source.id, writeContext);
      toast.success(t('design.toast.removed'));
      navigate(DESIGN_PATH);
    } catch (error) {
      failed(error);
    }
  };

  return (
    <>
      <DesignPalette
        onLift={(kind) => (event) => {
          event.dataTransfer?.setData('text/plain', '');
          setDragging(kind);
        }}
        onSettle={() => setDragging(null)}
        onAdd={add}
      />

      <section className="flex-1 min-w-0 flex flex-col bg-zinc-50 dark:bg-zinc-950">
        <header className="flex-shrink-0 flex items-center gap-3 flex-wrap px-6 pt-4 pb-2 border-b border-zinc-200 dark:border-zinc-800">
          <button
            type="button"
            aria-label={t('design.canvas.selectPage')}
            title={t('design.canvas.selectPage')}
            onClick={() => select({ on: 'page' })}
            className={headerButtonClass}
          >
            <Settings2 size={13} />
          </button>
          <button
            type="button"
            aria-label={t('design.canvas.translations')}
            title={t('design.canvas.translations')}
            onClick={() => select({ on: 'translations' })}
            className={headerButtonClass}
          >
            <Languages size={13} />
          </button>
          <h2 className="text-lg font-bold text-zinc-900 dark:text-white leading-tight">{spec.title}</h2>
          <span className="text-[10px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400">
            {source.seeded ? t('design.pages.seeded') : source.id ? t('design.pages.kept') : ''}
          </span>
          {dirty && <span className="text-[11px] text-amber-700 dark:text-amber-400">{t('design.unsaved')}</span>}

          <div className="ml-auto flex items-center gap-2 flex-wrap">
            {keepsInPlace ? (
              <>
                <button type="button" className={headerButtonClass} disabled={busy || !dirty || refused} title={refused ? t('design.findings.refused') : undefined} onClick={() => void keep()}>
                  {t('design.properties.keep')}
                </button>
                <button type="button" className={headerButtonClass} onClick={() => setRemoving(true)}>
                  <Trash2 size={13} />
                  {t('design.properties.removePage')}
                </button>
              </>
            ) : (
              <>
                <input
                  className={headerFieldClass}
                  aria-label={t('design.properties.pageName')}
                  placeholder={t('design.properties.pageNamePlaceholder')}
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                />
                <button type="button" className={headerButtonClass} disabled={busy || !trimmed || nameTaken || refused} title={refused ? t('design.findings.refused') : undefined} onClick={() => void keepAs()}>
                  {t('design.properties.keepAs')}
                </button>
              </>
            )}
          </div>
          {source.seeded && <p className="basis-full text-xs text-zinc-500 dark:text-zinc-400">{t('design.seededNotice')}</p>}
          {!keepsInPlace && nameTaken && (
            <p className="basis-full text-xs text-amber-700 dark:text-amber-400">{t('design.properties.nameTaken', { name: trimmed })}</p>
          )}
        </header>

        <DesignCanvas
          spec={spec}
          selection={selection}
          dragging={dragging}
          context={context}
          onSelect={select}
          onPlacements={(section, placements) => edit((held) => withPlacements(held, section, placements))}
          onDrop={drop}
          onAddSection={() => {
            edit((held) => withSectionAdded(held));
            select({ on: 'section', section: spec.sections.length });
          }}
        />
        <DesignFindingsBar findings={findings} onSelect={select} />
      </section>

      {selection?.on === 'translations' && <TranslationsPanel spec={spec} onEdit={edit} onClose={() => select(null)} />}
      {selection && selection.on !== 'translations' && <DesignPropertiesPanel spec={spec} selection={selection} context={bindingContext} onEdit={edit} onSelect={select} />}

      <ConfirmDialog
        open={removing}
        title={t('design.removeDialog.title')}
        message={t('design.removeDialog.message', { name: spec.title })}
        confirmLabel={t('design.removeDialog.confirm')}
        danger
        onConfirm={() => void remove()}
        onCancel={() => setRemoving(false)}
      />
    </>
  );
}

function Centered({ children }: { children: React.ReactNode }) {
  return <div className="flex-1 flex items-center justify-center p-8 text-sm text-zinc-500 dark:text-zinc-400">{children}</div>;
}
