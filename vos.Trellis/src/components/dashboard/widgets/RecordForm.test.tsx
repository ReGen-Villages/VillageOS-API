import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { FormWidget } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';

const mockOptions = vi.fn();
vi.mock('../../../hooks/useDashboard', () => ({
  useBindings: (bindings: unknown[]) =>
    bindings.map((binding) => ({ loading: false, error: false, value: binding ? mockOptions() : null })),
}));

import { ApiError } from '../../../api/client';
import { RecordForm } from './RecordForm';

const BOOK: FormWidget = {
  type: 'form',
  title: 'Book a reading',
  fields: [
    { key: 'spring', label: 'Spring', kind: 'choice', options: { kind: 'thingList', archetype: 'Spring' } },
    { key: 'litres', label: 'Litres', kind: 'number' },
    { key: 'note', label: 'Note', optional: true },
  ],
  submit: 'Book',
  preview: { act: 'cover', label: 'What this covers' },
  writes: { via: 'readings', act: 'book', archetype: 'Reading' },
};

const mockPost = vi.fn();
const ctx = { reads: { fromService: (endpoint: string, body: unknown) => mockPost(endpoint, body) } } as unknown as ResolveContext;

function fill() {
  fireEvent.change(screen.getByLabelText('Spring'), { target: { value: 'SPRING-1' } });
  fireEvent.change(screen.getByLabelText('Litres'), { target: { value: '12.5' } });
}

describe('RecordForm', () => {
  beforeEach(() => {
    mockOptions.mockReset().mockReturnValue([{ id: 's1', name: 'SPRING-1' }, { id: 's2', name: 'SPRING-2' }]);
    mockPost.mockReset().mockResolvedValue({ said: 'Reading booked for SPRING-1' });
  });

  it('offers the Things a choice field lists by name, with nothing chosen yet, and waits for every required field', () => {
    render(<RecordForm widget={BOOK} ctx={ctx} />);
    expect(screen.getByRole('option', { name: 'SPRING-2' })).toBeInTheDocument();
    expect((screen.getByLabelText('Spring') as HTMLSelectElement).value).toBe('');
    expect(screen.getByRole('button', { name: 'Book' })).toBeDisabled();
  });

  it('posts the act and the filled fields to the endpoint the spec names, and nothing naming who asked', async () => {
    render(<RecordForm widget={BOOK} ctx={ctx} />);
    fill();
    fireEvent.click(screen.getByRole('button', { name: 'Book' }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/endpoints/readings', { view: 'book', spring: 'SPRING-1', litres: 12.5 }));
  });

  it('shows a refusal in the words the endpoint used and keeps what was typed', async () => {
    mockPost.mockRejectedValueOnce(new ApiError(400, JSON.stringify({ error: 'SPRING-1 is dry this month' })));
    render(<RecordForm widget={BOOK} ctx={ctx} />);
    fill();
    fireEvent.click(screen.getByRole('button', { name: 'Book' }));

    expect(await screen.findByText('SPRING-1 is dry this month')).toBeInTheDocument();
    expect((screen.getByLabelText('Litres') as HTMLInputElement).value).toBe('12.5');
  });

  it('says what the endpoint said once it has taken the press, and clears the form', async () => {
    render(<RecordForm widget={BOOK} ctx={ctx} />);
    fill();
    fireEvent.click(screen.getByRole('button', { name: 'Book' }));

    expect(await screen.findByText('Reading booked for SPRING-1')).toBeInTheDocument();
    expect((screen.getByLabelText('Litres') as HTMLInputElement).value).toBe('');
  });

  it('posts what is filled so far under the preview act, before every required field is given', async () => {
    mockPost.mockResolvedValueOnce({ said: 'Covers the north ridge' });
    render(<RecordForm widget={BOOK} ctx={ctx} />);
    fireEvent.change(screen.getByLabelText('Spring'), { target: { value: 'SPRING-1' } });
    fireEvent.click(screen.getByRole('button', { name: 'What this covers' }));

    await waitFor(() => expect(mockPost).toHaveBeenCalledWith('/api/endpoints/readings', { view: 'cover', spring: 'SPRING-1' }));
    expect(await screen.findByText('Covers the north ridge')).toBeInTheDocument();
  });

  it('shows no preview at all where the spec names none', () => {
    render(<RecordForm widget={{ ...BOOK, preview: undefined }} ctx={ctx} />);
    expect(screen.queryByRole('button', { name: 'What this covers' })).toBeNull();
  });
});
