import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import type { EffectiveProperty, VosRelationship, VosThing } from '../types/vos';
import type { BasemapSource } from '../types/basemap';
import { ALLOCATION_CATEGORY_ARCHETYPE_FLAG } from './modelVocabulary';

vi.mock('../api/thingApi', () => ({
  thingApi: { getAll: vi.fn(), getAllProperties: vi.fn() },
}));
vi.mock('../api/relationshipApi', () => ({
  relationshipApi: { getAll: vi.fn() },
}));
vi.mock('../api/intakeApi', () => ({
  intakeApi: { configured: vi.fn(), submit: vi.fn() },
}));
vi.mock('../components/common/toastStore', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));
vi.mock('../hooks/useAuth', () => ({ useAuth: () => ({ modelId: 'model-1' }) }));
vi.mock('../components/map/MapView', () => ({
  MapView: ({
    latitude,
    longitude,
    sources,
    boundary,
    onBoundaryChange,
  }: {
    latitude: number;
    longitude: number;
    sources: BasemapSource[];
    boundary?: { latitude: number; longitude: number }[];
    onBoundaryChange?: (boundary: { latitude: number; longitude: number }[]) => void;
  }) => (
    <div
      data-testid="site-map"
      data-latitude={latitude}
      data-longitude={longitude}
      data-sources={sources.map((source) => source.name).join(',')}
      data-corners={boundary?.length ?? ''}
    >
      {onBoundaryChange && (
        <button
          onClick={() =>
            onBoundaryChange([
              ...(boundary ?? []),
              { latitude: 39.5 + (boundary?.length ?? 0) / 500, longitude: -8.41 },
            ])
          }
        >
          stub-place-corner
        </button>
      )}
    </div>
  ),
}));

import { intakeApi } from '../api/intakeApi';
import { relationshipApi } from '../api/relationshipApi';
import { thingApi } from '../api/thingApi';
import { toast } from '../components/common/toastStore';
import { IntakeWizardPage } from './IntakeWizardPage';
import { loadDraft, saveDraft, emptyDraft, type SubmissionDraft } from './intakeWizard';

// Spelled unlike the shipped land-intake template, so a page answering only to that spelling fails.
const thing = (Id: string, Name: string, IsArchetype = false): VosThing => ({
  Id,
  Name,
  IsArchetype,
  Properties: {},
});

const edge = (Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship => ({
  Id,
  SubjectId,
  PredicateId,
  TargetId,
  Properties: {},
});

const owned = (Value: unknown): EffectiveProperty => ({ Value, Type: 'vos.Boolean', IsInherited: false });

const THINGS: VosThing[] = [
  thing('is', 'is'),
  thing('land-use', 'LandUse', true),
  thing('housing', 'housing'),
  thing('growing', 'growing'),
  thing('roads', 'roads'),
  thing('basemap-source', 'BasemapSource', true),
  {
    Id: 'aerial',
    Name: 'Aerial imagery',
    Properties: { attribution: 'Sample credit', tileUrl: 'https://tiles.example.org/{z}/{x}/{y}.png' },
  },
];

const EDGES: VosRelationship[] = [
  edge('e1', 'housing', 'is', 'land-use'),
  edge('e2', 'growing', 'is', 'land-use'),
  edge('e3', 'roads', 'is', 'land-use'),
  edge('e4', 'aerial', 'is', 'basemap-source'),
];

const PROPERTIES = { 'land-use': { [ALLOCATION_CATEGORY_ARCHETYPE_FLAG]: owned(true) } };

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  vi.mocked(thingApi.getAll).mockResolvedValue(THINGS);
  vi.mocked(relationshipApi.getAll).mockResolvedValue(EDGES);
  vi.mocked(thingApi.getAllProperties).mockResolvedValue(PROPERTIES);
  vi.mocked(intakeApi.configured).mockReturnValue(true);
  vi.mocked(intakeApi.submit).mockResolvedValue({ siteId: 'site-1', studyId: 'study-1' });
});

/** Walk to a step by pressing Next, which is also what makes each one reachable again. */
function goToStep(times: number): void {
  for (let step = 0; step < times; step += 1) fireEvent.click(screen.getByRole('button', { name: 'Next' }));
}

