import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';

vi.mock('./toastStore', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock('../../utils/logDownload', () => ({ triggerDownload: vi.fn() }));

import { toast } from './toastStore';
import { triggerDownload } from '../../utils/logDownload';
import { MarkdownExportButtons } from './MarkdownExportButtons';

function withClipboard(writeText: (text: string) => Promise<void>) {
  Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
}

describe('MarkdownExportButtons', () => {
  afterEach(() => vi.clearAllMocks());

  it('copies the Markdown and says so', async () => {
    const writeText = vi.fn(() => Promise.resolve());
    withClipboard(writeText);
    render(<MarkdownExportButtons markdown={() => '# Found'} fileName="found.md" />);

    fireEvent.click(screen.getByRole('button', { name: 'Copy as Markdown' }));

    expect(writeText).toHaveBeenCalledWith('# Found');
    await waitFor(() => expect(toast.success).toHaveBeenCalled());
  });

  it('says so when the copy is refused', async () => {
    withClipboard(() => Promise.reject(new Error('denied')));
    render(<MarkdownExportButtons markdown={() => '# Found'} fileName="found.md" />);

    fireEvent.click(screen.getByRole('button', { name: 'Copy as Markdown' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('downloads the Markdown under the name it is given', async () => {
    render(<MarkdownExportButtons markdown={() => '# Found'} fileName="found.md" />);

    fireEvent.click(screen.getByRole('button', { name: 'Download as Markdown' }));

    const [blob, fileName] = vi.mocked(triggerDownload).mock.calls[0];
    expect(fileName).toBe('found.md');
    expect(blob.type).toBe('text/markdown');
    expect(await blob.text()).toBe('# Found');
  });
});
