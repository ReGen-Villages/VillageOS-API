import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import type { EffectiveProperty, VosRelationship, VosThing } from '../types/vos';
import {
  COLD_STORAGE_PERIOD_PROPERTY,
  DISPOSITION_ARCHETYPE_FLAG,
  DISPOSITION_PREDICATE_FLAG,
  PROPOSED_SITE_PREDICATE_FLAG,
} from './submissionReview';

vi.mock('../api/thingApi', () => ({
  thingApi: { getAll: vi.fn(), getAllProperties: vi.fn(), setProperty: vi.fn() },
}));
vi.mock('../api/relationshipApi', () => ({
  relationshipApi: { getAll: vi.fn(), create: vi.fn() },
}));
vi.mock('../api/modelApi', () => ({
  modelApi: { promote: vi.fn() },
}));
vi.mock('../components/common/toastStore', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

import { modelApi } from '../api/modelApi';
import { relationshipApi } from '../api/relationshipApi';
import { thingApi } from '../api/thingApi';
import { toast } from '../components/common/toastStore';
import { SubmissionReviewPage } from './SubmissionReviewPage';

// Spelled unlike the shipped land-intake template throughout, so a page answering only to that
// spelling fails here rather than passing.
const thing = (Id: string, Name: string): VosThing => ({ Id, Name, Properties: {} });

const edge = (Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship => ({
  Id,
  SubjectId,
  PredicateId,
  TargetId,
  Properties: {},
});

const held = (Value: unknown): EffectiveProperty => ({ Value, Type: 'vos.String', IsInherited: false });

const THINGS: VosThing[] = [
  thing('is', 'is'),
  thing('puts-forward', 'puts-forward'),
  thing('decided', 'decided'),
  thing('covers', 'covers'),
  thing('verdict', 'Verdict'),
  thing('binned', 'binned'),
  thing('taken-on', 'taken-on'),
  thing('arrival-1', 'Meadow Lane arrival'),
  thing('meadow', 'Meadow Lane'),
];

const EDGES: VosRelationship[] = [
  edge('e1', 'arrival-1', 'puts-forward', 'meadow'),
  edge('e2', 'binned', 'is', 'verdict'),
  edge('e3', 'taken-on', 'is', 'verdict'),
  edge('e4', 'meadow', 'covers', 'meadow-plot'),
];

const PROPERTIES: Record<string, Record<string, EffectiveProperty>> = {
  'puts-forward': { [PROPOSED_SITE_PREDICATE_FLAG]: held(true) },
  decided: { [DISPOSITION_PREDICATE_FLAG]: held(true) },
  verdict: { [DISPOSITION_ARCHETYPE_FLAG]: held(true) },
  binned: { [COLD_STORAGE_PERIOD_PROPERTY]: held(30) },
  'arrival-1': { submissionId: held('sub-0001'), submittedAt: held('2026-08-20T09:00:00Z') },
};

function modelAnswers(
  things: VosThing[] = THINGS,
  relationships: VosRelationship[] = EDGES,
  properties = PROPERTIES,
) {
  vi.mocked(thingApi.getAll).mockResolvedValue(things);
  vi.mocked(relationshipApi.getAll).mockResolvedValue(relationships);
  vi.mocked(thingApi.getAllProperties).mockResolvedValue(properties);
}

async function shown() {
  render(<SubmissionReviewPage />);
  await screen.findByText('Submissions');
}

async function promoteWith(template: string, predicate: string, projectName: string) {
  fireEvent.click(screen.getByRole('button', { name: 'Promote' }));
  const dialog = within(await screen.findByRole('dialog'));
  fireEvent.change(dialog.getByLabelText('Template'), { target: { value: template } });
  fireEvent.click(dialog.getByLabelText(predicate));
  fireEvent.change(dialog.getByLabelText('Project name'), { target: { value: projectName } });
  fireEvent.click(dialog.getByRole('button', { name: 'Promote' }));
}

beforeEach(() => {
  vi.clearAllMocks();
  modelAnswers();
  vi.mocked(relationshipApi.create).mockResolvedValue({} as VosRelationship);
  vi.mocked(thingApi.setProperty).mockResolvedValue({} as VosThing);
  vi.mocked(modelApi.promote).mockResolvedValue({
    modelId: 'project-1',
    modelName: 'Meadow Lane',
    rootThingId: 'meadow-copy',
  });
});

describe('what a reviewer sees', () => {
  it('lists a waiting submission with its arrival and what it proposes', async () => {
    await shown();

    expect(screen.getByText('sub-0001')).toBeInTheDocument();
    expect(screen.getByText('2026-08-20T09:00:00Z')).toBeInTheDocument();
    expect(screen.getByText('Meadow Lane')).toBeInTheDocument();
    expect(screen.getByText('Waiting')).toBeInTheDocument();
  });

  it('reads properties resolved rather than own, because seeding moves own values into overrides', async () => {
    await shown();

    expect(thingApi.getAllProperties).toHaveBeenCalledWith('effective');
  });

  it('keeps a submission whose detail cannot be read, with what is known', async () => {
    modelAnswers(THINGS, EDGES, { 'puts-forward': PROPERTIES['puts-forward'] });
    await shown();

    expect(screen.getByText('Meadow Lane arrival')).toBeInTheDocument();
    expect(screen.getByText('Not recorded')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Promote' }));
    expect(await screen.findByRole('dialog')).toHaveTextContent('Promote Meadow Lane arrival');
  });

  it('tells an empty model apart from a queue whose work is all done', async () => {
    modelAnswers(THINGS, EDGES.filter((one) => one.PredicateId !== 'puts-forward'));
    await shown();
    expect(screen.getByText(/Nothing is waiting/)).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Show decided'));

    expect(screen.getByText(/No submissions have arrived/)).toBeInTheDocument();
  });

  it('says so where the model marks no predicate as reaching a proposed site', async () => {
    modelAnswers(THINGS, EDGES, { ...PROPERTIES, 'puts-forward': {} });
    await shown();

    expect(screen.getByText(/marks no single predicate/)).toBeInTheDocument();
  });

  it('says the model could not be read rather than showing an empty queue', async () => {
    vi.mocked(thingApi.getAll).mockRejectedValue(new Error('no'));
    await shown();

    expect(screen.getByText(/could not be read/)).toBeInTheDocument();
  });

  it('drops the list a failed re-read replaced, rather than leaving it under the message', async () => {
    await shown();
    expect(screen.getByText('sub-0001')).toBeInTheDocument();

    vi.mocked(thingApi.getAll).mockRejectedValue(new Error('no'));
    fireEvent.click(screen.getByRole('button', { name: /Read again/ }));

    expect(await screen.findByText(/could not be read/)).toBeInTheDocument();
    expect(screen.queryByText('sub-0001')).not.toBeInTheDocument();
  });
});

describe('rejecting', () => {
  it('relates the submission to the disposition naming a period, through the marked predicate', async () => {
    await shown();
    fireEvent.click(screen.getByRole('button', { name: 'Reject' }));

    await waitFor(() => expect(relationshipApi.create).toHaveBeenCalledWith('arrival-1', 'decided', 'binned'));
    expect(thingApi.setProperty).toHaveBeenCalledWith('arrival-1', 'resolvedAt', 'vos.DateTime', expect.any(String));
  });

  it('leaves the list once the model says it was decided, and stays gone on a re-read', async () => {
    await shown();
    expect(screen.getByText('sub-0001')).toBeInTheDocument();

    modelAnswers(THINGS, [...EDGES, edge('e5', 'arrival-1', 'decided', 'binned')]);
    fireEvent.click(screen.getByRole('button', { name: 'Reject' }));

    await waitFor(() => expect(screen.queryByText('sub-0001')).not.toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: /Read again/ }));
    await waitFor(() => expect(thingApi.getAll).toHaveBeenCalledTimes(3));
    expect(screen.queryByText('sub-0001')).not.toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Show decided'));
    expect(await screen.findByText('binned')).toBeInTheDocument();
  });

  it('shows a refusal and leaves the row where it was', async () => {
    vi.mocked(relationshipApi.create).mockRejectedValue(new Error('Property does not exist on the thing'));
    await shown();
    fireEvent.click(screen.getByRole('button', { name: 'Reject' }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('Property does not exist on the thing')),
    );
    expect(screen.getByText('sub-0001')).toBeInTheDocument();
  });

  it('writes nothing where the model marks no predicate to write a decision through', async () => {
    modelAnswers(THINGS, EDGES, { ...PROPERTIES, decided: {} });
    await shown();
    fireEvent.click(screen.getByRole('button', { name: 'Reject' }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('marks no predicate')),
    );
    expect(relationshipApi.create).not.toHaveBeenCalled();
  });

  it('says what a rejection would mean where no disposition names a period', async () => {
    modelAnswers(THINGS, EDGES, { ...PROPERTIES, binned: {} });
    await shown();
    fireEvent.click(screen.getByRole('button', { name: 'Reject' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('names a period')));
    expect(relationshipApi.create).not.toHaveBeenCalled();
  });
});

describe('promoting', () => {
  it('walks from the site the submission proposes, not from the record of the arrival', async () => {
    await shown();
    await promoteWith('site-analysis.template.json', 'covers', 'Meadow Lane');

    await waitFor(() =>
      expect(modelApi.promote).toHaveBeenCalledWith('meadow', ['covers'], 'site-analysis.template.json', 'Meadow Lane'),
    );
  });

  it('marks the submission with the disposition naming no period', async () => {
    await shown();
    await promoteWith('site-analysis.template.json', 'covers', 'Meadow Lane');

    await waitFor(() => expect(relationshipApi.create).toHaveBeenCalledWith('arrival-1', 'decided', 'taken-on'));
  });

  it('shows the same project both times it is pressed, because the server answers the same', async () => {
    await shown();
    fireEvent.click(screen.getByLabelText('Show decided'));
    await promoteWith('site-analysis.template.json', 'covers', 'Meadow Lane');
    expect(await screen.findByText(/Promoted into Meadow Lane/)).toBeInTheDocument();

    await promoteWith('site-analysis.template.json', 'covers', 'Meadow Lane');

    await waitFor(() => expect(modelApi.promote).toHaveBeenCalledTimes(2));
    expect(screen.getAllByText(/Promoted into Meadow Lane/)).toHaveLength(1);
    expect(screen.getByText('project-1')).toBeInTheDocument();
  });

  it('shows a refusal and marks nothing', async () => {
    vi.mocked(modelApi.promote).mockRejectedValue(new Error('The project model cannot answer every name'));
    await shown();
    await promoteWith('site-analysis.template.json', 'covers', 'Meadow Lane');

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('The project model cannot answer every name')),
    );
    expect(relationshipApi.create).not.toHaveBeenCalled();
  });

  it('names the project whatever the reviewer calls it, not what the site is called', async () => {
    await shown();
    await promoteWith('site-analysis.template.json', 'covers', 'Meadow Lane phase one');

    await waitFor(() =>
      expect(modelApi.promote).toHaveBeenCalledWith(
        'meadow',
        ['covers'],
        'site-analysis.template.json',
        'Meadow Lane phase one',
      ),
    );
  });

  it('offers the submission as the project name where the model no longer names the site', async () => {
    modelAnswers(THINGS.filter((one) => one.Id !== 'meadow'));
    await shown();
    fireEvent.click(screen.getByRole('button', { name: 'Promote' }));

    expect(within(await screen.findByRole('dialog')).getByLabelText('Project name')).toHaveValue(
      'Meadow Lane arrival',
    );
  });

  it('builds nothing when the dialog is dismissed', async () => {
    await shown();
    fireEvent.click(screen.getByRole('button', { name: 'Promote' }));
    fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(modelApi.promote).not.toHaveBeenCalled();
  });

  it('builds nothing where the model marks no predicate to write a decision through', async () => {
    modelAnswers(THINGS, EDGES, { ...PROPERTIES, decided: {} });
    await shown();
    await promoteWith('site-analysis.template.json', 'covers', 'Meadow Lane');

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('marks no predicate')));
    expect(modelApi.promote).not.toHaveBeenCalled();
  });

  it('builds nothing where the model declares no disposition that keeps a submission', async () => {
    modelAnswers(THINGS, EDGES, { ...PROPERTIES, 'taken-on': { [COLD_STORAGE_PERIOD_PROPERTY]: held(7) } });
    await shown();
    await promoteWith('site-analysis.template.json', 'covers', 'Meadow Lane');

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('keeps a submission')));
    expect(modelApi.promote).not.toHaveBeenCalled();
  });

  it('offers only the predicates the model asserts through', async () => {
    await shown();
    fireEvent.click(screen.getByRole('button', { name: 'Promote' }));

    expect(await screen.findByLabelText('covers')).toBeInTheDocument();
    expect(screen.getByLabelText('is')).toBeInTheDocument();
    expect(screen.queryByLabelText('has')).not.toBeInTheDocument();
  });

  it('drops a predicate the reviewer ticked and then unticked', async () => {
    await shown();
    fireEvent.click(screen.getByRole('button', { name: 'Promote' }));
    const dialog = within(await screen.findByRole('dialog'));
    fireEvent.change(dialog.getByLabelText('Template'), { target: { value: 'site-analysis.template.json' } });
    fireEvent.click(dialog.getByLabelText('covers'));
    fireEvent.click(dialog.getByLabelText('is'));
    fireEvent.click(dialog.getByLabelText('covers'));
    fireEvent.click(dialog.getByRole('button', { name: 'Promote' }));

    await waitFor(() =>
      expect(modelApi.promote).toHaveBeenCalledWith('meadow', ['is'], 'site-analysis.template.json', 'Meadow Lane'),
    );
  });
});
