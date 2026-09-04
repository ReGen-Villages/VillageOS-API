import { describe, it, expect } from 'vitest';
import type { VosThing, VosRelationship } from '../types/vos';
import { buildModelIndex } from './dashboardApi';
import { discoverBasemapSources } from './basemapApi';
import { DEFAULT_RASTER_MAXIMUM_ZOOM } from '../types/basemap';

function thing(Id: string, Name: string, Properties: Record<string, unknown> = {}): VosThing {
  return { Id, Name, Properties };
}

function isEdge(SubjectId: string, TargetId: string): VosRelationship {
  return {
    Id: `${SubjectId}-is-${TargetId}`,
    Name: `${SubjectId} is ${TargetId}`,
    SubjectId,
    PredicateId: 'is',
    TargetId,
    Properties: {},
  };
}

function indexOf(sources: VosThing[], extraEdges: VosRelationship[] = []) {
  const things = [
    thing('is', 'is'),
    { ...thing('arch-basemap', 'BasemapSource'), IsArchetype: true },
    ...sources,
  ];
  const relationships = [...sources.map((s) => isEdge(s.Id, 'arch-basemap')), ...extraEdges];
  return buildModelIndex(things, relationships);
}

describe('discoverBasemapSources', () => {
  it('reads a vector style source', () => {
    const found = discoverBasemapSources(
      indexOf([
        thing('src-1', 'Streets', {
          styleUrl: 'https://tiles.example.org/styles/plain',
          attribution: 'Example data contributors',
        }),
      ]),
    );
    expect(found).toEqual([
      {
        id: 'src-1',
        name: 'Streets',
        attribution: 'Example data contributors',
        kind: 'style',
        styleUrl: 'https://tiles.example.org/styles/plain',
      },
    ]);
  });

  it('reads a raster source and the depth it claims', () => {
    const [source] = discoverBasemapSources(
      indexOf([
        thing('src-1', 'Aerial', {
          tileUrl: 'https://tiles.example.org/aerial/{z}/{x}/{y}.png',
          attribution: 'Example national mapping agency',
          maximumZoom: 17,
        }),
      ]),
    );
    expect(source).toMatchObject({ kind: 'raster', maximumZoom: 17 });
  });

  // A signed-in page reads the model itself rather than being handed sources by the intake service, so
  // what the model says about raising the ground has to be read here too. Read in only one of the two
  // places, the same model draws land on the public page and a diagram on the wizard beside it.
  it('reads what a source says about raising the ground', () => {
    const [source] = discoverBasemapSources(
      indexOf([
        thing('src-1', 'Streets', {
          styleUrl: 'https://tiles.example.org/styles/plain',
          attribution: 'Example credit',
          terrainTileUrl: 'https://elevation.example.org/{z}/{x}/{y}.png',
          terrainEncoding: 'terrarium',
          terrainExaggeration: 2,
          buildingSourceLayer: 'building',
        }),
      ]),
    );

    expect(source.terrain).toEqual({
      tileUrl: 'https://elevation.example.org/{z}/{x}/{y}.png',
      encoding: 'terrarium',
      exaggeration: 2,
    });
    expect(source.buildingSourceLayer).toBe('building');
  });

  it('falls back to a default depth when the model does not state one', () => {
    const [source] = discoverBasemapSources(
      indexOf([
        thing('src-1', 'Aerial', {
          tileUrl: 'https://tiles.example.org/aerial/{z}/{x}/{y}.png',
          attribution: 'Example national mapping agency',
        }),
      ]),
    );
    expect(source).toMatchObject({ kind: 'raster', maximumZoom: DEFAULT_RASTER_MAXIMUM_ZOOM });
  });

  it('refuses a source with no attribution, because displaying one bare breaks its licence', () => {
    expect(
      discoverBasemapSources(
        indexOf([thing('src-1', 'Streets', { styleUrl: 'https://tiles.example.org/styles/plain' })]),
      ),
    ).toEqual([]);
  });

  it('refuses a source that gives no address', () => {
    expect(
      discoverBasemapSources(indexOf([thing('src-1', 'Streets', { attribution: 'Example' })])),
    ).toEqual([]);
  });

  it('refuses a source that gives both a style and a tile address, rather than guessing which was meant', () => {
    expect(
      discoverBasemapSources(
        indexOf([
          thing('src-1', 'Streets', {
            styleUrl: 'https://tiles.example.org/styles/plain',
            tileUrl: 'https://tiles.example.org/plain/{z}/{x}/{y}.png',
            attribution: 'Example',
          }),
        ]),
      ),
    ).toEqual([]);
  });

  it('offers sources in name order, so the same model always opens on the same layer', () => {
    const found = discoverBasemapSources(
      indexOf([
        thing('src-2', 'Streets', { styleUrl: 'https://tiles.example.org/s', attribution: 'A' }),
        thing('src-1', 'Aerial', { tileUrl: 'https://tiles.example.org/{z}/{x}/{y}.png', attribution: 'B' }),
      ]),
    );
    expect(found.map((s) => s.name)).toEqual(['Aerial', 'Streets']);
  });

  // Seed normalization moves a value that shadows an archetype's declaration out of the Thing's own
  // properties and into InheritedOverrides, leaving the archetype's valueless declaration as the only
  // thing under that key on the archetype. A reader that only looked at own properties would find a
  // model whose sources all declare an address and offer none of them.
  it('reads a source normalized the way a generated seed carries it', () => {
    const things = [
      thing('is', 'is'),
      {
        ...thing('arch-basemap', 'BasemapSource', {
          styleUrl: { typeInfo: 'vos.String' },
          tileUrl: { typeInfo: 'vos.String' },
          attribution: { typeInfo: 'vos.String' },
        }),
        IsArchetype: true,
      },
      {
        ...thing('src-1', 'Streets'),
        InheritedOverrides: {
          'arch-basemap': {
            SourceId: 'arch-basemap',
            SourceName: 'BasemapSource',
            InheritedAt: '2026-07-05T00:00:00+00:00',
            Properties: {
              styleUrl: 'https://tiles.example.org/styles/plain',
              attribution: 'Example data contributors',
            },
          },
        },
      },
    ];
    const relationships = [isEdge('src-1', 'arch-basemap')];
    expect(discoverBasemapSources(buildModelIndex(things, relationships))).toEqual([
      {
        id: 'src-1',
        name: 'Streets',
        attribution: 'Example data contributors',
        kind: 'style',
        styleUrl: 'https://tiles.example.org/styles/plain',
      },
    ]);
  });

  it('takes attribution inherited from the archetype when the source itself does not restate it', () => {
    const things = [
      thing('is', 'is'),
      { ...thing('arch-basemap', 'BasemapSource'), IsArchetype: true },
      { ...thing('arch-agency', 'AgencyBasemap', { attribution: 'Example national mapping agency' }), IsArchetype: true },
      thing('src-1', 'Aerial', { tileUrl: 'https://tiles.example.org/{z}/{x}/{y}.png' }),
    ];
    const relationships = [isEdge('arch-agency', 'arch-basemap'), isEdge('src-1', 'arch-agency')];
    const [source] = discoverBasemapSources(buildModelIndex(things, relationships));
    expect(source.attribution).toBe('Example national mapping agency');
  });
});
