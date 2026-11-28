import { describe, it, expect, vi, beforeEach } from 'vitest';

import { intakeApi } from './intakeApi';
import { LAND_INTAKE_ROLE } from './landIntakeRoles';
import type { SubmissionDocument } from '../intake/submissionDraft';

const fetchMock = vi.fn();

const SUBMISSION: SubmissionDocument = {
  submissionId: '9f1c74d6-0b8e-4a52-bd31-6c7e5a92f048',
  project: { name: 'Willow Bend Regeneration' },
  contact: { name: 'Ana Ferreira', emailAddress: 'ana.ferreira@example.pt' },
  site: { name: 'Willow Bend', latitude: 39.5012, longitude: -8.4137 },
};

/** Every faked answer carries headers, because a real accepted post returns the renewed ticket in one. */
function answering(submissionAnswer: object) {
  fetchMock.mockImplementation((url: string) =>
    Promise.resolve(
      url.endsWith('/submissions/ticket')
        ? { ok: true, headers: new Headers(), json: () => Promise.resolve({ ticket: 'ticket-1', validForSeconds: 600 }) }
        : { headers: new Headers(), ...submissionAnswer },
    ),
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  (import.meta.env as Record<string, string>).VITE_INTAKE_URL = 'http://localhost:6200';
  vi.stubGlobal('fetch', fetchMock);
});

