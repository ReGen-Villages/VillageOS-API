/**
 * What has arrived in this model, and what a reviewer decides about it: list the submissions, reject
 * the junk, promote the rest into a project model of its own (#6621, the client half of #6045).
 *
 * The page holds no archetype and no predicate name. Everything it reads it finds by the marks the
 * model puts on its own vocabulary (see `submissionReview.ts`), and what travels with a promoted
 * site is chosen from the predicates the model actually asserts through.
 */
import { useEffect, useMemo, useState } from 'react';
import { Inbox, RefreshCw } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { modelApi, type PromotionResult } from '../api/modelApi';
import { relationshipApi } from '../api/relationshipApi';
import { thingApi } from '../api/thingApi';
import { toast } from '../components/common/toastStore';
import {
  byArrival,
  disposableDisposition,
  dispositionPredicate,
  keptDisposition,
  predicateNamesIn,
  proposedSitePredicate,
  submissionsIn,
  type Disposition,
  type Submission,
} from './submissionReview';
import type { ModelReading } from './modelVocabulary';

/** When the decision was made, so a list can be ordered without reading history. Who made it is the
 *  Fact the write itself lays down, which is the record that cannot be typed in. */
const RESOLVED_AT = 'resolvedAt';

/** What the page holds before the first read answers, so everything below it asks the same questions
 *  of an empty model as of a full one. */
const NOTHING_READ: ModelReading = { things: [], relationships: [], properties: {} };

