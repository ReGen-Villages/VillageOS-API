/**
 * The surveys a submitter can share once the report is up: a soil test, a water report, a topographical
 * survey. Each file goes to the intake service with the description typed beside it, one after another
 * so every send rides the ticket the last one handed back, and what the model holds is listed after.
 */
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  MAXIMUM_SURVEY_MEGABYTES,
  asRefused,
  asSending,
  asShared,
  readyToSend,
  withChosen,
  withDescription,
  withListed,
  withoutQueued,
  type QueuedSurvey,
  type SharedSurvey,
} from './sharedSurveys';

type Answer = 'unanswered' | 'yes' | 'notNow';

export function SurveysStep({
  share,
  list,
}: {
  share: (file: File, description: string, onProgress: (fraction: number) => void) => Promise<SharedSurvey>;
  list: () => Promise<SharedSurvey[]>;
}) {
  const { t, i18n } = useTranslation();
  const [answer, setAnswer] = useState<Answer>('unanswered');
  const [queue, setQueue] = useState<readonly QueuedSurvey[]>([]);
  const [shared, setShared] = useState<readonly SharedSurvey[]>([]);
  const [sending, setSending] = useState(false);
  const percent = useMemo(() => new Intl.NumberFormat(i18n.language, { style: 'percent' }), [i18n.language]);

  useEffect(() => {
    let abandoned = false;
    list()
      .then((listed) => {
        if (!abandoned) setShared((current) => withListed(current, listed));
      })
      .catch(() => undefined);
    return () => {
      abandoned = true;
    };
  }, [list]);

  async function send(): Promise<void> {
    setSending(true);
    try {
      for (const queued of readyToSend(queue)) {
        setQueue((current) => asSending(current, queued.key, 0));
        try {
          const document = await share(queued.file, queued.description.trim(), (fraction) =>
            setQueue((current) => asSending(current, queued.key, fraction)));
          setQueue((current) => asShared(current, queued.key));
          setShared((current) => withListed(current, [document]));
        } catch (error) {
          setQueue((current) =>
            asRefused(current, queued.key, error instanceof Error ? error.message : String(error)));
        }
      }
    } finally {
      setSending(false);
    }
  }

  const tooLarge = t('explore.surveys.tooLarge', { megabytes: MAXIMUM_SURVEY_MEGABYTES });
  const asked = answer !== 'notNow';

  if (!asked && shared.length === 0) return null;

  return (
    <section className="mt-5 bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-800 rounded-lg p-5">
      {asked && (
        <>
          <h2 className="text-base font-semibold text-zinc-900 dark:text-zinc-100">{t('explore.surveys.title')}</h2>
          <p className="mb-4 text-xs text-zinc-400 dark:text-zinc-500">
            {t('explore.surveys.hint', { megabytes: MAXIMUM_SURVEY_MEGABYTES })}
          </p>
        </>
      )}

      {answer === 'unanswered' && (
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => setAnswer('yes')}
            className="px-3 py-1.5 text-sm rounded-md bg-emerald-600 hover:bg-emerald-700 text-white"
          >
            {t('explore.surveys.yes')}
          </button>
          <button
            type="button"
            onClick={() => setAnswer('notNow')}
            className="px-3 py-1.5 text-sm rounded-md bg-zinc-200 dark:bg-zinc-700 text-zinc-900 dark:text-zinc-100 hover:bg-zinc-300 dark:hover:bg-zinc-600"
          >
            {t('explore.surveys.notNow')}
          </button>
        </div>
      )}

      {answer === 'yes' && (
        <>
          <label className="inline-block px-3 py-1.5 text-sm rounded-md bg-zinc-200 dark:bg-zinc-700 text-zinc-900 dark:text-zinc-100 hover:bg-zinc-300 dark:hover:bg-zinc-600 cursor-pointer">
            {t('explore.surveys.choose')}
            <input
              type="file"
              multiple
              aria-label={t('explore.surveys.choose')}
              className="sr-only"
              onChange={(event) => {
                const chosen = Array.from(event.target.files ?? []);
                setQueue((current) => withChosen(current, chosen, () => crypto.randomUUID(), tooLarge));
                event.target.value = '';
              }}
            />
          </label>

          {queue.length > 0 && (
            <ul className="mt-3 space-y-3">
              {queue.map((queued) => (
                <li key={queued.key} className="rounded-md border border-zinc-200 dark:border-zinc-700 p-3">
                  <div className="flex items-center justify-between gap-3">
                    <span className="text-sm font-medium text-zinc-800 dark:text-zinc-100 truncate">{queued.file.name}</span>
                    <span className="text-xs text-zinc-500 dark:text-zinc-400 tabular-nums">{sizeOf(queued.file.size, i18n.language)}</span>
                    {queued.state !== 'sending' && queued.state !== 'shared' && (
                      <button
                        type="button"
                        onClick={() => setQueue((current) => withoutQueued(current, queued.key))}
                        className="text-xs text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200"
                      >
                        {t('explore.surveys.remove')}
                      </button>
                    )}
                  </div>
                  {queued.state === 'waiting' && (
                    <input
                      aria-label={t('explore.surveys.description')}
                      value={queued.description}
                      onChange={(event) => setQueue((current) => withDescription(current, queued.key, event.target.value))}
                      placeholder={t('explore.surveys.descriptionPlaceholder')}
                      className="mt-2 w-full px-2 py-1.5 text-sm rounded-md bg-zinc-50 dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 text-zinc-900 dark:text-zinc-100"
                    />
                  )}
                  {queued.state === 'sending' && (
                    <div className="mt-2 flex items-center gap-2 text-xs text-zinc-500 dark:text-zinc-400">
                      <progress value={queued.progress} max={1} className="flex-1 h-1.5" />
                      <span className="tabular-nums">{t('explore.surveys.sending', { percent: percent.format(queued.progress) })}</span>
                    </div>
                  )}
                  {queued.state === 'shared' && (
                    <p className="mt-2 text-xs text-emerald-700 dark:text-emerald-400">{t('explore.surveys.shared')}</p>
                  )}
                  {queued.state === 'refused' && (
                    <p className="mt-2 text-xs text-amber-700 dark:text-amber-400">
                      {t('explore.surveys.refused', { reason: queued.reason })}
                    </p>
                  )}
                </li>
              ))}
            </ul>
          )}

          {readyToSend(queue).length > 0 && (
            <button
              type="button"
              onClick={() => void send()}
              disabled={sending}
              className="mt-3 px-3 py-1.5 text-sm rounded-md bg-emerald-600 hover:bg-emerald-700 text-white disabled:opacity-40"
            >
              {t('explore.surveys.send')}
            </button>
          )}
        </>
      )}

      {shared.length > 0 && (
        <>
          <h3 className="mt-5 text-xs font-semibold text-zinc-500 dark:text-zinc-400">{t('explore.surveys.listTitle')}</h3>
          <ul aria-label={t('explore.surveys.listTitle')} className="mt-2 divide-y divide-zinc-200 dark:divide-zinc-800">
            {shared.map((survey) => (
              <li key={survey.id} className="py-2 flex items-baseline justify-between gap-3">
                <div className="min-w-0">
                  <div className="text-sm font-medium text-zinc-800 dark:text-zinc-100 truncate">{survey.fileName}</div>
                  <div className="text-xs text-zinc-500 dark:text-zinc-400">
                    {survey.description && survey.description.length > 0 ? survey.description : t('explore.surveys.noDescription')}
                  </div>
                </div>
                {survey.sizeBytes !== null && (
                  <span className="text-xs text-zinc-500 dark:text-zinc-400 tabular-nums">{sizeOf(survey.sizeBytes, i18n.language)}</span>
                )}
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}

function sizeOf(bytes: number, language: string): string {
  const megabyte = 1024 * 1024;
  return bytes >= megabyte
    ? new Intl.NumberFormat(language, { style: 'unit', unit: 'megabyte', maximumFractionDigits: 1 }).format(bytes / megabyte)
    : new Intl.NumberFormat(language, { style: 'unit', unit: 'kilobyte', maximumFractionDigits: 0 }).format(bytes / 1024);
}
