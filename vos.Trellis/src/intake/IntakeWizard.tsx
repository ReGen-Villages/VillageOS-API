/**
 * The land-intake wizard: what somebody knows about a piece of land, collected step by step and posted to
 * the intake service as one document (#6016), with the parcel boundary drawn on the map last (#6015).
 *
 * One definition, rendered both by the planner's page and by the public form, so a field added here
 * appears in both and there is one shape of submission rather than two that can disagree.
 *
 * Three things shape it. Nothing is kept in the browser beyond the draft — the answers become Things the
 * moment the submission is accepted, so there is no file to download and nothing to lose. The programme
 * categories are read out of the model by whoever renders this rather than listed here, because the
 * intake service resolves a submitted word against the terms the model declares and offering anything
 * else would collect an answer that is then refused. And nothing but the email address reaches the
 * service until the code sent to that address has been answered.
 */
import { Suspense, lazy, useCallback, useMemo, useState } from 'react';
import type { ParseKeys } from 'i18next';
import { Check } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { intakeApi, type SubmissionAccepted } from '../api/intakeApi';
import { toast } from '../components/common/toastStore';
import type { BasemapSource } from '../types/basemap';
import { locationFromMapLink, type MapLinkReading } from '../utils/mapLink';
import { areaMatch, sphericalAreaHectares, type BoundaryPoint } from '../utils/parcelGeometry';
import {
  STEPS,
  boundaryCleared,
  boundaryDrafted,
  boundaryDrawn,
  clearDraft,
  coordinatesFrom,
  documentFrom,
  emptyDraft,
  fromHectares,
  loadDraft,
  readyToSubmit,
  saveDraft,
  statedAreaHectares,
  wholePercentages,
  withCategoryChosen,
  withCategoryDropped,
  withShareSet,
  withStepVisited,
  type AreaUnit,
  type StepId,
  withHazardReported,
  type SubmissionDraft,
} from './submissionDraft';

const MapView = lazy(() => import('../components/map/MapView').then((m) => ({ default: m.MapView })));

