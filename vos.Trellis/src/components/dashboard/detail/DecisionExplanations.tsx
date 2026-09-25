/**
 * Why a service decided about the Thing this card is open on: what it chose, the rule it decided
 * under, what spoke for the choice and what turned candidates away.
 *
 * Both halves are drawn by one renderer, because both are the same Thing held through a different
 * predicate. Every number a constraint names was read as it stood when the decision was taken, so a
 * card explaining this morning's decision shows this morning's values; a value the platform cannot
 * answer for that instant reads as not recorded rather than as today's.
 */
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ChevronDown, ChevronRight } from 'lucide-react';
import { formatDateTime, formatPropertyValue } from '../../../utils/formatters';
import { useNumberDisplaySettings } from '../../../hooks/useNumberDisplaySettings';
import { useDecisionExplanations } from './useDecisionExplanations';
import {
  comparisonsOf,
  type Comparison,
  type Consideration,
  type DecisionExplanation,
  type InstantReadings,
  type NamedThing,
} from './decisionExplanation';

interface Props {
  decisions: DecisionExplanation[];
  openDetail: (thingId: string) => void;
}

function ThingLink({ thing, openDetail }: { thing: NamedThing; openDetail: (thingId: string) => void }) {
  return (
    <button
      onClick={() => openDetail(thing.id)}
      className="font-semibold text-blue-600 dark:text-blue-400 hover:underline"
    >
      {thing.name}
    </button>
  );
}

function ConsiderationRow({
  decision,
  consideration,
  readings,
  settled,
  openDetail,
}: {
  decision: DecisionExplanation;
  consideration: Consideration;
  readings: InstantReadings;
  settled: boolean;
  openDetail: (thingId: string) => void;
}) {
  const { t } = useTranslation();
  const numbers = useNumberDisplaySettings();
  const comparisons = comparisonsOf(decision, consideration, readings);
  const named = consideration.refusedCandidates;
  const total = consideration.candidatesRefused;

  const amount = (value: number | null) =>
    value === null
      ? settled
        ? t('entityDetail.decisionNotRecorded')
        : t('entityDetail.loading')
      : formatPropertyValue(value, 'vos.Double', numbers);

  const measureText = (comparison: Comparison) =>
    comparison.limit === undefined
      ? t('entityDetail.decisionMeasure', {
          measure: amount(comparison.measure),
          unit: comparison.unit ?? '',
        }).trim()
      : t('entityDetail.decisionMeasureAgainstLimit', {
          measure: amount(comparison.measure),
          limit: amount(comparison.limit),
          unit: comparison.unit ?? '',
        }).trim();

  return (
    <li className="text-[11.5px]">
      <span className="flex flex-wrap items-center gap-x-1.5">
        {consideration.cause && <ThingLink thing={consideration.cause} openDetail={openDetail} />}
        {consideration.cited && (
          <span className="text-zinc-400 dark:text-zinc-500">
            {t('entityDetail.decisionAbout')} <ThingLink thing={consideration.cited} openDetail={openDetail} />
          </span>
        )}
      </span>
      {comparisons.map((comparison, position) => (
        <span key={comparison.candidate?.id ?? position} className="flex flex-wrap items-center gap-x-1.5 pl-3">
          {comparison.candidate && <ThingLink thing={comparison.candidate} openDetail={openDetail} />}
          <span className="font-mono text-zinc-700 dark:text-zinc-200">
            {measureText(comparison)}
          </span>
        </span>
      ))}
      {consideration.half === 'refusal' && (named.length > 0 || total !== undefined) && (
        <span className="flex flex-wrap items-center gap-x-1.5 pl-3 text-zinc-400 dark:text-zinc-500">
          {t('entityDetail.decisionTurnedAway')}
          {named.map((candidate) => (
            <ThingLink key={candidate.id} thing={candidate} openDetail={openDetail} />
          ))}
          {total !== undefined && total > named.length && (
            <span>{t('entityDetail.decisionRefusedInTotal', { count: total })}</span>
          )}
        </span>
      )}
    </li>
  );
}

function ConsiderationHalf({
  title,
  decision,
  considerations,
  readings,
  settled,
  openDetail,
}: {
  title: string;
  decision: DecisionExplanation;
  considerations: Consideration[];
  readings: InstantReadings;
  settled: boolean;
  openDetail: (thingId: string) => void;
}) {
  if (considerations.length === 0) return null;
  return (
    <div className="mt-1.5">
      <div className="text-[10px] font-semibold uppercase tracking-wide text-zinc-400 dark:text-zinc-500">
        {title}
      </div>
      <ul className="mt-0.5 space-y-1">
        {considerations.map((consideration) => (
          <ConsiderationRow
            key={consideration.id}
            decision={decision}
            consideration={consideration}
            readings={readings}
            settled={settled}
            openDetail={openDetail}
          />
        ))}
      </ul>
    </div>
  );
}

