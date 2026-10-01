import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, fireEvent, waitFor, act } from '@testing-library/react';

vi.mock('../../api/ingestApi', () => ({ ingestApi: { configured: vi.fn(() => true), upload: vi.fn() } }));
vi.mock('../../hooks/useModelData', () => ({ reloadModelData: vi.fn() }));
vi.mock('../common/toastStore', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

import i18n from '../../i18n';
import { SUPPORTED_LANGUAGES } from '../../i18n/languages';
import { IfcUploadDropzone } from './IfcUploadDropzone';
import { ingestApi } from '../../api/ingestApi';
import { reloadModelData } from '../../hooks/useModelData';
import { toast } from '../common/toastStore';

const fileInput = (c: HTMLElement) => c.querySelector('input[type="file"]') as HTMLInputElement;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(ingestApi.configured).mockReturnValue(true);
});

afterEach(async () => {
  await act(() => i18n.changeLanguage('en'));
});

describe('IfcUploadDropzone (US #5844)', () => {
  it('uploads a picked file (merge) and reloads the model on success', async () => {
    vi.mocked(ingestApi.upload).mockResolvedValue({ success: true, thingsCreated: 3, thingsUpdated: 1, relationshipsCreated: 2 });
    const { container } = render(<IfcUploadDropzone />);
    const file = new File(['ISO-10303-21;'], 'building.ifc');

    fireEvent.change(fileInput(container), { target: { files: [file] } });

    await waitFor(() => expect(ingestApi.upload).toHaveBeenCalledWith(file, 'building', 'merge'));
    await waitFor(() => expect(toast.success).toHaveBeenCalled());
    expect(reloadModelData).toHaveBeenCalled();
  });

  const upload = async () => {
    const { container } = render(<IfcUploadDropzone />);
    fireEvent.change(fileInput(container), { target: { files: [new File(['ISO-10303-21;'], 'building.ifc')] } });
    await waitFor(() => expect(toast.success).toHaveBeenCalled());
    return vi.mocked(toast.success).mock.calls[0][0];
  };

  it('says how many Things were created and updated and how many relationships were created', async () => {
    vi.mocked(ingestApi.upload).mockResolvedValue({ success: true, thingsCreated: 3, thingsUpdated: 1, relationshipsCreated: 2 });

    expect(await upload()).toBe('Ingested building.ifc: 3 created, 1 updated, 2 relationships.');
  });

  it('says the counts were not reported when the service answers none, and still reloads the model', async () => {
    vi.mocked(ingestApi.upload).mockResolvedValue({ success: true, thingsCreated: null, thingsUpdated: null, relationshipsCreated: null });

    expect(await upload()).toBe('Ingested building.ifc. The ingestion service did not report how many things and relationships it wrote.');
    expect(reloadModelData).toHaveBeenCalled();
  });

  it.each(['thingsCreated', 'thingsUpdated', 'relationshipsCreated'] as const)('shows no count at all when only %s is missing', async (missing) => {
    vi.mocked(ingestApi.upload).mockResolvedValue({ success: true, thingsCreated: 3, thingsUpdated: 1, relationshipsCreated: 2, [missing]: null });

    expect(await upload()).toBe('Ingested building.ifc. The ingestion service did not report how many things and relationships it wrote.');
  });

  it.each(SUPPORTED_LANGUAGES.map((language) => language.code))('fills every value its upload messages ask for in %s', async (code) => {
    await act(() => i18n.changeLanguage(code));

    vi.mocked(ingestApi.upload).mockResolvedValue({ success: true, thingsCreated: 3, thingsUpdated: 1, relationshipsCreated: 2 });
    const withCounts = await upload();
    vi.mocked(toast.success).mockClear();
    vi.mocked(ingestApi.upload).mockResolvedValue({ success: true, thingsCreated: null, thingsUpdated: null, relationshipsCreated: null });
    const withoutCounts = await upload();

    expect(withCounts).toContain('building.ifc');
    expect(withCounts).not.toContain('{{');
    expect(withoutCounts).toContain('building.ifc');
    expect(withoutCounts).not.toContain('{{');
    expect(withoutCounts).not.toBe(withCounts);
    expect(withoutCounts).not.toMatch(/null|\d/);
  });

  it('surfaces a failed ingest as an error toast and does not reload', async () => {
    vi.mocked(ingestApi.upload).mockResolvedValue({ success: false, thingsCreated: 0, thingsUpdated: 0, relationshipsCreated: 0, error: 'xbim parse error' });
    const { container } = render(<IfcUploadDropzone />);

    fireEvent.change(fileInput(container), { target: { files: [new File([''], 'x.ifc')] } });

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('xbim parse error'));
    expect(reloadModelData).not.toHaveBeenCalled();
  });

  it('falls back to a hint when no ingestion service is configured', () => {
    vi.mocked(ingestApi.configured).mockReturnValue(false);
    const { container, getByText } = render(<IfcUploadDropzone />);
    expect(fileInput(container)).toBeNull();
    expect(getByText(/VITE_INGEST_URL/)).toBeTruthy();
  });

  it.each(SUPPORTED_LANGUAGES.map((language) => language.code))('names the file to ingest in angle brackets in %s', async (code) => {
    vi.mocked(ingestApi.configured).mockReturnValue(false);
    await act(() => i18n.changeLanguage(code));
    const { container } = render(<IfcUploadDropzone />);

    expect(container.textContent).toContain('ingest <file.ifc>');
    expect(container.textContent).not.toContain('&lt;');
  });
});
