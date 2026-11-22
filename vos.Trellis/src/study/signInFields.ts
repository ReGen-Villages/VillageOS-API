export interface SignInFields {
  readonly emailAddress: string;
  readonly projectName: string;
}

export interface SignInProblems {
  readonly emailAddress: 'missing' | 'invalid' | null;
  readonly projectName: 'missing' | null;
}

// The browser's own rule for an email field, so the page refuses what the demo's form refused.
const EMAIL_ADDRESS =
  /^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$/;

export function signInProblems({ emailAddress, projectName }: SignInFields): SignInProblems {
  const address = emailAddress.trim();
  return {
    emailAddress: address === '' ? 'missing' : EMAIL_ADDRESS.test(address) ? null : 'invalid',
    projectName: projectName.trim() === '' ? 'missing' : null,
  };
}
