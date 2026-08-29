/**
 * The submission form as somebody with land reaches it: a page on a public site, holding no account and
 * no credential of any kind.
 *
 * It renders the same wizard the planner's page renders, so a field added to one appears in the other.
 * What differs is where the two get what the form is drawn with. The planner's page reads the model over
 * the authenticated broker; this page cannot, and asks the intake service, which reads the model under
 * its own credential and answers with the categories and the imagery and nothing else.
 *
 * Nothing here reaches the broker. That is the point of the page rather than an accident of how it was
 * written, and `noSignedInCode.test.ts` fails if an import ever leads back to it.
 */
import { useEffect, useState } from 'react';
import { ClipboardList } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { intakeApi, type FormOptions } from '../api/intakeApi';
import { LanguageSwitcher } from '../components/common/LanguageSwitcher';
import { ToastContainer } from '../components/common/Toast';
import { directionFor } from '../i18n/languages';
import { IntakeWizard } from '../intake/IntakeWizard';
import { attachThemeMediaListener, useThemeStore } from '../stores/themeStore';

/** One submitter in one browser, so the draft belongs to the form rather than to a model — which is what
 *  it belongs to when a planner fills the same wizard in. */
const DRAFT_OWNER = 'public-form';

export function PublicSubmissionPage() {
  const { t, i18n } = useTranslation();
  const theme = useThemeStore((state) => state.theme);
  const [options, setOptions] = useState<FormOptions>({ allocationCategories: [], basemapSources: [], hazardTypes: [], hazardLevels: [] });
  const [unreachable, setUnreachable] = useState(false);

  // The signed-in application's shell sets these two on the document for its own pages. This form is
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

  useEffect(() => {
    let abandoned = false;
    intakeApi
      .formOptions()
      .then((answered) => {
        if (!abandoned) setOptions(answered);
      })
      .catch(() => {
        if (!abandoned) setUnreachable(true);
      });
    return () => {
      abandoned = true;
    };
  }, []);

  return (
    <div className="min-h-screen bg-zinc-50 dark:bg-zinc-950">
      <header className="max-w-2xl mx-auto flex items-center gap-3 px-6 pt-10 pb-4">
        <div className="w-9 h-9 rounded-lg grid place-items-center text-white bg-gradient-to-br from-emerald-600 to-teal-500">
          <ClipboardList size={18} />
        </div>
        <div className="flex-1">
          <h1 className="text-xl font-bold text-zinc-900 dark:text-white leading-tight">
            {t('publicForm.title')}
          </h1>
          <p className="text-xs text-zinc-500 dark:text-zinc-400">{t('publicForm.intro')}</p>
        </div>
        <LanguageSwitcher />
      </header>

      <main className="max-w-2xl mx-auto px-6 pb-16">
        {unreachable && (
          <p className="mb-4 text-sm text-amber-600 dark:text-amber-400">{t('publicForm.unreachable')}</p>
        )}
        <IntakeWizard
          categories={options.allocationCategories}
          basemapSources={options.basemapSources}
          draftOwner={DRAFT_OWNER}
        />
        <p className="mt-6 text-[11px] text-zinc-400 dark:text-zinc-500">
          {t('publicForm.keptInThisBrowser')}
        </p>
      </main>

      <ToastContainer />
    </div>
  );
}
