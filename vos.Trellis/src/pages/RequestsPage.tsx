import { useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { ArrowLeftRight, HardDriveDownload, X } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useRequestLog } from '../hooks/useRequestLog';
import { fetchRequest, fetchRequestHour, hourOf, type RequestLogEntry, type RequestLookup } from '../api/requestLogApi';
import { useModelStore } from '../stores/modelStore';
import { useSubscription } from '../hooks/useSse';
import { WHOLE_MODEL } from '../types/subscription';
import { PipelineModel } from '../pipeline/model';
import { triggerDownload } from '../utils/logDownload';
import { ConnectionMark } from '../components/common/ConnectionMark';

const CONNECTION_PARAMETER = 'connection';
const ENTRY_PARAMETER = 'entry';

/** An instant as the broker's files and Taproot write it: universal time to the second. */
function timeOf(entry: RequestLogEntry): string {
  return `${new Date(entry.Time).toISOString().slice(0, 19).replace('T', ' ')}Z`;
}

/** The start of the hour a person is in, as a `datetime-local` field holds it. */
function thisHourLocally(now: Date): string {
  const local = new Date(now.getTime() - now.getTimezoneOffset() * 60_000);
  return `${local.toISOString().slice(0, 13)}:00`;
}

/**
 * The broker's request log: every request it passed to a service, kept outside the model for a limited
 * time. `?connection=` narrows it to one connection and `?entry=` opens one request, which is the address
 * a pipeline run's history links to.
 */
export function RequestsPage() {
  const { t } = useTranslation();
  const [parameters, setParameters] = useSearchParams();
  const connection = parameters.get(CONNECTION_PARAMETER) ?? undefined;
  const entry = parameters.get(ENTRY_PARAMETER) ?? undefined;
  useSubscription(WHOLE_MODEL);
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);

  const connections = useMemo(
    () => new PipelineModel(things, relationships).catalystConnections()
      .map(({ connectionId, name }) => ({ connectionId, name }))
      .sort((a, b) => a.name.localeCompare(b.name)),
    [things, relationships],
  );

  const setParameter = (name: string, value: string | undefined) =>
    setParameters((previous) => {
      const next = new URLSearchParams(previous);
      if (value) next.set(name, value);
      else next.delete(name);
      return next;
    });

  return (
    <div className="h-full flex flex-col">
      <div className="flex flex-wrap items-center justify-between gap-3 px-6 pt-6 pb-4 flex-shrink-0">
        <div className="flex items-center gap-3">
          <ArrowLeftRight className="w-6 h-6 text-zinc-500" />
          <h2 className="text-xl font-bold">{t('requests.title')}</h2>
        </div>
        <div className="flex flex-wrap items-center gap-4 text-xs">
          <div className="flex items-center gap-1.5">
            <span className="text-zinc-500" aria-hidden="true">{t('requests.connection')}</span>
            <select
              value={connection ?? ''}
              onChange={(event) => setParameter(CONNECTION_PARAMETER, event.target.value || undefined)}
              aria-label={t('requests.connection')}
              className="px-2 py-1 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800"
            >
              <option value="">{t('requests.everyConnection')}</option>
              {connection && !connections.some((c) => c.connectionId === connection) && (
                <option value={connection}>{connection}</option>
              )}
              {connections.map((c) => (
                <option key={c.connectionId} value={c.connectionId}>{c.name}</option>
              ))}
            </select>
          </div>
          <HourDownload />
        </div>
      </div>
      <div className="flex-1 min-h-0 flex gap-4 px-6 pb-6">
        <RequestLogView
          key={connection ?? ''}
          connection={connection}
          onOpen={(id) => setParameter(ENTRY_PARAMETER, id)}
        />
        {entry && <RequestEntryPanel key={entry} id={entry} onClose={() => setParameter(ENTRY_PARAMETER, undefined)} />}
      </div>
    </div>
  );
}