export function IntakeWizard({
  categories,
  basemapSources,
  hazardTypes,
  hazardLevels,
  draftOwner,
}: {
  categories: readonly string[];
  basemapSources: BasemapSource[];
  /** The hazards somebody may report on and the words they may use, both the model's. Empty where a
   *  deployment declares none, which draws the step with nothing to mark rather than hiding it — a
   *  person who reached it should be told why there is nothing there. */
  hazardTypes: readonly string[];
  hazardLevels: readonly string[];
  /** What the half-finished submission is kept under, or null while that is still being established. */
  draftOwner: string | null;
}) {
  const { t } = useTranslation();
  const [draft, setDraft] = useState<SubmissionDraft | null>(null);
  const [step, setStep] = useState<StepId>('project');
  const [submitting, setSubmitting] = useState(false);
  const [awaitingCode, setAwaitingCode] = useState(false);
  const [code, setCode] = useState('');
  const [accepted, setAccepted] = useState<SubmissionAccepted | null>(null);

  // The draft is picked up once its owner is known — and whoever has never started one gets a fresh
  // identifier, which every Thing the submission mints derives from. Picked up while rendering rather
  // than in an effect: it is a synchronous read of local storage, and an effect would paint an empty form
  // and then replace it with the answers already on disk.
  const [loadedFor, setLoadedFor] = useState<string | null>(null);
  if (draftOwner !== null && draftOwner !== loadedFor) {
    setLoadedFor(draftOwner);
    setDraft(loadDraft(draftOwner) ?? emptyDraft(crypto.randomUUID()));
  }

  const change = useCallback(
    (patch: Partial<SubmissionDraft>) => {
      // A code is sent to the address as it read when it was asked for. Touching that field leaves the
      // code good for a mailbox this submission may no longer name, so the exchange starts again rather
      // than offering a box whose code the service will refuse.
      if (patch.emailAddress !== undefined) {
        setAwaitingCode(false);
        setCode('');
      }
      setDraft((current) => {
        if (!current) return current;
        const next = { ...current, ...patch };
        if (draftOwner) saveDraft(draftOwner, next);
        return next;
      });
    },
    [draftOwner],
  );

  const goTo = useCallback(
    (next: StepId) => {
      setDraft((current) => {
        if (!current) return current;
        const visited = withStepVisited(current, next);
        if (draftOwner && visited !== current) saveDraft(draftOwner, visited);
        return visited;
      });
      setStep(next);
    },
    [draftOwner],
  );

  // Submitting is two acts with a person in the middle of them: the service sends a code to the address
  // on the form, and the submission goes once that code has been read back off it. The code is held only
  // until it is spent — it is not part of the draft, and a draft come back to tomorrow starts here again.
  async function askForCode(): Promise<void> {
    if (!draft) return;
    setSubmitting(true);
    try {
      await intakeApi.askForCode(draft.emailAddress.trim());
      setAwaitingCode(true);
      toast.success(t('intake.codeSent', { address: draft.emailAddress.trim() }));
    } catch (error) {
      toast.error(t('intake.refused', { reason: error instanceof Error ? error.message : String(error) }));
    } finally {
      setSubmitting(false);
    }
  }

  async function submit(): Promise<void> {
    if (!draft) return;
    setSubmitting(true);
    try {
      const result = await intakeApi.submit(documentFrom(draft), code.trim());
      setAccepted(result);
      setAwaitingCode(false);
      setCode('');
      if (draftOwner) clearDraft(draftOwner);
      toast.success(t('intake.accepted', { site: draft.siteName.trim() }));
    } catch (error) {
      toast.error(t('intake.refused', { reason: error instanceof Error ? error.message : String(error) }));
    } finally {
      setSubmitting(false);
    }
  }

  function startAnother(): void {
    setAccepted(null);
    setAwaitingCode(false);
    setCode('');
    setDraft(emptyDraft(crypto.randomUUID()));
    setStep('project');
  }

  if (!draft) {
    return (
      <div className="h-full flex items-center justify-center p-8 text-sm text-zinc-500 dark:text-zinc-400">
        {t('common.loading')}
      </div>
    );
  }

  if (accepted) return <Accepted accepted={accepted} onStartAnother={startAnother} />;

  return (
    <>
      <StepBar current={step} visited={draft.visited} onGoTo={goTo} />
      <div className="mt-5 bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-800 rounded-lg p-5">
        {step === 'project' && <ProjectStep draft={draft} onChange={change} />}
        {step === 'contact' && <ContactStep draft={draft} onChange={change} />}
        {step === 'location' && <LocationStep draft={draft} sources={basemapSources} onChange={change} />}
        {step === 'programme' && <ProgrammeStep draft={draft} categories={categories} onChange={change} />}
        {step === 'parcel' && <ParcelStep draft={draft} sources={basemapSources} onChange={change} />}
        {step === 'hazards' && (
          <HazardsStep draft={draft} types={hazardTypes} levels={hazardLevels} onChange={change} />
        )}
      </div>
      <Navigation
        step={step}
        ready={readyToSubmit(draft)}
        submitting={submitting}
        configured={intakeApi.configured()}
        awaitingCode={awaitingCode}
        code={code}
        onCodeChange={setCode}
        onGoTo={goTo}
        onSubmit={() => void (awaitingCode ? submit() : askForCode())}
      />
    </>
  );
}

