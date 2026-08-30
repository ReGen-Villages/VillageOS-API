import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';

vi.mock('../api/findingsApi', () => ({
  findingsApi: { askForCode: vi.fn(), read: vi.fn() },
}));

import { findingsApi } from '../api/findingsApi';
import { PublicFindingsPage } from './PublicFindingsPage';
import type { FindingsAnswer } from './answeredFindings';

const REFERENCE = '9f1c74d6-0b8e-4a52-bd31-6c7e5a92f048';
const ADDRESS = 'ana.ferreira@example.pt';

const SPEC = JSON.stringify({
  title: 'Site submission',
  subtitle: 'What was submitted, what was discovered, and what the analysis makes of it',
  compare: { label: 'site', archetype: 'Site' },
  sections: [
    {
      title: 'The land',
      widgets: [
        {
          type: 'kpi',
          title: 'Stated area',
          unit: 'hectares',
          value: { kind: 'property', thing: '$scope', property: 'statedAreaHectares' },
        },
      ],
    },
  ],
});

const ANSWER: FindingsAnswer = {
  spec: SPEC,
  scopeId: 'site-1',
  things: [
    { Id: 'site-1', Name: 'Willow Bend', Properties: { statedAreaHectares: { typeInfo: 'vos.Double', value: 24 } } },
  ],
  relationships: [],
  ranges: {},
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(findingsApi.askForCode).mockResolvedValue(undefined);
  vi.mocked(findingsApi.read).mockResolvedValue(ANSWER);
});

function fill(): void {
  fireEvent.change(screen.getByLabelText(/Reference/), { target: { value: REFERENCE } });
  fireEvent.change(screen.getByLabelText(/Email address/), { target: { value: ADDRESS } });
}

/** The whole exchange, for a test that came to say something about what happens after it. */
async function read(): Promise<void> {
  fill();
  fireEvent.click(screen.getByRole('button', { name: 'Send a code' }));
  fireEvent.change(await screen.findByLabelText(/^Code$/), { target: { value: '123456' } });
  fireEvent.click(screen.getByRole('button', { name: 'Show my findings' }));
}

describe('the page a submitter reads their own findings on', () => {
  it('asks for a code before it asks for anything else', async () => {
    render(<PublicFindingsPage />);
    fill();

    fireEvent.click(screen.getByRole('button', { name: 'Send a code' }));

    await waitFor(() => expect(findingsApi.askForCode).toHaveBeenCalledWith(ADDRESS));
    expect(findingsApi.read).not.toHaveBeenCalled();
  });

  // The reference names a submission and the code proves the mailbox. Neither alone establishes
  // anything, so both travel on the one request the ticket was got for.
  it('reads the findings with the reference, the address and the code together', async () => {
    render(<PublicFindingsPage />);
    fill();
    fireEvent.click(screen.getByRole('button', { name: 'Send a code' }));
    await screen.findByLabelText(/^Code$/);

    fireEvent.change(screen.getByLabelText(/^Code$/), { target: { value: '123456' } });
    fireEvent.click(screen.getByRole('button', { name: 'Show my findings' }));

    await waitFor(() => expect(findingsApi.read).toHaveBeenCalledWith(REFERENCE, ADDRESS, '123456'));
  });

  it('draws the page the model declared, in the model\'s own words', async () => {
    render(<PublicFindingsPage />);
    fill();
    fireEvent.click(screen.getByRole('button', { name: 'Send a code' }));
    await screen.findByLabelText(/^Code$/);
    fireEvent.change(screen.getByLabelText(/^Code$/), { target: { value: '123456' } });
    fireEvent.click(screen.getByRole('button', { name: 'Show my findings' }));

    expect(await screen.findByText('Site submission')).toBeInTheDocument();
    expect(await screen.findByText('The land')).toBeInTheDocument();
    expect(await screen.findByText('Stated area')).toBeInTheDocument();
  });

  it('goes back to the form when the reader asks about another submission', async () => {
    render(<PublicFindingsPage />);
    await read();

    fireEvent.click(await screen.findByRole('button', { name: 'Ask about another submission' }));

    expect(await screen.findByLabelText(/Reference/)).toBeInTheDocument();
  });

  // Drawing nothing is how a figure the analysis has not computed reads, so a page listing no sections
  // has to say that is what it is rather than looking like an analysis that found nothing.
  it('says so when the page it was sent lists no sections', async () => {
    vi.mocked(findingsApi.read).mockResolvedValue({
      ...ANSWER,
      spec: JSON.stringify({ title: 'Site submission', sections: [] }),
    });
    render(<PublicFindingsPage />);

    await read();

    expect(await screen.findByText(/lists no sections/)).toBeInTheDocument();
  });

  // Drawing nothing is how a figure the analysis has not computed reads. A page that cannot be drawn at
  // all has to say so, in the place every other refusal is said.
  it('says so when the service answered a page it cannot draw', async () => {
    vi.mocked(findingsApi.read).mockResolvedValue({ ...ANSWER, spec: 'not a spec' });
    render(<PublicFindingsPage />);

    await read();

    expect(screen.queryByText('Site submission')).not.toBeInTheDocument();
    expect(await screen.findByLabelText(/Reference/)).toBeInTheDocument();
    expect(screen.getByText(/JSON|Unexpected/)).toBeInTheDocument();
  });

  // What the service said, not the status it said it under: the whole value of a refusal to the person
  // reading it is which of the two they got wrong.
  it('says what the service refused it for', async () => {
    vi.mocked(findingsApi.read).mockRejectedValue(new Error('No submission was found for that reference and address.'));
    render(<PublicFindingsPage />);
    fill();
    fireEvent.click(screen.getByRole('button', { name: 'Send a code' }));
    await screen.findByLabelText(/^Code$/);
    fireEvent.change(screen.getByLabelText(/^Code$/), { target: { value: '123456' } });
    fireEvent.click(screen.getByRole('button', { name: 'Show my findings' }));

    expect(await screen.findByText(/No submission was found/)).toBeInTheDocument();
  });
});
