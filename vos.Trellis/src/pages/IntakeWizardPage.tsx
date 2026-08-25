/**
 * The land-intake wizard: what a planner knows about a piece of land, collected step by step and posted
 * to the intake service as one document (#6016), with the parcel boundary drawn on the map last (#6015).
 *
 * Two things shape the whole page. Nothing is kept in the browser beyond the draft — the answers become
 * Things the moment the submission is accepted, so there is no file to download and nothing to lose. And
 * the programme categories are read out of the model rather than listed here: the intake service resolves
 * a submitted word against the terms the model declares, so offering anything else would collect an
 * answer that is then refused.
 */
import { Suspense, lazy, useCallback, useEffect, useMemo, useState } from 'react';
import type { ParseKeys } from 'i18next';
import { ClipboardList, Check } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { discoverBasemapSources } from '../api/basemapApi';
import { modelIndexFor } from '../api/dashboardApi';
import { intakeApi, type SubmissionAccepted } from '../api/intakeApi';
import { relationshipApi } from '../api/relationshipApi';
import { thingApi } from '../api/thingApi';
import { toast } from '../components/common/toastStore';
import { useAuth } from '../hooks/useAuth';
import type { BasemapSource } from '../types/basemap';
import { locationFromMapLink, type MapLinkReading } from '../utils/mapLink';
import { areaMatch, sphericalAreaHectares, type BoundaryPoint } from '../utils/parcelGeometry';
import { ALLOCATION_CATEGORY_ARCHETYPE_FLAG, termsMarked } from './modelVocabulary';
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
  type SubmissionDraft,
} from './intakeWizard';

const MapView = lazy(() => import('../components/map/MapView').then((m) => ({ default: m.MapView })));

export function IntakeWizardPage() {
  const { t } = useTranslation();
  const { modelId } = useAuth();
  const [draft, setDraft] = useState<SubmissionDraft | null>(null);
  const [step, setStep] = useState<StepId>('project');
  const [categories, setCategories] = useState<readonly string[]>([]);
  const [basemapSources, setBasemapSources] = useState<BasemapSource[]>([]);
  const [submitting, setSubmitting] = useState(false);
  const [accepted, setAccepted] = useState<SubmissionAccepted | null>(null);

  // A draft belongs to the model it proposes land into, so it is picked up once that is known — and a
  // planner who has never started one gets a fresh identifier, which every Thing the submission mints
  // derives from. Picked up while rendering rather than in an effect: it is a synchronous read of local
  // storage, and an effect would paint an empty form and then replace it with the answers already on
  // disk.
  const [draftModelId, setDraftModelId] = useState<string | null>(null);
  if (modelId !== null && modelId !== draftModelId) {
    setDraftModelId(modelId);
    setDraft(loadDraft(modelId) ?? emptyDraft(crypto.randomUUID()));
  }

  // The vocabulary is read from the model itself rather than through the app shell's load, which a model
  // may narrow to the properties its pages are drawn with — and a mark is nobody's idea of a page
  // property. A model that declares none leaves the step saying so rather than empty.
  useEffect(() => {
    let abandoned = false;
    Promise.all([thingApi.getAll(), relationshipApi.getAll(), thingApi.getAllProperties('effective')])
      .then(([things, relationships, properties]) => {
        if (abandoned) return;
        setCategories(termsMarked({ things, relationships, properties }, ALLOCATION_CATEGORY_ARCHETYPE_FLAG));
        setBasemapSources(discoverBasemapSources(modelIndexFor(things, relationships)));
      })
      .catch(() => {
        if (abandoned) return;
        setCategories([]);
        setBasemapSources([]);
      });
    return () => {
      abandoned = true;
    };
  }, []);

  const change = useCallback(
    (patch: Partial<SubmissionDraft>) => {
      setDraft((current) => {
        if (!current) return current;
        const next = { ...current, ...patch };
        if (modelId) saveDraft(modelId, next);
        return next;
      });
    },
    [modelId],
  );

  const goTo = useCallback(
    (next: StepId) => {
      setDraft((current) => {
        if (!current) return current;
        const visited = withStepVisited(current, next);
        if (modelId && visited !== current) saveDraft(modelId, visited);
        return visited;
      });
      setStep(next);
    },
    [modelId],
  );

  async function submit(): Promise<void> {
    if (!draft) return;
    setSubmitting(true);
    try {
      const result = await intakeApi.submit(documentFrom(draft));
      setAccepted(result);
      if (modelId) clearDraft(modelId);
      toast.success(t('intake.accepted', { site: draft.siteName.trim() }));
    } catch (error) {
      toast.error(t('intake.refused', { reason: error instanceof Error ? error.message : String(error) }));
    } finally {
      setSubmitting(false);
    }
  }

  function startAnother(): void {
    setAccepted(null);
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

  return (
    <div className="h-full flex flex-col bg-zinc-50 dark:bg-zinc-950">
      <header className="flex-shrink-0 flex items-center gap-3 px-6 pt-6 pb-4">
        <div className="w-9 h-9 rounded-lg grid place-items-center text-white bg-gradient-to-br from-emerald-600 to-teal-500">
          <ClipboardList size={18} />
        </div>
        <div>
          <h2 className="text-xl font-bold text-zinc-900 dark:text-white leading-tight">{t('intake.title')}</h2>
          <div className="text-xs text-zinc-400 dark:text-zinc-500">{t('intake.subtitle')}</div>
        </div>
      </header>

      <div className="flex-1 overflow-auto px-6 pb-10">
        <div className="max-w-2xl">
          {accepted ? (
            <Accepted accepted={accepted} onStartAnother={startAnother} />
          ) : (
            <>
              <StepBar current={step} visited={draft.visited} onGoTo={goTo} />
              <div className="mt-5 bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-800 rounded-lg p-5">
                {step === 'project' && <ProjectStep draft={draft} onChange={change} />}
                {step === 'contact' && <ContactStep draft={draft} onChange={change} />}
                {step === 'location' && <LocationStep draft={draft} sources={basemapSources} onChange={change} />}
                {step === 'programme' && (
                  <ProgrammeStep draft={draft} categories={categories} onChange={change} />
                )}
                {step === 'parcel' && <ParcelStep draft={draft} sources={basemapSources} onChange={change} />}
              </div>
              <Navigation
                step={step}
                ready={readyToSubmit(draft)}
                submitting={submitting}
                configured={intakeApi.configured()}
                onGoTo={goTo}
                onSubmit={() => void submit()}
              />
            </>
          )}
        </div>
      </div>
    </div>
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
  onGoTo,
  onSubmit,
}: {
  step: StepId;
  ready: boolean;
  submitting: boolean;
  configured: boolean;
  onGoTo: (step: StepId) => void;
  onSubmit: () => void;
}) {
  const { t } = useTranslation();
  const index = STEPS.indexOf(step);
  const last = index === STEPS.length - 1;

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
          <span className="text-xs text-amber-600 dark:text-amber-400">{t('intake.siteNameNeeded')}</span>
        )}
        <button
          onClick={() => (last ? onSubmit() : onGoTo(STEPS[index + 1]))}
          disabled={last && (submitting || !configured || !ready)}
          className="px-3 py-1.5 text-sm rounded-md bg-emerald-600 hover:bg-emerald-700 text-white disabled:opacity-40"
        >
          {last ? t('intake.submit') : t('intake.next')}
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