describe('posting a submission', () => {
  it('spends the code on a ticket and posts the document under it', async () => {
    answering({ ok: true, json: () => Promise.resolve({ reference: 'sub-ref-1' }) });

    const accepted = await intakeApi.submit(SUBMISSION, '314159');

    const [ticketUrl, ticketRequest] = fetchMock.mock.calls[0];
    expect(ticketUrl).toBe('http://localhost:6200/submissions/ticket');
    expect(JSON.parse(ticketRequest.body)).toEqual({
      emailAddress: 'ana.ferreira@example.pt',
      code: '314159',
    });
    const [url, requestOptions] = fetchMock.mock.calls[1];
    expect(url).toBe('http://localhost:6200/submissions');
    expect(requestOptions.method).toBe('POST');
    expect(requestOptions.headers['X-Submission-Ticket']).toBe('ticket-1');
    expect(requestOptions.headers['Content-Type']).toBe('application/json');
    expect(JSON.parse(requestOptions.body)).toEqual(SUBMISSION);
    expect(accepted.reference).toBe('sub-ref-1');
  });

  // A form sent under a planner's token would work only for a planner, and this is the route a stranger uses.
  it('carries no credential', async () => {
    answering({ ok: true, json: () => Promise.resolve({ reference: 'sub-ref-1' }) });

    await intakeApi.submit(SUBMISSION, '314159');

    expect(fetchMock.mock.calls[1][1].headers.Authorization).toBeUndefined();
  });

  it('does not double the separator when the configured address ends in one', async () => {
    (import.meta.env as Record<string, string>).VITE_INTAKE_URL = 'http://localhost:6200/';
    answering({ ok: true, json: () => Promise.resolve({ reference: 'sub-ref-1' }) });

    await intakeApi.submit(SUBMISSION, '314159');

    expect(fetchMock.mock.calls[0][0]).toBe('http://localhost:6200/submissions/ticket');
    expect(fetchMock.mock.calls[1][0]).toBe('http://localhost:6200/submissions');
  });

  it('raises what the service said was wrong, not the status it said it under', async () => {
    answering({
      ok: false,
      status: 400,
      json: () => Promise.resolve({ error: "'site.name' is missing: a Thing is created under a name." }),
    });

    await expect(intakeApi.submit(SUBMISSION, '314159')).rejects.toThrow("'site.name' is missing");
  });

  it('raises the detail where the service answered with a problem document instead', async () => {
    answering({
      ok: false,
      status: 503,
      json: () => Promise.resolve({ title: 'Service unavailable', detail: 'This service cannot accept submissions at the moment.' }),
    });

    await expect(intakeApi.submit(SUBMISSION, '314159')).rejects.toThrow('cannot accept submissions');
  });

  it('still says the submission was refused when the refusal carries no readable body', async () => {
    answering({ ok: false, status: 503, json: () => Promise.reject(new Error('no body')) });

    await expect(intakeApi.submit(SUBMISSION, '314159')).rejects.toThrow('503');
  });

  it('raises the refusal where the ticket itself could not be had', async () => {
    fetchMock.mockResolvedValue({
      ok: false,
      status: 429,
      json: () => Promise.resolve({ error: 'Too many requests. Try again in 600 seconds.' }),
    });

    await expect(intakeApi.submit(SUBMISSION, '314159')).rejects.toThrow('Too many requests');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('refuses to post at all when no intake address is configured', async () => {
    (import.meta.env as Record<string, string>).VITE_INTAKE_URL = '';

    await expect(intakeApi.submit(SUBMISSION, '314159')).rejects.toThrow('VITE_INTAKE_URL');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('reports whether an address is configured, which decides whether the wizard is offered', () => {
    expect(intakeApi.configured()).toBe(true);
    (import.meta.env as Record<string, string>).VITE_INTAKE_URL = '';
    expect(intakeApi.configured()).toBe(false);
  });
});

describe('asking for a code', () => {
  it('names the address the code is to be sent to', async () => {
    fetchMock.mockResolvedValue({ ok: true, json: () => Promise.resolve({}) });

    await intakeApi.askForCode('ana.ferreira@example.pt');

    const [url, requestOptions] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:6200/submissions/verification');
    expect(requestOptions.method).toBe('POST');
    expect(JSON.parse(requestOptions.body)).toEqual({ emailAddress: 'ana.ferreira@example.pt' });
    expect(requestOptions.headers.Authorization).toBeUndefined();
  });

  it('raises what the service said was wrong', async () => {
    fetchMock.mockResolvedValue({
      ok: false,
      status: 429,
      json: () => Promise.resolve({ error: 'That address has been sent as many codes as it can be for now.' }),
    });

    await expect(intakeApi.askForCode('ana.ferreira@example.pt')).rejects.toThrow('as many codes');
  });
});

describe('what the form draws itself with', () => {
  it('asks the service, because a page holding no credential cannot read the model', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ allocationCategories: [{ id: 'category-1', name: 'residential' }], basemapSources: [] }),
    });

    const options = await intakeApi.formOptions();

    const [url, requestOptions] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:6200/submissions/form');
    expect(requestOptions).toEqual({ method: 'GET', headers: {} });
    expect(options.allocationCategories).toEqual([{ id: 'category-1', name: 'residential' }]);
  });

  it('knows each boundary source by the role that binds it, and none whose role binds two', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      json: () =>
        Promise.resolve({
          boundarySources: [
            { role: LAND_INTAKE_ROLE.BoundaryDrawnByHand, id: 'source-drawn', name: 'traced' },
            { role: LAND_INTAKE_ROLE.BoundaryFetchedFromRegister, id: 'source-register', name: 'fetched' },
            { role: LAND_INTAKE_ROLE.BoundaryImportedFromFile, id: 'source-imported-1', name: 'imported' },
            { role: LAND_INTAKE_ROLE.BoundaryImportedFromFile, id: 'source-imported-2', name: 'uploaded' },
          ],
        }),
    });

    const options = await intakeApi.formOptions();

    expect(options.boundarySources).toEqual({ drawn: 'source-drawn', register: 'source-register' });
  });

  it('judges the sources it is told about by the rule the signed-in page judges them by', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      json: () =>
        Promise.resolve({
          allocationCategories: [],
          basemapSources: [
            { id: 'src-1', name: 'Streets', attribution: 'Example', styleUrl: 'https://example.test/s.json' },
            { id: 'src-2', name: 'Unlicensed', attribution: null, styleUrl: 'https://example.test/u.json' },
          ],
        }),
    });

    const options = await intakeApi.formOptions();

    expect(options.basemapSources.map((source) => source.name)).toEqual(['Streets']);
  });

  it('resolves a tile address that is a path against the intake service', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      json: () =>
        Promise.resolve({
          allocationCategories: [],
          basemapSources: [
            { id: 'src-1', name: 'Satellite', attribution: 'Example imagery', tileUrl: '/basemaps/satellite-tiles/{z}/{x}/{y}' },
            { id: 'src-2', name: 'Streets', attribution: 'Example', styleUrl: 'https://example.test/s.json' },
          ],
        }),
    });

    const options = await intakeApi.formOptions();

    expect(options.basemapSources.map((source) => source.kind === 'raster' ? source.tileUrl : source.styleUrl)).toEqual([
      'http://localhost:6200/basemaps/satellite-tiles/{z}/{x}/{y}',
      'https://example.test/s.json',
    ]);
  });

  it('carries the themes the model declares, and none where the service names none', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      json: () =>
        Promise.resolve({
          allocationCategories: [],
          themes: [{ name: 'Water', colour: '#3B7DE0', icon: 'droplet', order: 2 }, { name: 'Bare' }],
        }),
    });

    const options = await intakeApi.formOptions();

    expect(options.themes).toEqual([
      { name: 'Water', colour: '#3B7DE0', icon: 'droplet', order: 2 },
      { name: 'Bare', colour: null, icon: null, order: null },
    ]);

    fetchMock.mockResolvedValue({ ok: true, json: () => Promise.resolve({ allocationCategories: [] }) });
    expect((await intakeApi.formOptions()).themes).toEqual([]);
  });

  it('carries the layer kinds the model declares with their themes, and none where the service names none', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      json: () =>
        Promise.resolve({
          allocationCategories: [],
          layerKinds: [
            { id: 'kind-1', name: 'brook-or-wadi', colour: '#2F80ED', themes: ['Vegetation', 'Water'] },
            { name: 'other-layer' },
          ],
        }),
    });

    expect((await intakeApi.formOptions()).layerKinds).toEqual([
      { id: 'kind-1', name: 'brook-or-wadi', colour: '#2F80ED', themes: ['Vegetation', 'Water'] },
      { id: '', name: 'other-layer', colour: null, themes: [] },
    ]);

    fetchMock.mockResolvedValue({ ok: true, json: () => Promise.resolve({ allocationCategories: [] }) });
    expect((await intakeApi.formOptions()).layerKinds).toEqual([]);
  });

  // A deployment with no seeded model answers with a problem document; read as an empty model, the form
  // would offer an empty programme step with no explanation.
  it('raises what the service said rather than reading a refusal as an empty model', async () => {
    fetchMock.mockResolvedValue({
      ok: false,
      status: 503,
      json: () => Promise.resolve({ detail: 'This service cannot answer at the moment.' }),
    });

    await expect(intakeApi.formOptions()).rejects.toThrow('cannot answer');
  });
});

