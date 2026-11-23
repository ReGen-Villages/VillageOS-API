//   VOS_INTEGRATION_URL       default https://localhost:7243
//   VOS_INTEGRATION_USERNAME  default admin
//   VOS_INTEGRATION_PASSWORD  default admin
//   VOS_INTEGRATION_MODEL     the model to sign in to, when the platform serves several

export const BASE_URL = process.env.VOS_INTEGRATION_URL || 'https://localhost:7243';
const USERNAME = process.env.VOS_INTEGRATION_USERNAME || 'admin';
const PASSWORD = process.env.VOS_INTEGRATION_PASSWORD || 'admin';
const MODEL = process.env.VOS_INTEGRATION_MODEL;

interface ModelChoiceResponse {
  models?: { Id: string; Name: string }[];
}

async function requestToken(modelId?: string) {
  const body: Record<string, string> = { Username: USERNAME, Password: PASSWORD };
  if (modelId) body.ModelId = modelId;
  return fetch(`${BASE_URL}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

export async function login(): Promise<string> {
  let response = await requestToken(MODEL);
  if (response.status === 400 && !MODEL) {
    const choice = (await response.clone().json()) as ModelChoiceResponse;
    const offered = choice.models?.[0]?.Id;
    if (offered) response = await requestToken(offered);
  }
  if (!response.ok) {
    throw new Error(`Could not sign in to ${BASE_URL}: ${response.status} ${await response.text()}`);
  }
  return ((await response.json()) as { token: string }).token;
}