function typeInto(label: string, value: string): void {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

/** The slider in the row a category's checkbox sits in, so a test never depends on the order the
 *  categories happen to be listed in. */
function sliderFor(category: string): HTMLElement {
  const row = screen.getByLabelText(category).closest('div')!;
  return within(row).getByRole('slider');
}

describe('collecting a submission', () => {
  it('opens on the project step', () => {
    render(<IntakeWizardPage />);

    expect(screen.getByLabelText('Project name')).toBeInTheDocument();
  });

  it('keeps what the planner typed when the tab is closed and opened again', () => {
    const { unmount } = render(<IntakeWizardPage />);
    typeInto('Project name', 'Willow Bend Regeneration');
    unmount();

    render(<IntakeWizardPage />);

    expect(screen.getByLabelText('Project name')).toHaveValue('Willow Bend Regeneration');
  });

  it('goes back to an earlier step and takes the changed answer with it', () => {
    render(<IntakeWizardPage />);
    typeInto('Project name', 'Willow Bend Regeneration');
    goToStep(1);

    fireEvent.click(screen.getByRole('button', { name: '1. Project' }));
    typeInto('Project name', 'Old Quarry Regeneration');

    expect(loadDraft('model-1')?.projectName).toBe('Old Quarry Regeneration');
  });

  it('keeps every answer the project step asks for', () => {
    render(<IntakeWizardPage />);

    typeInto('Project name', 'Willow Bend Regeneration');
    typeInto('Country', 'Portugal');
    typeInto('Closest city', 'Santarém');
    typeInto('Surveys or data already held', 'Rainfall held from a 2024 survey.');

    expect(loadDraft('model-1')).toMatchObject({
      projectName: 'Willow Bend Regeneration',
      country: 'Portugal',
      nearestCity: 'Santarém',
      existingDataNotes: 'Rainfall held from a 2024 survey.',
    });
  });

  it('keeps the contact answers, which hang off the project rather than the site', () => {
    render(<IntakeWizardPage />);
    goToStep(1);

    typeInto('Name', 'Ana Ferreira');
    typeInto('Relationship to the project', 'landowner');
    typeInto('Email address', 'ana.ferreira@example.pt');
    typeInto('Phone number', '+351 200 000 000');

    expect(loadDraft('model-1')?.contactName).toBe('Ana Ferreira');
    expect(loadDraft('model-1')?.relationshipToProject).toBe('landowner');
    expect(loadDraft('model-1')?.emailAddress).toBe('ana.ferreira@example.pt');
    expect(loadDraft('model-1')?.phoneNumber).toBe('+351 200 000 000');
  });

  it('steps back one at a time as well as jumping to a visited step', () => {
    render(<IntakeWizardPage />);
    goToStep(2);

    fireEvent.click(screen.getByRole('button', { name: 'Back' }));

    expect(screen.getByLabelText('Name')).toBeInTheDocument();
  });

  it('leaves a step nobody has been to yet out of reach', () => {
    render(<IntakeWizardPage />);

    expect(screen.getByRole('button', { name: '4. Size and programme' })).toBeDisabled();
  });

  it('fills the coordinates in from a pasted map link', () => {
    render(<IntakeWizardPage />);
    goToStep(2);

    typeInto('Paste a map link', 'https://www.google.com/maps/@39.5012,-8.4137,15z');

    expect(screen.getByLabelText('Latitude')).toHaveValue('39.5012');
    expect(screen.getByLabelText('Longitude')).toHaveValue('-8.4137');
    expect(screen.getByText('Coordinates read from the link.')).toBeInTheDocument();
  });

  it('takes a site name and coordinates typed by hand, with no link involved', () => {
    render(<IntakeWizardPage />);
    goToStep(2);

    typeInto('Site name', 'Willow Bend');
    typeInto('Latitude', '39.5012');
    typeInto('Longitude', '-8.4137');

    const draft = loadDraft('model-1');
    expect(draft?.siteName).toBe('Willow Bend');
    expect(draft?.latitude).toBe('39.5012');
    expect(draft?.longitude).toBe('-8.4137');
  });

  it('stops saying anything about the link once it is cleared', () => {
    render(<IntakeWizardPage />);
    goToStep(2);
    typeInto('Paste a map link', 'https://www.example.com/about-us');
    expect(screen.getByText('No coordinates were found in that text.')).toBeInTheDocument();

    typeInto('Paste a map link', '');

    expect(screen.queryByText('No coordinates were found in that text.')).not.toBeInTheDocument();
  });

  it('says what to do about a shortened link rather than failing silently', () => {
    render(<IntakeWizardPage />);
    goToStep(2);

    typeInto('Paste a map link', 'https://maps.app.goo.gl/AbCdEf123');

    expect(screen.getByText(/shortened link cannot be opened/)).toBeInTheDocument();
    expect(screen.getByLabelText('Latitude')).toHaveValue('');
  });
});

describe('the site on the map', () => {
  it('appears once both halves of the position are given, on the sources the model declares', async () => {
    render(<IntakeWizardPage />);
    goToStep(2);
    expect(screen.getByText('The map appears once both coordinates are given.')).toBeInTheDocument();

    typeInto('Latitude', '39.5012');
    expect(screen.queryByTestId('site-map')).not.toBeInTheDocument();
    typeInto('Longitude', '-8.4137');

    const map = await screen.findByTestId('site-map');
    expect(map.dataset.latitude).toBe('39.5012');
    expect(map.dataset.longitude).toBe('-8.4137');
    expect(map.dataset.sources).toBe('Aerial imagery');
    expect(screen.queryByText('The map appears once both coordinates are given.')).not.toBeInTheDocument();
  });

  it('moves to what a pasted link says, and the stored coordinates follow it', async () => {
    render(<IntakeWizardPage />);
    goToStep(2);
    typeInto('Latitude', '39.5012');
    typeInto('Longitude', '-8.4137');
    await screen.findByTestId('site-map');

    typeInto('Paste a map link', 'https://www.google.com/maps/@41.1496,-8.6109,15z');

    const map = await screen.findByTestId('site-map');
    expect(map.dataset.latitude).toBe('41.1496');
    expect(map.dataset.longitude).toBe('-8.6109');
    expect(loadDraft('model-1')?.latitude).toBe('41.1496');
  });

  it('stays away while a coordinate points off the Earth, leaving the fields to correct', () => {
    render(<IntakeWizardPage />);
    goToStep(2);

    typeInto('Latitude', '95');
    typeInto('Longitude', '-8.4137');

    expect(screen.queryByTestId('site-map')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Latitude')).not.toBeDisabled();
  });
});

describe('the size and programme step', () => {
  beforeEach(() => {
    render(<IntakeWizardPage />);
    goToStep(3);
  });

  it('shows the area in the other unit as it is typed', () => {
    typeInto('Land area', '24');

    expect(screen.getByText('= 59.31 acres')).toBeInTheDocument();
  });

  it('shows an area typed in acres back in hectares', async () => {
    typeInto('Land area', '10');
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'acres' } });

    expect(screen.getByText('= 4.05 hectares')).toBeInTheDocument();
  });

  it('keeps how many people the land is for', () => {
    typeInto('Population', '320');
    typeInto('People per household', '2.4');

    expect(loadDraft('model-1')?.population).toBe('320');
    expect(loadDraft('model-1')?.householdSize).toBe('2.4');
  });

  it('says an area that is not a figure will be left out', () => {
    typeInto('Land area', '-4');

    expect(screen.getByText(/not an area/)).toBeInTheDocument();
  });

  it('offers the categories the model declares, not a list of its own', async () => {
    await waitFor(() => expect(screen.getByLabelText('housing')).toBeInTheDocument());

    expect(screen.getByLabelText('growing')).toBeInTheDocument();
  });

  it('moves the difference across the others when one share is dragged', async () => {
    await waitFor(() => expect(screen.getByLabelText('housing')).toBeInTheDocument());
    fireEvent.click(screen.getByLabelText('housing'));
    fireEvent.click(screen.getByLabelText('growing'));

    fireEvent.change(sliderFor('housing'), { target: { value: '70' } });

    expect(loadDraft('model-1')?.shares).toEqual({ housing: 70, growing: 30 });
  });

  it('shows percentages that add to the whole parcel, not to ninety-nine', async () => {
    await waitFor(() => expect(screen.getByLabelText('housing')).toBeInTheDocument());
    for (const category of ['housing', 'growing', 'roads']) fireEvent.click(screen.getByLabelText(category));

    const shown = screen.getAllByText(/^\d+%$/).map((cell) => Number(cell.textContent!.replace('%', '')));

    expect(shown).toHaveLength(3);
    expect(shown.reduce((sum, share) => sum + share, 0)).toBe(100);
  });

  it('gives a dropped category share back to the rest', async () => {
    await waitFor(() => expect(screen.getByLabelText('housing')).toBeInTheDocument());
    fireEvent.click(screen.getByLabelText('housing'));
    fireEvent.click(screen.getByLabelText('growing'));

    fireEvent.click(screen.getByLabelText('housing'));

    expect(loadDraft('model-1')?.shares).toEqual({ growing: 100 });
  });
});

