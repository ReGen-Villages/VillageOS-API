import { describe, it, expect } from 'vitest';
import { PipelineModel } from './model';
import { connectionSettings } from './connectionSettings';
import { connectionSettingsFixture } from './connectionSettings.test.fixture';

describe('connectionSettings', () => {
  it('lists every setting the connection inherits, each with the archetype it comes from', () => {
    const { things, relationships, id } = connectionSettingsFixture();

    const settings = connectionSettings(new PipelineModel(things, relationships), id.connection);

    expect(settings.map(({ name, value, setOnTheConnection, inherited }) => [name, value, setOnTheConnection, inherited?.from])).toEqual([
      ['callsAgainAtMost', 5, false, 'ServiceBinding'],
      ['callsAgainForNoLongerThanSeconds', 60, false, 'ServiceBinding'],
      ['callsAgainWhileItsThingStaysIn', true, false, 'ServiceBinding'],
      ['completion', 'ack', false, 'SiteAnalysisConnection'],
      ['doesNotCallAgainForSeconds', 10, false, 'ServiceBinding'],
      ['done_within', 300, false, 'SiteAnalysisConnection'],
      ['waitsForTheHandler', false, false, 'ServiceBinding'],
    ]);
  });

  it('names the nearest archetype stating a value, where one nearer overrides one farther up', () => {
    const { things, relationships, id } = connectionSettingsFixture();
    const siteAnalysis = things.find((thing) => thing.Id === id.siteAnalysisConnection)!;
    siteAnalysis.InheritedOverrides = {
      [id.serviceBinding]: { SourceId: id.serviceBinding, SourceName: 'ServiceBinding', Properties: { callsAgainAtMost: 3 } },
    };

    const settings = connectionSettings(new PipelineModel(things, relationships), id.connection);

    expect(settings.find((setting) => setting.name === 'callsAgainAtMost')).toEqual({
      name: 'callsAgainAtMost', value: 3, setOnTheConnection: false, inherited: { value: 3, from: 'SiteAnalysisConnection' },
    });
  });

  it('says a value written on the connection is set there, and keeps the value it would inherit without it', () => {
    const { things, relationships, id } = connectionSettingsFixture();
    const connection = things.find((thing) => thing.Id === id.connection)!;
    connection.InheritedOverrides = {
      [id.siteAnalysisConnection]: {
        SourceId: id.siteAnalysisConnection, SourceName: 'SiteAnalysisConnection', Properties: {},
        Inherited: { [id.serviceBinding]: { SourceId: id.serviceBinding, SourceName: 'ServiceBinding', Properties: { callsAgainForNoLongerThanSeconds: 120 } } },
      },
    };

    const settings = connectionSettings(new PipelineModel(things, relationships), id.connection);

    expect(settings.find((setting) => setting.name === 'callsAgainForNoLongerThanSeconds')).toEqual({
      name: 'callsAgainForNoLongerThanSeconds', value: 120, setOnTheConnection: true, inherited: { value: 60, from: 'ServiceBinding' },
    });
  });

  it('gives a value only the connection holds nothing to fall back to', () => {
    const { things, relationships, id } = connectionSettingsFixture();
    things.find((thing) => thing.Id === id.connection)!.Properties.Subdomain = 'estimate-rainfall';

    const settings = connectionSettings(new PipelineModel(things, relationships), id.connection);

    expect(settings.find((setting) => setting.name === 'Subdomain')).toEqual({ name: 'Subdomain', value: 'estimate-rainfall', setOnTheConnection: true });
  });

  it('lists a property the archetype declares later, with no change to the console', () => {
    const { things, relationships, id } = connectionSettingsFixture();
    things.find((thing) => thing.Id === id.serviceBinding)!.Properties.startsAfterSeconds = 0;

    const names = connectionSettings(new PipelineModel(things, relationships), id.connection).map((setting) => setting.name);

    expect(names).toContain('startsAfterSeconds');
  });

  it('lists nothing for a connection the model no longer holds', () => {
    const { things, relationships } = connectionSettingsFixture();

    expect(connectionSettings(new PipelineModel(things, relationships), 'gone')).toEqual([]);
  });
});
