import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ModelPage } from './ModelPage';

describe('ModelPage', () => {
  it('renders the Model heading and viewer placeholder', () => {
    render(<ModelPage />);

    expect(screen.getByRole('heading', { name: /model/i })).toBeInTheDocument();
    expect(screen.getByTestId('model-viewer-placeholder')).toBeInTheDocument();
    expect(screen.getByText(/3D viewer lands here/i)).toBeInTheDocument();
  });
});
