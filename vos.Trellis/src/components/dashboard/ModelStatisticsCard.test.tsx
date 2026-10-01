import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ModelStatisticsCard } from './ModelStatisticsCard';
import type { VosThing, VosRelationship } from '../../types/vos';

const thing = (Id: string, Properties: Record<string, unknown> = {}) => ({ Id, Name: Id, Properties }) as unknown as VosThing;
const relationship = (Id: string, PredicateId: string) =>
  ({ Id, SubjectId: 'a', PredicateId, TargetId: 'b', Properties: {} }) as unknown as VosRelationship;

const things = [thing('a', { area: 1, height: 2 }), thing('b'), thing('feeds')];
const relationships = [relationship('r1', 'feeds'), relationship('r2', 'feeds')];

const figure = (label: string) => screen.getByText(label).previousSibling?.textContent;

describe('ModelStatisticsCard', () => {
  it('counts what the model holds once the whole of it is held', () => {
    render(<ModelStatisticsCard things={things} relationships={relationships} wholeModelHeld />);

    expect(figure('Things')).toBe('3');
    expect(figure('Relationships')).toBe('2');
    expect(figure('Properties')).toBe('2');
    expect(screen.getByText('feeds')).toBeInTheDocument();
    expect(screen.queryByText('Reading the model…')).toBeNull();
  });

  // What is held before the whole model arrives is one page's own set, or nothing. Counted, either
  // reads as the size of the model: a handful of Things, then none, then the real figure.
  it('says the model is being read, and counts nothing, until the whole of it is held', () => {
    render(<ModelStatisticsCard things={things} relationships={relationships} wholeModelHeld={false} />);

    expect(screen.getByText('Reading the model…')).toBeInTheDocument();
    expect(figure('Things')).toBe('—');
    expect(figure('Relationships')).toBe('—');
    expect(screen.queryByText('feeds')).toBeNull();
  });
});
