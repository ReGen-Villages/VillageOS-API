'use strict';

// Migrates a VillageOS seed file from fused service definitions (launch
// properties living directly on Handled-Predicate / EndpointService Things)
// to the Connection --hasHandler--> Handler --is--> PrototypeHandler model.
// See Feature #5615 and tools/seed-migrate/README.md.

const crypto = require('crypto');

const NAMESPACE = '7c9e6f50-5e15-4d2a-9b3a-5e6d12340000';

const TYPE_ENDPOINT_SERVICE = 'EndpointService';
const TYPE_HANDLED_PREDICATE = 'Handled Predicate';
const PREDICATE_IS = 'is';
const PREDICATE_HAS = 'has';

const FLAG_CONNECTION = '__IsConnection';
const FLAG_HANDLER = '__IsHandler';
const FLAG_PROTOTYPE = '__IsPrototypeHandler';

// Launch properties that move off the connection onto the handler/prototype.
// Subdomain is deliberately excluded — it is a connection selector, not launch info.
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

function prototypeName(executablePath) {
  const file = String(executablePath).split(/[\\/]/).pop().replace(/\.dll$/i, '');
  const stripped = file.replace(/^vos\.ManagedMicroservice\./i, '');
  return `${stripped} Prototype`;
}

function findIdByName(things, name) {
  const t = things.find((x) => x.Name === name);
  return t ? t.Id : null;
}

// Type Things are the targets of `is`; connection instances are their subjects.
function typeIds(things) {
  const ids = {};
  for (const name of [TYPE_ENDPOINT_SERVICE, TYPE_HANDLED_PREDICATE]) {
    ids[name] = findIdByName(things, name);
  }
  return ids;
}

