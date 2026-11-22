import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useState } from 'react';
import i18n from '../i18n';
import type { BorderEdit } from '../components/map/borderEdit';
import type { BoundaryPoint } from '../utils/parcelGeometry';

interface ShownMap {
  latitude: number;
  longitude: number;
  boundary?: readonly BoundaryPoint[];
  borderEditing?: { placesCorners: boolean; movesCorners: boolean; onEdit: (edit: BorderEdit) => void };
  plots?: readonly (readonly BoundaryPoint[])[];
  onPlotPress?: (index: number) => void;
  onViewSettle?: (view: object, zoom: number) => void;
  showMarker?: boolean;
  boundaryLabel?: string;
}

const shown = vi.hoisted(() => ({ map: null as ShownMap | null }));

vi.mock('../components/map/MapView', () => ({
  MapView: (props: ShownMap) => {
    shown.map = props;
    return <div data-testid="map" />;
  },
}));

vi.mock('../api/intakeApi', () => ({
  intakeApi: {
    formOptions: vi.fn(),
    searchPlaces: vi.fn(),
    readFile: vi.fn(),
    plotsInView: vi.fn(),
    borderElevation: vi.fn(),
    submitWithTicket: vi.fn(),
    sendDocument: vi.fn(),
  },
}));

import { intakeApi, type FormOptions } from '../api/intakeApi';
import { LocationPage } from './LocationPage';
import type { LocationSite } from './locationSite';

const at = (latitude: number, longitude: number): BoundaryPoint => ({ latitude, longitude });
const BORDER = [at(25.149, 55.55), at(25.149, 55.553), at(25.152, 55.553), at(25.152, 55.55)];
const SESSION = { emailAddress: 'planner@example.org', projectName: 'Oasis', ticket: 'ticket-1' };

const FORM = {
  basemapSources: [],
  parcelLookup: true,
  placeSearch: true,
  areaMethod: 'meanSphereAreaMethod',
} as unknown as FormOptions;

const confirmed = vi.fn();
const sitesShown: (LocationSite | null)[] = [];

function Page() {
  const [site, setSite] = useState<LocationSite | null>(null);
  return (
    <LocationPage
      session={SESSION}
      site={site}
      onSiteChange={(changed) => {
        sitesShown.push(changed);
        setSite(changed);
      }}
      onConfirmed={confirmed}
    />
  );
}

const map = () => shown.map!;
const edit = (made: BorderEdit) => act(() => map().borderEditing!.onEdit(made));
const press = (name: string) => fireEvent.click(screen.getByRole('button', { name }));
const fact = (name: string) => screen.getByRole('definition', { name });

async function opened() {
  render(<Page />);
  await screen.findByTestId('map');
}

async function findSiteByCoordinates() {
  fireEvent.change(screen.getByRole('searchbox', { name: 'Address or coordinates' }), { target: { value: '25.150467, 55.551533' } });
  press('Find');
  await screen.findByRole('definition', { name: 'Coordinates' });
}

async function drawBorder(corners = BORDER) {
  press('Draw border');
  corners.forEach((corner) => edit({ kind: 'place', corner }));
  edit({ kind: 'close' });
  await screen.findByRole('button', { name: 'Redraw' });
}

beforeEach(async () => {
  vi.clearAllMocks();
  shown.map = null;
  sitesShown.length = 0;
  await i18n.changeLanguage('en');
  vi.mocked(intakeApi.formOptions).mockResolvedValue(FORM);
  vi.mocked(intakeApi.borderElevation).mockResolvedValue({
    elevation: { siteMetres: 100.4, lowestMetres: 90, highestMetres: 120, attribution: null },
    ticket: 'ticket-elevation',
  });
});

