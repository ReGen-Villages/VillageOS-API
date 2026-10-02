import { describe, it, expect, vi } from 'vitest';
import { render, fireEvent, screen } from '@testing-library/react';
import { GraphSearchBar } from './GraphSearchBar';

function renderCreating(newThingIsAType: boolean) {
  const setNewThingIsAType = vi.fn();
  const setNewThingName = vi.fn();
  const setShowCreateThing = vi.fn();
  render(
    <GraphSearchBar
      searchQuery="" setSearchQuery={vi.fn()}
      caseSensitive={false} setCaseSensitive={vi.fn()}
      exactMatch={false} setExactMatch={vi.fn()}
      useRegex={false} setUseRegex={vi.fn()}
      matchCount={0}
      showCreateThing setShowCreateThing={setShowCreateThing}
      newThingName="Reservoir" setNewThingName={setNewThingName}
      newThingIsAType={newThingIsAType} setNewThingIsAType={setNewThingIsAType}
      creatingThing={false} onCreateThing={vi.fn()}
    />,
  );
  return { setNewThingIsAType, setNewThingName, setShowCreateThing };
}

describe('GraphSearchBar, creating a Thing', () => {
  it('lets the author say the new Thing is a type', () => {
    const { setNewThingIsAType } = renderCreating(false);

    fireEvent.click(screen.getByLabelText('Is a type'));

    expect(setNewThingIsAType).toHaveBeenCalledWith(true);
  });

  // A box left ticked would make the next Thing a type without anyone having said so.
  it('forgets the choice when the author cancels', () => {
    const { setNewThingIsAType, setNewThingName } = renderCreating(true);

    fireEvent.click(screen.getByTitle('Cancel'));

    expect(setNewThingIsAType).toHaveBeenCalledWith(false);
    expect(setNewThingName).toHaveBeenCalledWith('');
  });
});
