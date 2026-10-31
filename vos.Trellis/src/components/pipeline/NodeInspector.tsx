import { useTranslation } from 'react-i18next';
import type { PortInformation } from '../../pipeline/model';
import type { EditorChoice } from '../../pipeline/serialize';
import type { PipelineNodeData } from './PipelineNodeView';

interface Props {
  nodeId: string;
  data: PipelineNodeData;
  /** The input ports a wire already fills, which a parameter cannot. */
  wiredInputs: Set<string>;
  onSetPorts: (nodeId: string, ports: PortInformation[]) => void;
  onSetBinding: (nodeId: string, port: string, parameterKey: string) => void;
  onSetChoices: (nodeId: string, choices: EditorChoice[]) => void;
  onSetRule: (nodeId: string, rule: string | undefined) => void;
  onClose: () => void;
}

const FIELD = 'flex-1 px-1 py-0.5 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900';
const RULE = 'w-full px-1 py-0.5 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 font-mono';

/** A new rule starts as what having no rule means, so the person edits a rule that already works. */
const NO_FAILURE_REACHED = '$count(failures) = 0';

/** The selected node: a boundary node's declared ports and the Output node's rule, or a service node's inputs bound
 *  to run parameters and the choices it holds. */
export function NodeInspector({ nodeId, data, wiredInputs, onSetPorts, onSetBinding, onSetChoices, onSetRule, onClose }: Props) {
  const { t } = useTranslation();
  const inputs = data.ports.filter((p) => p.direction === 'in');
  const bindings = data.parameterBindings ?? {};
  const choices = data.choices ?? [];
  const setChoice = (index: number, changed: Partial<EditorChoice>) =>
    onSetChoices(nodeId, choices.map((choice, at) => (at === index ? { ...choice, ...changed } : choice)));

  return (
    <div className="absolute top-2 right-2 w-64 bg-white dark:bg-zinc-800 border border-zinc-300 dark:border-zinc-600 rounded shadow-lg p-2 text-xs z-10">
      <div className="font-semibold mb-1.5 flex items-center justify-between gap-2">
        <span className="truncate">{data.label}</span>
        <button onClick={onClose} aria-label={t('pipeline.closeInspector')} className="text-zinc-400 hover:text-zinc-600">×</button>
      </div>
      {data.kind ? (
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
            </div>
          ))}
          <button
            onClick={() => onSetChoices(nodeId, [...choices, { id: crypto.randomUUID(), name: `choice${choices.length + 1}`, when: '' }])}
            className="text-blue-500 hover:text-blue-600 text-left"
          >{t('pipeline.addChoice')}</button>
        </div>
      )}
    </div>
  );
}
