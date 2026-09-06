/**
 * The plot-first way into land intake, beside the guided form and from the same build: start at a map,
 * click your land, and land on the report — where the programme, the population and the household size
 * are dials whose change re-posts the same submission and re-reads the findings.
 *
 * The page holds no credential and reaches only the intake service, like the guided form; the
 * import-walk test in `../publicForm/noSignedInCode.test.ts` holds that true. The report renders the
 * model's own dashboard through the same widgets the findings page renders, so a figure added there
 * appears here with no change.
 */
import { Suspense, lazy, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Compass } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { findingsApi } from '../api/findingsApi';
import { localizeSpec } from '../api/dashboardLocalization';
import { intakeApi, type FormOptions, type FoundPlace } from '../api/intakeApi';
import { LanguageSwitcher } from '../components/common/LanguageSwitcher';
import { ToastContainer } from '../components/common/Toast';
import { toast } from '../components/common/toastStore';
import { DashboardSections } from '../components/dashboard/DashboardSections';
import { useElementWidth } from '../hooks/useElementWidth';
import { useResolveContext } from '../hooks/useDashboard';
import { useStandalonePageDocument } from '../hooks/useStandalonePageDocument';
import { withShareSet, wholePercentages } from '../intake/submissionDraft';
import { findingsFrom, type Findings } from '../publicFindings/answeredFindings';
import { sphericalAreaHectares, type BoundaryPoint } from '../utils/parcelGeometry';
import {
  documentFrom,
  emptyExplore,
  landDescribed,
  readyToSubmit,
  seededShares,
  withBoundaryCleared,
  withDrawnBoundary,
  withFetchedBoundary,
  withPosition,
  type ExploreState,
} from './exploreState';

const MapView = lazy(() => import('../components/map/MapView').then((m) => ({ default: m.MapView })));

/** The whole world, which is where the map opens: the position comes from the map here, not the map
 *  from the position. */
const WORLD = { latitude: 20, longitude: 0, zoom: 1.6 };

/** Close enough to see one landholding once a position is picked. */
const PLOT_ZOOM = 16;

/** How the report keeps up with discovery without a person pressing anything: a few re-reads, spaced
 *  wide enough that a whole session stays far inside the request budget the service allows a source. */
const POLLS_AFTER_SUBMIT = 4;
const POLL_GAP_MILLISECONDS = 8_000;

/** One re-post per pause in the turning, not one per pixel of slider travel. */
const DIAL_SETTLE_MILLISECONDS = 800;

const WIDE = 720;

