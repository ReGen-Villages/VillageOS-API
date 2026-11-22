export interface SignInFields {
  readonly emailAddress: string;
  readonly projectName: string;
}

export interface SignInProblems {
  readonly emailAddress: 'missing' | 'invalid' | null;
  readonly projectName: 'missing' | null;
}

export function signInProblems(_fields: SignInFields): SignInProblems {
  throw new Error('not built yet');
}
