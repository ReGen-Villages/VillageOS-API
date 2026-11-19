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
import { ClipboardList } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { FormOptions } from '../api/intakeApi';
import { useFormOptions } from '../hooks/useFormOptions';
import { StandalonePage } from '../components/common/StandalonePage';
import { useStandalonePageDocument } from '../hooks/useStandalonePageDocument';
import { IntakeWizard } from '../intake/IntakeWizard';

/** One submitter in one browser, so the draft belongs to the form rather than to a model — which is what
 *  it belongs to when a planner fills the same wizard in. */
const DRAFT_OWNER = 'public-form';

const NO_OPTIONS_YET: FormOptions = {
  allocationCategories: [], basemapSources: [], hazardTypes: [], hazardLevels: [],
  defaultProgramme: [], parcelLookup: false, placeSearch: false, themes: [], layerKinds: [], wording: {},
};

export function PublicSubmissionPage() {
  const { t } = useTranslation();
  useStandalonePageDocument('publicForm.title');
  const { options: answered, unreachable } = useFormOptions();
  const options = answered ?? NO_OPTIONS_YET;

  return (
    <StandalonePage icon={<ClipboardList size={18} />} title={t('publicForm.title')} intro={t('publicForm.intro')} width="narrow">
      {unreachable && (
        <p className="mb-4 text-sm text-amber-600 dark:text-amber-400">{t('publicForm.unreachable')}</p>
      )}
      <IntakeWizard
        categories={options.allocationCategories}
        basemapSources={options.basemapSources}
        hazardTypes={options.hazardTypes}
        hazardLevels={options.hazardLevels}
        wording={options.wording}
        draftOwner={DRAFT_OWNER}
      />
      <p className="mt-6 text-[11px] text-zinc-400 dark:text-zinc-500">
        {t('publicForm.keptInThisBrowser')}
      </p>
      <p className="mt-2 text-[11px] text-zinc-400 dark:text-zinc-500">
        <a className="hover:underline" href="./explore.html">{t('publicForm.tryExplore')}</a>
      </p>
    </StandalonePage>
  );
}
