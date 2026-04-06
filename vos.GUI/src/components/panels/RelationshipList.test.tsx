import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { RelationshipList } from './RelationshipList';
import type { VosRelationship, VosThing } from '../../types/vos';

const things = new Map<string, VosThing>([
  ['s1', { Id: 's1', Name: 'Subject', Properties: {} }],
  ['p1', { Id: 'p1', Name: 'connects', Properties: {} }],
  ['t1', { Id: 't1', Name: 'Target', Properties: {} }],
]);

const rel: VosRelationship = {
  Id: 'r1',
  Name: 'rel1',
  SubjectId: 's1',
  PredicateId: 'p1',
  TargetId: 't1',
  Properties: {},
};

describe('RelationshipList', () => {
  it('renders relationship with edge detail link when onSelectEdge provided', () => {
    const onSelectNode = vi.fn();
    const onSelectEdge = vi.fn();

    render(
      <RelationshipList
        relationships={[rel]}
        direction="outgoing"
        allThings={things}
        onSelectNode={onSelectNode}
        onSelectEdge={onSelectEdge}
      />,
    );

    const edgeButton = screen.getByTitle('Open edge detail');
    expect(edgeButton).toBeDefined();
  });

  it('calls onSelectEdge with relationship id when edge link clicked', () => {
    const onSelectNode = vi.fn();
    const onSelectEdge = vi.fn();

    render(
      <RelationshipList
        relationships={[rel]}
        direction="outgoing"
        allThings={things}
        onSelectNode={onSelectNode}
        onSelectEdge={onSelectEdge}
      />,
    );

    fireEvent.click(screen.getByTitle('Open edge detail'));
    expect(onSelectEdge).toHaveBeenCalledWith('r1');
    expect(onSelectNode).not.toHaveBeenCalled();
  });

  it('does not render edge detail link when onSelectEdge is not provided', () => {
    const onSelectNode = vi.fn();

    render(
      <RelationshipList
        relationships={[rel]}
        direction="outgoing"
        allThings={things}
        onSelectNode={onSelectNode}
      />,
    );

    expect(screen.queryByTitle('Open edge detail')).toBeNull();
  });

  it('calls onSelectNode when node name clicked (outgoing)', () => {
    const onSelectNode = vi.fn();

    render(
      <RelationshipList
        relationships={[rel]}
        direction="outgoing"
        allThings={things}
        onSelectNode={onSelectNode}
      />,
    );

    fireEvent.click(screen.getByText('Target'));
    expect(onSelectNode).toHaveBeenCalledWith('t1');
  });

  it('calls onSelectNode when node name clicked (incoming)', () => {
    const onSelectNode = vi.fn();

    render(
      <RelationshipList
        relationships={[rel]}
        direction="incoming"
        allThings={things}
        onSelectNode={onSelectNode}
      />,
    );

    fireEvent.click(screen.getByText('Subject'));
    expect(onSelectNode).toHaveBeenCalledWith('s1');
  });
});
