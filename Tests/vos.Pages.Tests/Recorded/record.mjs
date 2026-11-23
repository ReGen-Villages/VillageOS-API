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
const model = mycelium ? await aRunningMycelium(mycelium) : whatWasRecorded();

async function answered(asked, ask) {
  if (asked in recording.refusedReads) throw new Error(recording.refusedReads[asked]);
  if (!(asked in recording.reads)) {
    try {
      recording.reads[asked] = JSON.stringify(await ask());
    } catch (refused) {
      recording.refusedReads[asked] = String(refused.message ?? refused);
      throw refused;
    }
  }
  return recording.reads[asked];
}

const host = {
  get: (path) => answered(`GET ${path}`, () => model.get(path)),
  post: (path, bodyJson) => answered(`POST ${path} ${bodyJson}`, () => model.post(path, bodyJson)),
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

// A few Things whose kind derives figures are also asked about on their own: their inputs, each
// derived figure explained, and where each input's value came from. A few, because each one costs
// the recording the Things its selector reaches.
const THINGS_ASKED_ABOUT = 2;
recording.things = [];
for (const page of recording.pages) {
  const everyScope = recording.selections[reader.selectorFor(navigation, page.pageId, JSON.stringify(page.scopes.map((scope) => scope.id)))];
  const isId = everyScope.things.find((one) => one.Name === 'is')?.Id;
  const derivingKinds = new Set(everyScope.things.filter((one) => one.DerivedProperties && Object.keys(one.DerivedProperties).length > 0).map((one) => one.Id));
  const ofADerivingKind = new Set(everyScope.relationships.filter((one) => one.PredicateId === isId && derivingKinds.has(one.TargetId)).map((one) => one.SubjectId));
  const asked = everyScope.things
    .filter((one) => !one.IsArchetype && ofADerivingKind.has(one.Id))
    .sort((a, b) => (a.Name < b.Name ? -1 : 1))
    .slice(0, THINGS_ASKED_ABOUT);
  for (const thing of asked) {
    let held = await hold(reader.selectorForThing(thing.Id, '[]'));
    const memberKinds = reader.memberKindsOf(held, thing.Id);
    if (memberKinds !== '[]') held = await hold(reader.selectorForThing(thing.Id, memberKinds));
    const inputs = JSON.parse(await reader.inputsOfThing(held, thing.Id, null, 'en', host));
    const explained = {};
    for (const figure of new Set([...inputs.inputs, ...inputs.leftOut].map((input) => input.figure))) {
      explained[figure] = JSON.parse(await reader.explain(held, thing.Id, figure, 'en', host));
    }
    const origins = {};
    for (const input of inputs.inputs.filter((one) => one.heldBy)) origins[input.term] = JSON.parse(reader.originOf(held, thing.Id, input.term));
    recording.things.push({ thingId: thing.Id, name: thing.Name, inputs, explained, origins });
  }
}

fs.writeFileSync(recordingPath, `${JSON.stringify(recording, null, 1)}\n`);
console.log(`${recording.recordingOf}: ${recording.listed.length} page(s) listed, ${recording.pages.length} read, ${recording.things.length} Thing(s) asked about; `
  + `${Object.keys(recording.selections).length} selection(s), ${Object.keys(recording.reads).length} read(s) answered, `
  + `${Object.keys(recording.refusedReads).length} refused`);

function whatWasRecorded() {
  const notRecorded = (what) => { throw new Error(`The recording holds no answer for ${what}. Record again from a running Mycelium.`); };
  return {
    select: async (selectorJson) => notRecorded(`the selector ${selectorJson}`),
    get: async (path) => notRecorded(`GET ${path}`),
    post: async (path, bodyJson) => notRecorded(`POST ${path} ${bodyJson}`),
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

  return {
    async select(selectorJson) {
      const answer = await post('/api/snapshots', selectorJson);
      return { things: answer.snapshot.things, relationships: answer.snapshot.relationships };
    },
    get,
    post,
  };
}