describe('before a site', () => {
  it('asks for an address or coordinates, or a parcel file, over the map', async () => {
    await opened();

    expect(screen.getByRole('heading', { name: 'Location' })).toBeInTheDocument();
    expect(screen.getByText("Search for your site's address, or type its coordinates, to locate it on the map.")).toBeInTheDocument();
    expect(screen.getByText('Or start from a parcel file.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Import file' })).toBeInTheDocument();
    expect(map()).toMatchObject({ latitude: 24, longitude: 47, showMarker: false });
  });

  it('opens the file chooser from Import file, before a site and after one', async () => {
    const chooserOpened = vi.spyOn(HTMLInputElement.prototype, 'click').mockImplementation(() => {});
    try {
      await opened();
      press('Import file');
      await findSiteByCoordinates();
      press('Import file');

      expect(chooserOpened).toHaveBeenCalledTimes(2);
    } finally {
      chooserOpened.mockRestore();
    }
  });

  it('shows what the intake said when it cannot give the form', async () => {
    vi.mocked(intakeApi.formOptions).mockRejectedValue(new Error('The intake cannot read the model.'));
    await opened();

    expect(await screen.findByRole('alert')).toHaveTextContent('The intake cannot read the model.');
  });
});

describe('finding the site', () => {
  it('takes typed coordinates as the site without a search', async () => {
    await opened();

    await findSiteByCoordinates();

    expect(intakeApi.searchPlaces).not.toHaveBeenCalled();
    expect(fact('Coordinates')).toHaveTextContent('25.15047°N, 55.55153°E');
    expect(map()).toMatchObject({ latitude: 25.150467, longitude: 55.551533, showMarker: true });
  });

  it('searches for an address, carrying the ticket, and takes the first place found', async () => {
    vi.mocked(intakeApi.searchPlaces).mockResolvedValue({
      places: [
        { name: 'Al Awir 2, Dubai', latitude: 25.15, longitude: 55.55 },
        { name: 'Al Awir 1, Dubai', latitude: 25.1, longitude: 55.5 },
      ],
      ticket: 'ticket-2',
    });
    await opened();

    fireEvent.change(screen.getByRole('searchbox', { name: 'Address or coordinates' }), { target: { value: 'Al Awir' } });
    press('Find');

    await screen.findByRole('definition', { name: 'Address' });
    expect(intakeApi.searchPlaces).toHaveBeenCalledWith('Al Awir', 'ticket-1');
    expect(fact('Address')).toHaveTextContent('Al Awir 2, Dubai');
  });

  it('says what to type when the box is empty', async () => {
    await opened();

    press('Find');

    expect(await screen.findByText('Type an address or coordinates (e.g. 25.1505, 55.5515).')).toBeInTheDocument();
  });

  it('says so when nothing is found, and when the search is unavailable', async () => {
    vi.mocked(intakeApi.searchPlaces).mockResolvedValueOnce({ places: [], ticket: 'ticket-2' }).mockRejectedValueOnce(new Error('down'));
    await opened();
    const box = screen.getByRole('searchbox', { name: 'Address or coordinates' });

    fireEvent.change(box, { target: { value: 'Nowhere' } });
    press('Find');
    expect(await screen.findByText('No match found. Try a town or a street.')).toBeInTheDocument();

    fireEvent.change(box, { target: { value: 'Somewhere' } });
    await waitFor(() => expect(screen.queryByText('No match found. Try a town or a street.')).not.toBeInTheDocument());
    press('Find');
    expect(await screen.findByText('Search is unavailable right now.', {}, { timeout: 3000 })).toBeInTheDocument();
  });
});

