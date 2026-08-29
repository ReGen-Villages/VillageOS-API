import { describe, it, expect, beforeEach } from 'vitest';
import { sphericalAreaHectares, type BoundaryPoint } from '../utils/parcelGeometry';
import {
  BOUNDARY_DRAWN_BY_HAND,
  BOUNDARY_GENERATED_FROM_STATED_AREA,
  HECTARES_PER_ACRE,
  boundaryCleared,
  boundaryDrafted,
  boundaryDrawn,
  clearDraft,
  coordinatesFrom,
  documentFrom,
  emptyDraft,
  fromHectares,
  loadDraft,
  readyToSubmit,
  saveDraft,
  statedAreaHectares,
  wholePercentages,
  withCategoryChosen,
  withCategoryDropped,
  withShareSet,
  withStepVisited,
  type ProgrammeShares,
  type SubmissionDraft,
} from './submissionDraft';

const MODEL = 'model-under-test';

function filled(patch: Partial<SubmissionDraft> = {}): SubmissionDraft {
  return {
    ...emptyDraft('sub-0001'),
    projectName: 'Willow Bend Regeneration',
    country: 'Portugal',
    nearestCity: 'Santarém',
    existingDataNotes: 'Rainfall held from a 2024 survey.',
    contactName: 'Ana Ferreira',
    relationshipToProject: 'landowner',
    emailAddress: 'ana.ferreira@example.pt',
    phoneNumber: '+351 200 000 000',
    siteName: 'Willow Bend',
    latitude: '39.5012',
    longitude: '-8.4137',
    statedArea: '24',
    population: '320',
    householdSize: '2.4',
    ...patch,
  };
}

const total = (shares: ProgrammeShares) => Object.values(shares).reduce((sum, share) => sum + share, 0);

describe('the area and the unit it was typed in', () => {
  it('converts an area typed in acres to the hectares that are stored', () => {
    expect(statedAreaHectares(filled({ statedArea: '10', areaUnit: 'acres' }))).toBeCloseTo(4.0468564224, 9);
    expect(statedAreaHectares(filled({ statedArea: '10', areaUnit: 'hectares' }))).toBe(10);
  });

  it('shows a stored area back in whichever unit is being typed in', () => {
    expect(fromHectares(4.0468564224, 'acres')).toBeCloseTo(10, 9);
    expect(fromHectares(24, 'hectares')).toBe(24);
  });

  it('round-trips through both units without drifting', () => {
    const stored = statedAreaHectares(filled({ statedArea: '24', areaUnit: 'acres' }))!;

    expect(fromHectares(stored, 'acres')).toBeCloseTo(24, 9);
  });

  it('submits hectares whatever unit the planner was typing in', () => {
    expect(documentFrom(filled({ statedArea: '24', areaUnit: 'acres' })).site.statedAreaHectares).toBeCloseTo(
      24 * HECTARES_PER_ACRE,
      9,
    );
  });

  it.each(['-4', 'twelve', '', '   ', '-'])('treats %s as no area rather than as a figure', (typed) => {
    expect(statedAreaHectares(filled({ statedArea: typed }))).toBeNull();
    expect(documentFrom(filled({ statedArea: typed })).site.statedAreaHectares).toBeUndefined();
  });

  it('keeps a stated area of nothing, which is a figure somebody gave', () => {
    expect(documentFrom(filled({ statedArea: '0' })).site.statedAreaHectares).toBe(0);
  });

  it('keeps a coordinate west of Greenwich, where the same figure would be no area at all', () => {
    const document = documentFrom(filled({ longitude: '-8.4137', statedArea: '-8.4137' }));

    expect(document.site.longitude).toBe(-8.4137);
    expect(document.site.statedAreaHectares).toBeUndefined();
  });
});

describe('where the site sits', () => {
  it('is where the two typed coordinates put it', () => {
    expect(coordinatesFrom(filled())).toEqual({ latitude: 39.5012, longitude: -8.4137 });
  });

  it.each([
    ['latitude', { latitude: '' }],
    ['longitude', { longitude: '   ' }],
  ])('is nowhere while the %s is still missing', (_half, patch) => {
    expect(coordinatesFrom(filled(patch))).toBeNull();
  });

  it('is nowhere when a coordinate is not a figure', () => {
    expect(coordinatesFrom(filled({ latitude: 'north a bit' }))).toBeNull();
  });

  it.each([
    ['91', '-8.4137'],
    ['39.5', '-181'],
  ])('is nowhere on Earth at latitude %s, longitude %s', (latitude, longitude) => {
    expect(coordinatesFrom(filled({ latitude, longitude }))).toBeNull();
  });
});