describe('the lookups a deployment may not register', () => {
  it('answers no parcel and no places where the service answers that nothing is there', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 404, headers: new Headers(), json: () => Promise.resolve({}) });

    await expect(intakeApi.parcelAt(39.5, -8.4)).resolves.toBeNull();
    await expect(intakeApi.searchPlaces('Willow', 'ticket-1')).resolves.toEqual({ places: [], ticket: 'ticket-1' });
  });

  it('raises any other refusal of a lookup rather than reading it as nothing there', async () => {
    fetchMock.mockResolvedValue({
      ok: false,
      status: 503,
      json: () => Promise.resolve({ detail: 'This service cannot answer at the moment.' }),
    });

    await expect(intakeApi.parcelAt(39.5, -8.4)).rejects.toThrow('cannot answer');
    await expect(intakeApi.searchPlaces('Willow', 'ticket-1')).rejects.toThrow('cannot answer');
  });

  it('posts a lookup as JSON and reads the form options with no body', async () => {
    fetchMock.mockResolvedValue({ ok: true, headers: new Headers(), json: () => Promise.resolve({ places: [] }) });

    await intakeApi.searchPlaces('Willow', 'ticket-1');
    const [, posted] = fetchMock.mock.calls[0];
    expect(posted).toMatchObject({ method: 'POST', body: JSON.stringify({ query: 'Willow' }) });
    expect(posted.headers['Content-Type']).toBe('application/json');

    await intakeApi.formOptions();
    const [, read] = fetchMock.mock.calls[1];
    expect(read.method).toBe('GET');
    expect(read.body).toBeUndefined();
    expect(read.headers['Content-Type']).toBeUndefined();
  });
});

describe('how the form measures a border', () => {
  it.each(['meanSphereAreaMethod', 'wgs84EllipsoidAreaMethod'])('carries the area method the service names, %s', async (areaMethod) => {
    fetchMock.mockResolvedValue({ ok: true, json: () => Promise.resolve({ areaMethod }) });

    expect((await intakeApi.formOptions()).areaMethod).toBe(areaMethod);
  });

  // Measuring with a method the model did not name would show an area the submission then disagrees with.
  it.each([undefined, 'geodesicAreaMethod'])('carries no area method where the service names none this page measures with (%s)', async (areaMethod) => {
    fetchMock.mockResolvedValue({ ok: true, json: () => Promise.resolve({ areaMethod }) });

    expect((await intakeApi.formOptions()).areaMethod).toBeNull();
  });
});

const BORDER = [
  { latitude: 25.15, longitude: 55.55 },
  { latitude: 25.15, longitude: 55.56 },
  { latitude: 25.16, longitude: 55.56 },
];

function answeringWithTicket(status: number, body: object, renewed: string | null = 'ticket-2') {
  fetchMock.mockResolvedValue({
    ok: status < 400,
    status,
    headers: new Headers(renewed === null ? {} : { 'X-Submission-Ticket': renewed }),
    json: () => Promise.resolve(body),
  });
}

describe('the place search after sign-in', () => {
  it('carries the ticket, so it is counted against the signed-in budget, and renews it', async () => {
    const place = { name: 'Al Awir 2, Dubai', latitude: 25.15, longitude: 55.55 };
    answeringWithTicket(200, { places: [place] });

    const answered = await intakeApi.searchPlaces('Al Awir', 'ticket-1');

    const [, posted] = fetchMock.mock.calls[0];
    expect(posted.headers['X-Submission-Ticket']).toBe('ticket-1');
    expect(answered).toEqual({ places: [place], ticket: 'ticket-2' });
  });
});