describe('drawing the border', () => {
  it('places corners and closes on the first, then measures the border with the form’s method', async () => {
    await opened();
    await findSiteByCoordinates();

    await drawBorder();

    expect(fact('Site area')).toHaveTextContent(/^\d+\.\d ha \(drawn\)$/);
    expect(sitesShown.at(-1)).toMatchObject({ border: BORDER, borderSource: 'drawn' });
  });

  it('refuses a corner that would make the border cross itself', async () => {
    await opened();
    await findSiteByCoordinates();
    press('Draw border');
    [at(0, 0), at(0, 2), at(2, 1)].forEach((corner) => edit({ kind: 'place', corner }));

    edit({ kind: 'place', corner: at(-1, 1) });

    expect(screen.getByRole('alert')).toHaveTextContent('Borders can’t cross themselves');
    expect(map().boundary).toHaveLength(3);
  });

  it('takes back the last corner, finishes on Done from three corners, and cancels on Esc', async () => {
    await opened();
    await findSiteByCoordinates();
    press('Draw border');
    BORDER.slice(0, 2).forEach((corner) => edit({ kind: 'place', corner }));
    expect(screen.getByRole('button', { name: 'Done' })).toBeDisabled();

    press('Undo corner');
    expect(map().boundary).toHaveLength(1);
    fireEvent.keyDown(window, { key: 'Escape' });

    expect(map().borderEditing).toBeUndefined();
    expect(screen.getByRole('button', { name: 'Draw border' })).toBeInTheDocument();
  });

  it('undoes on Backspace and finishes on Enter', async () => {
    await opened();
    await findSiteByCoordinates();
    press('Draw border');
    [...BORDER, at(25.15, 55.549)].forEach((corner) => edit({ kind: 'place', corner }));

    fireEvent.keyDown(window, { key: 'Backspace' });
    fireEvent.keyDown(window, { key: 'Enter' });

    expect(await screen.findByRole('button', { name: 'Redraw' })).toBeInTheDocument();
    expect(sitesShown.at(-1)?.border).toEqual(BORDER);
  });

  it('edits a border by moving corners, with the area following, and finishes on Done', async () => {
    await opened();
    await findSiteByCoordinates();
    await drawBorder();
    const before = fact('Site area').textContent;

    press('Edit border');
    expect(map().borderEditing).toMatchObject({ placesCorners: false, movesCorners: true });
    edit({ kind: 'move', index: 2, to: at(25.16, 55.56) });
    expect(fact('Site area').textContent).not.toBe(before);
    press('Done');

    expect(sitesShown.at(-1)?.border[2]).toEqual(at(25.16, 55.56));
  });

  it('finishes editing on Esc, keeping the moved corner', async () => {
    await opened();
    await findSiteByCoordinates();
    await drawBorder();
    press('Edit border');
    edit({ kind: 'move', index: 2, to: at(25.16, 55.56) });

    fireEvent.keyDown(window, { key: 'Escape' });

    expect(await screen.findByRole('button', { name: 'Redraw' })).toBeInTheDocument();
    expect(sitesShown.at(-1)?.border[2]).toEqual(at(25.16, 55.56));
  });

  it('starts a new border on Redraw', async () => {
    await opened();
    await findSiteByCoordinates();
    await drawBorder();

    press('Redraw');

    expect(map().boundary).toEqual([]);
    expect(map().borderEditing).toMatchObject({ placesCorners: true, movesCorners: false });
  });

  it('clears the border', async () => {
    await opened();
    await findSiteByCoordinates();
    await drawBorder();

    press('Clear');

    expect(fact('Site area')).toHaveTextContent('—');
    expect(screen.getByRole('button', { name: 'Draw border' })).toBeInTheDocument();
  });
});

describe('the elevation', () => {
  it('is looked up for a finished border and shows the range within it', async () => {
    await opened();
    await findSiteByCoordinates();

    await drawBorder();

    await waitFor(() => expect(fact('Elevation')).toHaveTextContent('100 m (90–120 m within border)'));
    expect(intakeApi.borderElevation).toHaveBeenCalledWith(BORDER, 'ticket-1');
  });

  it('is never looked up while a border is being drawn, and reads — when the lookup fails', async () => {
    vi.mocked(intakeApi.borderElevation).mockRejectedValue(new Error('down'));
    await opened();
    await findSiteByCoordinates();
    press('Draw border');
    BORDER.forEach((corner) => edit({ kind: 'place', corner }));
    expect(intakeApi.borderElevation).not.toHaveBeenCalled();

    edit({ kind: 'close' });

    await waitFor(() => expect(fact('Elevation')).toHaveTextContent('—'));
  });
});

