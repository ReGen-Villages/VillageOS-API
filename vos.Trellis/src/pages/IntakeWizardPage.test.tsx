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
  intakeApi: { configured: vi.fn(), submit: vi.fn(), askForCode: vi.fn() },
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
import {
  BOUNDARY_GENERATED_FROM_STATED_AREA,
  loadDraft,
  saveDraft,
  emptyDraft,
  type SubmissionDraft,
} from '../intake/submissionDraft';

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
  vi.mocked(intakeApi.submit).mockResolvedValue({ reference: 'sub-0001' });
  // Resolved a tick late, the way a real request does, so the code field appears after the click
  // rather than with it. A mock resolving at once hid that a test read the field before it existed,
  // which only a loaded build agent showed.
  vi.mocked(intakeApi.askForCode).mockImplementation(() => new Promise((resolve) => setTimeout(resolve, 0)));
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
  /** A draft carrying what a submission cannot go without: the site, the project it belongs to, and who
   *  to tell what was decided about it. */
  function submittable(patch: Partial<SubmissionDraft> = {}): SubmissionDraft {
    return {
      ...emptyDraft('sub-0001'),
      siteName: 'Willow Bend',
      projectName: 'Willow Bend Regeneration',
      contactName: 'Ana Ferreira',
      emailAddress: 'ana.ferreira@example.pt',
      // The land the submission is about. A draft describing none cannot be submitted at all, so a
      // fixture named for being submittable has to enclose some — and has to say how the boundary was
      // come by, because a stored one without its source is not a parcel and is dropped when read back.
      boundary: [
        { latitude: 39.4988, longitude: -8.4168 },
        { latitude: 39.5036, longitude: -8.4168 },
        { latitude: 39.5036, longitude: -8.4106 },
      ],
      boundarySource: BOUNDARY_GENERATED_FROM_STATED_AREA,
      ...patch,
    };
  }

  /** The whole of what submitting is now: ask for a code, read it off the mail the person got, and send
   *  the submission under it. */
  async function askForACodeAndSubmit(code = '314159'): Promise<void> {
    fireEvent.click(screen.getByRole('button', { name: 'Send a code' }));

    // Waited for the field itself, not for the request that leads to it: the field appears when the
    // request resolves, a tick after it was made.
    fireEvent.change(await screen.findByLabelText('Code'), { target: { value: code } });
    fireEvent.click(screen.getByRole('button', { name: 'Submit' }));
  }

  it('will not ask for a code until it names the site, the project, and who to send it to', () => {
    render(<IntakeWizardPage />);
    goToStep(4);

    expect(screen.getByRole('button', { name: 'Send a code' })).toBeDisabled();
    expect(
      screen.getByText(
        'A site name, a project name, and a contact name and email address are all needed.',
      ),
    ).toBeInTheDocument();
  });

  it('will not submit a site nobody can be told the decision about', () => {
    saveDraft('model-1', submittable({ emailAddress: '' }));
    render(<IntakeWizardPage />);
    goToStep(4);

    expect(screen.getByRole('button', { name: 'Send a code' })).toBeDisabled();
  });

  it('will not submit where no intake address is configured, and says so', () => {
    vi.mocked(intakeApi.configured).mockReturnValue(false);
    saveDraft('model-1', submittable());
    render(<IntakeWizardPage />);
    goToStep(4);

    expect(screen.getByRole('button', { name: 'Send a code' })).toBeDisabled();
    expect(screen.getByText('No intake service address is configured.')).toBeInTheDocument();
  });

  it('posts what was collected and answers with the reference to quote', async () => {
    saveDraft('model-1', submittable({ statedArea: '24' }));
    render(<IntakeWizardPage />);
    goToStep(4);

    await askForACodeAndSubmit();

    await waitFor(() => expect(screen.getByText('Submitted')).toBeInTheDocument());
    expect(intakeApi.askForCode).toHaveBeenCalledWith('ana.ferreira@example.pt');
    expect(intakeApi.submit).toHaveBeenCalledWith(
      expect.objectContaining({ submissionId: 'sub-0001', site: { name: 'Willow Bend', statedAreaHectares: 24 } }),
      '314159',
    );
    expect(screen.getByText('sub-0001')).toBeInTheDocument();
  });

  it('posts the boundary and how it was obtained along with everything else', async () => {
    const corners = [
      { latitude: 39.5, longitude: -8.41 },
      { latitude: 39.502, longitude: -8.41 },
      { latitude: 39.502, longitude: -8.408 },
    ];
    saveDraft('model-1', submittable({ boundary: corners, boundarySource: 'drawn-by-hand' }));
    render(<IntakeWizardPage />);
    goToStep(4);

    await askForACodeAndSubmit();

    await waitFor(() =>
      expect(intakeApi.submit).toHaveBeenCalledWith(
        expect.objectContaining({ parcel: { boundarySource: 'drawn-by-hand', boundary: corners } }),
        '314159',
      ),
    );
  });

  it('clears the draft once it is in the model, so reopening starts a new submission', async () => {
    saveDraft('model-1', submittable());
    render(<IntakeWizardPage />);
    goToStep(4);

    await askForACodeAndSubmit();

    await waitFor(() => expect(loadDraft('model-1')).toBeNull());
  });

  it('starts a fresh submission after one has landed, under a new identifier', async () => {
    saveDraft('model-1', submittable());
    render(<IntakeWizardPage />);
    goToStep(4);
    await askForACodeAndSubmit();
    await waitFor(() => expect(screen.getByText('Submitted')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Start another' }));

    expect(screen.getByLabelText('Project name')).toHaveValue('');
    expect(screen.getByRole('button', { name: '4. Size and programme' })).toBeDisabled();
  });

  it('keeps the draft when the service refuses it, and says what was wrong', async () => {
    vi.mocked(intakeApi.submit).mockRejectedValue(new Error("'site.name' is missing"));
    saveDraft('model-1', submittable());
    render(<IntakeWizardPage />);
    goToStep(4);

    await askForACodeAndSubmit();

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(loadDraft('model-1')?.siteName).toBe('Willow Bend');
  });

  // A code that never arrived is where a submitter is left with a form and nothing to do, so the page
  // says what went wrong and stays where it was rather than asking for a code it never sent.
  it('says why no code could be sent, and does not ask for one back', async () => {
    vi.mocked(intakeApi.askForCode).mockRejectedValue(
      new Error('That address has been sent as many codes as it can be for now.'),
    );
    saveDraft('model-1', submittable());
    render(<IntakeWizardPage />);
    goToStep(4);

    fireEvent.click(screen.getByRole('button', { name: 'Send a code' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(screen.queryByLabelText('Code')).not.toBeInTheDocument();
    expect(intakeApi.submit).not.toHaveBeenCalled();
  });

  // The code was sent to the address as it read at the time. Going back and changing it leaves that code
  // good for a mailbox this submission no longer names, and the service would refuse it — so the page
  // asks again rather than offering a box whose code cannot work.
  it('asks for a new code when the address is changed after one was sent', async () => {
    saveDraft('model-1', submittable());
    render(<IntakeWizardPage />);
    goToStep(4);
    fireEvent.click(screen.getByRole('button', { name: 'Send a code' }));
    await waitFor(() => expect(screen.getByLabelText('Code')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: '2. Contact' }));
    typeInto('Email address', 'somebody.else@example.pt');
    goToStep(3);

    expect(screen.queryByLabelText('Code')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Send a code' })).toBeInTheDocument();
  });

  // The code is what says this address can be read by whoever is submitting, so the submission cannot go
  // before one has been entered.
  it('will not submit with the code box empty', async () => {
    saveDraft('model-1', submittable());
    render(<IntakeWizardPage />);
    goToStep(4);

    fireEvent.click(screen.getByRole('button', { name: 'Send a code' }));
    await waitFor(() => expect(screen.getByLabelText('Code')).toBeInTheDocument());

    expect(screen.getByRole('button', { name: 'Submit' })).toBeDisabled();
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
