/**
 * The form is the shared wizard the public page draws too; this page adds the frame and the read. The
 * vocabulary and imagery are read from the model itself, because the shell's load may be narrowed to what the
 * pages draw and a role Thing is on no page. A model declaring no categories leaves that step saying so.
 */
import { useEffect, useState } from 'react';
import { ClipboardList } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { discoverBasemapSources } from '../api/basemapApi';
import { modelIndexFor } from '@tendril-ui/tendril';
import { relationshipApi } from '../api/relationshipApi';
import { thingApi } from '../api/thingApi';
import { useAuthentication } from '../hooks/useAuthentication';
import { IntakeWizard } from '../intake/IntakeWizard';
import type { BasemapSource } from '../types/basemap';
import {
  ALLOCATION_CATEGORY_ROLE,
  HAZARD_LEVEL_ROLE,
  HAZARD_TYPE_ROLE,
  termsBound,
  termWordingBound,
} from './modelVocabulary';
import type { TermWording } from '@tendril-ui/tendril';

export function IntakeWizardPage() {
  const { t } = useTranslation();
  const { modelId } = useAuthentication();
  const [categories, setCategories] = useState<readonly string[]>([]);
  const [basemapSources, setBasemapSources] = useState<BasemapSource[]>([]);
  const [hazardTypes, setHazardTypes] = useState<readonly string[]>([]);
  const [hazardLevels, setHazardLevels] = useState<readonly string[]>([]);
  const [wording, setWording] = useState<TermWording>({});

  useEffect(() => {
    let abandoned = false;
    Promise.all([thingApi.getAll(), relationshipApi.getAll(), thingApi.getAllProperties()])
      .then(([things, relationships, properties]) => {
        if (abandoned) return;
        setCategories(termsBound({ things, relationships, properties }, ALLOCATION_CATEGORY_ROLE));
        setBasemapSources(discoverBasemapSources(modelIndexFor(things, relationships)));
        const read = { things, relationships, properties };
        setHazardTypes(termsBound(read, HAZARD_TYPE_ROLE));
        setHazardLevels(termsBound(read, HAZARD_LEVEL_ROLE));
        setWording(termWordingBound(read, [ALLOCATION_CATEGORY_ROLE, HAZARD_TYPE_ROLE, HAZARD_LEVEL_ROLE]));
      })
      .catch(() => {
        if (abandoned) return;
        setCategories([]);
        setBasemapSources([]);
        setHazardTypes([]);
        setHazardLevels([]);
        setWording({});
      });
    return () => {
      abandoned = true;
    };
  }, []);

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
          <IntakeWizard
            categories={categories}
            basemapSources={basemapSources}
            hazardTypes={hazardTypes}
            hazardLevels={hazardLevels}
            wording={wording}
            draftOwner={modelId}
          />
        </div>
      </div>
    </div>
  );
}
