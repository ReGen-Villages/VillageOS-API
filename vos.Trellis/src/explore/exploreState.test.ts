import { describe, expect, it } from 'vitest';
import type { FormOptions } from '../api/intakeApi';
import {
  BOUNDARY_FETCHED_FROM_REGISTER,
  documentFrom,
  emptyExplore,
  landDescribed,
  readyToSubmit,
  seededShares,
  withBoundaryCleared,
  withDrawnBoundary,
  withFetchedBoundary,
  withPosition,
} from './exploreState';

const aField = { latitude: 48.801, longitude: 2.301 };
const aRing = [
  { latitude: 48.801, longitude: 2.301 },
  { latitude: 48.802, longitude: 2.301 },
  { latitude: 48.802, longitude: 2.303 },
];

const options: FormOptions = {
  allocationCategories: ['residential', 'food-and-agriculture'],
  basemapSources: [],
  hazardTypes: [],
  hazardLevels: [],
  defaultProgramme: [
    { category: 'residential', sharePct: 22 },
    { category: 'food-and-agriculture', sharePct: 34 },
  ],
  parcelLookup: true,
  placeSearch: true,
};

describe('the exploration before anything is posted', () => {
  it('starts with nothing described and nothing ready', () => {
    const state = emptyExplore('11111111-1111-4111-8111-111111111111');
    expect(landDescribed(state)).toBe(false);
    expect(readyToSubmit(state)).toBe(false);
  });

  it('seeds the full starting programme the model offers', () => {
    expect(seededShares(options)).toEqual({ residential: 22, 'food-and-agriculture': 34 });
  });

  it('names the land after its position until somebody says otherwise', () => {
    const state = withPosition(emptyExplore('id'), aField);
    expect(state.siteName).toBe('Site at 48.8010, 2.3010');
  });

  it('starts the land over when the pin moves, because the boundary described the old position', () => {
    const withBoundary = withFetchedBoundary(
      withPosition(emptyExplore('id'), aField), aRing, '© the land register');

    const moved = withPosition(withBoundary, { latitude: 39.5, longitude: -8.4 });

    expect(moved.boundary).toEqual([]);
    expect(moved.boundarySource).toBeNull();
    expect(moved.boundaryAttribution).toBeNull();
  });

  it('a corner placed by hand makes the whole boundary the persons own assertion', () => {
    const fetched = withFetchedBoundary(
      withPosition(emptyExplore('id'), aField), aRing, '© the land register');

    const adjusted = withDrawnBoundary(fetched, [...aRing, { latitude: 48.803, longitude: 2.302 }]);

    expect(adjusted.boundarySource).toBe('drawn-by-hand');
    expect(adjusted.boundaryAttribution).toBeNull();
  });

  it('clearing the boundary keeps the pin', () => {
    const cleared = withBoundaryCleared(
      withFetchedBoundary(withPosition(emptyExplore('id'), aField), aRing, null));
    expect(cleared.position).toEqual(aField);
    expect(cleared.boundary).toEqual([]);
  });
});

describe('the document an exploration posts', () => {
  const described = {
    ...withFetchedBoundary(withPosition(emptyExplore('an-id'), aField), aRing, '© the register'),
    contactName: 'Ana Ferreira',
    emailAddress: 'ana.ferreira@example.pt',
    shares: seededShares(options),
  };

  it('is ready once the land is described and somebody is named', () => {
    expect(readyToSubmit(described)).toBe(true);
  });

  it('carries the land, the register boundary source, and the full programme', () => {
    const document = documentFrom(described);

    expect(document.submissionId).toBe('an-id');
    expect(document.site.latitude).toBe(48.801);
    expect(document.parcel?.boundarySource).toBe(BOUNDARY_FETCHED_FROM_REGISTER);
    expect(document.parcel?.boundary).toHaveLength(3);
    expect(document.allocations).toEqual([
      { category: 'residential', sharePct: 22 },
      { category: 'food-and-agriculture', sharePct: 34 },
    ]);
    expect(document.project.name).toContain(described.siteName);
  });

  it('leaves the dials out until they are turned, so the report leads with capacity', () => {
    const document = documentFrom(described);
    expect(document.site.population).toBeUndefined();
    expect(document.site.householdSize).toBeUndefined();
    expect(document.hazards).toBeUndefined();
  });

  it('carries a turned dial as the figure it was turned to', () => {
    const document = documentFrom({ ...described, population: '320', householdSize: '2.4' });
    expect(document.site.population).toBe(320);
    expect(document.site.householdSize).toBe(2.4);
  });

  it('treats a half-typed figure as not yet turned rather than as zero', () => {
    const document = documentFrom({ ...described, population: '-', householdSize: '2.' });
    expect(document.site.population).toBeUndefined();
    expect(document.site.householdSize).toBe(2);
  });

  it('carries what was seen against the hazards the model names', () => {
    const document = documentFrom({ ...described, reportedHazards: { 'river-flood': 'high' } });
    expect(document.hazards).toEqual([{ hazardType: 'river-flood', reportedLevel: 'high' }]);
  });
});
