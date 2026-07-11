import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent, screen, waitFor } from '@testing-library/react';
import { EditableThingName } from './EditableThingName';
import { thingApi } from '../../api/thingApi';

vi.mock('../../api/thingApi', () => ({ thingApi: { rename: vi.fn() } }));
vi.mock('../common/Toast', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const renameMock = vi.mocked(thingApi.rename);

describe('EditableThingName (US #5862)', () => {
  beforeEach(() => renameMock.mockReset());

  function enterEditMode() {
    render(<EditableThingName thingId="t1" name="OldName" />);
    fireEvent.click(screen.getByLabelText('Rename'));
    return screen.getByLabelText('Thing name') as HTMLInputElement;
  }

  it('renames via the api on Enter and notifies the parent', async () => {
    renameMock.mockResolvedValue({ Id: 't1', Name: 'NewName' } as never);
    const onRenamed = vi.fn();
    render(<EditableThingName thingId="t1" name="OldName" onRenamed={onRenamed} />);

    fireEvent.click(screen.getByLabelText('Rename'));
    fireEvent.change(screen.getByLabelText('Thing name'), { target: { value: 'NewName' } });
    fireEvent.keyDown(screen.getByLabelText('Thing name'), { key: 'Enter' });

    await waitFor(() => expect(renameMock).toHaveBeenCalledWith('t1', 'NewName'));
    expect(onRenamed).toHaveBeenCalledWith('NewName');
  });

  it('trims the new name before sending', async () => {
    renameMock.mockResolvedValue({ Id: 't1', Name: 'Trimmed' } as never);
    const input = enterEditMode();
    fireEvent.change(input, { target: { value: '  Trimmed  ' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    await waitFor(() => expect(renameMock).toHaveBeenCalledWith('t1', 'Trimmed'));
  });

  it('Escape cancels without calling the api', () => {
    const input = enterEditMode();
    fireEvent.change(input, { target: { value: 'Discarded' } });
    fireEvent.keyDown(input, { key: 'Escape' });
    expect(renameMock).not.toHaveBeenCalled();
    expect(screen.getByText('OldName')).toBeInTheDocument();
  });

  it('an unchanged name is a no-op (no api call)', () => {
    const input = enterEditMode();
    fireEvent.keyDown(input, { key: 'Enter' }); // draft still "OldName"
    expect(renameMock).not.toHaveBeenCalled();
  });

  it('a blank name is a no-op (no api call)', () => {
    const input = enterEditMode();
    fireEvent.change(input, { target: { value: '   ' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(renameMock).not.toHaveBeenCalled();
  });
});