describe('the parcel boundary', () => {
  const CORNERS: readonly BoundaryPoint[] = [
    { latitude: 39.5, longitude: -8.41 },
    { latitude: 39.502, longitude: -8.41 },
    { latitude: 39.502, longitude: -8.408 },
  ];

  beforeEach(() => clearDraft(MODEL));

  it('placing a draft generates a square of the stated area, and says that is how it was obtained', () => {
    const drafted = boundaryDrafted(filled({ statedArea: '24' }));

    expect(drafted?.boundarySource).toBe(BOUNDARY_GENERATED_FROM_STATED_AREA);
    expect(drafted?.boundary).toHaveLength(4);
    expect(sphericalAreaHectares(drafted!.boundary!)).toBeCloseTo(24, 2);
  });

  it('places no draft while the position, the stated area, or any area at all is missing', () => {
    expect(boundaryDrafted(filled({ latitude: '' }))).toBeNull();
    expect(boundaryDrafted(filled({ statedArea: '' }))).toBeNull();
    expect(boundaryDrafted(filled({ statedArea: '0' }))).toBeNull();
  });

  it('a boundary the planner drew or edited was drawn by hand, whatever it was before', () => {
    expect(boundaryDrawn(CORNERS)).toEqual({ boundary: CORNERS, boundarySource: BOUNDARY_DRAWN_BY_HAND });
  });

  it('clearing removes the corners and how they were obtained together', () => {
    expect(boundaryCleared()).toEqual({ boundary: [], boundarySource: null });
  });

  it('is posted with its corners and its origin once it has three of them', () => {
    const document = documentFrom(
      filled({ boundary: CORNERS, boundarySource: BOUNDARY_DRAWN_BY_HAND }),
    );

    expect(document.parcel).toEqual({ boundarySource: 'drawn-by-hand', boundary: CORNERS });
  });

  it('is left out of the document below three corners, which enclose nothing', () => {
    const twoCorners = filled({ boundary: CORNERS.slice(0, 2), boundarySource: BOUNDARY_DRAWN_BY_HAND });

    expect(documentFrom(twoCorners).parcel).toBeUndefined();
    expect(documentFrom(filled()).parcel).toBeUndefined();
  });

  it('posts only the corner coordinates, not whatever else a stored corner carried', () => {
    const carrying = [...CORNERS.slice(0, 2), { ...CORNERS[2], zoom: 15 } as BoundaryPoint];

    const posted = documentFrom(filled({ boundary: carrying, boundarySource: BOUNDARY_DRAWN_BY_HAND }));

    expect(posted.parcel?.boundary[2]).toEqual(CORNERS[2]);
  });

  it('survives the tab closing, corners and origin both', () => {
    saveDraft(MODEL, filled({ boundary: CORNERS, boundarySource: BOUNDARY_DRAWN_BY_HAND }));

    const restored = loadDraft(MODEL);

    expect(restored?.boundary).toEqual(CORNERS);
    expect(restored?.boundarySource).toBe(BOUNDARY_DRAWN_BY_HAND);
  });

  it.each([
    ['a word where the corners should be', { boundary: 'a word' }],
    ['a corner that is not a coordinate pair', { boundary: [{ latitude: 'north', longitude: -8.41 }] }],
    ['an origin the wizard cannot truthfully claim', { boundary: CORNERS, boundarySource: 'imported-from-file' }],
    ['corners with no origin at all', { boundary: CORNERS, boundarySource: null }],
  ])('falls back to no boundary where what was stored holds %s', (_case, patch) => {
    localStorage.setItem(
      `vos-intake-draft:${MODEL}`,
      JSON.stringify({ ...filled(), ...patch }),
    );

    const restored = loadDraft(MODEL);

    expect(restored?.boundary).toEqual([]);
    expect(restored?.boundarySource).toBeNull();
  });
});

