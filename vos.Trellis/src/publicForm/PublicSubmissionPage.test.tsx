import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import i18n from '../i18n';
import { MULTI_STEP_FORM_TEST_TIMEOUT_MILLISECONDS } from '../testTimeouts';

vi.setConfig({ testTimeout: MULTI_STEP_FORM_TEST_TIMEOUT_MILLISECONDS });

vi.mock('../api/intakeApi', () => ({
  intakeApi: { configured: vi.fn(), formOptions: vi.fn(), submit: vi.fn(), askForCode: vi.fn() },
}));
vi.mock('../components/map/MapView', () => ({ MapView: () => <div data-testid="site-map" /> }));

import { intakeApi } from '../api/intakeApi';
import {
  BOUNDARY_GENERATED_FROM_STATED_AREA,
  loadDraft,
  saveDraft,
  emptyDraft,
  type SubmissionDraft,
} from '../intake/submissionDraft';
import { PublicSubmissionPage } from './PublicSubmissionPage';

const DRAFT_OWNER = 'public-form';

/** A draft carrying what a submission cannot go without, so a test about the exchange does not spend its
 *  length filling a form in. */
function submittable(patch: Partial<SubmissionDraft> = {}): SubmissionDraft {
  return {
    ...emptyDraft('sub-0001'),
    siteName: 'Willow Bend',
    projectName: 'Willow Bend Regeneration',
    contactName: 'Ana Ferreira',
    emailAddress: 'ana.ferreira@example.pt',
    // The land the submission is about, with how the boundary was come by: a draft describing none
    // cannot be submitted, and a stored boundary without its source is not a parcel and is dropped
    // when read back.
    boundary: [
      { latitude: 39.4988, longitude: -8.4168 },
      { latitude: 39.5036, longitude: -8.4168 },
      { latitude: 39.5036, longitude: -8.4106 },
    ],
    boundarySource: BOUNDARY_GENERATED_FROM_STATED_AREA,
    ...patch,
  };
}

function goToLastStep(): void {
  for (let step = 0; step < 5; step += 1) {
    fireEvent.click(screen.getByRole('button', { name: 'Next' }));
  }
}

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  vi.mocked(intakeApi.configured).mockReturnValue(true);
  vi.mocked(intakeApi.formOptions).mockResolvedValue({
    allocationCategories: ['residential', 'food-and-agriculture'],
    basemapSources: [],
    hazardTypes: [],
    hazardLevels: [],
    defaultProgramme: [],
    parcelLookup: false,
    placeSearch: false,
    wording: {},
    themes: [],
  });
});

