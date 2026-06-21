'use strict';

// Migrates a VillageOS seed from the old fused service model (launch properties
// living directly on Handled-Predicate / EndpointService Things) to the model
// where a user-style `is` relationship to archetype Things does all the work:
//
//   Connection --has--> Service --is--> <shared Service prototype> --is--> Service
//   <connection> --is--> Connection
//
// Recognition is not hardcoded: the broker is told the archetype Thing names via
// config (PrototypeConnectionThingName / PrototypeServiceThingName) and finds
// instances by transitive `is`. The helper just establishes those `is`
// relationships — the role a user normally plays — for pre-existing seeds.
// See Feature #5615 and tools/seed-migrate/README.md.

const crypto = require('crypto');

const NAMESPACE = '7c9e6f50-5e15-4d2a-9b3a-5e6d12340000';

const OLD_TYPE_ENDPOINT_SERVICE = 'EndpointService';
const OLD_TYPE_HANDLED_PREDICATE = 'Handled Predicate';
const PREDICATE_IS = 'is';
const PREDICATE_HAS = 'has';

const DEFAULT_CONNECTION_ARCHETYPE = 'Connection';
const DEFAULT_SERVICE_ARCHETYPE = 'Service';

// Launch properties that move off the connection onto the service/prototype.
// Subdomain is excluded — it is a connection selector, not launch info.
const LAUNCH_PROPS = ['ExecutablePath', 'ServicePort', 'ServiceArgs', 'onLoad'];