function migrateSeed(input) {
  const seed = JSON.parse(JSON.stringify(input));
  seed.Things = seed.Things || [];
  seed.Relationships = seed.Relationships || [];

  const summary = { connections: 0, handlers: 0, prototypes: 0, skipped: 0, alreadyMigrated: 0 };

  const isPredicateId = findIdByName(seed.Things, PREDICATE_IS);
  if (!isPredicateId) throw new Error("Seed has no 'is' predicate; not a recognizable VillageOS seed.");

  const types = typeIds(seed.Things);
  const typeIdSet = new Set(Object.values(types).filter(Boolean));

  const byId = new Map(seed.Things.map((t) => [t.Id, t]));

  // Connection instances: subjects of `is` pointing at a service type, excluding the type Things themselves.
  const connectionInstances = [];
  for (const rel of seed.Relationships) {
    if (rel.Predicate !== isPredicateId) continue;
    if (rel.Target !== types[TYPE_ENDPOINT_SERVICE] && rel.Target !== types[TYPE_HANDLED_PREDICATE]) continue;
    const inst = byId.get(rel.Subject);
    if (!inst || typeIdSet.has(inst.Id)) continue;
    connectionInstances.push(inst);
  }

  const hasId = ensureHasPredicate(seed.Things);
  const prototypesByExe = new Map();
  const existingIds = new Set(seed.Things.map((t) => t.Id));

  for (const conn of connectionInstances) {
    if (conn.Properties && conn.Properties[FLAG_CONNECTION]) {
      summary.alreadyMigrated++;
      continue;
    }
    const executablePath = effective(conn, 'ExecutablePath');
    if (!executablePath) {
      summary.skipped++; // in-process (e.g. `is`) — no daemon
      continue;
    }
    const servicePort = effective(conn, 'ServicePort');
    const serviceArgs = effective(conn, 'ServiceArgs');
    const onLoad = effective(conn, 'onLoad');
    const subdomain = effective(conn, 'Subdomain');

    let proto = prototypesByExe.get(executablePath);
    if (!proto) {
      const protoId = uuidv5(`prototype:${executablePath}`);
      proto = byId.get(protoId);
      if (!proto) {
        proto = {
          Id: protoId,
          Name: prototypeName(executablePath),
          Properties: {
            ExecutablePath: prop(executablePath),
            RunMode: prop('daemon'),
            [FLAG_PROTOTYPE]: prop(true),
          },
        };
        seed.Things.push(proto);
        existingIds.add(protoId);
        summary.prototypes++;
      }
      prototypesByExe.set(executablePath, proto);
    }

    const handlerId = uuidv5(`handler:${conn.Id}`);
    const handlerProps = { [FLAG_HANDLER]: prop(true) };
    if (servicePort !== undefined) handlerProps.ServicePort = prop(servicePort);
    if (serviceArgs !== undefined && serviceArgs !== '') handlerProps.ServiceArgs = prop(serviceArgs);
    if (onLoad !== undefined) handlerProps.AutoStart = prop(!!onLoad);
    const handler = { Id: handlerId, Name: `${conn.Name} handler`, Properties: handlerProps };
    seed.Things.push(handler);
    summary.handlers++;

    addRelationship(seed, uuidv5(`is:${handlerId}`), `${handler.Name} is ${proto.Name}`, handlerId, isPredicateId, proto.Id);
    addRelationship(seed, uuidv5(`bind:${conn.Id}`), `${conn.Name} has ${handler.Name}`, conn.Id, hasId, handlerId);

    conn.Properties = conn.Properties || {};
    conn.Properties[FLAG_CONNECTION] = prop(true);
    conn.Properties.trigger = prop(subdomain !== undefined ? 'http' : 'graph');
    if (subdomain !== undefined) conn.Properties.Subdomain = prop(subdomain);
    for (const key of LAUNCH_PROPS) delete conn.Properties[key];
    summary.connections++;
  }

  // Launch props must not survive on type templates or inherited snapshots,
  // or inheritance would re-supply what we just lifted onto handlers.
  const ownsLaunchInfo = new Set(
    seed.Things.filter((t) => t.Properties && (t.Properties[FLAG_HANDLER] || t.Properties[FLAG_PROTOTYPE])).map((t) => t.Id),
  );
  for (const t of seed.Things) {
    if (ownsLaunchInfo.has(t.Id)) continue;
    for (const key of LAUNCH_PROPS) {
      if (t.Properties) delete t.Properties[key];
    }
    if (typeIdSet.has(t.Id) && t.Properties) delete t.Properties.Subdomain;
    for (const src of Object.values(t.InheritedProperties || {})) {
      for (const key of [...LAUNCH_PROPS, 'Subdomain']) {
        if (src.Properties) delete src.Properties[key];
      }
    }
  }

  return { seed, summary };
}

// The binding uses the generic `has` predicate; a reader identifies the handler
// as the `has`-target flagged __IsHandler, never by the predicate name.
function ensureHasPredicate(things) {
  const named = things.find((t) => t.Name === PREDICATE_HAS);
  if (named) return named.Id;
  const id = uuidv5('predicate:has');
  things.push({ Id: id, Name: PREDICATE_HAS, Properties: {} });
  return id;
}

function addRelationship(seed, id, name, subject, predicate, target) {
  if (seed.Relationships.some((r) => r.Id === id)) return;
  seed.Relationships.push({ Id: id, Name: name, Subject: subject, Predicate: predicate, Target: target, Properties: {} });
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
  const original = fs.readFileSync(file, 'utf8');
  const { seed, summary } = migrateSeed(JSON.parse(original));
  const output = JSON.stringify(seed, null, 2);
  const changed = summary.connections > 0;
  console.error(`seed-migrate: ${JSON.stringify(summary)}`);
  if (check) {
    if (changed) {
      console.error(`${file} needs migration (run with --write).`);
      process.exit(1);
    }
    process.exit(0);
  }
  if (write) {
    fs.writeFileSync(file, output + '\n');
    console.error(`Wrote ${file}`);
  } else {
    process.stdout.write(output + '\n');
  }
}
