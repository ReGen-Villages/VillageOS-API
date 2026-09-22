import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent, waitFor } from '@testing-library/react';

vi.mock('../../api/ingestApi', () => ({ ingestApi: { configured: vi.fn(() => true), upload: vi.fn() } }));
vi.mock('../../hooks/useModelData', () => ({ reloadModelData: vi.fn() }));
vi.mock('../common/toastStore', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

import { IfcUploadDropzone } from './IfcUploadDropzone';
import { ingestApi } from '../../api/ingestApi';
import { reloadModelData } from '../../hooks/useModelData';
import { toast } from '../common/toastStore';

const fileInput = (c: HTMLElement) => c.querySelector('input[type="file"]') as HTMLInputElement;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(ingestApi.configured).mockReturnValue(true);
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
});
