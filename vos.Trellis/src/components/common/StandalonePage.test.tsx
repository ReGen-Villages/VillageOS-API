import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { StandalonePage } from './StandalonePage';

describe('StandalonePage', () => {
  it('heads the page with its title and intro, and draws what it is given in a column of the width asked for', () => {
    render(
      <StandalonePage icon={<svg data-testid="icon" />} title="Explore your land" intro="Start at a map" width="narrow">
        <p>The form</p>
      </StandalonePage>,
    );

    expect(screen.getByRole('heading', { level: 1, name: 'Explore your land' })).toBeInTheDocument();
    expect(screen.getByText('Start at a map')).toBeInTheDocument();
    expect(screen.getByTestId('icon')).toBeInTheDocument();
    expect(screen.getByRole('main')).toHaveClass('max-w-2xl');
    expect(screen.getByRole('main')).toHaveTextContent('The form');
  });
});
