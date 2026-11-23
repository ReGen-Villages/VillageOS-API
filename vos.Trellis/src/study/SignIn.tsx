import { useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { intakeApi } from '../api/intakeApi';
import { signInProblems } from './signInFields';

export interface StudySession {
  readonly emailAddress: string;
  readonly projectName: string;
  readonly ticket: string;
}

const LAST_ADDRESS_KEY = 'studyLastEmailAddress';

function lastAddress(): string {
  try {
    return localStorage.getItem(LAST_ADDRESS_KEY) ?? '';
  } catch {
    return '';
  }
}

function rememberAddress(emailAddress: string): void {
  try {
    localStorage.setItem(LAST_ADDRESS_KEY, emailAddress);
  } catch {
    // A browser that keeps nothing starts the next sign-in with an empty field.
  }
}

export function SignIn({ onSignedIn }: { onSignedIn: (session: StudySession) => void }) {
  const { t } = useTranslation();
  const [emailAddress, setEmailAddress] = useState(lastAddress);
  const [projectName, setProjectName] = useState('');
  const [code, setCode] = useState('');
  const [codeSentTo, setCodeSentTo] = useState<string | null>(null);
  const [checked, setChecked] = useState(false);
  const [waiting, setWaiting] = useState(false);
  const [refusal, setRefusal] = useState<string | null>(null);

  const problems = signInProblems({ emailAddress, projectName });

  async function sendCode(event: FormEvent) {
    event.preventDefault();
    setChecked(true);
    if (problems.emailAddress !== null || problems.projectName !== null) return;
    const address = emailAddress.trim();
    setWaiting(true);
    setRefusal(null);
    try {
      await intakeApi.askForCode(address);
      rememberAddress(address);
      setCodeSentTo(address);
    } catch (error) {
      setRefusal((error as Error).message);
    } finally {
      setWaiting(false);
    }
  }

  async function confirmCode(event: FormEvent, sentTo: string) {
    event.preventDefault();
    setWaiting(true);
    setRefusal(null);
    try {
      const ticket = await intakeApi.exchangeTicket(sentTo, code.trim());
      onSignedIn({ emailAddress: sentTo, projectName: projectName.trim(), ticket });
    } catch (error) {
      setRefusal((error as Error).message);
      setWaiting(false);
    }
  }

  const fieldClass = 'w-full rounded-md border border-zinc-300 px-3 py-2 text-base dark:border-zinc-700 dark:bg-zinc-900';

  return (
    <main className="mx-auto mt-16 w-full max-w-sm px-6">
      <h1 className="mb-6 text-2xl font-bold">{t('study.signIn.title')}</h1>
      {codeSentTo === null ? (
        <form onSubmit={sendCode} noValidate className="space-y-4">
          <div>
            <label htmlFor="study-email" className="mb-1 block text-sm font-medium">
              {t('study.signIn.emailAddress')}
            </label>
            <input
              id="study-email"
              type="email"
              autoComplete="email"
              value={emailAddress}
              onChange={(event) => setEmailAddress(event.target.value)}
              aria-invalid={checked && problems.emailAddress !== null}
              className={fieldClass}
            />
            {checked && problems.emailAddress === 'missing' && <p className="mt-1 text-sm text-red-700">{t('study.signIn.emailMissing')}</p>}
            {checked && problems.emailAddress === 'invalid' && <p className="mt-1 text-sm text-red-700">{t('study.signIn.emailInvalid')}</p>}
          </div>
          <div>
            <label htmlFor="study-project" className="mb-1 block text-sm font-medium">
              {t('study.signIn.projectName')}
            </label>
            <input
              id="study-project"
              type="text"
              value={projectName}
              onChange={(event) => setProjectName(event.target.value)}
              aria-invalid={checked && problems.projectName !== null}
              className={fieldClass}
            />
            {checked && problems.projectName === 'missing' && <p className="mt-1 text-sm text-red-700">{t('study.signIn.projectMissing')}</p>}
          </div>
          <button type="submit" disabled={waiting} className="w-full rounded-md bg-zinc-800 px-4 py-2 text-white disabled:opacity-50">
            {waiting ? t('study.signIn.sending') : t('study.signIn.continue')}
          </button>
        </form>
      ) : (
        <form onSubmit={(event) => confirmCode(event, codeSentTo)} className="space-y-4">
          <p className="text-sm">{t('study.signIn.codeSent', { emailAddress: codeSentTo })}</p>
          <div>
            <label htmlFor="study-code" className="mb-1 block text-sm font-medium">
              {t('study.signIn.code')}
            </label>
            <input
              id="study-code"
              type="text"
              inputMode="numeric"
              autoComplete="one-time-code"
              value={code}
              onChange={(event) => setCode(event.target.value)}
              className={fieldClass}
            />
          </div>
          <button type="submit" disabled={waiting} className="w-full rounded-md bg-zinc-800 px-4 py-2 text-white disabled:opacity-50">
            {waiting ? t('study.signIn.confirming') : t('study.signIn.confirm')}
          </button>
        </form>
      )}
      {refusal !== null && (
        <p role="alert" className="mt-4 text-sm text-red-700">
          {refusal}
        </p>
      )}
    </main>
  );
}
