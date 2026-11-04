import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { PipelineNamed, PortInformation } from '../../pipeline/model';
import type { EditorChoice } from '../../pipeline/serialize';
import { passedOn, removePassedOn, renamePassedOn } from '../../pipeline/passedOn';
import type { VosThing } from '../../types/vos';
import { ThingPicker } from '../common/ThingPicker';
import type { PipelineNodeData } from './PipelineNodeView';

interface Props {
  nodeId: string;
  data: PipelineNodeData;
  /** The input ports a wire already fills, which a parameter cannot. */
  wiredInputs: Set<string>;
  /** In-port identifier → the Things that port offers. */
  offeredByPort: Record<string, VosThing[]>;
  /** Every Thing the person may read of the kinds the in ports offer, once the page has been asked for them. */
  widerList: VosThing[];
  /** Whether a choice is searching the wider list, so the page can fetch it and let it go again. */
  onWiden: (widened: boolean) => void;
  nameOf: (thingId: string) => string;
  /** What a tell or follow-on node may stand for: the systems told through a connection, or the other pipelines. */
  standsForOptions: PipelineNamed[];
  onSetStandsFor: (nodeId: string, thingId: string) => void;
  onSetPorts: (nodeId: string, ports: PortInformation[]) => void;
  onSetBinding: (nodeId: string, port: string, parameterKey: string) => void;
  onSetChosen: (nodeId: string, chosen: Record<string, string>) => void;
  onSetChoices: (nodeId: string, choices: EditorChoice[]) => void;
  onSetRule: (nodeId: string, rule: string | undefined) => void;
  onClose: () => void;
}

const FIELD = 'flex-1 px-1 py-0.5 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900';
const RULE = 'w-full px-1 py-0.5 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 font-mono';

/** A new rule starts as what having no rule means, so the person edits a rule that already works. */
const NO_FAILURE_REACHED = '$count(failures) = 0';

/** The selected node: a boundary node's declared ports and the Output node's rule, what a tell or follow-on node
 *  stands for and passes on, or the inputs of a service or stop node bound to run parameters and the choices a service
 *  node holds. */