function StepBar({
  current,
  visited,
  onGoTo,
}: {
  current: StepId;
  visited: readonly StepId[];
  onGoTo: (step: StepId) => void;
}) {
  const { t } = useTranslation();
  return (
    <nav className="flex flex-wrap gap-2">
      {STEPS.map((step, index) => {
        const reachable = visited.includes(step);
        return (
          <button
            key={step}
            onClick={() => onGoTo(step)}
            disabled={!reachable}
            className={`text-xs font-semibold px-3 py-1.5 rounded-md border ${
              step === current
                ? 'bg-emerald-600 border-emerald-600 text-white'
                : reachable
                  ? 'bg-white dark:bg-zinc-800 border-zinc-200 dark:border-zinc-700 text-zinc-600 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-700'
                  : 'bg-transparent border-dashed border-zinc-200 dark:border-zinc-800 text-zinc-300 dark:text-zinc-600'
            }`}
          >
            {index + 1}. {t(`intake.step.${step}`)}
          </button>
        );
      })}
    </nav>
  );
}

function Navigation({
  step,
  ready,
  submitting,
  configured,
  awaitingCode,
  code,
  onCodeChange,
  onGoTo,
  onSubmit,
}: {
  step: StepId;
  ready: boolean;
  submitting: boolean;
  configured: boolean;
  awaitingCode: boolean;
  code: string;
  onCodeChange: (code: string) => void;
  onGoTo: (step: StepId) => void;
  onSubmit: () => void;
}) {
  const { t } = useTranslation();
  const index = STEPS.indexOf(step);
  const last = index === STEPS.length - 1;
  const heldBack =
    last && (submitting || !configured || !ready || (awaitingCode && code.trim().length === 0));

  return (
    <div className="mt-4 flex items-center justify-between gap-3">
      <button
        onClick={() => onGoTo(STEPS[index - 1])}
        disabled={index === 0}
        className="px-3 py-1.5 text-sm rounded-md bg-zinc-200 dark:bg-zinc-700 text-zinc-900 dark:text-zinc-100 hover:bg-zinc-300 dark:hover:bg-zinc-600 disabled:opacity-40"
      >
        {t('intake.back')}
      </button>

      <div className="flex items-center gap-3">
        {last && !configured && (
          <span className="text-xs text-amber-600 dark:text-amber-400">{t('intake.notConfigured')}</span>
        )}
        {last && configured && !ready && (
          <span className="text-xs text-amber-600 dark:text-amber-400">{t('intake.fieldsNeeded')}</span>
        )}
        {last && awaitingCode && (
          <input
            aria-label={t('intake.code')}
            value={code}
            onChange={(event) => onCodeChange(event.target.value)}
            placeholder={t('intake.codePlaceholder')}
            className="w-28 px-2 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100"
          />
        )}
        <button
          onClick={() => (last ? onSubmit() : onGoTo(STEPS[index + 1]))}
          disabled={heldBack}
          className="px-3 py-1.5 text-sm rounded-md bg-emerald-600 hover:bg-emerald-700 text-white disabled:opacity-40"
        >
          {!last ? t('intake.next') : awaitingCode ? t('intake.submit') : t('intake.sendCode')}
        </button>
      </div>
    </div>
  );
}

function ProjectStep({ draft, onChange }: StepProps) {
  return (
    <>
      <StepHeading step="project" />
      <Field labelKey="intake.projectName" value={draft.projectName} onChange={(projectName) => onChange({ projectName })} />
      <Field labelKey="intake.country" value={draft.country} onChange={(country) => onChange({ country })} />
      <Field labelKey="intake.nearestCity" value={draft.nearestCity} onChange={(nearestCity) => onChange({ nearestCity })} />
      <Field
        labelKey="intake.existingDataNotes"
        hintKey="intake.existingDataNotesHint"
        value={draft.existingDataNotes}
        onChange={(existingDataNotes) => onChange({ existingDataNotes })}
        lines={3}
      />
    </>
  );
}

function ContactStep({ draft, onChange }: StepProps) {
  return (
    <>
      <StepHeading step="contact" />
      <Field labelKey="intake.contactName" value={draft.contactName} onChange={(contactName) => onChange({ contactName })} />
      <Field
        labelKey="intake.relationshipToProject"
        value={draft.relationshipToProject}
        onChange={(relationshipToProject) => onChange({ relationshipToProject })}
      />
      <Field labelKey="intake.emailAddress" value={draft.emailAddress} onChange={(emailAddress) => onChange({ emailAddress })} />
      <Field labelKey="intake.phoneNumber" value={draft.phoneNumber} onChange={(phoneNumber) => onChange({ phoneNumber })} />
    </>
  );
}

