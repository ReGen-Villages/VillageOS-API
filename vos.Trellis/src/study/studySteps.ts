export type Edition = 'lite' | 'pro';

export type StudyStep = 'location' | 'data' | 'village' | 'layout' | 'villageLayout' | 'timeline';

export const EDITIONS: readonly Edition[] = ['lite', 'pro'];

export const STUDY_STEPS: readonly StudyStep[] = ['location', 'data', 'village', 'layout', 'villageLayout', 'timeline'];

const STEPS: Record<Edition, StudyStep[]> = {
  pro: ['location', 'data', 'village', 'layout', 'timeline'],
  lite: ['location', 'data', 'villageLayout', 'timeline'],
};

const STEPS_WITH_A_PAGE: ReadonlySet<StudyStep> = new Set(['location']);

const EDITION_KEY = 'studyEdition';

export function stepsFor(edition: Edition): StudyStep[] {
  return STEPS[edition];
}

export function canOpen(step: StudyStep, locationConfirmed: boolean): true | 'locationNeeded' | 'notBuilt' {
  if (step === 'location') return true;
  if (!locationConfirmed) return 'locationNeeded';
  return STEPS_WITH_A_PAGE.has(step) ? true : 'notBuilt';
}

// A browser that keeps nothing, as in a private window, is given Lite, as the demo gives it.
export function editionChosen(storage: Storage): Edition {
  try {
    return storage.getItem(EDITION_KEY) === 'pro' ? 'pro' : 'lite';
  } catch {
    return 'lite';
  }
}

export function chooseEdition(storage: Storage, edition: Edition): void {
  try {
    storage.setItem(EDITION_KEY, edition);
  } catch {
    // The choice then lasts for this visit only.
  }
}