function DecisionBlock({
  decision,
  readings,
  settled,
  openDetail,
}: {
  decision: DecisionExplanation;
  readings: InstantReadings;
  settled: boolean;
  openDetail: (thingId: string) => void;
}) {
  const { t } = useTranslation();
  const numbers = useNumberDisplaySettings();

  return (
    <div className="rounded-md border border-zinc-200 dark:border-zinc-700 px-2 py-1.5">
      <div className="flex flex-wrap items-baseline gap-x-2">
        <span className="text-[12px] font-semibold text-zinc-700 dark:text-zinc-200">
          {decision.kindName ?? decision.name}
        </span>
        <span className="text-[10.5px] font-mono text-zinc-400 dark:text-zinc-500">
          {decision.decidedAt ? formatDateTime(decision.decidedAt) : t('entityDetail.decisionNotRecorded')}
        </span>
      </div>

      <div className="mt-0.5 flex flex-wrap items-center gap-x-1.5 text-[11.5px]">
        <span className="text-zinc-400 dark:text-zinc-500">{t('entityDetail.decisionChose')}</span>
        {decision.chose ? (
          <ThingLink thing={decision.chose} openDetail={openDetail} />
        ) : (
          <span className="text-zinc-500 dark:text-zinc-400">{t('entityDetail.decisionChoseNothing')}</span>
        )}
        {decision.under && (
          <>
            <span className="text-zinc-400 dark:text-zinc-500">{t('entityDetail.decisionUnder')}</span>
            <ThingLink thing={decision.under} openDetail={openDetail} />
          </>
        )}
      </div>

      {(decision.score !== undefined || decision.candidatesConsidered !== undefined) && (
        <div className="mt-0.5 flex flex-wrap gap-x-2 text-[10.5px] text-zinc-400 dark:text-zinc-500">
          {decision.score !== undefined && (
            <span>
              {t('entityDetail.decisionScore')}{' '}
              {formatPropertyValue(decision.score, 'vos.Double', numbers)}
            </span>
          )}
          {decision.candidatesConsidered !== undefined && (
            <span>
              {t('entityDetail.decisionConsidered')} {decision.candidatesConsidered}
            </span>
          )}
        </div>
      )}

      <ConsiderationHalf
        title={t('entityDetail.decisionForTheChoice')}
        decision={decision}
        considerations={decision.supports}
        readings={readings}
        settled={settled}
        openDetail={openDetail}
      />
      <ConsiderationHalf
        title={t('entityDetail.decisionTurnedCandidatesAway')}
        decision={decision}
        considerations={decision.refusals}
        readings={readings}
        settled={settled}
        openDetail={openDetail}
      />

      {decision.decidedAt && (
        <div className="mt-1.5 text-[10.5px] text-zinc-400 dark:text-zinc-500">
          {t('entityDetail.decisionAtTheInstant', { time: formatDateTime(decision.decidedAt) })}
        </div>
      )}
    </div>
  );
}

export function DecisionExplanations({ decisions, openDetail }: Props) {
  const { t } = useTranslation();
  const [earlierShown, setEarlierShown] = useState(false);
  const [latest, ...earlier] = decisions;
  const onScreen = useMemo(() => (earlierShown ? decisions : decisions.slice(0, 1)), [decisions, earlierShown]);
  const { readings, settled } = useDecisionExplanations(onScreen);

  return (
    <div className="space-y-1.5">
      <DecisionBlock decision={latest} readings={readings} settled={settled} openDetail={openDetail} />
      {earlier.length > 0 && (
        <button
          onClick={() => setEarlierShown((shown) => !shown)}
          className="flex items-center gap-1 text-[11px] text-zinc-500 dark:text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200"
        >
          {earlierShown ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
          {t('entityDetail.decisionEarlier', { count: earlier.length })}
        </button>
      )}
      {earlierShown &&
        earlier.map((decision) => (
          <DecisionBlock
            key={decision.id}
            decision={decision}
            readings={readings}
            settled={settled}
            openDetail={openDetail}
          />
        ))}
    </div>
  );
}