function LocationStep({ draft, sources, onChange }: StepProps & { sources: BasemapSource[] }) {
  const { t } = useTranslation();
  const [pasted, setPasted] = useState('');
  const [reading, setReading] = useState<MapLinkReading['kind'] | null>(null);
  const position = coordinatesFrom(draft);

  function read(text: string): void {
    setPasted(text);
    if (text.trim().length === 0) {
      setReading(null);
      return;
    }
    const found = locationFromMapLink(text);
    setReading(found.kind);
    if (found.kind === 'located') {
      onChange({ latitude: String(found.latitude), longitude: String(found.longitude) });
    }
  }

  return (
    <>
      <StepHeading step="location" />
      <Field labelKey="intake.siteName" hintKey="intake.siteNameHint" value={draft.siteName} onChange={(siteName) => onChange({ siteName })} />

      <Field labelKey="intake.mapLink" hintKey="intake.mapLinkHint" value={pasted} onChange={read} />
      {reading === 'located' && <Note tone="good">{t('intake.mapLinkRead')}</Note>}
      {reading === 'shortened' && <Note tone="warn">{t('intake.mapLinkShortened')}</Note>}
      {reading === 'unrecognised' && <Note tone="warn">{t('intake.mapLinkUnrecognised')}</Note>}

      <div className="grid grid-cols-2 gap-3">
        <Field labelKey="intake.latitude" value={draft.latitude} onChange={(latitude) => onChange({ latitude })} />
        <Field labelKey="intake.longitude" value={draft.longitude} onChange={(longitude) => onChange({ longitude })} />
      </div>

      {position ? (
        <SiteMap position={position} sources={sources} />
      ) : (
        <Note tone="quiet">{t('intake.mapNeedsPosition')}</Note>
      )}
    </>
  );
}

/** The shared map module in the wizard's frame: a fixed height, so the step keeps its shape while the
 *  map library loads. */
function SiteMap({
  position,
  sources,
  boundary,
  onBoundaryChange,
}: {
  position: { latitude: number; longitude: number };
  sources: BasemapSource[];
  boundary?: readonly BoundaryPoint[];
  onBoundaryChange?: (boundary: BoundaryPoint[]) => void;
}) {
  return (
    <div className="h-72 overflow-hidden rounded-md border border-zinc-200 dark:border-zinc-700">
      <Suspense fallback={null}>
        <MapView
          latitude={position.latitude}
          longitude={position.longitude}
          sources={sources}
          boundary={boundary}
          onBoundaryChange={onBoundaryChange}
        />
      </Suspense>
    </div>
  );
}

