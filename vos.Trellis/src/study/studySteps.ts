export type Edition = 'lite' | 'pro';

export type StudyStep = 'location' | 'data' | 'village' | 'layout' | 'villageLayout' | 'timeline';

const notBuilt = (): never => {
  throw new Error('not built yet');
};

export const stepsFor = (_edition: Edition): StudyStep[] => notBuilt();
export const canOpen = (_step: StudyStep, _locationConfirmed: boolean): true | 'locationNeeded' | 'notBuilt' => notBuilt();
export const editionChosen = (_storage: Storage): Edition => notBuilt();
export const chooseEdition = (_storage: Storage, _edition: Edition): void => notBuilt();