export function NodeInspector({
  nodeId, data, wiredInputs, offeredByPort, widerList, onWiden, nameOf, standsForOptions, onSetStandsFor,
  onSetPorts, onSetBinding, onSetChosen, onSetChoices, onSetRule, onClose,
}: Props) {
  const { t } = useTranslation();
  const [searchingChoices, setSearchingChoices] = useState<ReadonlySet<string>>(new Set());
  const widened = searchingChoices.size > 0;
  useEffect(() => { onWiden(widened); }, [widened, onWiden]);
  useEffect(() => () => onWiden(false), [onWiden]);
  const setSearching = (choiceId: string, searching: boolean) => setSearchingChoices((before) => {
    const next = new Set(before);
    if (searching) next.add(choiceId);
    else next.delete(choiceId);
    return next;
  });
  const inputs = data.ports.filter((p) => p.direction === 'in');
  const bindings = data.parameterBindings ?? {};
  const chosen = data.chosen ?? {};
  const offeringInputs = inputs.filter((p) => (offeredByPort[p.portId] ?? []).length > 0);
  const offered = [...new Map(offeringInputs.flatMap((p) => offeredByPort[p.portId]).map((thing) => [thing.Id, thing])).values()];
  const choose = (portId: string, thingId: string) => {
    const next = { ...chosen };
    if (thingId) next[portId] = thingId;
    else delete next[portId];
    onSetChosen(nodeId, next);
  };
  const choices = data.choices ?? [];
  const setChoice = (index: number, changed: Partial<EditorChoice>) =>
    onSetChoices(nodeId, choices.map((choice, at) => (at === index ? { ...choice, ...changed } : choice)));

  return (
    <div className="absolute top-2 right-2 w-64 max-h-[calc(100%-1rem)] overflow-y-auto bg-white dark:bg-zinc-800 border border-zinc-300 dark:border-zinc-600 rounded shadow-lg p-2 text-xs z-10">
      <div className="font-semibold mb-1.5 flex items-center justify-between gap-2">
        <span className="truncate">{data.label}</span>
        <button onClick={onClose} aria-label={t('pipeline.closeInspector')} className="text-zinc-400 hover:text-zinc-600">×</button>
      </div>
      {data.kind === 'tell' || data.kind === 'followOn' ? (
        <PassedOn
          nodeId={nodeId}
          data={data}
          standsForOptions={standsForOptions}
          nameOf={nameOf}
          onSetStandsFor={onSetStandsFor}
          onSetPorts={onSetPorts}
        />
      ) : data.kind === 'input' || data.kind === 'output' ? (
        <div className="flex flex-col gap-1">
          <div className="text-zinc-400">{t('pipeline.ports', { direction: data.kind === 'input' ? t('pipeline.outputs.word') : t('pipeline.inputs') })}</div>
          {data.ports.map((p, i) => (
            <div key={p.portId} className="flex items-center gap-1">
              <input
                aria-label={t('pipeline.portName', { index: i + 1 })}
                value={p.portName}
                onChange={(e) => onSetPorts(nodeId, data.ports.map((q, j) => (j === i ? { ...q, portName: e.target.value } : q)))}
                className={FIELD}
              />
              <button
                onClick={() => onSetPorts(nodeId, data.ports.filter((_, j) => j !== i))}
                aria-label={t('pipeline.removePort', { name: p.portName })}
                className="text-zinc-400 hover:text-red-400 px-1"
              >×</button>
            </div>
          ))}
          <button
            onClick={() => onSetPorts(nodeId, [...data.ports, { portId: crypto.randomUUID(), portName: `port${data.ports.length + 1}`, direction: data.kind === 'input' ? 'out' : 'in', type: 'any', required: data.kind === 'output' }])}
            className="mt-1 text-blue-500 hover:text-blue-600 text-left"
          >{t('pipeline.addPort')}</button>
          {data.kind === 'output' && (
            <>
              <div className="text-zinc-400 mt-2">{t('pipeline.ruleLabel')}</div>
              {data.rule === undefined ? (
                <>
                  <div className="text-zinc-500 italic">{t('pipeline.noRule')}</div>
                  <button onClick={() => onSetRule(nodeId, NO_FAILURE_REACHED)} className="text-blue-500 hover:text-blue-600 text-left">
                    {t('pipeline.addRule')}
                  </button>
                </>
              ) : (
                <>
                  <textarea
                    aria-label={t('pipeline.outputRule')}
                    value={data.rule}
                    onChange={(e) => onSetRule(nodeId, e.target.value)}
                    rows={2}
                    className={RULE}
                  />
                  <button onClick={() => onSetRule(nodeId, undefined)} className="text-zinc-400 hover:text-red-400 text-left">
                    {t('pipeline.removeRule')}
                  </button>
                </>
              )}
            </>
          )}
        </div>
      ) : (
        <div className="flex flex-col gap-1">
          {inputs.length === 0 ? (
            <div className="text-zinc-400">{t('pipeline.noInputPorts')}</div>
          ) : (
            <>
              <div className="text-zinc-400">{t('pipeline.bindInput')}</div>
              {inputs.map((p) => (
                <label key={p.portId} className="flex items-center gap-1">
                  <span className="w-16 truncate text-zinc-600 dark:text-zinc-300">{p.portName}</span>
                  {wiredInputs.has(p.portId) ? (
                    <span className="flex-1 italic text-zinc-400">{t('pipeline.wired')}</span>
                  ) : (
                    <input
                      placeholder={t('pipeline.fromParameter')}
                      value={bindings[p.portName] ?? ''}
                      onChange={(e) => onSetBinding(nodeId, p.portName, e.target.value)}
                      className={FIELD}
                    />
                  )}
                </label>
              ))}
            </>
          )}
          {offeringInputs.length > 0 && (
            <>
              <div className="text-zinc-400 mt-2">{t('pipeline.chosenLabel')}</div>
              {offeringInputs.map((p) => (
                <label key={p.portId} className="flex items-center gap-1">
                  <span className="w-16 truncate text-zinc-600 dark:text-zinc-300">{p.portName}</span>
                  <select
                    aria-label={t('pipeline.chosenFor', { port: p.portName })}
                    value={chosen[p.portId] ?? ''}
                    onChange={(e) => choose(p.portId, e.target.value)}
                    className={FIELD}
                  >
                    <option value="">{t('pipeline.nothingChosen')}</option>
                    {offeredByPort[p.portId].map((thing) => <option key={thing.Id} value={thing.Id}>{thing.Name}</option>)}
                  </select>
                </label>
              ))}
            </>
          )}
          {data.kind !== 'stop' && (
            <>
              <div className="text-zinc-400 mt-2">{t('pipeline.choicesLabel')}</div>
              {choices.map((choice, index) => (
                <div key={choice.id} className="flex flex-col gap-0.5 border-l-2 border-zinc-200 dark:border-zinc-700 pl-1">
                  <div className="flex items-center gap-1">
                    <input
                      aria-label={t('pipeline.choiceName', { index: index + 1 })}
                      value={choice.name}
                      onChange={(e) => setChoice(index, { name: e.target.value })}
                      className={FIELD}
                    />
                    <button
                      onClick={() => onSetChoices(nodeId, choices.filter((_, at) => at !== index))}
                      aria-label={t('pipeline.removeChoice', { name: choice.name })}
                      className="text-zinc-400 hover:text-red-400 px-1"
                    >×</button>
                  </div>
                  <textarea
                    aria-label={t('pipeline.choiceRule', { index: index + 1 })}
                    value={choice.when}
                    onChange={(e) => setChoice(index, { when: e.target.value })}
                    rows={2}
                    className={RULE}
                  />
                  <ComparedThings
                    index={index + 1}
                    comparesWith={choice.comparesWith}
                    offered={offered}
                    widerList={widerList}
                    searching={searchingChoices.has(choice.id)}
                    onSearch={(searching) => setSearching(choice.id, searching)}
                    nameOf={nameOf}
                    onChange={(comparesWith) => setChoice(index, { comparesWith })}
                  />
                </div>
              ))}
              <button
                onClick={() => onSetChoices(nodeId, [...choices, { id: crypto.randomUUID(), name: `choice${choices.length + 1}`, when: '', comparesWith: [] }])}
                className="text-blue-500 hover:text-blue-600 text-left"
              >{t('pipeline.addChoice')}</button>
            </>
          )}
        </div>
      )}
    </div>
  );
}

