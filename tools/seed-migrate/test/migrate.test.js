'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { migrateSeed } = require('../migrate');

const S = (value) => ({ typeInfo: 'vos.String', value });
const N = (value) => ({ typeInfo: 'vos.LongInteger', value });
const B = (value) => ({ typeInfo: 'vos.Boolean', value });

// Mirrors the canonical village.seed.json shape: an `is` predicate, two old
// service type Things, two predicate instances sharing the Metabolism binary,
// and one EndpointService instance inheriting its launch props from the type.
function baseSeed() {
  const META = '../vos.ManagedMicroservice.Metabolism/bin/x/vos.ManagedMicroservice.Metabolism.dll';
  const ECHO = '../vos.ManagedMicroservice.Echo/bin/x/vos.ManagedMicroservice.Echo.dll';
  return {
    Id: 'seed-1', Name: 'test',
    Things: [
      { Id: 'is-id', Name: 'is', Properties: {} },
      { Id: 'hp-id', Name: 'Handled Predicate', Properties: { ExecutablePath: S(''), ServicePort: N(0), onLoad: B(false) } },
      { Id: 'es-id', Name: 'EndpointService', Properties: { ExecutablePath: S(ECHO), ServicePort: N(7110), Subdomain: S('echo') } },
      { Id: 'consumes-id', Name: 'consumes', Properties: { ExecutablePath: S(META), ServicePort: N(7102), ServiceArgs: S('--mode=consumes'), onLoad: B(true) } },
      { Id: 'produces-id', Name: 'produces', Properties: { ExecutablePath: S(META), ServicePort: N(7103), ServiceArgs: S('--mode=produces'), onLoad: B(true) } },
      { Id: 'echo-id', Name: 'Echo', Properties: {}, InheritedProperties: { 'es-id': { SourceId: 'es-id', SourceName: 'EndpointService', Properties: { ExecutablePath: S(ECHO), ServicePort: N(7110), Subdomain: S('echo') } } } },
    ],
    Relationships: [
      { Id: 'r1', Name: 'consumes is Handled Predicate', Subject: 'consumes-id', Predicate: 'is-id', Target: 'hp-id', Properties: {} },
      { Id: 'r2', Name: 'produces is Handled Predicate', Subject: 'produces-id', Predicate: 'is-id', Target: 'hp-id', Properties: {} },
      { Id: 'r3', Name: 'Echo is EndpointService', Subject: 'echo-id', Predicate: 'is-id', Target: 'es-id', Properties: {} },
    ],
  };
}

const byName = (seed, name) => seed.Things.find((t) => t.Name === name);
const byId = (seed, id) => seed.Things.find((t) => t.Id === id);
const isEdges = (seed, subjId) => {
  const isId = byName(seed, 'is').Id;
  return seed.Relationships.filter((r) => r.Predicate === isId && r.Subject === subjId).map((r) => r.Target);
};

test('connection declares membership via `is Connection` (no flags)', () => {
  const { seed, summary } = migrateSeed(baseSeed());
  assert.equal(summary.connections, 3);
  const connId = byName(seed, 'Connection').Id;
  assert.ok(isEdges(seed, byName(seed, 'consumes').Id).includes(connId));
  assert.ok(isEdges(seed, byName(seed, 'Echo').Id).includes(connId));
  // no leftover __Is* flags anywhere
  assert.equal(seed.Things.some((t) => Object.keys(t.Properties || {}).some((k) => k.startsWith('__Is'))), false);
});

test('archetypes carry the inheritable schema', () => {
  const { seed } = migrateSeed(baseSeed());
  const svc = byName(seed, 'Service');
  assert.equal(svc.Properties.ExecutablePath.value, '');
  assert.equal(svc.Properties.AutoStart.value, false);
  assert.equal(svc.Properties.RunMode.value, 'daemon');
  assert.equal(byName(seed, 'Connection').Properties.trigger.value, 'graph');
});

test('two predicates on the same binary share one Service prototype', () => {
  const { seed, summary } = migrateSeed(baseSeed());
  assert.equal(summary.prototypes, 2); // Metabolism + Echo, not 3
  const metab = byName(seed, 'Metabolism prototype');
  const svcId = byName(seed, 'Service').Id;
  assert.ok(isEdges(seed, metab.Id).includes(svcId), 'Metabolism prototype is Service');
  assert.match(metab.Properties.ExecutablePath.value, /Metabolism\.dll$/);

  const cSvc = byName(seed, 'consumes service');
  const pSvc = byName(seed, 'produces service');
  assert.ok(isEdges(seed, cSvc.Id).includes(metab.Id), 'consumes service is Metabolism prototype');
  assert.ok(isEdges(seed, pSvc.Id).includes(metab.Id), 'produces service is Metabolism prototype');
  assert.notEqual(cSvc.Id, pSvc.Id); // distinct processes, shared definition
  assert.equal(cSvc.Properties.ExecutablePath, undefined); // inherited, not duplicated
});

test('service carries only per-instance overrides', () => {
  const { seed } = migrateSeed(baseSeed());
  const cSvc = byName(seed, 'consumes service');
  assert.equal(cSvc.Properties.ServicePort.value, 7102);
  assert.equal(cSvc.Properties.ServiceArgs.value, '--mode=consumes');
  assert.equal(cSvc.Properties.AutoStart.value, true);
});

test('connection binds its service with the generic `has`', () => {
  const { seed } = migrateSeed(baseSeed());
  const hasId = byName(seed, 'has').Id;
  const consumes = byName(seed, 'consumes');
  const binding = seed.Relationships.find((r) => r.Subject === consumes.Id && r.Predicate === hasId);
  assert.ok(binding, 'consumes has a `has` binding');
  assert.equal(byId(seed, binding.Target).Name, 'consumes service');
});

test('http vs graph trigger inferred; subdomain kept; launch props stripped', () => {
  const { seed } = migrateSeed(baseSeed());
  const echo = byName(seed, 'Echo');
  assert.equal(echo.Properties.trigger.value, 'http');
  assert.equal(echo.Properties.Subdomain.value, 'echo');
  assert.equal(echo.Properties.ExecutablePath, undefined);
  const consumes = byName(seed, 'consumes');
  assert.equal(consumes.Properties.trigger.value, 'graph');
  assert.equal(consumes.Properties.ServicePort, undefined);
});

test('old service types and their `is` edges are removed', () => {
  const { seed } = migrateSeed(baseSeed());
  assert.equal(byName(seed, 'Handled Predicate'), undefined);
  assert.equal(byName(seed, 'EndpointService'), undefined);
  // consumes no longer claims the old type; only `is Connection`
  const connId = byName(seed, 'Connection').Id;
  assert.deepEqual(isEdges(seed, byName(seed, 'consumes').Id), [connId]);
});

test('is idempotent — second run is a no-op', () => {
  const once = migrateSeed(baseSeed()).seed;
  const twice = migrateSeed(once);
  assert.equal(twice.summary.connections, 0);
  assert.deepEqual(twice.seed, once);
});