export function ExplorePage() {
  const { t } = useTranslation();
  useStandalonePageDocument();

  const [options, setOptions] = useState<FormOptions | null>(null);
  const [unreachable, setUnreachable] = useState(false);
  const [state, setState] = useState<ExploreState>(() => emptyExplore(crypto.randomUUID()));
  const [fetchingParcel, setFetchingParcel] = useState(false);
  const [awaitingCode, setAwaitingCode] = useState(false);
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [session, setSession] = useState<{ reference: string } | null>(null);
  const [findings, setFindings] = useState<Findings | null>(null);
  const [expired, setExpired] = useState(false);

  // The ticket outlives many renders and every renewal must win over whatever a stale closure held,
  // so it lives in a ref rather than in state nothing draws.
  const ticket = useRef<string | null>(null);

  // What the dial effect and the polls read, so neither re-posts a state a render has already left.
  // Synced in an effect declared before every effect that reads it, so each sees the settled render.
  const stateRef = useRef(state);
  useEffect(() => {
    stateRef.current = state;
  });

  useEffect(() => {
    let abandoned = false;
    intakeApi
      .formOptions()
      .then((answered) => {
        if (abandoned) return;
        setOptions(answered);
        setState((current) =>
          Object.keys(current.shares).length === 0
            ? { ...current, shares: seededShares(answered) }
            : current);
      })
      .catch(() => {
        if (!abandoned) setUnreachable(true);
      });
    return () => {
      abandoned = true;
    };
  }, []);

  const pick = useCallback((position: BoundaryPoint) => {
    setState((current) => withPosition(current, position));
    setFetchingParcel(true);
    intakeApi
      .parcelAt(position.latitude, position.longitude)
      .then((parcel) => {
        setState((current) =>
          current.position === null
          || current.position.latitude !== position.latitude
          || current.position.longitude !== position.longitude
            ? current
            : parcel && parcel.boundary.length >= 3
              ? withFetchedBoundary(current, parcel.boundary, parcel.attribution)
              : current);
      })
      .catch((error: unknown) =>
        toast.error(t('explore.refused', { reason: reasonOf(error) })))
      .finally(() => setFetchingParcel(false));
  }, [t]);

  const useMyLocation = useCallback(() => {
    navigator.geolocation?.getCurrentPosition(
      (found) => pick({ latitude: found.coords.latitude, longitude: found.coords.longitude }),
      () => toast.error(t('explore.geolocationRefused')),
    );
  }, [pick, t]);

  async function askForCode(): Promise<void> {
    setBusy(true);
    try {
      await intakeApi.askForCode(state.emailAddress.trim());
      setAwaitingCode(true);
      toast.success(t('explore.codeSent', { address: state.emailAddress.trim() }));
    } catch (error) {
      toast.error(t('explore.refused', { reason: reasonOf(error) }));
    } finally {
      setBusy(false);
    }
  }

  async function submit(): Promise<void> {
    setBusy(true);
    try {
      const first = await intakeApi.exchangeTicket(state.emailAddress.trim(), code.trim());
      const posted = await intakeApi.submitWithTicket(documentFrom(state), first);
      ticket.current = posted.ticket;
      setSession({ reference: posted.accepted.reference });
      setAwaitingCode(false);
      setCode('');
      await readFindings();
    } catch (error) {
      toast.error(t('explore.refused', { reason: reasonOf(error) }));
    } finally {
      setBusy(false);
    }
  }

  const readFindings = useCallback(async (): Promise<void> => {
    const held = ticket.current;
    if (held === null) return;
    const read = await findingsApi.readWithTicket(
      stateRef.current.submissionId, stateRef.current.emailAddress.trim(), held);
    ticket.current = read.ticket;
    setFindings(findingsFrom(read.findings));
  }, []);

  // The report replaces the two sections above it, and a page left where the claim step had scrolled to
  // opens partway down its own report — past the figures it leads with, which reads as a report with
  // nothing in it.
  useEffect(() => {
    if (session !== null) window.scrollTo({ top: 0, behavior: 'smooth' });
  }, [session]);

  // Discovery answers within a minute of the submission landing; a few spaced re-reads let the report
  // fill in as it does, and the refresh button covers a discovery slower than the last poll.
  useEffect(() => {
    if (session === null) return;
    let stopped = false;
    let polled = 0;
    const poll = () => {
      if (stopped || polled >= POLLS_AFTER_SUBMIT) return;
      polled += 1;
      readFindings().catch(() => undefined);
      timer = window.setTimeout(poll, POLL_GAP_MILLISECONDS);
    };
    let timer = window.setTimeout(poll, POLL_GAP_MILLISECONDS);
    return () => {
      stopped = true;
      window.clearTimeout(timer);
    };
  }, [session, readFindings]);

  // The dials: a settled change re-posts the same document under the same identifier — the fragment
  // upserts, every figure recomputes, and the re-read shows what the model now says. What counts as a
  // change is the dialled values spelled out, so a render that moved nothing posts nothing.
  const dialled = JSON.stringify(
    [state.population, state.householdSize, state.shares, state.reportedHazards]);
  const posted = useRef<string | null>(null);
  useEffect(() => {
    if (session === null) return;
    if (posted.current === null) {
      // The submission that opened the report already carried this state.
      posted.current = dialled;
      return;
    }
    if (posted.current === dialled) return;
    posted.current = dialled;

    const timer = window.setTimeout(async () => {
      const held = ticket.current;
      if (held === null) return;
      try {
        const posted = await intakeApi.submitWithTicket(documentFrom(stateRef.current), held);
        ticket.current = posted.ticket;
        await readFindings();
      } catch (error) {
        // The one refusal a person can mend from here is a ticket that aged out while they thought.
        if (isTicketRefusal(error)) setExpired(true);
        else toast.error(t('explore.refused', { reason: reasonOf(error) }));
      }
    }, DIAL_SETTLE_MILLISECONDS);
    return () => window.clearTimeout(timer);
  }, [session, dialled, readFindings, t]);

  async function resume(): Promise<void> {
    setBusy(true);
    try {
      ticket.current = await intakeApi.exchangeTicket(state.emailAddress.trim(), code.trim());
      setExpired(false);
      setCode('');
      await readFindings();
    } catch (error) {
      toast.error(t('explore.refused', { reason: reasonOf(error) }));
    } finally {
      setBusy(false);
    }
  }

  const drawnArea = state.boundary.length >= 3 ? sphericalAreaHectares(state.boundary) : null;

  return (
    <div className="min-h-screen bg-zinc-50 dark:bg-zinc-950">
      <header className="max-w-5xl mx-auto flex items-center gap-3 px-6 pt-10 pb-4">
        <div className="w-9 h-9 rounded-lg grid place-items-center text-white bg-gradient-to-br from-emerald-600 to-teal-500">
          <Compass size={18} />
        </div>
        <div className="flex-1">
          <h1 className="text-xl font-bold text-zinc-900 dark:text-white leading-tight">
            {t('explore.title')}
          </h1>
          <p className="text-xs text-zinc-500 dark:text-zinc-400">{t('explore.intro')}</p>
        </div>
        <LanguageSwitcher />
      </header>

      <main className="max-w-5xl mx-auto px-6 pb-16">
        {unreachable && (
          <p className="mb-4 text-sm text-amber-600 dark:text-amber-400">{t('explore.unreachable')}</p>
        )}
        {!intakeApi.configured() && (
          <p className="mb-4 text-sm text-amber-600 dark:text-amber-400">{t('explore.notConfigured')}</p>
        )}

        {session === null ? (
          <>
            <PlotStep
              options={options}
              state={state}
              fetchingParcel={fetchingParcel}
              drawnArea={drawnArea}
              onPick={pick}
              onUseMyLocation={useMyLocation}
              onBoundaryDrawn={(boundary) => setState((s) => withDrawnBoundary(s, boundary))}
              onBoundaryCleared={() => setState(withBoundaryCleared)}
            />
            {landDescribed(state) && (
              <ClaimStep
                state={state}
                busy={busy}
                awaitingCode={awaitingCode}
                code={code}
                onChange={(patch) => {
                  if (patch.emailAddress !== undefined) {
                    setAwaitingCode(false);
                    setCode('');
                  }
                  setState((s) => ({ ...s, ...patch }));
                }}
                onCodeChange={setCode}
                onSubmit={() => void (awaitingCode ? submit() : askForCode())}
              />
            )}
            <p className="mt-6 text-[11px] text-zinc-400 dark:text-zinc-500">
              <a className="hover:underline" href="./index.html">{t('explore.tryGuided')}</a>
            </p>
          </>
        ) : (
          <ReportStep
            options={options}
            state={state}
            reference={session.reference}
            findings={findings}
            expired={expired}
            busy={busy}
            code={code}
            onCodeChange={setCode}
            onResumeAskForCode={() => void askForCode()}
            onResume={() => void resume()}
            onRefresh={() => void readFindings().catch(() => undefined)}
            onDial={(patch) => setState((s) => ({ ...s, ...patch }))}
          />
        )}
      </main>

      <ToastContainer />
    </div>
  );
}