describe('importing a parcel file', () => {
  const FILE = new File(['<kml/>'], 'site.kml', { type: 'application/vnd.google-earth.kml+xml' });

  function choose(file = FILE) {
    fireEvent.change(screen.getByLabelText('Parcel file'), { target: { files: [file] } });
  }

  it('sends the file with the address and ticket, and takes the largest area as the border', async () => {
    vi.mocked(intakeApi.readFile).mockResolvedValue({
      largestArea: { name: 'North field', boundary: BORDER, hectares: 10.14 },
      ticket: 'ticket-2',
    });
    await opened();

    choose();

    expect(await screen.findByRole('definition', { name: 'Site area' })).toHaveTextContent('10.1 ha (from file)');
    expect(intakeApi.readFile).toHaveBeenCalledWith(FILE, 'planner@example.org', 'ticket-1');
    expect(sitesShown.at(-1)).toMatchObject({ parcelName: 'North field', borderSource: 'imported' });
  });

  it('says so when the file holds no polygon, and when the intake refuses it', async () => {
    vi.mocked(intakeApi.readFile)
      .mockResolvedValueOnce({ largestArea: null, ticket: 'ticket-2' })
      .mockRejectedValueOnce(new Error('the file is not a parcel file'));
    await opened();

    choose();
    expect(await screen.findByText('No polygon found in this file.')).toBeInTheDocument();
    choose();
    expect(await screen.findByText('Could not read the file: the file is not a parcel file')).toBeInTheDocument();
  });
});

