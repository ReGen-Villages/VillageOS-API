import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import i18n from '../i18n';
import type { FormOptions } from '../api/intakeApi';

vi.mock('../api/intakeApi', () => ({
  intakeApi: {
    askForCode: vi.fn(),
    exchangeTicket: vi.fn(),
    formOptions: vi.fn(),
    searchPlaces: vi.fn(),
    borderElevation: vi.fn(),
    submitWithTicket: vi.fn(),
  },
}));

const shown = vi.hoisted(() => ({ onEdit: null as null | ((edit: unknown) => void) }));
vi.mock('../components/map/MapView', () => ({
  MapView: ({ borderEditing }: { borderEditing?: { onEdit: (edit: unknown) => void } }) => {
    shown.onEdit = borderEditing?.onEdit ?? null;
    return <div data-testid="map" />;
  },
}));

import { intakeApi } from '../api/intakeApi';
import { StudyPage } from './StudyPage';

const LAST_ADDRESS_KEY = 'studyLastEmailAddress';

beforeEach(async () => {
  vi.clearAllMocks();
  localStorage.clear();
  await i18n.changeLanguage('en');
  vi.mocked(intakeApi.askForCode).mockResolvedValue(undefined);
  vi.mocked(intakeApi.exchangeTicket).mockResolvedValue('ticket-1');
  vi.mocked(intakeApi.formOptions).mockResolvedValue({
    basemapSources: [],
    parcelLookup: false,
    placeSearch: true,
    areaMethod: 'meanSphereAreaMethod',
  } as unknown as FormOptions);
  vi.mocked(intakeApi.searchPlaces).mockResolvedValue({
    places: [{ name: 'Willow Bend Farm, Alentejo', latitude: 38.5, longitude: -7.9 }],
    ticket: 'ticket-2',
  });
  vi.mocked(intakeApi.borderElevation).mockResolvedValue({ elevation: null, ticket: 'ticket-3' });
  vi.mocked(intakeApi.submitWithTicket).mockResolvedValue({ accepted: { reference: 'reference-1' }, ticket: 'ticket-4' });
});

async function findAndConfirmSite() {
  fireEvent.change(await screen.findByRole('searchbox', { name: 'Address or coordinates' }), { target: { value: 'Willow Bend' } });
  fireEvent.click(screen.getByRole('button', { name: 'Find' }));
  await screen.findByRole('button', { name: 'Draw border' });
  fireEvent.click(screen.getByRole('button', { name: 'Draw border' }));
  const corners = [
    { latitude: 38.5, longitude: -7.9 },
    { latitude: 38.5, longitude: -7.89 },
    { latitude: 38.51, longitude: -7.89 },
  ];
  corners.forEach((corner) => act(() => shown.onEdit!({ kind: 'place', corner })));
  act(() => shown.onEdit!({ kind: 'close' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Confirm site' }));
  await waitFor(() => expect(intakeApi.submitWithTicket).toHaveBeenCalled());
}

function typeInto(label: string, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } });
}

async function signIn() {
  typeInto('Email', 'ana.ferreira@example.pt');
  typeInto('Project name', 'Willow Bend');
  fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
  fireEvent.change(await screen.findByLabelText('Code'), { target: { value: '314159' } });
  fireEvent.click(screen.getByRole('button', { name: 'Confirm' }));
  await screen.findByRole('navigation', { name: 'Study steps' });
}