function ProgrammeStep({
  draft,
  categories,
  onChange,
}: StepProps & { categories: readonly string[] }) {
  const { t, i18n } = useTranslation();
  const hectares = statedAreaHectares(draft);
  const shown = wholePercentages(draft.shares);
  // One formatter for every row rather than one per row per render: the rows redraw on each keystroke
  // anywhere in this step, and building a formatter is the expensive half of formatting one number.
  const percentage = useMemo(
    () => new Intl.NumberFormat(i18n.language, { style: 'percent' }),
    [i18n.language],
  );
  const equivalent = useMemo(() => {
    if (hectares === null) return null;
    const other: AreaUnit = draft.areaUnit === 'hectares' ? 'acres' : 'hectares';
    return t(`intake.areaEquivalent.${other}`, {
      value: new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 2 }).format(
        fromHectares(hectares, other),
      ),
    });
  }, [hectares, draft.areaUnit, t, i18n.language]);

  return (
    <>
      <StepHeading step="programme" />

      <label className="block mb-3">
        <span className="text-xs font-semibold text-zinc-500 dark:text-zinc-400">{t('intake.statedArea')}</span>
        <span className="mt-1 flex gap-2">
          <input
            value={draft.statedArea}
            onChange={(event) => onChange({ statedArea: event.target.value })}
            inputMode="decimal"
            className="flex-1 px-2 py-1.5 text-sm rounded-md bg-zinc-50 dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 text-zinc-900 dark:text-zinc-100"
          />
          <select
            aria-label={t('intake.areaUnit')}
            value={draft.areaUnit}
            onChange={(event) => onChange({ areaUnit: event.target.value as AreaUnit })}
            className="px-2 py-1.5 text-sm rounded-md bg-zinc-50 dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 text-zinc-900 dark:text-zinc-100"
          >
            <option value="hectares">{t('intake.hectares')}</option>
            <option value="acres">{t('intake.acres')}</option>
          </select>
        </span>
      </label>
      {equivalent && <Note tone="quiet">{equivalent}</Note>}
      {draft.statedArea.trim().length > 0 && hectares === null && (
        <Note tone="warn">{t('intake.areaNotAFigure')}</Note>
      )}

      <div className="grid grid-cols-2 gap-3">
        <Field labelKey="intake.population" value={draft.population} onChange={(population) => onChange({ population })} />
        <Field labelKey="intake.householdSize" value={draft.householdSize} onChange={(householdSize) => onChange({ householdSize })} />
      </div>

      <div className="mt-4 text-xs font-semibold text-zinc-500 dark:text-zinc-400">{t('intake.programme')}</div>
      <p className="mt-1 text-[11px] text-zinc-400 dark:text-zinc-500">{t('intake.programmeHint')}</p>
      {categories.length === 0 ? (
        <Note tone="warn">{t('intake.noCategories')}</Note>
      ) : (
        <div className="mt-2 space-y-2">
          {categories.map((category) => (
            <CategoryRow
              key={category}
              category={category}
              chosen={category in draft.shares}
              share={shown[category]}
              percentage={percentage}
              onToggle={(chosen) =>
                onChange({
                  shares: chosen
                    ? withCategoryChosen(draft.shares, category)
                    : withCategoryDropped(draft.shares, category),
                })
              }
              onShare={(share) => onChange({ shares: withShareSet(draft.shares, category, share) })}
            />
          ))}
        </div>
      )}
    </>
  );
}