describe('choosing a plot', () => {
  const PLOT = { boundary: BORDER, identifier: 'Plot 7', areaHectares: 9.04 };
  const VIEW = { southLatitude: 25.14, westLongitude: 55.54, northLatitude: 25.16, eastLongitude: 55.56 };

  it('is greyed as coming soon where the model holds no plot register', async () => {
    vi.mocked(intakeApi.formOptions).mockResolvedValue({ ...FORM, parcelLookup: false });
    await opened();
    await findSiteByCoordinates();

    expect(screen.getByRole('button', { name: 'Choose plot' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Choose plot' })).toHaveAttribute('title', 'Coming soon: needs cadastral plot data');
  });

  it('asks for the plots in view once the map rests close in, and takes the one pressed with its register area', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.mocked(intakeApi.plotsInView).mockResolvedValue({ plots: [PLOT], attributions: [], ticket: 'ticket-3' });
    await opened();
    await findSiteByCoordinates();
    press('Choose plot');

    act(() => map().onViewSettle!(VIEW, 14));
    act(() => map().onViewSettle!(VIEW, 15.2));
    await act(() => vi.advanceTimersByTimeAsync(250));
    vi.useRealTimers();

    expect(intakeApi.plotsInView).toHaveBeenCalledTimes(1);
    expect(intakeApi.plotsInView).toHaveBeenCalledWith(VIEW, 15.2, 'ticket-1');
    await waitFor(() => expect(map().plots).toEqual([BORDER]));
    act(() => map().onPlotPress!(0));

    expect(await screen.findByRole('definition', { name: 'Site area' })).toHaveTextContent('9.0 ha (Plot 7)');
    expect(map().plots).toBeUndefined();
  });

  it('greys Choose plot once the intake says no register covers the view', async () => {
    vi.mocked(intakeApi.plotsInView).mockResolvedValue({ plots: null, attributions: [], ticket: 'ticket-1' });
    await opened();
    await findSiteByCoordinates();
    act(() => map().onViewSettle!(VIEW, 16));

    press('Choose plot');

    await waitFor(() => expect(screen.getByRole('button', { name: 'Choose plot' })).toBeDisabled());
    expect(intakeApi.plotsInView).toHaveBeenCalledWith(VIEW, 16, 'ticket-1');
  });

  it('shows what the intake said when it refuses the plots', async () => {
    vi.mocked(intakeApi.plotsInView).mockRejectedValue(new Error('Too many requests for now.'));
    await opened();
    await findSiteByCoordinates();
    act(() => map().onViewSettle!(VIEW, 16));

    press('Choose plot');

    expect(await screen.findByRole('alert')).toHaveTextContent('Too many requests for now.');
  });
});

describe('the label on the border', () => {
  it('names the parcel and its area', async () => {
    vi.mocked(intakeApi.readFile).mockResolvedValue({
      largestArea: { name: 'North field', boundary: BORDER, hectares: 10.14 },
      ticket: 'ticket-2',
    });
    await opened();

    fireEvent.change(screen.getByLabelText('Parcel file'), { target: { files: [new File(['<kml/>'], 'site.kml')] } });

    await waitFor(() => expect(map().boundaryLabel).toBe('North field · 10.1 ha'));
  });

  it('says Site for a border with no parcel or plot name, and is not shown while the border is being drawn', async () => {
    await opened();
    await findSiteByCoordinates();
    press('Draw border');
    BORDER.forEach((corner) => edit({ kind: 'place', corner }));
    expect(map().boundaryLabel).toBeUndefined();

    edit({ kind: 'close' });

    await waitFor(() => expect(map().boundaryLabel).toMatch(/^Site · \d+\.\d ha$/));
  });
});

describe('confirming the site', () => {
  it('waits for a border', async () => {
    await opened();
    await findSiteByCoordinates();

    expect(screen.getByRole('button', { name: 'Confirm site' })).toBeDisabled();
  });

  it('posts the submission with the ticket each answer renewed, keeps the imported file, and says it is confirmed', async () => {
    const file = new File(['<kml/>'], 'site.kml');
    vi.mocked(intakeApi.readFile).mockResolvedValue({
      largestArea: { name: 'North field', boundary: BORDER, hectares: 10.14 },
      ticket: 'ticket-2',
    });
    vi.mocked(intakeApi.submitWithTicket).mockResolvedValue({ accepted: { reference: 'reference-1' }, ticket: 'ticket-4' });
    vi.mocked(intakeApi.sendDocument).mockResolvedValue('ticket-5');
    await opened();
    fireEvent.change(screen.getByLabelText('Parcel file'), { target: { files: [file] } });
    await screen.findByRole('definition', { name: 'Site area' });
    await waitFor(() => expect(fact('Elevation')).toHaveTextContent('100 m'));
    expect(intakeApi.borderElevation).toHaveBeenCalledWith(BORDER, 'ticket-2');

    press('Confirm site');

    await waitFor(() => expect(confirmed).toHaveBeenCalled());
    const [posted, ticket] = vi.mocked(intakeApi.submitWithTicket).mock.calls[0];
    expect(ticket).toBe('ticket-elevation');
    expect(posted).toMatchObject({
      contact: { name: 'planner@example.org', emailAddress: 'planner@example.org' },
      site: { name: 'North field' },
      parcel: { boundarySource: 'imported-from-file', boundary: BORDER },
    });
    expect(intakeApi.sendDocument).toHaveBeenCalledWith(posted.submissionId, file, 'ticket-4');
  });

  it('shows the intake’s refusal and stays on the page', async () => {
    vi.mocked(intakeApi.submitWithTicket).mockRejectedValue(new Error('the site name is missing'));
    await opened();
    await findSiteByCoordinates();
    await drawBorder();

    press('Confirm site');

    expect(await screen.findByRole('alert')).toHaveTextContent('the site name is missing');
    expect(confirmed).not.toHaveBeenCalled();
  });
});
