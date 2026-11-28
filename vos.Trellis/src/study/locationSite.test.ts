import { describe, expect, it } from 'vitest';
import {
  borderCleared,
  borderDrawn,
  borderImported,
  plotChosen,
  siteArea,
  siteConfirmable,
  siteDocument,
  siteFound,
} from './locationSite';
import { WordNotHeldError } from '../intake/submissionDraft';
import { borderAreaHectares } from '../utils/borderArea';
import type { BoundaryPoint } from '../utils/parcelGeometry';

const at = (latitude: number, longitude: number): BoundaryPoint => ({ latitude, longitude });

const SITE = siteFound('Al Awir 2, Dubai', at(25.150467, 55.551533));
const NEARBY_BORDER = [at(25.149, 55.55), at(25.149, 55.553), at(25.152, 55.553), at(25.152, 55.55)];
const FAR_BORDER = [at(25.2, 55.6), at(25.2, 55.603), at(25.203, 55.603), at(25.203, 55.6)];
const SESSION = { emailAddress: 'planner@example.org', projectName: 'Oasis', ticket: 'ticket-1' };

describe('a site found by search or coordinates', () => {
  it('starts with no border and no parcel', () => {
    expect(SITE).toEqual({
      address: 'Al Awir 2, Dubai',
      position: at(25.150467, 55.551533),
      parcelName: null,
      border: [],
      borderSource: null,
      plotLabel: null,
      givenHectares: null,
    });
  });
});

describe('a border', () => {
  it('drawn by hand is measured, not given, and drops a chosen plot', () => {
    const drawn = borderDrawn(plotChosen(SITE, { boundary: NEARBY_BORDER, identifier: 'Plot 7', areaHectares: 9 }), NEARBY_BORDER);

    expect(drawn).toMatchObject({ border: NEARBY_BORDER, borderSource: 'drawn', plotLabel: null, givenHectares: null });
  });

  it('chosen from a register keeps the register’s label and area', () => {
    expect(plotChosen(SITE, { boundary: NEARBY_BORDER, identifier: 'Plot 7', areaHectares: 9.04 })).toMatchObject({
      border: NEARBY_BORDER,
      borderSource: 'register',
      plotLabel: 'Plot 7',
      givenHectares: 9.04,
    });
  });

  it('imported near the site keeps the site where it is and names the parcel after the file’s shape', () => {
    const imported = borderImported(SITE, { name: 'North field', boundary: NEARBY_BORDER, hectares: 10.1 });

    expect(imported).toMatchObject({
      position: SITE.position,
      parcelName: 'North field',
      border: NEARBY_BORDER,
      borderSource: 'imported',
      givenHectares: 10.1,
    });
  });

  it('imported far from the site moves the site to the border', () => {
    const imported = borderImported(SITE, { name: null, boundary: FAR_BORDER, hectares: 10.1 });

    expect(imported.position.latitude).toBeCloseTo(25.2015, 6);
    expect(imported.position.longitude).toBeCloseTo(55.6015, 6);
    expect(imported.address).toBe(SITE.address);
  });

  it('imported with no site yet makes the site at the border', () => {
    const imported = borderImported(null, { name: 'North field', boundary: FAR_BORDER, hectares: 10.1 });

    expect(imported).toMatchObject({ address: null, parcelName: 'North field', borderSource: 'imported' });
    expect(imported.position.latitude).toBeCloseTo(25.2015, 6);
  });

  it('imported keeps a parcel name the site already has', () => {
    const named = borderImported(SITE, { name: 'North field', boundary: NEARBY_BORDER, hectares: 10.1 });

    expect(borderImported(named, { name: 'South field', boundary: NEARBY_BORDER, hectares: 10.1 }).parcelName).toBe('North field');
  });

  it('cleared leaves the site with no border, plot or given area', () => {
    const cleared = borderCleared(plotChosen(SITE, { boundary: NEARBY_BORDER, identifier: 'Plot 7', areaHectares: 9 }));

    expect(cleared).toMatchObject({ border: [], borderSource: null, plotLabel: null, givenHectares: null });
  });
});