describe('the parcel step', () => {
  function located(patch: Partial<SubmissionDraft> = {}): void {
    saveDraft('model-1', {
      ...emptyDraft('sub-0001'),
      siteName: 'Willow Bend',
      latitude: '39.5',
      longitude: '-8.41',
      statedArea: '24',
      ...patch,
    });
  }

  it('drafts a square from the stated area and reports it within tolerance of itself', async () => {
    located();
    render(<IntakeWizardPage />);
    goToStep(4);

    fireEvent.click(screen.getByRole('button', { name: 'Place a draft boundary' }));

    expect(screen.getByText('The drawn and stated areas agree within tolerance.')).toBeInTheDocument();
    expect(loadDraft('model-1')?.boundarySource).toBe('generated-from-stated-area');
    expect(loadDraft('model-1')?.boundary).toHaveLength(4);
    expect((await screen.findByTestId('site-map')).dataset.corners).toBe('4');
  });

  it('offers no draft while the stated area is missing', () => {
    located({ statedArea: '' });
    render(<IntakeWizardPage />);
    goToStep(4);

    expect(screen.getByRole('button', { name: 'Place a draft boundary' })).toBeDisabled();
  });

  it('marks a boundary drawn on the map as drawn by hand', async () => {
    located();
    render(<IntakeWizardPage />);
    goToStep(4);

    for (let corner = 0; corner < 3; corner += 1) {
      fireEvent.click(await screen.findByRole('button', { name: 'stub-place-corner' }));
    }

    expect(loadDraft('model-1')?.boundarySource).toBe('drawn-by-hand');
    expect(loadDraft('model-1')?.boundary).toHaveLength(3);
  });

  it('says how far apart the drawn and stated areas are when they disagree', () => {
    located({
      boundary: [
        { latitude: 39.5, longitude: -8.41 },
        { latitude: 39.501, longitude: -8.41 },
        { latitude: 39.501, longitude: -8.409 },
      ],
      boundarySource: 'drawn-by-hand',
    });
    render(<IntakeWizardPage />);
    goToStep(4);

    expect(screen.getByText(/away from the stated area/)).toBeInTheDocument();
  });

  it('clears the boundary from the map and the stored draft together', async () => {
    located();
    render(<IntakeWizardPage />);
    goToStep(4);
    fireEvent.click(screen.getByRole('button', { name: 'Place a draft boundary' }));

    fireEvent.click(screen.getByRole('button', { name: 'Clear the boundary' }));

    expect(loadDraft('model-1')?.boundary).toEqual([]);
    expect((await screen.findByTestId('site-map')).dataset.corners).toBe('0');
  });

  it('asks for a position before there is a map to draw on', () => {
    located({ latitude: '', longitude: '' });
    render(<IntakeWizardPage />);
    goToStep(4);

    expect(screen.getByText('The map appears once both coordinates are given.')).toBeInTheDocument();
    expect(screen.queryByTestId('site-map')).not.toBeInTheDocument();
  });
});

