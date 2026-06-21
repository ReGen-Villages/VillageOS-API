'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { migrateSeed } = require('../migrate');

const S = (value) => ({ typeInfo: 'vos.String', value });
const N = (value) => ({ typeInfo: 'vos.LongInteger', value });

// Mirrors the canonical village.seed.json shape: an `is` predicate, two service
// type Things, two predicate instances sharing the Metabolism binary, and one
// EndpointService instance inheriting its launch props from the type template.
function baseSeed() {
  return {
    Id: 'seed-1',
    Name: 'test',
    Things: [
      { Id: 'is-id', Name: 'is', Properties: {} },
      { Id: 'hp-id', Name: 'Handled Predicate', Properties: { ExecutablePath: S(''), ServicePort: N(0), onLoad: { typeInfo: 'vos.Boolean', value: false } } },
      { Id: 'es-id', Name: 'EndpointService', Properties: { ExecutablePath: S('../vos.ManagedMicroservice.Echo/bin/x/vos.ManagedMicroservice.Echo.dll'), ServicePort: N(7110), Subdomain: S('echo') } },
      { Id: 'consumes-id', Name: 'consumes', Properties: { ExecutablePath: S('../vos.ManagedMicroservice.Metabolism/bin/x/vos.ManagedMicroservice.Metabolism.dll'), ServicePort: N(7102), ServiceArgs: S('--mode=consumes'), onLoad: { typeInfo: 'vos.Boolean', value: true } } },
      { Id: 'produces-id', Name: 'produces', Properties: { ExecutablePath: S('../vos.ManagedMicroservice.Metabolism/bin/x/vos.ManagedMicroservice.Metabolism.dll'), ServicePort: N(7103), ServiceArgs: S('--mode=produces'), onLoad: { typeInfo: 'vos.Boolean', value: true } } },
      {
        Id: 'echo-id', Name: 'Echo', Properties: {},
        InheritedProperties: { 'es-id': { SourceId: 'es-id', SourceName: 'EndpointService', Properties: { ExecutablePath: S('../vos.ManagedMicroservice.Echo/bin/x/vos.ManagedMicroservice.Echo.dll'), ServicePort: N(7110), Subdomain: S('echo') } } },
      },
    ],
    Relationships: [
      { Id: 'r1', Name: 'consumes is Handled Predicate', Subject: 'consumes-id', Predicate: 'is-id', Target: 'hp-id', Properties: {} },
      { Id: 'r2', Name: 'produces is Handled Predicate', Subject: 'produces-id', Predicate: 'is-id', Target: 'hp-id', Properties: {} },
      { Id: 'r3', Name: 'Echo is EndpointService', Subject: 'echo-id', Predicate: 'is-id', Target: 'es-id', Properties: {} },
    ],
  };
}

const flag = (t, name) => !!(t.Properties && t.Properties[name] && t.Properties[name].value);
const byName = (seed, name) => seed.Things.find((t) => t.Name === name);
const byId = (seed, id) => seed.Things.find((t) => t.Id === id);

test('lifts launch props into a Handler per connection', () => {
  const { seed, summary } = migrateSeed(baseSeed());
  assert.equal(summary.connections, 3); // consumes, produces, Echo
  assert.equal(summary.handlers, 3);

  const consumes = byName(seed, 'consumes');
  assert.equal(flag(consumes, '__IsConnection'), true);
  assert.equal(consumes.Properties.trigger.value, 'graph');
  assert.equal(consumes.Properties.ExecutablePath, undefined);
  assert.equal(consumes.Properties.ServicePort, undefined);
});

test('two predicates on the same binary share one PrototypeHandler', () => {
  const { seed, summary } = migrateSeed(baseSeed());
  assert.equal(summary.prototypes, 2); // Metabolism + Echo, not 3

  const protos = seed.Things.filter((t) => flag(t, '__IsPrototypeHandler'));
  assert.equal(protos.length, 2);
  const metab = protos.find((p) => p.Name === 'Metabolism Prototype');
  assert.ok(metab, 'Metabolism prototype exists');

  const bind = (subjName) => seed.Relationships.find((r) => byId(seed, r.Subject) && byId(seed, r.Subject).Name === subjName && byName(seed, 'hasHandler').Id === r.Predicate);
  const cHandler = byId(seed, bind('consumes').Target);
  const pHandler = byId(seed, bind('produces').Target);
  const isId = byName(seed, 'is').Id;
  const protoOf = (h) => seed.Relationships.find((r) => r.Subject === h.Id && r.Predicate === isId).Target;
  assert.equal(protoOf(cHandler), metab.Id);
  assert.equal(protoOf(pHandler), metab.Id);
  assert.notEqual(cHandler.Id, pHandler.Id); // distinct processes, shared definition
});

test('handler keeps only per-instance overrides; prototype keeps the binary', () => {
  const { seed } = migrateSeed(baseSeed());
  const metab = byName(seed, 'Metabolism Prototype');
  assert.match(metab.Properties.ExecutablePath.value, /Metabolism\.dll$/);
  assert.equal(metab.Properties.RunMode.value, 'daemon');

  const cHandler = byName(seed, 'consumes handler');
  assert.equal(cHandler.Properties.ServicePort.value, 7102);
  assert.equal(cHandler.Properties.ServiceArgs.value, '--mode=consumes');
  assert.equal(cHandler.Properties.AutoStart.value, true);
  assert.equal(cHandler.Properties.ExecutablePath, undefined); // inherited, not duplicated
});

test('binding predicate is flagged __BindsHandler, not the generic has', () => {
  const { seed } = migrateSeed(baseSeed());
  const binder = byName(seed, 'hasHandler');
  assert.ok(binder && flag(binder, '__BindsHandler'));
});

test('http vs graph trigger inferred from Subdomain; subdomain kept on connection', () => {
  const { seed } = migrateSeed(baseSeed());
  const echo = byName(seed, 'Echo');
  assert.equal(echo.Properties.trigger.value, 'http');
  assert.equal(echo.Properties.Subdomain.value, 'echo');
  assert.equal(flag(echo, '__IsConnection'), true);
});

test('in-process predicate (is) and type templates are not migrated', () => {
  const { seed, summary } = migrateSeed(baseSeed());
  assert.equal(summary.skipped, 0); // `is` is not an instance of a service type here
  const isThing = byName(seed, 'is');
  assert.equal(flag(isThing, '__IsConnection'), false);
  // type templates lose their stale launch props
  assert.equal(byName(seed, 'EndpointService').Properties.ExecutablePath, undefined);
  assert.equal(byName(seed, 'EndpointService').Properties.Subdomain, undefined);
});

test('is idempotent — second run is a no-op', () => {
  const once = migrateSeed(baseSeed()).seed;
  const twice = migrateSeed(once);
  assert.equal(twice.summary.connections, 0);
  assert.equal(twice.summary.alreadyMigrated, 3);
  assert.deepEqual(twice.seed, once);
});
