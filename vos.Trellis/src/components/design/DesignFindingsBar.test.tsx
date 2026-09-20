import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { DesignFindingsBar } from './DesignFindingsBar';

describe('DesignFindingsBar', () => {
  it('says the page can be kept when there is nothing to say', () => {
    render(<DesignFindingsBar findings={[]} onSelect={vi.fn()} />);
    expect(screen.getByText('The page can be kept.')).toBeInTheDocument();
  });

  it('names each finding with the word it is about, and selects what a finding is about when clicked', () => {
    const onSelect = vi.fn();
    render(
      <DesignFindingsBar
        findings={[
          { severity: 'refusal', code: 'namesAnUnknownKind', section: 0, widget: 1, named: 'Dam' },
          { severity: 'warning', code: 'pageWithoutAnIcon' },
        ]}
        onSelect={onSelect}
      />,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Names the kind “Dam”, which the model does not declare.' }));
    expect(onSelect).toHaveBeenCalledWith({ on: 'widget', section: 0, widget: 1 });
    fireEvent.click(screen.getByRole('button', { name: 'The page names no icon.' }));
    expect(onSelect).toHaveBeenCalledWith({ on: 'page' });
  });
});