describe('the plots in view', () => {
  const view ={ southLatitude: 25.1, westLongitude: 55.5, northLatitude: 25.2, eastLongitude: 55.6 };

  it('asks for the plots inside the view at its zoom under the ticket, and renews the ticket', async () => {
    const plot = { boundary: BORDER, identifier: 'plot 7', areaHectares: 1.2 };
    answeringWithTicket(200, { plots: [plot], attributions: ['Land register'] });

    const answered = await intakeApi.plotsInView(view, 15, 'ticket-1');

    const [url, posted] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:6200/submissions/plots-in-view');
    expect(JSON.parse(posted.body)).toEqual({ ...view, zoom: 15 });
    expect(posted.headers['X-Submission-Ticket']).toBe('ticket-1');
    expect(answered).toEqual({ plots: [plot], attributions: ['Land register'], ticket: 'ticket-2' });
  });

  it('answers no plots where the model has no register for the view', async () => {
    answeringWithTicket(404, { code: 'noPlots' }, null);

    expect(await intakeApi.plotsInView(view, 15, 'ticket-1')).toEqual({ plots: null, attributions: [], ticket: 'ticket-1' });
  });
});

describe('the elevation within a border', () => {
  it('asks for the border under the ticket and reads the heights it answers', async () => {
    answeringWithTicket(200, { elevationMetres: 31.6, elevationMinimumMetres: 18.2, elevationMaximumMetres: 44.9, attribution: 'Terrain' });

    const answered = await intakeApi.borderElevation(BORDER, 'ticket-1');

    const [url, posted] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:6200/submissions/border-elevation');
    expect(JSON.parse(posted.body)).toEqual({ boundary: BORDER });
    expect(posted.headers['X-Submission-Ticket']).toBe('ticket-1');
    expect(answered).toEqual({
      elevation: { siteMetres: 31.6, lowestMetres: 18.2, highestMetres: 44.9, attribution: 'Terrain' },
      ticket: 'ticket-2',
    });
  });

  it('answers no elevation where no height is held for the border', async () => {
    answeringWithTicket(404, { code: 'noElevation' }, null);

    expect(await intakeApi.borderElevation(BORDER, 'ticket-1')).toEqual({ elevation: null, ticket: 'ticket-1' });
  });
});

describe('reading a border file', () => {
  it('sends the file with the address the ticket was issued for, as a form, and takes the largest area', async () => {
    const largestArea = { shape: 1, boundary: BORDER, hectares: 62.5 };
    answeringWithTicket(200, { shapes: [{ name: 'Barn' }, { name: 'Willow Bend' }], largestArea });
    const file = new File(['<kml/>'], 'parcel.kml');

    const answered = await intakeApi.readFile(file, 'ana.ferreira@example.pt', 'ticket-1');

    const [url, posted] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:6200/submissions/read-file');
    expect(posted.method).toBe('POST');
    expect(posted.headers['X-Submission-Ticket']).toBe('ticket-1');
    expect(posted.headers['Content-Type']).toBeUndefined();
    expect((posted.body as FormData).get('file')).toBe(file);
    expect((posted.body as FormData).get('emailAddress')).toBe('ana.ferreira@example.pt');
    expect(answered).toEqual({ largestArea: { name: 'Willow Bend', boundary: BORDER, hectares: 62.5 }, ticket: 'ticket-2' });
  });

  it('answers no largest area for a file holding no polygon', async () => {
    answeringWithTicket(200, { shapes: [{ name: 'A track' }], largestArea: null });

    expect((await intakeApi.readFile(new File([''], 'track.gpx'), 'ana.ferreira@example.pt', 'ticket-1')).largestArea).toBeNull();
  });

  it('raises what the service said when it refuses the file', async () => {
    answeringWithTicket(400, { error: 'The file is not one this service reads.' }, null);

    await expect(intakeApi.readFile(new File([''], 'notes.txt'), 'ana.ferreira@example.pt', 'ticket-1')).rejects.toThrow(
      'not one this service reads',
    );
  });
});

describe('keeping the imported file with the submission', () => {
  it('sends the file as a form to the submission documents under the ticket, and renews the ticket', async () => {
    answeringWithTicket(200, { documentId: 'doc-1' });
    const file = new File(['<kml/>'], 'parcel.kml');

    const ticket = await intakeApi.sendDocument('sub-1', file, 'ticket-1');

    const [url, posted] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:6200/submissions/sub-1/documents');
    expect(posted.headers['X-Submission-Ticket']).toBe('ticket-1');
    expect((posted.body as FormData).get('file')).toBe(file);
    expect(ticket).toBe('ticket-2');
  });
});
