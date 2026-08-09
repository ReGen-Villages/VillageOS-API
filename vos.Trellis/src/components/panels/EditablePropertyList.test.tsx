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
  return screen.getByLabelText('door_number') as HTMLInputElement;
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
    return screen.getByLabelText('open_ratio') as HTMLInputElement;
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

// The control a property is edited with follows the type the platform declares for it (#6164).
describe('the control offered for editing', () => {
  function rowFor(type: string, value: unknown) {
    render(
      <EditablePropertyList
        properties={[{ name: 'field', value, type }]}
        entityId="thing-1"
        entityType="thing"
        editMode
        showAddRow={false}
      />,
    );
    return screen.queryByLabelText('field') as HTMLInputElement | null;
  }

  it('is a checkbox for true/false, not a box to type the words into', () => {
    expect(rowFor('vos.Boolean', true)?.type).toBe('checkbox');
  });

  // The picker's default step is a minute, which would drop the seconds off a timestamp a user only
  // meant to nudge.
  it('is a picker for a date, keeping its seconds', () => {
    const picker = rowFor('vos.DateTime', '2026-08-05T09:20:14Z');
    expect(picker?.type).toBe('datetime-local');
    expect(picker?.step).toBe('1');
  });

  it('is a numeric field for the number types, whole numbers stepping by one', () => {
    expect(rowFor('vos.Integer', 3)?.step).toBe('1');
  });

  it('is a numeric field for a reading, stepping by any amount', () => {
    expect(rowFor('vos.Double', 0.5)?.step).toBe('any');
  });

  it('is a text box for text', () => {
    expect(rowFor('vos.String', 'hello')?.type).toBe('text');
  });

  // A JSON body typed into a narrow panel field is not editing. Saying so beats a disabled box,
  // which reads as broken rather than deliberate.
  it('is absent for the types written by ingest, which say so instead', () => {
    expect(rowFor('vos.GeoJson', { Json: '{}' })).toBeNull();
    expect(screen.getByText('written by ingest')).toBeInTheDocument();
  });
});

describe('a value the declared type cannot hold', () => {
  function editRow(type: string, value: unknown) {
    render(
      <EditablePropertyList
        properties={[{ name: 'field', value, type }]}
        entityId="thing-1"
        entityType="thing"
        editMode
        showAddRow={false}
      />,
    );
    return screen.getByLabelText('field') as HTMLInputElement;
  }

  it('never reaches the platform', () => {
    const input = editRow('vos.Integer', 3);
    fireEvent.change(input, { target: { value: '3.7' } });
    fireEvent.blur(input);
    expect(setProperty).not.toHaveBeenCalled();
  });

  it('says what the property holds and what was typed', () => {
    const input = editRow('vos.Integer', 3);
    fireEvent.change(input, { target: { value: '3.7' } });
    fireEvent.blur(input);
    expect(errors()[0].message).toContain('whole number');
    expect(errors()[0].message).toContain('3.7');
  });

  it('lets a value the type can hold through', () => {
    const input = editRow('vos.Integer', 3);
    fireEvent.change(input, { target: { value: '4' } });
    fireEvent.blur(input);
    expect(setProperty).toHaveBeenCalledWith('thing-1', 'field', 'vos.Integer', '4');
  });
});

// Choosing "bool" and then typing the word "true" into a text box is the same guess this work
// removes, just made by the user rather than by the code.
describe('the add-property row', () => {
  function addRow() {
    render(
      <EditablePropertyList
        properties={[]}
        entityId="thing-1"
        entityType="thing"
        editMode
      />,
    );
    return {
      type: screen.getByLabelText('Property type'),
      value: () => screen.getByLabelText('value') as HTMLInputElement,
    };
  }

  it('starts on a text box, matching the type it starts on', () => {
    expect(addRow().value().type).toBe('text');
  });

  it('swaps the value control when the type changes', () => {
    const row = addRow();
    fireEvent.change(row.type, { target: { value: 'vos.Boolean' } });
    expect(row.value().type).toBe('checkbox');
    fireEvent.change(row.type, { target: { value: 'vos.DateTime' } });
    expect(row.value().type).toBe('datetime-local');
  });

  it('refuses a value the chosen type cannot hold, without a request', () => {
    const row = addRow();
    fireEvent.change(screen.getByPlaceholderText('name'), { target: { value: 'count' } });
    fireEvent.change(row.type, { target: { value: 'vos.Integer' } });
    fireEvent.change(row.value(), { target: { value: '3.7' } });
    fireEvent.click(screen.getByTitle('Add property'));
    expect(thingApi.addProperty).not.toHaveBeenCalled();
    expect(errors()[0].message).toContain('whole number');
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