function uuidv5(name) {
  const ns = Buffer.from(NAMESPACE.replace(/-/g, ''), 'hex');
  const bytes = crypto.createHash('sha1').update(ns).update(name).digest().subarray(0, 16);
  bytes[6] = (bytes[6] & 0x0f) | 0x50;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const h = bytes.toString('hex');
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20, 32)}`;
}

function prop(value) {
  if (typeof value === 'boolean') return { typeInfo: 'vos.Boolean', value };
  if (typeof value === 'number') return { typeInfo: 'vos.LongInteger', value };
  return { typeInfo: 'vos.String', value };
}

function effective(thing, name) {
  const own = thing.Properties && thing.Properties[name];
  if (own && own.value !== undefined && own.value !== '') return own.value;
  for (const src of Object.values(thing.InheritedProperties || {})) {
    const inh = src.Properties && src.Properties[name];
    if (inh && inh.value !== undefined && inh.value !== '') return inh.value;
  }
  return undefined;
}

// "…/vos.ManagedMicroservice.Metabolism.dll" -> "Metabolism prototype".
// The " prototype" suffix avoids colliding with a connection of the same name
// (an EndpointService instance "Echo" vs the Echo binary).
function prototypeName(executablePath) {
  const file = String(executablePath).split(/[\\/]/).pop().replace(/\.dll$/i, '');
  return `${file.replace(/^vos\.ManagedMicroservice\./i, '')} prototype`;
}

function findIdByName(things, name) {
  const t = things.find((x) => x.Name === name);
  return t ? t.Id : null;
}

function addRelationship(seed, id, name, subject, predicate, target) {
  if (seed.Relationships.some((r) => r.Id === id)) return;
  seed.Relationships.push({ Id: id, Name: name, Subject: subject, Predicate: predicate, Target: target, Properties: {} });
}

function migrateSeed(input, options = {}) {
  const connectionArchetype = options.connectionArchetype || DEFAULT_CONNECTION_ARCHETYPE;
  const serviceArchetype = options.serviceArchetype || DEFAULT_SERVICE_ARCHETYPE;

  const seed = JSON.parse(JSON.stringify(input));
  seed.Things = seed.Things || [];
  seed.Relationships = seed.Relationships || [];

  const summary = { connections: 0, services: 0, prototypes: 0, skipped: 0 };

  const isPredicateId = findIdByName(seed.Things, PREDICATE_IS);
  if (!isPredicateId) throw new Error("Seed has no 'is' predicate; not a recognizable VillageOS seed.");

  const oldTypeIds = [OLD_TYPE_ENDPOINT_SERVICE, OLD_TYPE_HANDLED_PREDICATE]
    .map((n) => findIdByName(seed.Things, n))
    .filter(Boolean);
  const oldTypeIdSet = new Set(oldTypeIds);
  const byId = new Map(seed.Things.map((t) => [t.Id, t]));

  // Connection instances: subjects of `is` pointing at an old service type,
  // excluding the type Things themselves. Once migrated, the old types are
  // gone, so a second run finds nothing — idempotent by construction.
  const connectionInstances = [];
  for (const rel of seed.Relationships) {
    if (rel.Predicate !== isPredicateId || !oldTypeIdSet.has(rel.Target)) continue;
    const inst = byId.get(rel.Subject);
    if (inst && !oldTypeIdSet.has(inst.Id)) connectionInstances.push(inst);
  }
  if (connectionInstances.length === 0) return { seed, summary };

  const hasId = ensureHasPredicate(seed);
  const connArchetypeId = ensureArchetype(seed, connectionArchetype, { trigger: prop('graph') });
  const serviceArchetypeId = ensureArchetype(seed, serviceArchetype,
    { ExecutablePath: prop(''), ServicePort: prop(0), ServiceArgs: prop(''), AutoStart: prop(false), RunMode: prop('daemon') });

  const prototypesByExe = new Map();

  for (const conn of connectionInstances) {
    const executablePath = effective(conn, 'ExecutablePath');
    if (!executablePath) {
      summary.skipped++; // in-process (e.g. `is`) — no daemon
      continue;
    }
    const subdomain = effective(conn, 'Subdomain');

    let proto = prototypesByExe.get(executablePath);
    if (!proto) {
      const protoId = uuidv5(`prototype:${executablePath}`);
      proto = byId.get(protoId);
      if (!proto) {
        proto = { Id: protoId, Name: prototypeName(executablePath), Properties: { ExecutablePath: prop(executablePath) } };
        seed.Things.push(proto);
        addRelationship(seed, uuidv5(`is:${protoId}`), `${proto.Name} is ${serviceArchetype}`, protoId, isPredicateId, serviceArchetypeId);
        summary.prototypes++;
      }
      prototypesByExe.set(executablePath, proto);
    }

    const serviceId = uuidv5(`service:${conn.Id}`);
    const serviceProps = {};
    const servicePort = effective(conn, 'ServicePort');
    const serviceArgs = effective(conn, 'ServiceArgs');
    const onLoad = effective(conn, 'onLoad');
    if (servicePort !== undefined) serviceProps.ServicePort = prop(servicePort);
    if (serviceArgs !== undefined && serviceArgs !== '') serviceProps.ServiceArgs = prop(serviceArgs);
    if (onLoad !== undefined) serviceProps.AutoStart = prop(!!onLoad);
    const service = { Id: serviceId, Name: `${conn.Name} service`, Properties: serviceProps };
    seed.Things.push(service);
    summary.services++;

    addRelationship(seed, uuidv5(`is:${serviceId}`), `${service.Name} is ${proto.Name}`, serviceId, isPredicateId, proto.Id);
    addRelationship(seed, uuidv5(`has:${conn.Id}`), `${conn.Name} has ${service.Name}`, conn.Id, hasId, serviceId);
    addRelationship(seed, uuidv5(`isconn:${conn.Id}`), `${conn.Name} is ${connectionArchetype}`, conn.Id, isPredicateId, connArchetypeId);

    conn.Properties = conn.Properties || {};
    conn.Properties.trigger = prop(subdomain !== undefined ? 'http' : 'graph');
    if (subdomain !== undefined) conn.Properties.Subdomain = prop(subdomain);
    for (const key of LAUNCH_PROPS) delete conn.Properties[key];
    summary.connections++;
  }

  // Drop the now-superseded old type Things and EVERY relationship that touches
  // them in any role — not just `is`-to-type edges. The old types are also
  // subjects of their own type memberships (e.g. "Handled Predicate is
  // Predicate"); leaving those behind dangles a relationship onto a removed Thing.
  seed.Relationships = seed.Relationships.filter(
    (r) => !oldTypeIdSet.has(r.Subject) && !oldTypeIdSet.has(r.Predicate) && !oldTypeIdSet.has(r.Target),
  );
  seed.Things = seed.Things.filter((t) => !oldTypeIdSet.has(t.Id));

  // Launch props must survive only on the Service archetype (the schema) and on
  // Services (anything transitively `is` it); strip them everywhere else, or
  // inheritance would re-supply what we just lifted.
  const keepLaunchInfo = (id) => id === serviceArchetypeId || isService(seed, id, serviceArchetypeId, isPredicateId);
  for (const t of seed.Things) {
    if (keepLaunchInfo(t.Id)) continue;
    for (const key of LAUNCH_PROPS) if (t.Properties) delete t.Properties[key];
    for (const src of Object.values(t.InheritedProperties || {})) {
      for (const key of [...LAUNCH_PROPS, 'Subdomain']) if (src.Properties) delete src.Properties[key];
    }
  }

  return { seed, summary };
}

// A Service is anything transitively `is` the service archetype.
function isService(seed, thingId, serviceArchetypeId, isPredicateId) {
  const seen = new Set();
  let frontier = [thingId];
  while (frontier.length) {
    const next = [];
    for (const id of frontier) {
      if (seen.has(id)) continue;
      seen.add(id);
      for (const r of seed.Relationships) {
        if (r.Predicate === isPredicateId && r.Subject === id) {
          if (r.Target === serviceArchetypeId) return true;
          next.push(r.Target);
        }
      }
    }
    frontier = next;
  }
  return false;
}

function ensureArchetype(seed, name, templateProps) {
  const existing = seed.Things.find((t) => t.Name === name);
  if (existing) return existing.Id;
  const id = uuidv5(`archetype:${name}`);
  seed.Things.push({ Id: id, Name: name, Properties: { ...templateProps } });
  return id;
}

// The binding uses the generic `has`; a reader identifies the service as the
// `has`-target that is (transitively) a Service, never by the predicate name.
function ensureHasPredicate(seed) {
  const named = seed.Things.find((t) => t.Name === PREDICATE_HAS);
  if (named) return named.Id;
  const id = uuidv5('predicate:has');
  seed.Things.push({ Id: id, Name: PREDICATE_HAS, Properties: {} });
  return id;
}

module.exports = { migrateSeed, uuidv5 };

if (require.main === module) {
  const fs = require('fs');
  const args = process.argv.slice(2);
  const check = args.includes('--check');
  const write = args.includes('--write');
  const file = args.find((a) => !a.startsWith('--'));
  if (!file) {
    console.error('Usage: node migrate.js <seed.json> [--write] [--check]');
    process.exit(2);
  }
  const { seed, summary } = migrateSeed(JSON.parse(fs.readFileSync(file, 'utf8')));
  const output = JSON.stringify(seed, null, 2);
  console.error(`seed-migrate: ${JSON.stringify(summary)}`);
  if (check) process.exit(summary.connections > 0 ? 1 : 0);
  if (write) {
    fs.writeFileSync(file, output + '\n');
    console.error(`Wrote ${file}`);
  } else {
    process.stdout.write(output + '\n');
  }
}