export function SubmissionReviewPage() {
  const { t } = useTranslation();
  const [reading, setReading] = useState<ModelReading | null>(null);
  const [unreadable, setUnreadable] = useState(false);
  const [rereadCount, setRereadCount] = useState(0);
  const [showDecided, setShowDecided] = useState(false);
  // One row's decision does not settle another's: a single busy row would let a second decision
  // re-enable the first row's buttons while its write is still in flight.
  const [busySubmissionIds, setBusySubmissionIds] = useState<ReadonlySet<string>>(new Set());
  const [promoting, setPromoting] = useState<Submission | null>(null);
  const [promotions, setPromotions] = useState<Record<string, PromotionResult>>({});

  // The page reads the model itself rather than through the app shell's load, which a model may
  // narrow to the properties it declares its pages are drawn with — and the marks this page reads
  // are nobody's idea of a page property. Reading again after a decision is also what makes the
  // decision show: the list is what the model says, not what this page remembers doing.
  useEffect(() => {
    let abandoned = false;
    Promise.all([thingApi.getAll(), relationshipApi.getAll(), thingApi.getAllProperties('effective')])
      .then(([things, relationships, properties]) => {
        if (abandoned) return;
        setReading({ things, relationships, properties });
        setUnreadable(false);
      })
      .catch(() => {
        if (abandoned) return;
        // What was read before is dropped rather than left on screen under a message saying the
        // model could not be read. A list that is no longer what the model says is worse than none.
        setReading(null);
        setUnreadable(true);
      });
    return () => {
      abandoned = true;
    };
  }, [rereadCount]);

  // Held against the reading rather than recomputed per render: each of these walks every Thing and
  // every edge in the model, and a render happens on every click.
  const model = reading ?? NOTHING_READ;
  const submissions = useMemo(() => byArrival(submissionsIn(model)), [model]);
  const predicateNames = useMemo(() => predicateNamesIn(model), [model]);
  const { marksProposedSite, resolvedAs, disposable, kept } = useMemo(
    () => ({
      marksProposedSite: proposedSitePredicate(model) !== null,
      resolvedAs: dispositionPredicate(model),
      disposable: disposableDisposition(model),
      kept: keptDisposition(model),
    }),
    [model],
  );
  const listed = showDecided ? submissions : submissions.filter((one) => !one.disposition);

  function markBusy(submissionId: string, busy: boolean): void {
    setBusySubmissionIds((already) => {
      const next = new Set(already);
      if (busy) next.add(submissionId);
      else next.delete(submissionId);
      return next;
    });
  }

  /** Relate a submission to what was decided about it, and record when. Who decided is the Fact the
   *  write itself lays down, which is the record that cannot be typed in. */
  async function resolve(submission: Submission, disposition: Disposition, predicate: string): Promise<void> {
    await relationshipApi.create(submission.id, predicate, disposition.id);
    await thingApi.setProperty(submission.id, RESOLVED_AT, 'vos.DateTime', new Date().toISOString());
  }

  async function reject(submission: Submission): Promise<void> {
    if (!disposable) {
      toast.error(t('submissionReview.noDisposableDisposition'));
      return;
    }
    if (!resolvedAs) {
      toast.error(t('submissionReview.noDispositionPredicate'));
      return;
    }
    markBusy(submission.id, true);
    try {
      await resolve(submission, disposable, resolvedAs);
      toast.success(t('submissionReview.rejected', { submission: named(submission), disposition: disposable.name }));
      setRereadCount((count) => count + 1);
    } catch (error) {
      toast.error(t('submissionReview.refused', { reason: reasonFor(error) }));
    } finally {
      markBusy(submission.id, false);
    }
  }

  // Refused before anything is built rather than after: a project whose submission still reads as
  // waiting is the trap promoting twice was made idempotent to avoid.
  //
  // The walk starts at the site the submission proposes, never at the record of the arrival: that
  // record belongs to intake and is resolved after the copy has landed, so a copy of it in a project
  // model would read as waiting for ever.
  async function promote(submission: Submission, plan: PromotionPlan): Promise<void> {
    if (!kept) {
      toast.error(t('submissionReview.noKeptDisposition'));
      return;
    }
    if (!resolvedAs) {
      toast.error(t('submissionReview.noDispositionPredicate'));
      return;
    }
    markBusy(submission.id, true);
    try {
      const promoted = await modelApi.promote(
        submission.proposedSiteId,
        plan.followedPredicateNames,
        plan.template,
        plan.projectName,
      );
      setPromotions((already) => ({ ...already, [submission.id]: promoted }));
      setPromoting(null);
      await resolve(submission, kept, resolvedAs);
      toast.success(t('submissionReview.promoted', { project: promoted.modelName }));
      setRereadCount((count) => count + 1);
    } catch (error) {
      toast.error(t('submissionReview.refused', { reason: reasonFor(error) }));
    } finally {
      markBusy(submission.id, false);
    }
  }

  if (reading === null && !unreadable) {
    return (
      <div className="h-full flex items-center justify-center p-8 text-sm text-zinc-500 dark:text-zinc-400">
        {t('common.loading')}
      </div>
    );
  }

  return (
    <div className="h-full flex flex-col bg-zinc-50 dark:bg-zinc-950">
      <header className="flex-shrink-0 flex items-center justify-between gap-4 flex-wrap px-6 pt-6 pb-4">
        <div className="flex items-center gap-3">
          <div className="w-9 h-9 rounded-lg grid place-items-center text-white bg-gradient-to-br from-blue-600 to-violet-500">
            <Inbox size={18} />
          </div>
          <div>
            <h2 className="text-xl font-bold text-zinc-900 dark:text-white leading-tight">
              {t('submissionReview.title')}
            </h2>
            <div className="text-xs text-zinc-400 dark:text-zinc-500">{t('submissionReview.subtitle')}</div>
          </div>
        </div>

        <div className="flex items-center gap-4">
          <label className="inline-flex items-center gap-2 text-xs text-zinc-500 dark:text-zinc-400">
            <input
              type="checkbox"
              checked={showDecided}
              onChange={(event) => setShowDecided(event.target.checked)}
              className="accent-blue-600"
            />
            {t('submissionReview.showDecided')}
          </label>
          <button
            onClick={() => setRereadCount((count) => count + 1)}
            className="inline-flex items-center gap-1.5 text-xs font-semibold px-3 py-1.5 rounded-md bg-white dark:bg-zinc-800 border border-zinc-200 dark:border-zinc-700 text-zinc-600 dark:text-zinc-300 hover:bg-zinc-100 dark:hover:bg-zinc-700"
          >
            <RefreshCw size={14} />
            {t('submissionReview.reread')}
          </button>
        </div>
      </header>

      <div className="flex-1 overflow-auto px-6 pb-10">
        {unreadable && <Notice>{t('submissionReview.unreadable')}</Notice>}
        {!unreadable && !marksProposedSite && <Notice>{t('submissionReview.noProposedSitePredicate')}</Notice>}
        {!unreadable && marksProposedSite && listed.length === 0 && (
          <Notice>{showDecided ? t('submissionReview.noneAtAll') : t('submissionReview.noneWaiting')}</Notice>
        )}
        {listed.length > 0 && (
          <table className="w-full text-sm border-separate border-spacing-y-1.5">
            <thead>
              <tr className="text-[11px] uppercase tracking-wider text-zinc-400 dark:text-zinc-500 text-left">
                <th className="font-semibold px-3 py-1">{t('submissionReview.arrived')}</th>
                <th className="font-semibold px-3 py-1">{t('submissionReview.state')}</th>
                <th className="font-semibold px-3 py-1">{t('submissionReview.submission')}</th>
                <th className="font-semibold px-3 py-1">{t('submissionReview.proposes')}</th>
                <th className="font-semibold px-3 py-1 text-right">{t('submissionReview.decision')}</th>
              </tr>
            </thead>
            <tbody>
              {listed.map((submission) => (
                <SubmissionRow
                  key={submission.id}
                  submission={submission}
                  promotion={promotions[submission.id]}
                  busy={busySubmissionIds.has(submission.id)}
                  onReject={() => void reject(submission)}
                  onPromote={() => setPromoting(submission)}
                />
              ))}
            </tbody>
          </table>
        )}
      </div>

      {promoting && (
        <PromoteDialog
          submission={promoting}
          predicateNames={predicateNames}
          busy={busySubmissionIds.has(promoting.id)}
          onCancel={() => setPromoting(null)}
          onPromote={(plan) => void promote(promoting, plan)}
        />
      )}
    </div>
  );
}

