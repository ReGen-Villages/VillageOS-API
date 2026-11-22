import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { SignIn, type StudySession } from './SignIn';
import { canOpen, chooseEdition, EDITIONS, editionChosen, stepsFor, type Edition, type StudyStep } from './studySteps';

export function StudyPage() {
  const [session, setSession] = useState<StudySession | null>(null);
  return session === null ? <SignIn onSignedIn={setSession} /> : <StudyFrame session={session} />;
}

function StudyFrame({ session }: { session: StudySession }) {
  const { t } = useTranslation();
  const [edition, setEdition] = useState<Edition>(() => editionChosen(localStorage));
  // Location is the one step with a page so far, and its border confirms nothing until the map is built.
  const current: StudyStep = 'location';
  const locationConfirmed = false;
  const [locationNeeded, setLocationNeeded] = useState(false);

  const steps = stepsFor(edition);
  const next = steps[steps.indexOf(current) + 1];

  function choose(chosen: Edition) {
    chooseEdition(localStorage, chosen);
    setEdition(chosen);
  }

  function open(step: StudyStep) {
    setLocationNeeded(canOpen(step, locationConfirmed) === 'locationNeeded');
  }

  return (
    <div className="flex h-full min-h-screen flex-col bg-zinc-50 text-zinc-900 dark:bg-zinc-950 dark:text-zinc-100">
      <header className="flex items-center justify-between gap-4 border-b border-zinc-200 px-6 py-3 dark:border-zinc-800">
        <span className="font-semibold">{session.projectName}</span>
        <span className="truncate text-sm text-zinc-500">{t('study.noAddress')}</span>
        <span />
      </header>

      <main className="flex-1 px-6 py-4">
        <h1 className="text-xl font-bold">{t(`study.steps.${current}`)}</h1>
        {locationNeeded && (
          <p role="status" className="mt-2 text-sm text-red-700">
            {t('study.locationNeeded')}
          </p>
        )}
      </main>

      <footer className="flex flex-wrap items-center justify-between gap-4 border-t border-zinc-200 px-6 py-3 dark:border-zinc-800">
        <nav aria-label={t('study.stepsLabel')}>
          <ol className="flex flex-wrap items-center gap-2">
            {steps.map((step) => (
              <li key={step}>
                <button
                  type="button"
                  onClick={() => open(step)}
                  aria-current={step === current ? 'step' : undefined}
                  title={canOpen(step, true) === 'notBuilt' ? t('study.comingSoon') : undefined}
                  className={`rounded-md px-3 py-1 text-sm ${step === current ? 'bg-zinc-800 text-white' : canOpen(step, true) === 'notBuilt' ? 'text-zinc-400' : ''}`}
                >
                  {t(`study.steps.${step}`)}
                </button>
              </li>
            ))}
          </ol>
        </nav>
        <button
          type="button"
          disabled={next === undefined || canOpen(next, locationConfirmed) !== true}
          title={t('study.completeThisStep')}
          className="rounded-md bg-zinc-800 px-4 py-1 text-sm text-white disabled:opacity-40"
        >
          {t('study.next')}
        </button>
        <div className="flex items-center gap-2 text-sm">
          <span>{t('study.version')}</span>
          {EDITIONS.map((choice) => (
            <button
              key={choice}
              type="button"
              aria-pressed={choice === edition}
              onClick={() => choose(choice)}
              className={`rounded-md border px-2 py-0.5 ${choice === edition ? 'border-zinc-800 font-semibold' : 'border-zinc-300'}`}
            >
              {t(`study.editions.${choice}`)}
            </button>
          ))}
        </div>
      </footer>
    </div>
  );
}