function ParcelStep({ draft, sources, onChange }: StepProps & { sources: BasemapSource[] }) {
  const { t, i18n } = useTranslation();
  const position = coordinatesFrom(draft);
  const stated = statedAreaHectares(draft);
  const drawn = draft.boundary.length >= 3 ? sphericalAreaHectares(draft.boundary) : null;
  const compared = drawn !== null && stated !== null ? areaMatch(drawn, stated) : null;
  const drafted = boundaryDrafted(draft);
  const unit = t(`intake.${draft.areaUnit}`);
  const area = useMemo(
    () => new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 2 }),
    [i18n.language],
  );
  const percentage = useMemo(
    () => new Intl.NumberFormat(i18n.language, { style: 'percent', maximumFractionDigits: 1 }),
    [i18n.language],
  );

  if (!position) {
    return (
      <>
        <StepHeading step="parcel" />
        <Note tone="quiet">{t('intake.mapNeedsPosition')}</Note>
      </>
    );
  }

  return (
    <>
      <StepHeading step="parcel" />

      <div className="mb-3 flex flex-wrap items-center gap-2">
        <button
          onClick={() => drafted && onChange(drafted)}
          disabled={drafted === null}
          className="px-3 py-1.5 text-sm rounded-md bg-emerald-600 hover:bg-emerald-700 text-white disabled:opacity-40"
        >
          {t('intake.placeDraftBoundary')}
        </button>
        <button
          onClick={() => onChange(boundaryCleared())}
          disabled={draft.boundary.length === 0}
          className="px-3 py-1.5 text-sm rounded-md bg-zinc-200 dark:bg-zinc-700 text-zinc-900 dark:text-zinc-100 hover:bg-zinc-300 dark:hover:bg-zinc-600 disabled:opacity-40"
        >
          {t('intake.clearBoundary')}
        </button>
      </div>
      {drafted === null && draft.boundary.length === 0 && (
        <Note tone="quiet">{t('intake.draftNeedsArea')}</Note>
      )}

      <SiteMap
        position={position}
        sources={sources}
        boundary={draft.boundary}
        onBoundaryChange={(boundary) => onChange(boundaryDrawn(boundary))}
      />
      <p className="mt-1 text-[11px] text-zinc-400 dark:text-zinc-500">{t('intake.drawByClicking')}</p>

      <div className="mt-3 text-sm text-zinc-700 dark:text-zinc-200 tabular-nums">
        {stated !== null && (
          <p>{t('intake.statedReadout', { value: area.format(fromHectares(stated, draft.areaUnit)), unit })}</p>
        )}
        {drawn !== null ? (
          <p>{t('intake.drawnReadout', { value: area.format(fromHectares(drawn, draft.areaUnit)), unit })}</p>
        ) : (
          <p className="text-zinc-400 dark:text-zinc-500">{t('intake.noBoundaryYet')}</p>
        )}
      </div>
      {compared &&
        (compared.kind === 'match' ? (
          <p className="mt-1 text-[11px] text-emerald-600 dark:text-emerald-400">{t('intake.areaWithin')}</p>
        ) : (
          <p className="mt-1 text-[11px] text-amber-600 dark:text-amber-400">
            {t('intake.areaApart', { difference: percentage.format(compared.relativeDifference) })}
          </p>
        ))}
    </>
  );
}

function CategoryRow({
  category,
  chosen,
  share,
  percentage,
  onToggle,
  onShare,
}: {
  category: string;
  chosen: boolean;
  /** A whole percentage, from a set that adds to a hundred across the chosen categories, so what is on
   *  screen and what the split claims to describe are the same thing. */
  share?: number;
  percentage: Intl.NumberFormat;
  onToggle: (chosen: boolean) => void;
  onShare: (share: number) => void;
}) {
  return (
    <div className="flex items-center gap-3">
      <label className="flex items-center gap-2 w-64 text-sm text-zinc-700 dark:text-zinc-200">
        <input
          type="checkbox"
          checked={chosen}
          onChange={(event) => onToggle(event.target.checked)}
          className="accent-emerald-600"
        />
        {category}
      </label>
      <input
        type="range"
        min={0}
        max={100}
        value={share ?? 0}
        disabled={!chosen}
        onChange={(event) => onShare(Number(event.target.value))}
        className="flex-1 accent-emerald-600 disabled:opacity-30"
      />
      <span className="w-14 text-right text-sm tabular-nums text-zinc-600 dark:text-zinc-300">
        {share === undefined ? '—' : percentage.format(share / 100)}
      </span>
    </div>
  );
}

function Accepted({
  accepted,
  onStartAnother,
}: {
  accepted: SubmissionAccepted;
  onStartAnother: () => void;
}) {
  const { t } = useTranslation();
  return (
    <div className="bg-white dark:bg-zinc-900 border border-emerald-200 dark:border-emerald-900 rounded-lg p-5">
      <div className="flex items-center gap-2 text-emerald-700 dark:text-emerald-400">
        <Check size={18} />
        <h3 className="text-lg font-semibold">{t('intake.acceptedTitle')}</h3>
      </div>
      <p className="mt-2 text-sm text-zinc-600 dark:text-zinc-400">{t('intake.acceptedBody')}</p>
      <dl className="mt-3 text-xs font-mono text-zinc-500 dark:text-zinc-400">
        <dt className="inline font-sans">{t('intake.reference')}</dt>{' '}
        <dd className="inline">{accepted.reference}</dd>
      </dl>
      <button
        onClick={onStartAnother}
        className="mt-4 px-3 py-1.5 text-sm rounded-md bg-emerald-600 hover:bg-emerald-700 text-white"
      >
        {t('intake.startAnother')}
      </button>
    </div>
  );
}