describe('the programme split', () => {
  it('gives the whole parcel to the first category chosen', () => {
    expect(withCategoryChosen({}, 'residential')).toEqual({ residential: 100 });
  });

  it('keeps the chosen categories describing the whole parcel as each is added', () => {
    let shares: ProgrammeShares = {};
    for (const category of ['residential', 'food-and-agriculture', 'mobility-and-infrastructure']) {
      shares = withCategoryChosen(shares, category);
      expect(total(shares)).toBeCloseTo(100, 9);
    }
    expect(Object.keys(shares)).toHaveLength(3);
  });

  it('gives a dropped category share back to the rest in proportion', () => {
    const shares = withCategoryDropped({ residential: 20, 'food-and-agriculture': 60, mobility: 20 }, 'residential');

    expect(total(shares)).toBeCloseTo(100, 9);
    expect(shares['food-and-agriculture']).toBeCloseTo(75, 9);
    expect(shares.mobility).toBeCloseTo(25, 9);
    expect(shares.residential).toBeUndefined();
  });

  it('leaves nothing chosen when the last category is dropped, rather than a not-a-number', () => {
    const shares = withCategoryDropped({ residential: 100 }, 'residential');

    expect(shares).toEqual({});
    expect(total(shares)).toBe(0);
    expect(documentFrom(filled({ shares })).allocations).toBeUndefined();
  });

  it('moves the difference across the others when one share is set', () => {
    const shares = withShareSet({ residential: 50, 'food-and-agriculture': 30, mobility: 20 }, 'residential', 60);

    expect(shares.residential).toBe(60);
    expect(shares['food-and-agriculture']).toBeCloseTo(24, 9);
    expect(shares.mobility).toBeCloseTo(16, 9);
    expect(total(shares)).toBeCloseTo(100, 9);
  });

  it('splits equally where the others were all holding nothing', () => {
    const shares = withShareSet({ residential: 100, 'food-and-agriculture': 0, mobility: 0 }, 'residential', 40);

    expect(shares['food-and-agriculture']).toBeCloseTo(30, 9);
    expect(shares.mobility).toBeCloseTo(30, 9);
    expect(total(shares)).toBeCloseTo(100, 9);
  });

  it('holds a share asked for outside the parcel at the nearest end', () => {
    expect(withShareSet({ residential: 50, mobility: 50 }, 'residential', 140).residential).toBe(100);
    expect(withShareSet({ residential: 50, mobility: 50 }, 'residential', -20).residential).toBe(0);
  });

  it('ignores a share set on a category nobody chose', () => {
    expect(withShareSet({ residential: 100 }, 'mobility', 40)).toEqual({ residential: 100 });
  });

  it('leaves the split alone when a category already chosen is chosen again', () => {
    const shares = { residential: 60, mobility: 40 };

    expect(withCategoryChosen(shares, 'residential')).toBe(shares);
  });

  it('leaves the split alone when a category nobody chose is dropped', () => {
    const shares = { residential: 100 };

    expect(withCategoryDropped(shares, 'mobility')).toBe(shares);
  });
});

describe('the split as it is shown', () => {
  it('shows whole percentages that still add to the whole parcel', () => {
    const thirds = { residential: 100 / 3, 'food-and-agriculture': 100 / 3, mobility: 100 / 3 };

    const shown = wholePercentages(thirds);

    expect(total(shown)).toBe(100);
    expect(Object.values(shown).every(Number.isInteger)).toBe(true);
  });

  it('gives the percentages left over to whoever was rounded down hardest', () => {
    const shown = wholePercentages({ residential: 33.8, 'food-and-agriculture': 33.7, mobility: 32.5 });

    expect(shown).toEqual({ residential: 34, 'food-and-agriculture': 34, mobility: 32 });
    expect(total(shown)).toBe(100);
  });

  it('leaves shares that are already whole alone', () => {
    expect(wholePercentages({ residential: 60, mobility: 40 })).toEqual({ residential: 60, mobility: 40 });
  });

  it('shows nothing where nothing is chosen', () => {
    expect(wholePercentages({})).toEqual({});
  });
});

describe('the document that is posted', () => {
  it('carries every answer the planner gave', () => {
    const document = documentFrom(filled({ shares: { residential: 40, 'food-and-agriculture': 60 } }));

    expect(document).toEqual({
      submissionId: 'sub-0001',
      project: {
        name: 'Willow Bend Regeneration',
        country: 'Portugal',
        nearestCity: 'Santarém',
        existingDataNotes: 'Rainfall held from a 2024 survey.',
      },
      contact: {
        name: 'Ana Ferreira',
        relationshipToProject: 'landowner',
        emailAddress: 'ana.ferreira@example.pt',
        phoneNumber: '+351 200 000 000',
      },
      site: {
        name: 'Willow Bend',
        latitude: 39.5012,
        longitude: -8.4137,
        statedAreaHectares: 24,
        population: 320,
        householdSize: 2.4,
      },
      allocations: [
        { category: 'residential', sharePct: 40 },
        { category: 'food-and-agriculture', sharePct: 60 },
      ],
    });
  });

  it('leaves out an optional field nobody filled in, and carries the ones a submission cannot go without', () => {
    const document = documentFrom({
      ...emptyDraft('sub-0002'),
      projectName: 'Old Quarry Regeneration',
      contactName: 'Ana Ferreira',
      emailAddress: 'ana.ferreira@example.pt',
      siteName: 'Old Quarry',
    });

    expect(document).toEqual({
      submissionId: 'sub-0002',
      project: { name: 'Old Quarry Regeneration' },
      contact: { name: 'Ana Ferreira', emailAddress: 'ana.ferreira@example.pt' },
      site: { name: 'Old Quarry' },
    });
  });

  it('refuses a population typed with a decimal point rather than rounding it', () => {
    expect(documentFrom(filled({ population: '320.5' })).site.population).toBeUndefined();
  });

  it('cannot be submitted until it names the site, the project, and who to tell what was decided', () => {
    expect(readyToSubmit(filled())).toBe(true);
    expect(readyToSubmit(filled({ siteName: '  ' }))).toBe(false);
    expect(readyToSubmit(filled({ projectName: '' }))).toBe(false);
    expect(readyToSubmit(filled({ contactName: '' }))).toBe(false);
    expect(readyToSubmit(filled({ emailAddress: '  ' }))).toBe(false);
  });
});