/** What a promotion is asked for: the template the project model is built from, what belongs with
 *  the site, and what the project is called. */
interface PromotionPlan {
  template: string;
  followedPredicateNames: string[];
  projectName: string;
}

function SubmissionRow({
  submission,
  promotion,
  busy,
  onReject,
  onPromote,
}: {
  submission: Submission;
  promotion?: PromotionResult;
  busy: boolean;
  onReject: () => void;
  onPromote: () => void;
}) {
  const { t } = useTranslation();
  return (
    <tr className="bg-white dark:bg-zinc-900">
      <td className="px-3 py-2 rounded-l-md font-mono text-xs text-zinc-500 dark:text-zinc-400 whitespace-nowrap">
        {submission.submittedAt ?? t('submissionReview.unrecorded')}
      </td>
      <td className="px-3 py-2">
        <span
          className={`inline-block text-[11px] font-semibold px-2 py-0.5 rounded-full ${
            submission.disposition
              ? 'bg-zinc-100 text-zinc-600 dark:bg-zinc-800 dark:text-zinc-300'
              : 'bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300'
          }`}
        >
          {submission.disposition ?? t('submissionReview.waiting')}
        </span>
      </td>
      <td className="px-3 py-2 text-zinc-800 dark:text-zinc-100">
        <div>{named(submission)}</div>
        {promotion && (
          <div className="text-[11px] text-emerald-600 dark:text-emerald-400">
            {t('submissionReview.promotedInto', { project: promotion.modelName })}
            <span className="font-mono text-zinc-400 dark:text-zinc-500"> {promotion.modelId}</span>
          </div>
        )}
      </td>
      <td className="px-3 py-2 text-zinc-600 dark:text-zinc-300">
        {submission.proposedSiteName ?? t('submissionReview.unnamedSite')}
      </td>
      <td className="px-3 py-2 rounded-r-md text-right whitespace-nowrap">
        <button
          onClick={onReject}
          disabled={busy}
          className="text-xs font-semibold px-2.5 py-1 rounded-md text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 disabled:opacity-40"
        >
          {t('submissionReview.reject')}
        </button>
        <button
          onClick={onPromote}
          disabled={busy}
          className="ms-1 text-xs font-semibold px-2.5 py-1 rounded-md bg-blue-600 text-white hover:bg-blue-700 disabled:opacity-40"
        >
          {t('submissionReview.promote')}
        </button>
      </td>
    </tr>
  );
}

