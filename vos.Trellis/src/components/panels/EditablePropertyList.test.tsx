import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { EditablePropertyList } from './EditablePropertyList';
import { useToastStore } from '../common/toastStore';
import { thingApi } from '../../api/thingApi';

vi.mock('../../api/thingApi', () => ({
  thingApi: { setProperty: vi.fn().mockResolvedValue({}), addProperty: vi.fn().mockResolvedValue({}) },
}));

const setProperty = vi.mocked(thingApi.setProperty);

function editableRow(type: string) {
  render(
    <EditablePropertyList
      properties={[{ name: 'door_number', value: '4711', type }]}
      entityId="thing-1"
      entityType="thing"
      editMode
      showAddRow={false}
    />,
  );
  return screen.getByDisplayValue('4711');
}

const errors = () => useToastStore.getState().toasts.filter((t) => t.type === 'error');

beforeEach(() => {
  vi.clearAllMocks();
  useToastStore.setState({ toasts: [] });
});

// A property's type is a plain settable string on the platform, so one can report a name no write
// route accepts. Sending it earns an opaque "Invalid type specified" from the server; the panel
// should say which type it does not know instead.
describe('a property reporting a type outside the platform set', () => {
  it('is not sent', () => {
    const input = editableRow('acme.PartNumber');
    fireEvent.change(input, { target: { value: '4712' } });
    fireEvent.blur(input);
    expect(setProperty).not.toHaveBeenCalled();
  });

  it('says which type it could not save', () => {
    const input = editableRow('acme.PartNumber');
    fireEvent.change(input, { target: { value: '4712' } });
    fireEvent.blur(input);
    expect(errors()).toHaveLength(1);
    expect(errors()[0].message).toContain('acme.PartNumber');
    expect(errors()[0].message).toContain('door_number');
  });
});

describe('a property reporting a type the platform accepts', () => {
  it('is sent, stating that type', () => {
    const input = editableRow('vos.String');
    fireEvent.change(input, { target: { value: '4712' } });
    fireEvent.blur(input);
    expect(setProperty).toHaveBeenCalledWith('thing-1', 'door_number', 'vos.String', '4712');
    expect(errors()).toHaveLength(0);
  });
});

// The display row rounds a reading to the places the model asks for. An edit box holding that
// rounded text would save the rounding back over the stored value as soon as anything else on the
// row changed — editing works on the value, not on how the value is presented (#6163).
describe('editing a reading', () => {
  function editableReading() {
    render(
      <EditablePropertyList
        properties={[{ name: 'open_ratio', value: 0.123456789, type: 'vos.Double' }]}
        entityId="thing-1"
        entityType="thing"
        editMode
        showAddRow={false}
      />,
    );
    return screen.getByRole('textbox') as HTMLInputElement;
  }

  it('starts from the stored value, not from the rounded one on show', () => {
    expect(editableReading().value).toBe('0.123456789');
  });

  it('sends the stored value untouched when something else on the row is saved', () => {
    const input = editableReading();
    fireEvent.change(input, { target: { value: '0.987654321' } });
    fireEvent.blur(input);
    expect(setProperty).toHaveBeenCalledWith('thing-1', 'open_ratio', 'vos.Double', '0.987654321');
  });
});

describe('a displayed property', () => {
  it('names the type it holds', () => {
    render(
      <EditablePropertyList
        properties={[{ name: 'open_ratio', value: 0.123456789, type: 'vos.Double' }]}
        entityId="thing-1"
        entityType="thing"
        editMode={false}
      />,
    );
    expect(screen.getByTitle('vos.Double')).toBeInTheDocument();
    expect(screen.getByText('0.12346')).toBeInTheDocument();
  });
});