function reasonOf(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

/** Whether a refusal is the ticket's rather than the submission's — the wording the service uses for
 *  every ticket refusal invites re-verification, which is the one mend a person can make here. */
function isTicketRefusal(error: unknown): boolean {
  return error instanceof Error && error.message.includes('Verify the address again');
}

function PlotStep({
  options,
  state,
  fetchingParcel,
  drawnArea,
  onPick,
  onUseMyLocation,
  onBoundaryDrawn,
  onBoundaryCleared,
}: {
  options: FormOptions | null;
  state: ExploreState;
  fetchingParcel: boolean;
  drawnArea: number | null;
  onPick: (position: BoundaryPoint) => void;
  onUseMyLocation: () => void;
  onBoundaryDrawn: (boundary: BoundaryPoint[]) => void;
  onBoundaryCleared: () => void;
}) {
  const { t, i18n } = useTranslation();
  const area = useMemo(
    () => new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 2 }),
    [i18n.language],
  );
  const picked = state.position !== null;
  const centre = state.position ?? { latitude: WORLD.latitude, longitude: WORLD.longitude };

  return (
    <section className="bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-800 rounded-lg p-5">
      <h2 className="text-base font-semibold text-zinc-900 dark:text-zinc-100">{t('explore.plotTitle')}</h2>
      <p className="mb-3 text-xs text-zinc-400 dark:text-zinc-500">
        {picked
          ? state.boundary.length > 0
            ? t('explore.adjustHint')
            : t('explore.drawHint')
          : t('explore.pickHint')}
      </p>

      <div className="mb-3 flex flex-wrap items-center gap-2">
        {options?.placeSearch && <PlaceSearch onPick={onPick} />}
        <button
          type="button"
          onClick={onUseMyLocation}
          className="px-3 py-1.5 text-sm rounded-md bg-zinc-200 dark:bg-zinc-700 text-zinc-900 dark:text-zinc-100 hover:bg-zinc-300 dark:hover:bg-zinc-600"
        >
          {t('explore.useMyLocation')}
        </button>
        {picked && state.boundary.length > 0 && (
          <button
            type="button"
            onClick={onBoundaryCleared}
            className="px-3 py-1.5 text-sm rounded-md bg-zinc-200 dark:bg-zinc-700 text-zinc-900 dark:text-zinc-100 hover:bg-zinc-300 dark:hover:bg-zinc-600"
          >
            {t('explore.clearBoundary')}
          </button>
        )}
      </div>

      <div className="h-96 overflow-hidden rounded-md border border-zinc-200 dark:border-zinc-700">
        <Suspense fallback={null}>
          <MapView
            latitude={centre.latitude}
            longitude={centre.longitude}
            sources={options?.basemapSources ?? []}
            initialZoom={picked ? PLOT_ZOOM : WORLD.zoom}
            focusZoom={PLOT_ZOOM}
            showMarker={picked}
            boundary={picked ? state.boundary : undefined}
            onBoundaryChange={picked ? onBoundaryDrawn : undefined}
            onPositionPick={picked ? undefined : onPick}
          />
        </Suspense>
      </div>

      <div className="mt-2 text-sm text-zinc-700 dark:text-zinc-200">
        {fetchingParcel && <p className="text-zinc-400 dark:text-zinc-500">{t('explore.fetchingParcel')}</p>}
        {!fetchingParcel && picked && state.boundarySource === 'fetched-from-register' && (
          <p className="text-emerald-600 dark:text-emerald-400">
            {t('explore.parcelFound')}
            {state.boundaryAttribution && (
              <span className="text-zinc-400 dark:text-zinc-500"> — {state.boundaryAttribution}</span>
            )}
          </p>
        )}
        {drawnArea !== null && (
          <p className="tabular-nums">{t('explore.areaReadout', { value: area.format(drawnArea) })}</p>
        )}
      </div>
    </section>
  );
}