describe('signing in', () => {
  it('asks for the address and the project name before anything else', () => {
    render(<StudyPage />);

    expect(screen.getByLabelText('Email')).toBeInTheDocument();
    expect(screen.getByLabelText('Project name')).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Study steps' })).not.toBeInTheDocument();
  });

  it('starts with the address this browser signed in with last', () => {
    localStorage.setItem(LAST_ADDRESS_KEY, 'ana.ferreira@example.pt');

    render(<StudyPage />);

    expect(screen.getByLabelText('Email')).toHaveValue('ana.ferreira@example.pt');
  });

  it('starts with an empty address where the browser keeps nothing', () => {
    localStorage.setItem(LAST_ADDRESS_KEY, 'ana.ferreira@example.pt');
    // The whole object is replaced, because a property set on a browser's storage is stored as an item.
    vi.stubGlobal('localStorage', {
      getItem: () => {
        throw new Error('storage is off');
      },
      setItem: () => {
        throw new Error('storage is off');
      },
    });
    try {
      render(<StudyPage />);

      expect(screen.getByLabelText('Email')).toHaveValue('');
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it('says what is missing and sends nothing', () => {
    render(<StudyPage />);

    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));

    expect(screen.getByText('Enter your email.')).toBeInTheDocument();
    expect(screen.getByText('Enter a project name.')).toBeInTheDocument();
    expect(intakeApi.askForCode).not.toHaveBeenCalled();
  });

  it('refuses an address that is not one', () => {
    render(<StudyPage />);
    typeInto('Email', 'ana.ferreira');
    typeInto('Project name', 'Willow Bend');

    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));

    expect(screen.getByText('Enter a valid email address.')).toBeInTheDocument();
    expect(intakeApi.askForCode).not.toHaveBeenCalled();
  });

  it('sends a code to the address, asks for it, and remembers the address for this browser', async () => {
    render(<StudyPage />);
    typeInto('Email', ' ana.ferreira@example.pt ');
    typeInto('Project name', 'Willow Bend');

    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));

    expect(await screen.findByLabelText('Code')).toBeInTheDocument();
    expect(intakeApi.askForCode).toHaveBeenCalledWith('ana.ferreira@example.pt');
    expect(localStorage.getItem(LAST_ADDRESS_KEY)).toBe('ana.ferreira@example.pt');
  });

  it('shows what the service said when it refuses to send a code', async () => {
    vi.mocked(intakeApi.askForCode).mockRejectedValue(new Error('That address has been sent as many codes as it can be for now.'));
    render(<StudyPage />);
    typeInto('Email', 'ana.ferreira@example.pt');
    typeInto('Project name', 'Willow Bend');

    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));

    expect(await screen.findByText('That address has been sent as many codes as it can be for now.')).toBeInTheDocument();
    expect(screen.queryByLabelText('Code')).not.toBeInTheDocument();
  });

  it('spends the code on a ticket and opens the study under the project name', async () => {
    render(<StudyPage />);

    await signIn();

    expect(intakeApi.exchangeTicket).toHaveBeenCalledWith('ana.ferreira@example.pt', '314159');
    expect(screen.getByRole('banner')).toHaveTextContent('Willow Bend');
  });

  it('shows what the service said when it does not accept the code', async () => {
    vi.mocked(intakeApi.exchangeTicket).mockRejectedValue(new Error('That code is not the one sent, or it has expired.'));
    render(<StudyPage />);
    typeInto('Email', 'ana.ferreira@example.pt');
    typeInto('Project name', 'Willow Bend');
    fireEvent.click(screen.getByRole('button', { name: 'Continue' }));
    fireEvent.change(await screen.findByLabelText('Code'), { target: { value: '000000' } });

    fireEvent.click(screen.getByRole('button', { name: 'Confirm' }));

    expect(await screen.findByText('That code is not the one sent, or it has expired.')).toBeInTheDocument();
    expect(screen.queryByRole('navigation', { name: 'Study steps' })).not.toBeInTheDocument();
  });
});

describe('the steps', () => {
  function stepNames() {
    return screen.getAllByRole('listitem').map((step) => step.textContent);
  }

  it('walks Lite through its steps until Pro is chosen', async () => {
    render(<StudyPage />);
    await signIn();

    expect(stepNames()).toEqual(['Location', 'Data', 'Village layout', 'Timeline']);
  });

  it('walks Pro through its steps once chosen, and keeps the choice for this browser', async () => {
    render(<StudyPage />);
    await signIn();

    fireEvent.click(screen.getByRole('button', { name: 'Pro' }));

    expect(stepNames()).toEqual(['Location', 'Data', 'Village', 'Layout', 'Timeline']);
    expect(localStorage.getItem('studyEdition')).toBe('pro');
  });

  it('marks Location as the step in hand', async () => {
    render(<StudyPage />);
    await signIn();

    expect(screen.getByRole('button', { name: 'Location' })).toHaveAttribute('aria-current', 'step');
  });

  it('keeps Next waiting until a location is confirmed', async () => {
    render(<StudyPage />);
    await signIn();

    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
  });

  it('says a location is needed when a later step is opened first, and stays on Location', async () => {
    render(<StudyPage />);
    await signIn();

    fireEvent.click(screen.getByRole('button', { name: 'Data' }));

    expect(screen.getByText('Location needed')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Location' })).toHaveAttribute('aria-current', 'step');
  });

  it('shows the site’s address in the header once it is found', async () => {
    render(<StudyPage />);
    await signIn();

    fireEvent.change(await screen.findByRole('searchbox', { name: 'Address or coordinates' }), { target: { value: 'Willow Bend' } });
    fireEvent.click(screen.getByRole('button', { name: 'Find' }));

    await waitFor(() => expect(screen.getByRole('banner')).toHaveTextContent('Willow Bend Farm, Alentejo'));
  });

  it('no longer says a location is needed once the site is confirmed', async () => {
    render(<StudyPage />);
    await signIn();
    await findAndConfirmSite();

    fireEvent.click(screen.getByRole('button', { name: 'Data' }));

    await waitFor(() => expect(screen.queryByText('Location needed')).not.toBeInTheDocument());
  });
});

describe('in another page language', () => {
  it('words the sign-in in the reader language', async () => {
    await i18n.changeLanguage('de');
    render(<StudyPage />);

    fireEvent.click(screen.getByRole('button', { name: i18n.t('study.signIn.continue') }));

    expect(screen.getByText(i18n.t('study.signIn.emailMissing'))).toBeInTheDocument();
    expect(i18n.t('study.signIn.emailMissing')).not.toBe('Enter your email.');
  });
});
