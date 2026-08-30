/**
 * What the platform worked out about what one submitter submitted, as the submitter reaches it: a page
 * on a public website, holding no account and no credential of any kind.
 *
 * It draws the page the model declares — the same spec, the same widgets and the same words the
 * signed-in dashboard draws, so a figure added to that page appears here without a change. What differs
 * is where the reading comes from: the application reads the model over the authenticated broker, and
 * this page cannot, so the intake service reads it under its own credential and answers with one
 * submission's findings and nothing else.
 *
 * Nothing here reaches the broker. That is the point of the page rather than an accident of how it was
 * written, and `../publicForm/noSignedInCode.test.ts` fails if an import ever leads back to it.
 */
import { useEffect, useMemo, useState } from 'react';
import { Sprout } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { findingsApi } from '../api/findingsApi';
import { localizeSpec } from '../api/dashboardLocalization';
import { DashboardSections } from '../components/dashboard/DashboardSections';
import { LanguageSwitcher } from '../components/common/LanguageSwitcher';
import { ToastContainer } from '../components/common/Toast';
import { useElementWidth } from '../hooks/useElementWidth';
import { useResolveContext } from '../hooks/useDashboard';
import { directionFor } from '../i18n/languages';
import { attachThemeMediaListener, useThemeStore } from '../stores/themeStore';
import { findingsFrom, type Findings } from './answeredFindings';

/** The width above which a section lays its widgets out in tracks, as the signed-in page uses. */
const WIDE = 720;

export function PublicFindingsPage() {
  const { t, i18n } = useTranslation();
  const theme = useThemeStore((state) => state.theme);
  const [findings, setFindings] = useState<Findings | null>(null);

  // The signed-in application's shell sets these two on the document for its own pages. This page is
  // served on its own, so it sets them itself and does nothing else that shell does.
  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark');
  }, [theme]);

  useEffect(() => {
    const root = document.documentElement;
    root.setAttribute('lang', i18n.language);
    root.setAttribute('dir', directionFor(i18n.language));
  }, [i18n.language]);

  useEffect(() => attachThemeMediaListener(), []);

  return (
    <div className="min-h-screen bg-zinc-50 dark:bg-zinc-950">
      <header className="max-w-5xl mx-auto flex items-center gap-3 px-6 pt-10 pb-4">
        <div className="w-9 h-9 rounded-lg grid place-items-center text-white bg-gradient-to-br from-emerald-600 to-teal-500">
          <Sprout size={18} />
        </div>
        <div className="flex-1">
          <h1 className="text-xl font-bold text-zinc-900 dark:text-white leading-tight">
            {t('publicFindings.title')}
          </h1>
          <p className="text-xs text-zinc-500 dark:text-zinc-400">{t('publicFindings.intro')}</p>
        </div>
        <LanguageSwitcher />
      </header>

      <main className="max-w-5xl mx-auto px-6 pb-16">
        {findings ? (
          <>
            <Drawn findings={findings} />
            <button
              type="button"
              onClick={() => setFindings(null)}
              className="mt-8 text-xs font-semibold text-blue-600 dark:text-blue-400 hover:underline"
            >
              {t('publicFindings.askAgain')}
            </button>
          </>
        ) : (
          <AskForFindings onRead={setFindings} />
        )}
      </main>

      <ToastContainer />
    </div>
  );
}

/** The exchange that stands in for an account: the reference names a submission, the code proves the
 *  mailbox, and the service refuses unless the submission names that mailbox. */
function AskForFindings({ onRead }: { onRead: (findings: Findings) => void }) {
  const { t } = useTranslation();
  const [submissionId, setSubmissionId] = useState('');
  const [emailAddress, setEmailAddress] = useState('');
  const [code, setCode] = useState('');
  const [sent, setSent] = useState(false);
  const [busy, setBusy] = useState(false);
  const [refusal, setRefusal] = useState<string | null>(null);

  const canAsk = submissionId.trim().length > 0 && emailAddress.trim().length > 0;

  async function attempt(act: () => Promise<void>) {
    setBusy(true);
    setRefusal(null);
    try {
      await act();
    } catch (error) {
      setRefusal(error instanceof Error ? error.message : t('publicFindings.unreachable'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form
      className="max-w-md space-y-4"
      onSubmit={(event) => {
        event.preventDefault();
        void attempt(async () =>
          onRead(findingsFrom(await findingsApi.read(submissionId.trim(), emailAddress.trim(), code.trim()))));
      }}
    >
      <Field label={t('publicFindings.reference')} hint={t('publicFindings.referenceHint')}>
        <input
          value={submissionId}
          onChange={(event) => setSubmissionId(event.target.value)}
          className={FIELD}
          autoComplete="off"
        />
      </Field>

      <Field label={t('publicFindings.emailAddress')} hint={t('publicFindings.emailAddressHint')}>
        <input
          type="email"
          value={emailAddress}
          onChange={(event) => setEmailAddress(event.target.value)}
          className={FIELD}
          autoComplete="email"
        />
      </Field>

      {!sent ? (
        <button
          type="button"
          disabled={!canAsk || busy}
          onClick={() =>
            void attempt(async () => {
              await findingsApi.askForCode(emailAddress.trim());
              setSent(true);
            })
          }
          className={BUTTON}
        >
          {t('publicFindings.sendCode')}
        </button>
      ) : (
        <>
          <p className="text-xs text-zinc-500 dark:text-zinc-400">
            {t('publicFindings.codeSent', { address: emailAddress.trim() })}
          </p>
          <Field label={t('publicFindings.code')}>
            <input
              value={code}
              onChange={(event) => setCode(event.target.value)}
              placeholder={t('publicFindings.codePlaceholder')}
              className={FIELD}
              inputMode="numeric"
              autoComplete="one-time-code"
            />
          </Field>
          <button type="submit" disabled={busy || code.trim().length === 0} className={BUTTON}>
            {busy ? t('publicFindings.reading') : t('publicFindings.read')}
          </button>
        </>
      )}

      {refusal && <p className="text-sm text-amber-600 dark:text-amber-400">{refusal}</p>}
    </form>
  );
}

function Drawn({ findings }: { findings: Findings }) {
  const { t, i18n } = useTranslation();
  const [measure, width] = useElementWidth();
  const spec = useMemo(() => localizeSpec(findings.spec, i18n.language), [findings.spec, i18n.language]);
  const ctx = useResolveContext(findings.index, findings.scopeId, spec.compare?.archetype, () => findings.reads);

  return (
    <div ref={measure}>
      <h2 className="text-lg font-bold text-zinc-900 dark:text-white">{spec.title}</h2>
      {spec.subtitle && <p className="text-xs text-zinc-500 dark:text-zinc-400">{spec.subtitle}</p>}
      <DashboardSections
        sections={spec.sections}
        ctx={ctx}
        isWide={width >= WIDE}
        whenEmpty={<p className="mt-4 text-sm text-zinc-500 dark:text-zinc-400">{t('publicFindings.emptyView')}</p>}
      />
    </div>
  );
}

const FIELD =
  'w-full rounded-md border border-zinc-300 dark:border-zinc-700 bg-white dark:bg-zinc-900 '
  + 'px-3 py-2 text-sm text-zinc-900 dark:text-zinc-100';

const BUTTON =
  'rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white disabled:opacity-50';

function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="block text-xs font-semibold text-zinc-600 dark:text-zinc-300 mb-1">{label}</span>
      {children}
      {hint && <span className="block text-[11px] text-zinc-400 dark:text-zinc-500 mt-1">{hint}</span>}
    </label>
  );
}