function PlaceSearch({ onPick }: { onPick: (position: BoundaryPoint) => void }) {
  const { t } = useTranslation();
  const [typed, setTyped] = useState('');
  const [found, setFound] = useState<FoundPlace[] | null>(null);
  const [searching, setSearching] = useState(false);

  async function search(): Promise<void> {
    if (typed.trim().length === 0) return;
    setSearching(true);
    try {
      setFound(await intakeApi.searchPlaces(typed.trim()));
    } catch (error) {
      toast.error(t('explore.refused', { reason: reasonOf(error) }));
    } finally {
      setSearching(false);
    }
  }

  return (
    <div className="relative flex items-center gap-2">
      <input
        value={typed}
        onChange={(event) => setTyped(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Enter') void search();
        }}
        placeholder={t('explore.searchPlaceholder')}
        aria-label={t('explore.searchPlaceholder')}
        className="w-64 px-2 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100"
      />
      <button
        type="button"
        onClick={() => void search()}
        disabled={searching || typed.trim().length === 0}
        className="px-3 py-1.5 text-sm rounded-md bg-emerald-600 hover:bg-emerald-700 text-white disabled:opacity-40"
      >
        {t('explore.searchButton')}
      </button>
      {found !== null && (
        <ul className="absolute top-full left-0 z-10 mt-1 w-80 rounded-md border border-zinc-200 dark:border-zinc-700 bg-white dark:bg-zinc-800 shadow">
          {found.length === 0 && (
            <li className="px-3 py-2 text-sm text-zinc-500 dark:text-zinc-400">{t('explore.noPlaces')}</li>
          )}
          {found.map((place) => (
            <li key={`${place.name}:${place.latitude}:${place.longitude}`}>
              <button
                type="button"
                onClick={() => {
                  setFound(null);
                  onPick({ latitude: place.latitude, longitude: place.longitude });
                }}
                className="w-full px-3 py-2 text-left text-sm text-zinc-700 dark:text-zinc-200 hover:bg-zinc-100 dark:hover:bg-zinc-700"
              >
                {place.name}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function ClaimStep({
  state,
  busy,
  awaitingCode,
  code,
  onChange,
  onCodeChange,
  onSubmit,
}: {
  state: ExploreState;
  busy: boolean;
  awaitingCode: boolean;
  code: string;
  onChange: (patch: Partial<ExploreState>) => void;
  onCodeChange: (code: string) => void;
  onSubmit: () => void;
}) {
  const { t } = useTranslation();
  const heldBack =
    busy || !intakeApi.configured() || !readyToSubmit(state)
    || (awaitingCode && code.trim().length === 0);

  return (
    <section className="mt-5 bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-800 rounded-lg p-5">
      <h2 className="text-base font-semibold text-zinc-900 dark:text-zinc-100">{t('explore.claimTitle')}</h2>
      <p className="mb-3 text-xs text-zinc-400 dark:text-zinc-500">{t('explore.claimHint')}</p>

      <Field labelKey="explore.siteName" value={state.siteName} onChange={(siteName) => onChange({ siteName })} />
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
        <Field labelKey="explore.yourName" value={state.contactName} onChange={(contactName) => onChange({ contactName })} />
        <Field labelKey="explore.emailAddress" value={state.emailAddress} onChange={(emailAddress) => onChange({ emailAddress })} />
      </div>

      <div className="mt-3 flex items-center gap-3">
        {awaitingCode && (
          <input
            aria-label={t('explore.code')}
            value={code}
            onChange={(event) => onCodeChange(event.target.value)}
            placeholder={t('explore.codePlaceholder')}
            className="w-28 px-2 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100"
          />
        )}
        <button
          type="button"
          onClick={onSubmit}
          disabled={heldBack}
          className="px-3 py-1.5 text-sm rounded-md bg-emerald-600 hover:bg-emerald-700 text-white disabled:opacity-40"
        >
          {awaitingCode ? t('explore.seeReport') : t('explore.sendCode')}
        </button>
        {!readyToSubmit(state) && (
          <span className="text-xs text-amber-600 dark:text-amber-400">{t('explore.fieldsNeeded')}</span>
        )}
      </div>
    </section>
  );
}

function ReportStep({
  options,
  state,
  reference,
  findings,
  expired,
  busy,
  code,
  onCodeChange,
  onResumeAskForCode,
  onResume,
  onRefresh,
  onDial,
}: {
  options: FormOptions | null;
  state: ExploreState;
  reference: string;
  findings: Findings | null;
  expired: boolean;
  busy: boolean;
  code: string;
  onCodeChange: (code: string) => void;
  onResumeAskForCode: () => void;
  onResume: () => void;
  onRefresh: () => void;
  onDial: (patch: Partial<ExploreState>) => void;
}) {
  const { t } = useTranslation();

  return (
    <>
      <section className="bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-800 rounded-lg p-5">
        <div className="flex items-start justify-between gap-3">
          <div>
            <h2 className="text-base font-semibold text-zinc-900 dark:text-zinc-100">
              {t('explore.reportTitle', { site: state.siteName })}
            </h2>
            <p className="text-xs text-zinc-400 dark:text-zinc-500">
              {t('explore.reference')} <span className="font-mono">{reference}</span>
            </p>
          </div>
          <button
            type="button"
            onClick={onRefresh}
            className="px-3 py-1.5 text-sm rounded-md bg-zinc-200 dark:bg-zinc-700 text-zinc-900 dark:text-zinc-100 hover:bg-zinc-300 dark:hover:bg-zinc-600"
          >
            {t('explore.refresh')}
          </button>
        </div>

        {expired && (
          <div className="mt-3 rounded-md border border-amber-300 dark:border-amber-700 p-3">
            <p className="text-sm text-amber-700 dark:text-amber-400">{t('explore.sessionExpired')}</p>
            <div className="mt-2 flex items-center gap-2">
              <button
                type="button"
                onClick={onResumeAskForCode}
                disabled={busy}
                className="px-3 py-1.5 text-sm rounded-md bg-zinc-200 dark:bg-zinc-700 text-zinc-900 dark:text-zinc-100 disabled:opacity-40"
              >
                {t('explore.sendCode')}
              </button>
              <input
                aria-label={t('explore.code')}
                value={code}
                onChange={(event) => onCodeChange(event.target.value)}
                placeholder={t('explore.codePlaceholder')}
                className="w-28 px-2 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100"
              />
              <button
                type="button"
                onClick={onResume}
                disabled={busy || code.trim().length === 0}
                className="px-3 py-1.5 text-sm rounded-md bg-emerald-600 hover:bg-emerald-700 text-white disabled:opacity-40"
              >
                {t('explore.resume')}
              </button>
            </div>
          </div>
        )}

        {findings ? <Drawn findings={findings} /> : (
          <p className="mt-4 text-sm text-zinc-500 dark:text-zinc-400">{t('explore.reportComing')}</p>
        )}
      </section>

      <Dials options={options} state={state} onDial={onDial} />
    </>
  );
}

/** The model's own dashboard, exactly as the findings page draws it. */
function Drawn({ findings }: { findings: Findings }) {
  const { t, i18n } = useTranslation();
  const [measure, width] = useElementWidth();
  const spec = useMemo(() => localizeSpec(findings.spec, i18n.language), [findings.spec, i18n.language]);
  const ctx = useResolveContext(findings.index, findings.scopeId, spec.compare?.archetype, () => findings.reads);

  return (
    <div ref={measure} className="mt-4">
      <DashboardSections
        sections={spec.sections}
        ctx={ctx}
        isWide={width >= WIDE}
        whenEmpty={<p className="mt-4 text-sm text-zinc-500 dark:text-zinc-400">{t('explore.emptyView')}</p>}
      />
    </div>
  );
}

function Dials({
  options,
  state,
  onDial,
}: {
  options: FormOptions | null;
  state: ExploreState;
  onDial: (patch: Partial<ExploreState>) => void;
}) {
  const { t, i18n } = useTranslation();
  const percentage = useMemo(
    () => new Intl.NumberFormat(i18n.language, { style: 'percent' }),
    [i18n.language],
  );
  const shown = wholePercentages(state.shares);

  return (
    <section className="mt-5 bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-800 rounded-lg p-5">
      <h2 className="text-base font-semibold text-zinc-900 dark:text-zinc-100">{t('explore.dialsTitle')}</h2>
      <p className="mb-4 text-xs text-zinc-400 dark:text-zinc-500">{t('explore.dialsHint')}</p>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <label className="block">
          <span className="text-xs font-semibold text-zinc-500 dark:text-zinc-400">{t('explore.population')}</span>
          <span className="mt-1 flex items-center gap-3">
            <input
              type="range"
              min={0}
              max={2000}
              step={10}
              value={state.population === '' ? 0 : Number(state.population)}
              onChange={(event) => onDial({ population: event.target.value })}
              className="flex-1 accent-emerald-600"
            />
            <input
              value={state.population}
              onChange={(event) => onDial({ population: event.target.value })}
              inputMode="numeric"
              placeholder={t('explore.notSet')}
              className="w-24 px-2 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100 tabular-nums"
            />
          </span>
        </label>
        <label className="block">
          <span className="text-xs font-semibold text-zinc-500 dark:text-zinc-400">{t('explore.householdSize')}</span>
          <span className="mt-1 flex items-center gap-3">
            <input
              type="range"
              min={1}
              max={8}
              step={0.1}
              value={state.householdSize === '' ? 2.4 : Number(state.householdSize)}
              onChange={(event) => onDial({ householdSize: event.target.value })}
              className="flex-1 accent-emerald-600"
            />
            <input
              value={state.householdSize}
              onChange={(event) => onDial({ householdSize: event.target.value })}
              inputMode="decimal"
              placeholder={t('explore.notSet')}
              className="w-24 px-2 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100 tabular-nums"
            />
          </span>
        </label>
      </div>

      <div className="mt-4 text-xs font-semibold text-zinc-500 dark:text-zinc-400">{t('explore.programmeTitle')}</div>
      <div className="mt-2 space-y-2">
        {Object.keys(state.shares).map((category) => (
          <div key={category} className="flex items-center gap-3">
            <span className="w-64 text-sm text-zinc-700 dark:text-zinc-200">{category}</span>
            <input
              type="range"
              min={0}
              max={100}
              value={shown[category] ?? 0}
              onChange={(event) =>
                onDial({ shares: withShareSet(state.shares, category, Number(event.target.value)) })}
              className="flex-1 accent-emerald-600"
            />
            <span className="w-14 text-right text-sm tabular-nums text-zinc-600 dark:text-zinc-300">
              {percentage.format((shown[category] ?? 0) / 100)}
            </span>
          </div>
        ))}
      </div>

      {options !== null && options.hazardTypes.length > 0 && options.hazardLevels.length > 0 && (
        <>
          <div className="mt-5 text-xs font-semibold text-zinc-500 dark:text-zinc-400">{t('explore.hazardsTitle')}</div>
          <p className="mt-1 text-[11px] text-zinc-400 dark:text-zinc-500">{t('explore.hazardsHint')}</p>
          <div className="mt-2 flex flex-col gap-2">
            {options.hazardTypes.map((hazardType) => (
              <label key={hazardType} className="flex items-center justify-between gap-3">
                <span className="text-sm text-zinc-700 dark:text-zinc-300">{hazardType}</span>
                <select
                  aria-label={hazardType}
                  value={state.reportedHazards[hazardType] ?? ''}
                  onChange={(event) => {
                    const reported = { ...state.reportedHazards };
                    if (event.target.value.length === 0) delete reported[hazardType];
                    else reported[hazardType] = event.target.value;
                    onDial({ reportedHazards: reported });
                  }}
                  className="px-2 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100"
                >
                  <option value="">{t('explore.notReported')}</option>
                  {options.hazardLevels.map((level) => (
                    <option key={level} value={level}>{level}</option>
                  ))}
                </select>
              </label>
            ))}
          </div>
        </>
      )}
    </section>
  );
}

function Field({
  labelKey,
  value,
  onChange,
}: {
  labelKey: string;
  value: string;
  onChange: (value: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <label className="block mb-3">
      <span className="text-xs font-semibold text-zinc-500 dark:text-zinc-400">
        {t(labelKey as never) as string}
      </span>
      <input
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="mt-1 w-full px-2 py-1.5 text-sm rounded-md bg-zinc-50 dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 text-zinc-900 dark:text-zinc-100"
      />
    </label>
  );
}
