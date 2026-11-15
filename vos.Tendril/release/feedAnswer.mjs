const NOT_FOUND = 'E404';

export function feedHolds({ status, stdout }, version) {
  if (status === 0) return JSON.parse(stdout) === version;
  const code = JSON.parse(stdout || '{}').error?.code;
  if (code === NOT_FOUND) return false;
  throw new Error(`The feed could not say whether it holds ${version}: ${code ?? 'no answer'}. Nothing was published.`);
}