describe('posting the submission', () => {
  it('will not submit without a site name, which is what a Thing is created under', () => {
    render(<IntakeWizardPage />);
    goToStep(4);

    expect(screen.getByRole('button', { name: 'Submit' })).toBeDisabled();
    expect(screen.getByText('A site name is needed.')).toBeInTheDocument();
  });

  it('will not submit where no intake address is configured, and says so', () => {
    vi.mocked(intakeApi.configured).mockReturnValue(false);
    saveDraft('model-1', { ...emptyDraft('sub-0001'), siteName: 'Willow Bend' });
    render(<IntakeWizardPage />);
    goToStep(4);

    expect(screen.getByRole('button', { name: 'Submit' })).toBeDisabled();
    expect(screen.getByText('No intake service address is configured.')).toBeInTheDocument();
  });

  it('posts what was collected and shows the Things it became', async () => {
    saveDraft('model-1', { ...emptyDraft('sub-0001'), siteName: 'Willow Bend', statedArea: '24' });
    render(<IntakeWizardPage />);
    goToStep(4);

    fireEvent.click(screen.getByRole('button', { name: 'Submit' }));

    await waitFor(() => expect(screen.getByText('Submitted')).toBeInTheDocument());
    expect(intakeApi.submit).toHaveBeenCalledWith(
      expect.objectContaining({ submissionId: 'sub-0001', site: { name: 'Willow Bend', statedAreaHectares: 24 } }),
    );
    expect(screen.getByText('site-1')).toBeInTheDocument();
  });

  it('posts the boundary and how it was obtained along with everything else', async () => {
    const corners = [
      { latitude: 39.5, longitude: -8.41 },
      { latitude: 39.502, longitude: -8.41 },
      { latitude: 39.502, longitude: -8.408 },
    ];
    saveDraft('model-1', {
      ...emptyDraft('sub-0001'),
      siteName: 'Willow Bend',
      boundary: corners,
      boundarySource: 'drawn-by-hand',
    });
    render(<IntakeWizardPage />);
    goToStep(4);

    fireEvent.click(screen.getByRole('button', { name: 'Submit' }));

    await waitFor(() =>
      expect(intakeApi.submit).toHaveBeenCalledWith(
        expect.objectContaining({ parcel: { boundarySource: 'drawn-by-hand', boundary: corners } }),
      ),
    );
  });

  it('clears the draft once it is in the model, so reopening starts a new submission', async () => {
    saveDraft('model-1', { ...emptyDraft('sub-0001'), siteName: 'Willow Bend' });
    render(<IntakeWizardPage />);
    goToStep(4);

    fireEvent.click(screen.getByRole('button', { name: 'Submit' }));

    await waitFor(() => expect(loadDraft('model-1')).toBeNull());
  });

  it('starts a fresh submission after one has landed, under a new identifier', async () => {
    saveDraft('model-1', { ...emptyDraft('sub-0001'), siteName: 'Willow Bend' });
    render(<IntakeWizardPage />);
    goToStep(4);
    fireEvent.click(screen.getByRole('button', { name: 'Submit' }));
    await waitFor(() => expect(screen.getByText('Submitted')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Start another' }));

    expect(screen.getByLabelText('Project name')).toHaveValue('');
    expect(screen.getByRole('button', { name: '4. Size and programme' })).toBeDisabled();
  });

  it('keeps the draft when the service refuses it, and says what was wrong', async () => {
    vi.mocked(intakeApi.submit).mockRejectedValue(new Error("'site.name' is missing"));
    saveDraft('model-1', { ...emptyDraft('sub-0001'), siteName: 'Willow Bend' });
    render(<IntakeWizardPage />);
    goToStep(4);

    fireEvent.click(screen.getByRole('button', { name: 'Submit' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(loadDraft('model-1')?.siteName).toBe('Willow Bend');
  });
});

describe('a model the vocabulary cannot be read from', () => {
  it('says there is nothing to divide the land between rather than showing an empty list', async () => {
    vi.mocked(thingApi.getAll).mockRejectedValue(new Error('unreadable'));
    render(<IntakeWizardPage />);
    goToStep(3);

    await waitFor(() =>
      expect(screen.getByText(/declares no programme categories/)).toBeInTheDocument(),
    );
  });
});