function RequestLogView({ connection, onOpen }: { connection?: string; onOpen: (id: string) => void }) {
  const { t } = useTranslation();
  const { entries, connection: streamState } = useRequestLog(connection);
  const streamInWords = {
    connecting: t('connection.connecting'),
    live: t('log.streaming'),
    lost: t('log.reconnecting'),
  }[streamState];

  return (
    <div className="flex-1 min-w-0 flex flex-col gap-2">
      <div className="flex items-center gap-1.5 text-xs">
        <ConnectionMark state={streamState} />
        <span className="text-zinc-500">{streamInWords}</span>
      </div>
      <div className="flex-1 overflow-auto rounded-md border border-zinc-200 dark:border-zinc-800">
        {entries.length === 0 ? (
          <div className="p-3 text-sm text-zinc-500">{t('requests.waiting')}</div>
        ) : (
          <table className="w-full text-xs">
            <thead className="text-left text-zinc-500">
              <tr>
                <th className="px-3 py-2">{t('requests.time')}</th>
                <th className="px-3 py-2">{t('requests.status')}</th>
                <th className="px-3 py-2">{t('requests.took')}</th>
                <th className="px-3 py-2">{t('requests.connection')}</th>
                <th className="px-3 py-2">{t('requests.caller')}</th>
                <th className="px-3 py-2">{t('requests.identifier')}</th>
              </tr>
            </thead>
            <tbody className="font-mono">
              {entries.map((entry) => (
                <tr key={entry.Id} className="border-t border-zinc-100 dark:border-zinc-800">
                  <td className="px-3 py-1.5 whitespace-nowrap">{timeOf(entry)}</td>
                  <td className="px-3 py-1.5">{entry.Status === 0 ? t('requests.nothingAnswered') : entry.Status}</td>
                  <td className="px-3 py-1.5 whitespace-nowrap">{t('requests.milliseconds', { value: entry.DurationMilliseconds })}</td>
                  <td className="px-3 py-1.5">{entry.ConnectionName}</td>
                  <td className="px-3 py-1.5">{entry.Caller ?? ''}</td>
                  <td className="px-3 py-1.5 whitespace-nowrap">
                    <button onClick={() => onOpen(entry.Id)} title={t('requests.open')} className="text-blue-600 hover:underline">
                      {entry.Id}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}

function HourDownload() {
  const { t } = useTranslation();
  const [hour, setHour] = useState(() => thisHourLocally(new Date()));
  const [downloading, setDownloading] = useState(false);
  const [said, setSaid] = useState<string | null>(null);

  const download = async () => {
    setDownloading(true);
    setSaid(null);
    try {
      const file = await fetchRequestHour(hourOf(new Date(hour)));
      if (file) triggerDownload(file.blob, file.fileName);
      else setSaid(t('requests.nothingInHour'));
    } catch {
      setSaid(t('requests.downloadFailed'));
    } finally {
      setDownloading(false);
    }
  };

  return (
    <div className="flex items-center gap-1.5">
      <input
        type="datetime-local"
        step={3600}
        value={hour}
        onChange={(event) => setHour(event.target.value)}
        aria-label={t('requests.hour')}
        className="px-2 py-1 rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-800"
      />
      <button
        onClick={download}
        disabled={downloading || !hour}
        title={t('requests.downloadHourTitle')}
        className="flex items-center gap-1.5 p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors disabled:opacity-40"
      >
        <HardDriveDownload size={14} />
        {downloading ? t('requests.downloading') : t('requests.downloadHour')}
      </button>
      {said && <span className="text-zinc-500">{said}</span>}
    </div>
  );
}

function RequestEntryPanel({ id, onClose }: { id: string; onClose: () => void }) {
  const { t } = useTranslation();
  const [lookup, setLookup] = useState<RequestLookup | 'failed' | null>(null);

  useEffect(() => {
    let current = true;
    fetchRequest(id).then(
      (answer) => { if (current) setLookup(answer); },
      () => { if (current) setLookup('failed'); },
    );
    return () => { current = false; };
  }, [id]);

  return (
    <section aria-label={t('requests.entry')} className="w-96 flex-shrink-0 overflow-auto rounded-md border border-zinc-200 dark:border-zinc-800 p-3 text-xs">
      <div className="flex items-center justify-between mb-2">
        <h3 className="text-sm font-semibold">{t('requests.entry')}</h3>
        <button onClick={onClose} aria-label={t('common.close')} className="p-1 rounded text-zinc-500 hover:bg-zinc-700">
          <X size={14} />
        </button>
      </div>
      {lookup === null && <p className="text-zinc-500">{t('requests.loading')}</p>}
      {lookup === 'failed' && <p className="text-red-500">{t('requests.readFailed')}</p>}
      {lookup !== null && lookup !== 'failed' && lookup.kind === 'noLongerKept' && <p>{t('requests.noLongerKept')}</p>}
      {lookup !== null && lookup !== 'failed' && lookup.kind === 'notShown' && <p>{t('requests.notShown')}</p>}
      {lookup !== null && lookup !== 'failed' && lookup.kind === 'found' && <EntryDetails entry={lookup.entry} />}
    </section>
  );
}

function EntryDetails({ entry }: { entry: RequestLogEntry }) {
  const { t } = useTranslation();
  const rows: [string, string][] = [
    [t('requests.identifier'), entry.Id],
    [t('requests.time'), timeOf(entry)],
    [t('requests.connection'), `${entry.ConnectionName} (${entry.ConnectionId})`],
    ...(entry.Caller ? [[t('requests.caller'), entry.Caller] as [string, string]] : []),
    ...(entry.SubjectId ? [[t('requests.subject'), entry.SubjectId] as [string, string]] : []),
    ...(entry.RelationshipId ? [[t('requests.relationship'), entry.RelationshipId] as [string, string]] : []),
    [t('requests.status'), entry.Status === 0 ? t('requests.nothingAnswered') : String(entry.Status)],
    [t('requests.took'), t('requests.milliseconds', { value: entry.DurationMilliseconds })],
    [t('requests.body'), entry.BodyKeptBytes === 0
      ? t('requests.bodyNoneKept', { bytes: entry.BodyBytes })
      : t('requests.bodyKept', { bytes: entry.BodyBytes, kept: entry.BodyKeptBytes })],
  ];

  return (
    <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
      {rows.map(([label, value]) => (
        <div key={label} className="contents">
          <dt className="text-zinc-500">{label}</dt>
          <dd className="font-mono break-all">{value}</dd>
        </div>
      ))}
    </dl>
  );
}