describe('the form somebody without an account fills in', () => {
  it('offers the categories the intake service answers with', async () => {
    render(<PublicSubmissionPage />);
    goToLastStep();
    fireEvent.click(screen.getByRole('button', { name: '4. Size and programme' }));

    expect(await screen.findByLabelText('residential')).toBeInTheDocument();
    expect(screen.getByLabelText('food-and-agriculture')).toBeInTheDocument();
  });

  // Offered in the reader's words and kept by the model's name, which is what the service resolves.
  it('offers each category in the reader’s language and keeps it by its name', async () => {
    vi.mocked(intakeApi.formOptions).mockResolvedValue({
      ...(await intakeApi.formOptions()),
      wording: { residential: { en: 'Housing', de: 'Wohnen' } },
    });
    render(<PublicSubmissionPage />);
    goToLastStep();
    fireEvent.click(screen.getByRole('button', { name: '4. Size and programme' }));
    await screen.findByLabelText('Housing');

    await act(() => i18n.changeLanguage('de'));
    try {
      fireEvent.click(screen.getByLabelText('Wohnen'));

      expect(Object.keys(loadDraft(DRAFT_OWNER)?.shares ?? {})).toContain('residential');
      expect(screen.getByLabelText('food-and-agriculture')).toBeInTheDocument();
    } finally {
      await act(() => i18n.changeLanguage('en'));
    }
  });

  it('says so when the service cannot be reached, rather than showing a form with nothing in it', async () => {
    vi.mocked(intakeApi.formOptions).mockRejectedValue(new Error('unreachable'));

    render(<PublicSubmissionPage />);

    expect(
      await screen.findByText(
        'This form cannot reach the submission service at the moment. Nothing you have typed has been sent.',
      ),
    ).toBeInTheDocument();
  });

  // The whole point of the exchange: the address goes on its own, and what was collected follows only
  // once the code sent to that address has been answered.
  it('sends the address first and the submission only after the code is answered', async () => {
    vi.mocked(intakeApi.askForCode).mockResolvedValue(undefined);
    vi.mocked(intakeApi.submit).mockResolvedValue({ reference: 'sub-ref-1' });
    saveDraft(DRAFT_OWNER, submittable());

    render(<PublicSubmissionPage />);
    goToLastStep();
    fireEvent.click(screen.getByRole('button', { name: 'Send a code' }));

    await waitFor(() => expect(intakeApi.askForCode).toHaveBeenCalledWith('ana.ferreira@example.pt'));
    expect(intakeApi.submit).not.toHaveBeenCalled();

    fireEvent.change(await screen.findByLabelText('Code'), { target: { value: '314159' } });
    fireEvent.click(screen.getByRole('button', { name: 'Submit' }));

    await waitFor(() => expect(intakeApi.submit).toHaveBeenCalledTimes(1));
    expect(vi.mocked(intakeApi.submit).mock.calls[0][1]).toBe('314159');
  });

  it('picks a half-finished form back up, so a person can come back to it', () => {
    saveDraft(DRAFT_OWNER, submittable());

    render(<PublicSubmissionPage />);

    expect(screen.getByLabelText('Project name')).toHaveValue('Willow Bend Regeneration');
  });

  // The draft belongs to the form rather than to a model, which is what a planner's draft belongs to.
  // Keyed the way a planner's is, a stranger's browser would hold a draft under nothing.
  it('keeps what was typed under the form, so a reload finds it', () => {
    render(<PublicSubmissionPage />);
    fireEvent.change(screen.getByLabelText('Project name'), { target: { value: 'Willow Bend' } });

    expect(loadDraft(DRAFT_OWNER)?.projectName).toBe('Willow Bend');
  });
});

describe('the form on a phone', () => {
  it('asks the phone for the keyboard each contact field needs', () => {
    saveDraft(DRAFT_OWNER, submittable());
    render(<PublicSubmissionPage />);
    goToLastStep();
    fireEvent.click(screen.getByRole('button', { name: '2. Contact' }));

    expect(screen.getByLabelText('Name')).toHaveAttribute('autocomplete', 'name');
    expect(screen.getByLabelText('Email address')).toHaveAttribute('type', 'email');
    expect(screen.getByLabelText('Email address')).toHaveAttribute('autocomplete', 'email');
    expect(screen.getByLabelText('Phone number')).toHaveAttribute('type', 'tel');
    expect(screen.getByLabelText('Phone number')).toHaveAttribute('autocomplete', 'tel');
  });

  it('offers the code the mail brought, on a number keyboard', async () => {
    vi.mocked(intakeApi.askForCode).mockResolvedValue(undefined);
    saveDraft(DRAFT_OWNER, submittable());
    render(<PublicSubmissionPage />);
    goToLastStep();
    fireEvent.click(screen.getByRole('button', { name: 'Send a code' }));

    const code = await screen.findByLabelText('Code');
    expect(code).toHaveAttribute('inputmode', 'numeric');
    expect(code).toHaveAttribute('autocomplete', 'one-time-code');
  });

  it('keeps the send-code label on one line and puts what is missing on a row of its own', () => {
    saveDraft(DRAFT_OWNER, submittable({ siteName: '' }));
    render(<PublicSubmissionPage />);
    goToLastStep();

    expect(screen.getByRole('button', { name: 'Send a code' })).toHaveClass('whitespace-nowrap');
    expect(
      screen.getByText('A site name, a project name, and a contact name and email address are all needed.'),
    ).toHaveClass('basis-full');
  });
});