describe('a draft left and come back to', () => {
  beforeEach(() => localStorage.clear());

  it('restores every answered field after the tab was closed', () => {
    const draft = filled({ shares: { residential: 100 }, visited: ['project', 'contact'] });
    saveDraft(MODEL, draft);

    expect(loadDraft(MODEL)).toEqual(draft);
  });

  it('keeps one draft per model, because a draft proposes land into one of them', () => {
    saveDraft(MODEL, filled({ siteName: 'Willow Bend' }));
    saveDraft('another-model', filled({ siteName: 'Old Quarry' }));

    expect(loadDraft(MODEL)?.siteName).toBe('Willow Bend');
    expect(loadDraft('another-model')?.siteName).toBe('Old Quarry');
  });

  it('carries a changed answer through to what is posted', () => {
    saveDraft(MODEL, filled());
    const reopened = loadDraft(MODEL)!;
    saveDraft(MODEL, { ...reopened, statedArea: '30' });

    expect(documentFrom(loadDraft(MODEL)!).site.statedAreaHectares).toBe(30);
  });

  it('reads nothing where no draft was left', () => {
    expect(loadDraft(MODEL)).toBeNull();
  });

  it('reads nothing rather than throwing where what was stored is not a draft', () => {
    localStorage.setItem('vos-intake-draft:' + MODEL, '{ not json');
    expect(loadDraft(MODEL)).toBeNull();

    localStorage.setItem('vos-intake-draft:' + MODEL, '{"siteName":"Willow Bend"}');
    expect(loadDraft(MODEL)).toBeNull();
  });

  it('falls back to an empty split where what was stored is not a set of shares', () => {
    localStorage.setItem('vos-intake-draft:' + MODEL, JSON.stringify({ submissionId: 'sub-0004', shares: 'all of it' }));

    expect(loadDraft(MODEL)?.shares).toEqual({});
  });

  it('falls back to an empty split where a share is not a figure', () => {
    localStorage.setItem(
      'vos-intake-draft:' + MODEL,
      JSON.stringify({ submissionId: 'sub-0005', shares: { residential: 'most' } }),
    );

    expect(loadDraft(MODEL)?.shares).toEqual({});
  });

  it('falls back to the first step where what was stored names no step this wizard has', () => {
    localStorage.setItem(
      'vos-intake-draft:' + MODEL,
      JSON.stringify({ submissionId: 'sub-0006', visited: ['hazards', 7] }),
    );

    expect(loadDraft(MODEL)?.visited).toEqual(['project']);
  });

  it('keeps the steps it recognises out of what was stored', () => {
    localStorage.setItem(
      'vos-intake-draft:' + MODEL,
      JSON.stringify({ submissionId: 'sub-0007', visited: ['project', 'hazards', 'location'] }),
    );

    expect(loadDraft(MODEL)?.visited).toEqual(['project', 'location']);
  });

  it('fills in a field added since the draft was stored, rather than reading it as undefined', () => {
    localStorage.setItem('vos-intake-draft:' + MODEL, JSON.stringify({ submissionId: 'sub-0003' }));

    expect(loadDraft(MODEL)).toEqual(emptyDraft('sub-0003'));
  });

  it('is gone once it is cleared', () => {
    saveDraft(MODEL, filled());
    clearDraft(MODEL);

    expect(loadDraft(MODEL)).toBeNull();
  });
});

describe('moving between steps', () => {
  it('records a step the planner has been to, so it can be gone back to', () => {
    const visited = withStepVisited(withStepVisited(emptyDraft('sub-0001'), 'contact'), 'location');

    expect(visited.visited).toEqual(['project', 'contact', 'location']);
  });

  it('records a step once however often it is returned to', () => {
    const draft = withStepVisited(emptyDraft('sub-0001'), 'contact');

    expect(withStepVisited(draft, 'contact')).toBe(draft);
  });
});
