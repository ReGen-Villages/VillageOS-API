// Writes the recording the tests read pages from without a server: the Things each selector reaches,
// the answer of every read a page makes, and each page as the reader resolves it in Node.
// ../README.md says when to run each form.
//
//   From a running Mycelium, with the administrator's password in RECORDING_ADMIN_PASSWORD:
//     node record.mjs <reader bundle> <recording file> <Mycelium address> <what is recorded>
//
//   Again from the recording itself, after the reader changed and the model did not:
//     node record.mjs <reader bundle> <recording file>
import fs from 'node:fs';

const [bundlePath, recordingPath, mycelium, recordingOf] = process.argv.slice(2);
(0, eval)(fs.readFileSync(bundlePath, 'utf8'));
const reader = globalThis.pageReader;

const recording = mycelium
  ? { recordingOf, selections: {}, reads: {}, refusedReads: {} }
  : JSON.parse(fs.readFileSync(recordingPath, 'utf8'));
const model = mycelium ? await aRunningMycelium(mycelium) : whatWasRecorded(recording);

const host = {
  async read(method, argumentsJson) {
    const asked = `${method} ${argumentsJson}`;
    if (asked in recording.refusedReads) throw new Error(recording.refusedReads[asked]);
    if (!(asked in recording.reads)) {
      try {
        recording.reads[asked] = JSON.stringify(await model.ask(method, JSON.parse(argumentsJson)));
      } catch (refused) {
        recording.refusedReads[asked] = String(refused.message ?? refused);
        throw refused;
      }
    }
    return recording.reads[asked];
  },
};

let held = 0;
async function hold(selectorJson) {
  const selected = (recording.selections[selectorJson] ??= await model.select(selectorJson));
  const key = String(++held);
  reader.hold(key, JSON.stringify(selected.things), JSON.stringify(selected.relationships));
  return key;
}

const navigation = await hold(reader.navigationSelector());
recording.listed = JSON.parse(reader.listPages(navigation, 'en'));
recording.pages = [];
for (const page of recording.listed.filter((one) => one.readable)) {
  const unscoped = await hold(reader.selectorFor(navigation, page.id, '[]'));
  const scopes = JSON.parse(reader.scopesOf(unscoped, page.id, 'en'));
  const read = { pageId: page.id, name: page.name, scopes, answers: {} };
  for (const scopeId of [null, ...scopes.map((scope) => scope.id)]) {
    const alone = await hold(reader.selectorFor(navigation, page.id, JSON.stringify(scopeId ? [scopeId] : [])));
    read.answers[scopeId ?? ''] = JSON.parse(await reader.readPage(alone, page.id, scopeId, 'en', host));
  }
  await hold(reader.selectorFor(navigation, page.id, JSON.stringify(scopes.map((scope) => scope.id))));
  recording.pages.push(read);
}

fs.writeFileSync(recordingPath, `${JSON.stringify(recording, null, 1)}\n`);
console.log(`${recording.recordingOf}: ${recording.listed.length} page(s) listed, ${recording.pages.length} read; `
  + `${Object.keys(recording.selections).length} selection(s), ${Object.keys(recording.reads).length} read(s) answered, `
  + `${Object.keys(recording.refusedReads).length} refused`);

function whatWasRecorded(recorded) {
  const notRecorded = (what) => { throw new Error(`The recording holds no answer for ${what}. Record again from a running Mycelium.`); };
  return {
    select: async (selectorJson) => notRecorded(`the selector ${selectorJson}`),
    ask: async (method, given) => notRecorded(`${method} ${JSON.stringify(given)}`),
  };
}

async function aRunningMycelium(address) {
  const signIn = await fetch(`${address}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Vos-Client': 'Trellis' },
    body: JSON.stringify({ Username: 'admin', Password: process.env.RECORDING_ADMIN_PASSWORD }),
  });
  if (!signIn.ok) throw new Error(`Sign-in was refused: ${signIn.status} ${await signIn.text()}`);
  const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${(await signIn.json()).token}` };

  const answerOf = async (response, asked) => {
    if (!response.ok) throw new Error(`${asked} answered ${response.status}: ${(await response.text()).slice(0, 300)}`);
    return response.json();
  };
  const get = async (path) => answerOf(await fetch(address + path, { headers }), path);
  const post = async (path, body) =>
    answerOf(await fetch(address + path, { method: 'POST', headers, body: typeof body === 'string' ? body : JSON.stringify(body) }), path);

  const asks = {
    thingsInState: ([state, narrowing]) => get(statePath(state, narrowing)),
    thingRanges: ([thingId]) => get(`/api/things/${thingId}/ranges`).catch(() => null),
    aggregate: ([query]) => post('/api/temporal/aggregate', query),
    reduce: ([query]) => post('/api/temporal/reduce', query),
    fromService: ([endpoint, body]) => post(endpoint, body ?? {}),
  };
  return {
    async select(selectorJson) {
      const answer = await post('/api/snapshots', selectorJson);
      return { things: answer.snapshot.things, relationships: answer.snapshot.relationships };
    },
    ask: (method, given) => asks[method](given),
  };
}

function statePath(state, narrowing) {
  const path = `/api/states/${encodeURIComponent(state)}/things`;
  if (!narrowing) return path;
  const query = new URLSearchParams();
  if (narrowing.alsoIn?.length) query.set('alsoIn', narrowing.alsoIn.join(','));
  if (narrowing.notIn?.length) query.set('notIn', narrowing.notIn.join(','));
  if (narrowing.type) query.set('type', narrowing.type);
  if (narrowing.within && narrowing.withinPredicate) {
    query.set('within', narrowing.within);
    query.set('withinPredicate', narrowing.withinPredicate);
  }
  if (narrowing.includeArchetypes) query.set('includeArchetypes', 'true');
  if (narrowing.limit !== undefined) query.set('limit', String(narrowing.limit));
  if (narrowing.properties?.length) query.set('properties', narrowing.properties.join(','));
  if (narrowing.countOnly) query.set('countOnly', 'true');
  const suffix = query.toString();
  return suffix ? `${path}?${suffix}` : path;
}
