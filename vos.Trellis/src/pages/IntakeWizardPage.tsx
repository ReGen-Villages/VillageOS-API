/**
 * The land-intake wizard as a signed-in planner reaches it. The form itself is the shared wizard, which
 * the public submission page renders too; what this page adds is the frame around it and the read that
 * fills it in.
 *
 * The vocabulary and the imagery are read from the model itself rather than through the app shell's load,
 * which a model may narrow to the properties its pages are drawn with — and a mark is nobody's idea of a
 * page property. A model that declares no categories leaves the step saying so rather than empty.
 */
import { useEffect, useState } from 'react';
import { ClipboardList } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { discoverBasemapSources } from '../api/basemapApi';
import { modelIndexFor } from '../api/dashboardApi';
import { relationshipApi } from '../api/relationshipApi';
import { thingApi } from '../api/thingApi';
import { useAuth } from '../hooks/useAuth';
import { IntakeWizard } from '../intake/IntakeWizard';
import type { BasemapSource } from '../types/basemap';
import {
  ALLOCATION_CATEGORY_ARCHETYPE_FLAG,
  HAZARD_LEVEL_ARCHETYPE_FLAG,
  HAZARD_TYPE_ARCHETYPE_FLAG,
  termsMarked,
} from './modelVocabulary';

export function IntakeWizardPage() {
  const { t } = useTranslation();
  const { modelId } = useAuth();
  const [categories, setCategories] = useState<readonly string[]>([]);
  const [basemapSources, setBasemapSources] = useState<BasemapSource[]>([]);
  const [hazardTypes, setHazardTypes] = useState<readonly string[]>([]);
  const [hazardLevels, setHazardLevels] = useState<readonly string[]>([]);

  useEffect(() => {
    let abandoned = false;
    Promise.all([thingApi.getAll(), relationshipApi.getAll(), thingApi.getAllProperties('effective')])
      .then(([things, relationships, properties]) => {
        if (abandoned) return;
        setCategories(termsMarked({ things, relationships, properties }, ALLOCATION_CATEGORY_ARCHETYPE_FLAG));
        setBasemapSources(discoverBasemapSources(modelIndexFor(things, relationships)));
        const read = { things, relationships, properties };
        setHazardTypes(termsMarked(read, HAZARD_TYPE_ARCHETYPE_FLAG));
        setHazardLevels(termsMarked(read, HAZARD_LEVEL_ARCHETYPE_FLAG));
      })
      .catch(() => {
        if (abandoned) return;
        setCategories([]);
        setBasemapSources([]);
        setHazardTypes([]);
        setHazardLevels([]);
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
            draftOwner={modelId}
          />
        </div>
      </div>
    </div>
  );
}