interface PassedOnProperties {
  nodeId: string;
  data: PipelineNodeData;
  standsForOptions: PipelineNamed[];
  nameOf: (thingId: string) => string;
  onSetStandsFor: (nodeId: string, thingId: string) => void;
  onSetPorts: (nodeId: string, ports: PortInformation[]) => void;
}

/** What a tell node tells or a follow-on node starts, and each value it passes on, named once for its in port and
 *  its out port. What it stands for stays listed after it stops being offered, because a select whose value is not
 *  among its options shows the first one instead. */
function PassedOn({ nodeId, data, standsForOptions, nameOf, onSetStandsFor, onSetPorts }: PassedOnProperties) {
  const { t } = useTranslation();
  const standing = data.standsForId;
  const listed = standing && !standsForOptions.some((option) => option.id === standing)
    ? [{ id: standing, name: nameOf(standing) }, ...standsForOptions]
    : standsForOptions;
  const taken = data.ports.filter((port) => port.direction === 'in');
  const standsForLabel = data.kind === 'tell' ? t('pipeline.tells') : t('pipeline.starts');

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-1">
        <span className="w-16 text-zinc-400">{standsForLabel}</span>
        <select aria-label={standsForLabel} value={standing ?? ''} onChange={(e) => onSetStandsFor(nodeId, e.target.value)} className={FIELD}>
          {listed.map((option) => <option key={option.id} value={option.id}>{option.name}</option>)}
        </select>
      </div>
      <div className="text-zinc-400 mt-2">{t('pipeline.passesOn')}</div>
      {taken.map((port, index) => (
        <div key={port.portId} className="flex items-center gap-1">
          <input
            aria-label={t('pipeline.portName', { index: index + 1 })}
            value={port.portName}
            onChange={(e) => onSetPorts(nodeId, renamePassedOn(data.ports, port.portId, e.target.value))}
            className={FIELD}
          />
          <button
            onClick={() => onSetPorts(nodeId, removePassedOn(data.ports, port.portId))}
            aria-label={t('pipeline.removePort', { name: port.portName })}
            className="text-zinc-400 hover:text-red-400 px-1"
          >×</button>
        </div>
      ))}
      <button
        onClick={() => onSetPorts(nodeId, [...data.ports, ...passedOn(`port${taken.length + 1}`)])}
        className="mt-1 text-blue-500 hover:text-blue-600 text-left"
      >{t('pipeline.addPort')}</button>
    </div>
  );
}

interface ComparedThingsProperties {
  index: number;
  comparesWith: string[];
  offered: VosThing[];
  widerList: VosThing[];
  searching: boolean;
  onSearch: (searching: boolean) => void;
  nameOf: (thingId: string) => string;
  onChange: (comparesWith: string[]) => void;
}

/** Adding lists only what the node's in ports offer until the person turns on the search of every Thing of
 *  their kinds, which turns off after each addition. */
function ComparedThings({ index, comparesWith, offered, widerList, searching, onSearch, nameOf, onChange }: ComparedThingsProperties) {
  const { t } = useTranslation();
  const [additions, setAdditions] = useState(0);
  const offeredIds = new Set(offered.map((thing) => thing.Id));
  const listed = searching ? [...offered, ...widerList.filter((thing) => !offeredIds.has(thing.Id))] : offered;
  const addable = listed.filter((thing) => !comparesWith.includes(thing.Id));

  return (
    <div className="flex flex-col gap-0.5">
      <div className="text-zinc-400">{t('pipeline.comparesWithLabel')}</div>
      {comparesWith.map((thingId) => (
        <div key={thingId} className="flex items-center gap-1">
          <span className="flex-1 truncate">{nameOf(thingId)}</span>
          <button
            onClick={() => onChange(comparesWith.filter((compared) => compared !== thingId))}
            aria-label={t('pipeline.removeCompared', { name: nameOf(thingId) })}
            className="text-zinc-400 hover:text-red-400 px-1"
          >×</button>
        </div>
      ))}
      <ThingPicker
        key={additions}
        things={addable}
        value=""
        placeholder={t('pipeline.addCompared', { index })}
        onChange={(thingId) => {
          onChange([...comparesWith, thingId]);
          onSearch(false);
          setAdditions((count) => count + 1);
        }}
      />
      <label className="flex items-center gap-1 text-zinc-500">
        <input type="checkbox" checked={searching} onChange={(e) => onSearch(e.target.checked)} />
        {t('pipeline.searchOfferedKinds')}
      </label>
      {searching && (
        <div role="status" className="text-zinc-500">{t('pipeline.ofTheOfferedKinds', { count: widerList.length })}</div>
      )}
    </div>
  );
}