function PromoteDialog({
  submission,
  predicateNames,
  busy,
  onCancel,
  onPromote,
}: {
  submission: Submission;
  predicateNames: readonly string[];
  busy: boolean;
  onCancel: () => void;
  onPromote: (plan: PromotionPlan) => void;
}) {
  const { t } = useTranslation();
  const [template, setTemplate] = useState('');
  const [projectName, setProjectName] = useState(submission.proposedSiteName ?? submission.name);
  const [travelling, setTravelling] = useState<string[]>([]);

  const ready = template.trim().length > 0 && projectName.trim().length > 0;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={onCancel}>
      <div
        role="dialog"
        aria-modal
        aria-labelledby="promote-dialog-title"
        className="bg-white dark:bg-zinc-800 rounded-lg shadow-xl p-6 max-w-lg w-full mx-4"
        onClick={(event) => event.stopPropagation()}
      >
        <h3 id="promote-dialog-title" className="text-lg font-semibold text-zinc-900 dark:text-zinc-100">
          {t('submissionReview.promoteTitle', { submission: named(submission) })}
        </h3>
        <p className="mt-1 text-sm text-zinc-600 dark:text-zinc-400">
          {t('submissionReview.promoteBody', { site: submission.proposedSiteName ?? t('submissionReview.unnamedSite') })}
        </p>

        <label className="block mt-4 text-xs font-semibold text-zinc-500 dark:text-zinc-400">
          {t('submissionReview.template')}
          <input
            value={template}
            onChange={(event) => setTemplate(event.target.value)}
            className="mt-1 w-full px-2 py-1.5 text-sm rounded-md bg-zinc-50 dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 text-zinc-900 dark:text-zinc-100 font-normal"
          />
        </label>
        <p className="mt-1 text-[11px] text-zinc-400 dark:text-zinc-500">{t('submissionReview.templateHint')}</p>

        <div className="mt-4 text-xs font-semibold text-zinc-500 dark:text-zinc-400">
          {t('submissionReview.travelsWith')}
        </div>
        <p className="mt-1 text-[11px] text-zinc-400 dark:text-zinc-500">{t('submissionReview.travelsWithHint')}</p>
        <div className="mt-2 max-h-40 overflow-auto flex flex-wrap gap-x-4 gap-y-1">
          {predicateNames.map((name) => (
            <label key={name} className="inline-flex items-center gap-1.5 text-sm text-zinc-700 dark:text-zinc-200">
              <input
                type="checkbox"
                checked={travelling.includes(name)}
                onChange={(event) =>
                  setTravelling((chosen) =>
                    event.target.checked ? [...chosen, name] : chosen.filter((one) => one !== name),
                  )
                }
                className="accent-blue-600"
              />
              {name}
            </label>
          ))}
        </div>

        <label className="block mt-4 text-xs font-semibold text-zinc-500 dark:text-zinc-400">
          {t('submissionReview.projectName')}
          <input
            value={projectName}
            onChange={(event) => setProjectName(event.target.value)}
            className="mt-1 w-full px-2 py-1.5 text-sm rounded-md bg-zinc-50 dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 text-zinc-900 dark:text-zinc-100 font-normal"
          />
        </label>

        <div className="mt-5 flex justify-end gap-2">
          <button
            onClick={onCancel}
            className="px-3 py-1.5 text-sm rounded-md bg-zinc-200 dark:bg-zinc-700 hover:bg-zinc-300 dark:hover:bg-zinc-600 text-zinc-900 dark:text-zinc-100"
          >
            {t('common.cancel')}
          </button>
          <button
            onClick={() =>
              onPromote({
                template: template.trim(),
                followedPredicateNames: travelling,
                projectName: projectName.trim(),
              })
            }
            disabled={!ready || busy}
            className="px-3 py-1.5 text-sm rounded-md bg-blue-600 hover:bg-blue-700 text-white disabled:opacity-40"
          >
            {t('submissionReview.promote')}
          </button>
        </div>
      </div>
    </div>
  );
}

function Notice({ children }: { children: React.ReactNode }) {
  return (
    <div className="max-w-2xl mt-6 text-sm text-zinc-500 dark:text-zinc-400 bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-800 rounded-lg p-4">
      {children}
    </div>
  );
}

/** What a reviewer calls a submission: the identifier it was submitted under, or its name where it
 *  arrived without one. */
function named(submission: Submission): string {
  return submission.submissionId ?? submission.name;
}

function reasonFor(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