describe('the site area', () => {
  it('is the register’s, labelled with the plot', () => {
    expect(siteArea(plotChosen(SITE, { boundary: NEARBY_BORDER, identifier: 'Plot 7', areaHectares: 9.04 }), 'meanSphereAreaMethod')).toEqual({
      kind: 'register',
      hectares: 9.04,
      label: 'Plot 7',
    });
  });

  it('is measured with the form’s method for a plot the register gives no area', () => {
    const plot = plotChosen(SITE, { boundary: NEARBY_BORDER, identifier: null, areaHectares: null });

    expect(siteArea(plot, 'wgs84EllipsoidAreaMethod')).toEqual({
      kind: 'register',
      hectares: borderAreaHectares(NEARBY_BORDER, 'wgs84EllipsoidAreaMethod'),
      label: null,
    });
  });

  it('is measured with the form’s method for a drawn border', () => {
    expect(siteArea(borderDrawn(SITE, NEARBY_BORDER), 'wgs84EllipsoidAreaMethod')).toEqual({
      kind: 'drawn',
      hectares: borderAreaHectares(NEARBY_BORDER, 'wgs84EllipsoidAreaMethod'),
    });
  });

  it('is the intake’s measure for an imported border', () => {
    expect(siteArea(borderImported(SITE, { name: null, boundary: NEARBY_BORDER, hectares: 10.1 }), 'meanSphereAreaMethod')).toEqual({
      kind: 'imported',
      hectares: 10.1,
    });
  });

  it('is unknown without a border, and for a drawn border the page cannot measure the way the model does', () => {
    expect(siteArea(SITE, 'meanSphereAreaMethod')).toEqual({ kind: 'unknown' });
    expect(siteArea(borderDrawn(SITE, NEARBY_BORDER), null)).toEqual({ kind: 'unknown' });
  });
});

describe('confirming the site', () => {
  it('needs a border that encloses land', () => {
    expect(siteConfirmable(SITE)).toBe(false);
    expect(siteConfirmable(borderDrawn(SITE, NEARBY_BORDER.slice(0, 2)))).toBe(false);
    expect(siteConfirmable(borderDrawn(SITE, NEARBY_BORDER))).toBe(true);
  });

  it('posts the border with how it was got, the email as the contact and the parcel name as the site', () => {
    const imported = borderImported(SITE, { name: 'North field', boundary: NEARBY_BORDER, hectares: 10.1 });

    expect(siteDocument(imported, SESSION, 'submission-1', SOURCES)).toEqual({
      submissionId: 'submission-1',
      project: { name: 'Oasis' },
      contact: { name: 'planner@example.org', emailAddress: 'planner@example.org' },
      site: { name: 'North field', latitude: 25.150467, longitude: 55.551533 },
      parcel: { boundarySource: 'source-imported', boundary: NEARBY_BORDER },
    });
  });

  it('names the site after the address where there is no parcel name, and after the project where there is neither', () => {
    expect(siteDocument(borderDrawn(SITE, NEARBY_BORDER), SESSION, 'submission-1', SOURCES)).toMatchObject({
      site: { name: 'Al Awir 2, Dubai' },
      parcel: { boundarySource: 'source-drawn' },
    });
    const unnamed = borderImported(null, { name: null, boundary: NEARBY_BORDER, hectares: 10.1 });
    expect(siteDocument(unnamed, SESSION, 'submission-1', SOURCES).site.name).toBe('Oasis');
  });

  it('says a register plot was fetched from the register, and names the site after the plot', () => {
    const plot = plotChosen(SITE, { boundary: NEARBY_BORDER, identifier: 'Plot 7', areaHectares: 9 });

    expect(siteDocument(plot, SESSION, 'submission-1', SOURCES)).toMatchObject({
      site: { name: 'Plot 7' },
      parcel: { boundarySource: 'source-register' },
    });
  });

  it('posts nothing where the model binds no source to the way the border was got', () => {
    const plot = plotChosen(SITE, { boundary: NEARBY_BORDER, identifier: 'Plot 7', areaHectares: 9 });

    expect(() => siteDocument(plot, SESSION, 'submission-1', { boundarySources: { drawn: 'source-drawn' } })).toThrow(
      WordNotHeldError,
    );
  });
});

const SOURCES = {
  boundarySources: { drawn: 'source-drawn', imported: 'source-imported', register: 'source-register' },
} as const;