/** What a person has seen of the land, against the hazards the model names. Every hazard is offered and
 *  every one starts unreported: somebody marking what they know is doing something different from
 *  nominating hazards from memory, and a blank row is an answer — this is not something they saw. */
function HazardsStep({
  draft,
  types,
  levels,
  onChange,
}: StepProps & { types: readonly string[]; levels: readonly string[] }) {
  const { t } = useTranslation();

  if (types.length === 0 || levels.length === 0) {
    return (
      <>
        <StepHeading step="hazards" />
        <p className="text-sm text-zinc-500 dark:text-zinc-400">{t('intake.hazardsNone')}</p>
      </>
    );
  }

  return (
    <>
      <StepHeading step="hazards" />
      <div className="flex flex-col gap-2">
        {types.map((hazardType) => (
          <label key={hazardType} className="flex items-center justify-between gap-3">
            <span className="text-sm text-zinc-700 dark:text-zinc-300">{hazardType}</span>
            <select
              aria-label={hazardType}
              value={draft.reportedHazards[hazardType] ?? ''}
              onChange={(event) =>
                onChange(withHazardReported(draft, hazardType, event.target.value))
              }
              className="px-2 py-1.5 text-sm rounded-md border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800 text-zinc-900 dark:text-zinc-100"
            >
              <option value="">{t('intake.notReported')}</option>
              {levels.map((level) => (
                <option key={level} value={level}>
                  {level}
                </option>
              ))}
            </select>
          </label>
        ))}
      </div>
    </>
  );
}

interface StepProps {
  draft: SubmissionDraft;
  onChange: (patch: Partial<SubmissionDraft>) => void;
}

function StepHeading({ step }: { step: StepId }) {
  const { t } = useTranslation();
  return (
    <div className="mb-4">
      <h3 className="text-base font-semibold text-zinc-900 dark:text-zinc-100">{t(`intake.step.${step}`)}</h3>
      <p className="text-xs text-zinc-400 dark:text-zinc-500">{t(`intake.stepHint.${step}`)}</p>
    </div>
  );
}

function Field({
  labelKey,
  hintKey,
  value,
  onChange,
  lines,
}: {
  labelKey: ParseKeys;
  hintKey?: ParseKeys;
  value: string;
  onChange: (value: string) => void;
  lines?: number;
}) {
  const { t } = useTranslation();
  const shared =
    'mt-1 w-full px-2 py-1.5 text-sm rounded-md bg-zinc-50 dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 text-zinc-900 dark:text-zinc-100';

  // The hint sits outside the label rather than inside it: everything a label wraps becomes part of the
  // field's name, so a hint inside one is read out as though it were half the question.
  return (
    <div className="mb-3">
      <label className="block">
        <span className="text-xs font-semibold text-zinc-500 dark:text-zinc-400">{t(labelKey)}</span>
        {lines ? (
          <textarea rows={lines} value={value} onChange={(event) => onChange(event.target.value)} className={shared} />
        ) : (
          <input value={value} onChange={(event) => onChange(event.target.value)} className={shared} />
        )}
      </label>
      {hintKey && <p className="mt-1 text-[11px] text-zinc-400 dark:text-zinc-500">{t(hintKey)}</p>}
    </div>
  );
}

function Note({ tone, children }: { tone: 'good' | 'warn' | 'quiet'; children: React.ReactNode }) {
  const colour = {
    good: 'text-emerald-600 dark:text-emerald-400',
    warn: 'text-amber-600 dark:text-amber-400',
    quiet: 'text-zinc-400 dark:text-zinc-500',
  }[tone];
  return <p className={`-mt-1 mb-3 text-[11px] ${colour}`}>{children}</p>;
}
