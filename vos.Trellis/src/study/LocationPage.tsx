import { lazy, Suspense, useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { intakeApi, type FormOptions, type RegisteredPlot, type ViewBox } from '../api/intakeApi';
import type { BorderEdit } from '../components/map/borderEdit';
import { enclosesLand, type BoundaryPoint } from '../utils/parcelGeometry';
import {
  applyBorderEdit,
  finishDrawing,
  finishEditing,
  startDrawing,
  startEditing,
  undoCorner,
  type BorderDrawing,
  type DrawingStep,
} from './borderDrawing';
import {
  borderCleared,
  borderDrawn,
  borderImported,
  plotChosen,
  siteArea,
  siteConfirmable,
  siteDocument,
  siteFound,
  type LocationSite,
} from './locationSite';
import { searchOnSubmit } from './placeSearch';
import type { StudySession } from './SignIn';
import { coordinatesText, elevationText, hectaresText, type Elevation } from './siteFacts';

const MapView = lazy(() => import('../components/map/MapView').then((module) => ({ default: module.MapView })));

const STUDY_BORDER_COLOURS = { outline: '#a9413d', fill: '#f08a24', fillOpacity: 0.45 };
const OPENING_VIEW: BoundaryPoint = { latitude: 24, longitude: 47 };
const OPENING_ZOOM = 5;
const SITE_ZOOM = 15;
/** Register plots are small enough that farther out they would cover the map. */
const PLOTS_FROM_ZOOM = 15;
const PLOTS_AFTER_REST_MILLISECONDS = 250;
const PARCEL_FILE_TYPES = '.kml,.kmz,.geojson,.json,.gpx';

type ElevationLookup = { readonly kind: 'loading' } | { readonly kind: 'found'; readonly elevation: Elevation } | { readonly kind: 'none' };

export function LocationPage({
  session,
  site,
  onSiteChange,
  onConfirmed,
}: {
  session: StudySession;
  site: LocationSite | null;
  onSiteChange: (site: LocationSite) => void;
  onConfirmed: (submissionId: string) => void;
}) {
  const { t } = useTranslation();
  // Renewed from every answer the intake gives, so each request carries the newest.
  const ticket = useRef(session.ticket);
  const [form, setForm] = useState<FormOptions | null>(null);
  const [refusal, setRefusal] = useState<string | null>(null);

  useEffect(() => {
    let live = true;
    intakeApi.formOptions().then(
      (loaded) => live && setForm(loaded),
      (error: unknown) => live && setRefusal(messageOf(error)),
    );
    return () => {
      live = false;
    };
  }, []);

  const [query, setQuery] = useState('');
  const [searchNotice, setSearchNotice] = useState<string | null>(null);
  const lastSearchAt = useRef<number | null>(null);
  const [drawing, setDrawing] = useState<BorderDrawing | null>(null);
  const [crossingRefused, setCrossingRefused] = useState(false);
  const [fileNotice, setFileNotice] = useState<string | null>(null);
  const [importedFile, setImportedFile] = useState<File | null>(null);
  const fileInput = useRef<HTMLInputElement | null>(null);
  const [choosingPlot, setChoosingPlot] = useState(false);
  const [plots, setPlots] = useState<readonly RegisteredPlot[]>([]);
  const [noPlotRegister, setNoPlotRegister] = useState(false);
  const lastView = useRef<{ view: ViewBox; zoom: number } | null>(null);
  const plotsTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const [confirming, setConfirming] = useState(false);

  useEffect(() => () => clearTimeout(plotsTimer.current), []);

  function choose(chosen: LocationSite) {
    setDrawing(null);
    setChoosingPlot(false);
    setPlots([]);
    if (chosen.address !== null) setQuery(chosen.address);
    onSiteChange(chosen);
  }

  async function find() {
    const decision = searchOnSubmit({
      text: query,
      now: Date.now(),
      lastSearchAt: lastSearchAt.current,
      currentAddress: site?.address ?? null,
    });
    if (decision.kind === 'empty') return setSearchNotice(t('study.location.searchEmpty'));
    if (decision.kind === 'unchanged') return;
    if (decision.kind === 'position') {
      setSearchNotice(null);
      return choose(siteFound(null, { latitude: decision.latitude, longitude: decision.longitude }));
    }
    setSearchNotice(t('study.location.searching'));
    await new Promise((resolve) => setTimeout(resolve, decision.afterMilliseconds));
    lastSearchAt.current = Date.now();
    try {
      const { places, ticket: renewed } = await intakeApi.searchPlaces(decision.query, ticket.current);
      ticket.current = renewed;
      const first = places[0];
      if (first === undefined) return setSearchNotice(t('study.location.searchNoMatch'));
      setSearchNotice(null);
      choose(siteFound(first.name, { latitude: first.latitude, longitude: first.longitude }));
    } catch {
      setSearchNotice(t('study.location.searchUnavailable'));
    }
  }

  function step(result: DrawingStep) {
    if (result.kind === 'tooFewCorners') return;
    setCrossingRefused(result.kind === 'crosses');
    if (result.kind === 'crosses') return;
    if (result.drawing.mode === 'done') finish(result.drawing);
    else setDrawing(result.drawing);
  }

  function finish(done: BorderDrawing) {
    setDrawing(null);
    if (site !== null && done.corners !== site.border) onSiteChange(borderDrawn(site, done.corners));
  }

  function done() {
    if (drawing === null) return;
    if (drawing.mode === 'drawing') step(finishDrawing(drawing));
    else finish(finishEditing(drawing));
  }

  function undo() {
    if (drawing?.mode !== 'drawing') return;
    setCrossingRefused(false);
    setDrawing(undoCorner(drawing));
  }

  useEffect(() => {
    if (drawing === null) return;
    const keyed = (event: KeyboardEvent) => {
      if (event.target instanceof HTMLInputElement) return;
      if (event.key === 'Backspace') undo();
      else if (event.key === 'Enter') done();
      else if (event.key === 'Escape') {
        if (drawing.mode === 'drawing') setDrawing(null);
        else done();
      }
    };
    window.addEventListener('keydown', keyed);
    return () => window.removeEventListener('keydown', keyed);
  });

  async function importFile(file: File) {
    setFileNotice(null);
    try {
      const { largestArea, ticket: renewed } = await intakeApi.readFile(file, session.emailAddress, ticket.current);
      ticket.current = renewed;
      if (largestArea === null) return setFileNotice(t('study.location.noPolygon'));
      setImportedFile(file);
      choose(borderImported(site, largestArea));
    } catch (error) {
      setFileNotice(t('study.location.fileUnreadable', { reason: messageOf(error) }));
    }
  }

  function askForPlots(view: ViewBox, zoom: number) {
    clearTimeout(plotsTimer.current);
    if (zoom < PLOTS_FROM_ZOOM) return setPlots([]);
    plotsTimer.current = setTimeout(async () => {
      try {
        const answered = await intakeApi.plotsInView(view, zoom, ticket.current);
        ticket.current = answered.ticket;
        if (answered.plots === null) {
          setNoPlotRegister(true);
          setChoosingPlot(false);
          return;
        }
        setPlots(answered.plots);
      } catch (error) {
        setRefusal(messageOf(error));
      }
    }, PLOTS_AFTER_REST_MILLISECONDS);
  }

  function viewSettled(view: ViewBox, zoom: number) {
    lastView.current = { view, zoom };
    if (choosingPlot) askForPlots(view, zoom);
  }

  function startChoosingPlot() {
    setChoosingPlot(true);
    if (lastView.current !== null) askForPlots(lastView.current.view, lastView.current.zoom);
  }

  const [elevation, setElevation] = useState<{ border: readonly BoundaryPoint[]; lookup: ElevationLookup } | null>(null);
  const border = site?.border;
  useEffect(() => {
    if (border === undefined || !enclosesLand(border)) return;
    let live = true;
    intakeApi.borderElevation(border, ticket.current).then(
      ({ elevation: found, ticket: renewed }) => {
        ticket.current = renewed;
        if (live) setElevation({ border, lookup: found === null ? { kind: 'none' } : { kind: 'found', elevation: found } });
      },
      () => live && setElevation({ border, lookup: { kind: 'none' } }),
    );
    return () => {
      live = false;
    };
  }, [border]);
  const elevationShown: ElevationLookup | null =
    border === undefined || !enclosesLand(border)
      ? null
      : elevation?.border === border
        ? elevation.lookup
        : { kind: 'loading' };

  async function confirm() {
    if (site === null || !siteConfirmable(site)) return;
    setConfirming(true);
    setRefusal(null);
    try {
      const submissionId = crypto.randomUUID();
      const posted = await intakeApi.submitWithTicket(
        siteDocument(site, session, submissionId, { boundarySources: form?.boundarySources ?? {} }),
        ticket.current,
      );
      ticket.current = posted.ticket;
      if (importedFile !== null) ticket.current = await intakeApi.sendDocument(submissionId, importedFile, ticket.current);
      onConfirmed(submissionId);
    } catch (error) {
      setRefusal(messageOf(error));
    } finally {
      setConfirming(false);
    }
  }

  const shownBorder = drawing?.corners ?? site?.border;
  const measured = site !== null && drawing !== null && drawing.corners !== site.border ? borderDrawn(site, drawing.corners) : site;
  const centre = site?.position ?? OPENING_VIEW;
  const hasBorder = site !== null && enclosesLand(site.border);
  const plotsOffered = form?.parcelLookup === true && !noPlotRegister;

  return (
    <div className="relative h-full min-h-[32rem] flex-1">
      <div className="absolute inset-0">
        <Suspense fallback={null}>
          <MapView
            latitude={centre.latitude}
            longitude={centre.longitude}
            sources={form?.basemapSources ?? []}
            initialZoom={OPENING_ZOOM}
            focusZoom={SITE_ZOOM}
            boundary={shownBorder}
            boundaryLabel={drawing === null && site !== null ? borderLabel(site) : undefined}
            borderColours={STUDY_BORDER_COLOURS}
            borderEditing={
              drawing === null
                ? undefined
                : {
                    placesCorners: drawing.mode === 'drawing',
                    movesCorners: drawing.mode === 'editing',
                    onEdit: (edit: BorderEdit) => step(applyBorderEdit(drawing, edit)),
                  }
            }
            plots={choosingPlot && plots.length > 0 ? plots.map((plot) => plot.boundary) : undefined}
            onPlotPress={(index) => choose(plotChosen(site, plots[index]))}
            onViewSettle={viewSettled}
            showMarker={site !== null && !enclosesLand(shownBorder ?? [])}
          />
        </Suspense>
      </div>

      <section className="absolute inset-x-0 bottom-0 max-h-[60%] overflow-auto border-t border-zinc-200 bg-white/95 px-6 py-4 text-sm dark:border-zinc-800 dark:bg-zinc-900/95">
        <h1 className="text-lg font-bold">{t('study.steps.location')}</h1>
        {site === null && <p className="mt-1">{t('study.location.findSentence')}</p>}

        <form
          role="search"
          className="mt-2 flex gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            void find();
          }}
        >
          <input
            type="search"
            aria-label={t('study.location.searchLabel')}
            placeholder={t('study.location.searchPlaceholder')}
            value={query}
            onChange={(event) => {
              setQuery(event.target.value);
              setSearchNotice(null);
            }}
            className="min-w-0 flex-1 rounded-md border border-zinc-300 px-2 py-1 dark:border-zinc-700 dark:bg-zinc-800"
          />
          <button type="submit" className="rounded-md bg-zinc-800 px-3 py-1 text-white">
            {t('study.location.find')}
          </button>
        </form>
        {searchNotice !== null && (
          <p role="status" className="mt-1 text-zinc-600">
            {searchNotice}
          </p>
        )}

        <input
          ref={fileInput}
          type="file"
          accept={PARCEL_FILE_TYPES}
          aria-label={t('study.location.parcelFile')}
          className="sr-only"
          onChange={(event) => {
            const file = event.target.files?.[0];
            event.target.value = '';
            if (file !== undefined) void importFile(file);
          }}
        />
        {site === null && (
          <p className="mt-3 flex items-center gap-2">
            {t('study.location.startFromFile')}
            <ActionButton onClick={() => fileInput.current?.click()}>{t('study.location.importFile')}</ActionButton>
          </p>
        )}
        {fileNotice !== null && (
          <p role="status" className="mt-1 text-red-700">
            {fileNotice}
          </p>
        )}

        {measured !== null && (
          <dl className="mt-3 grid grid-cols-2 gap-x-6 gap-y-1 sm:grid-cols-4">
            <Fact label={t('study.location.address')}>{measured.address ?? t('study.location.none')}</Fact>
            <Fact label={t('study.location.siteArea')}>{areaText(measured)}</Fact>
            <Fact label={t('study.location.coordinates')}>{coordinatesText(measured.position)}</Fact>
            <Fact label={t('study.location.elevation')}>{elevationWording(elevationShown)}</Fact>
          </dl>
        )}

        {drawing !== null && (
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <span className="text-zinc-600">
              {drawing.mode === 'drawing' ? t('study.location.drawingHint') : t('study.location.editingHint')}
            </span>
            {drawing.mode === 'drawing' && <ActionButton onClick={undo}>{t('study.location.undoCorner')}</ActionButton>}
            <ActionButton disabled={drawing.mode === 'drawing' && !enclosesLand(drawing.corners)} onClick={done}>
              {t('study.location.done')}
            </ActionButton>
          </div>
        )}
        {crossingRefused && (
          <p role="alert" className="mt-1 text-red-700">
            {t('study.location.bordersCannotCross')}
          </p>
        )}

        {site !== null && drawing === null && (
          <div className="mt-3 flex flex-wrap gap-2">
            {hasBorder ? (
              <>
                <ActionButton onClick={() => setDrawing(startDrawing())}>{t('study.location.redraw')}</ActionButton>
                <ActionButton onClick={() => setDrawing(startEditing({ mode: 'done', corners: site.border }))}>
                  {t('study.location.editBorder')}
                </ActionButton>
                <ActionButton onClick={() => onSiteChange(borderCleared(site))}>{t('study.location.clear')}</ActionButton>
              </>
            ) : (
              <ActionButton onClick={() => setDrawing(startDrawing())}>{t('study.location.drawBorder')}</ActionButton>
            )}
            <ActionButton
              disabled={!plotsOffered}
              title={plotsOffered ? undefined : t('study.location.plotsComingSoon')}
              onClick={startChoosingPlot}
            >
              {t('study.location.choosePlot')}
            </ActionButton>
            <ActionButton onClick={() => fileInput.current?.click()}>{t('study.location.importFile')}</ActionButton>
            <ActionButton disabled={!hasBorder || confirming} onClick={() => void confirm()}>
              {t('study.location.confirmSite')}
            </ActionButton>
          </div>
        )}
        {refusal !== null && (
          <p role="alert" className="mt-2 text-red-700">
            {refusal}
          </p>
        )}
      </section>
    </div>
  );

  function areaText(shown: LocationSite): string {
    const area = siteArea(shown, form?.areaMethod ?? null);
    if (area.kind === 'unknown') return t('study.location.none');
    const hectares = hectaresText(area.hectares);
    if (area.kind === 'register') return t('study.location.areaFromRegister', { hectares, label: area.label ?? t('study.location.plot') });
    return t(area.kind === 'drawn' ? 'study.location.areaDrawn' : 'study.location.areaFromFile', { hectares });
  }

  function borderLabel(shown: LocationSite): string {
    const name = shown.parcelName ?? shown.plotLabel ?? t('study.location.siteLabel');
    const area = siteArea(shown, form?.areaMethod ?? null);
    return area.kind === 'unknown' ? name : t('study.location.borderLabel', { name, hectares: hectaresText(area.hectares) });
  }

  function elevationWording(lookup: ElevationLookup | null): string {
    if (lookup === null || lookup.kind === 'none') return t('study.location.none');
    if (lookup.kind === 'loading') return t('study.location.loading');
    const { site: siteMetres, range } = elevationText(lookup.elevation);
    return range === null
      ? t('study.location.elevationMetres', { metres: siteMetres })
      : t('study.location.elevationWithRange', { metres: siteMetres, range });
  }
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  const id = useId();
  return (
    <div>
      <dt id={id} className="text-xs text-zinc-500">
        {label}
      </dt>
      <dd aria-labelledby={id}>{children}</dd>
    </div>
  );
}

function ActionButton({
  children,
  onClick,
  disabled = false,
  title,
}: {
  children: ReactNode;
  onClick: () => void;
  disabled?: boolean;
  title?: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      title={title}
      className="rounded-md border border-zinc-300 px-3 py-1 disabled:opacity-40 dark:border-zinc-700"
    >
      {children}
    </button>
  );
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
