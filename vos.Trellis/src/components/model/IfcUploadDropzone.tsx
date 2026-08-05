import { useRef, useState } from 'react';
import { useTranslation, Trans } from 'react-i18next';
import { Upload } from 'lucide-react';
import { ingestApi } from '../../api/ingestApi';
import { reloadModelData } from '../../hooks/useModelData';
import { toast } from '../common/toastStore';

// In-app IFC ingestion (#5844): drop or pick an .ifc, upload it to the Xylem service, and let the model
// reload over SSE — replacing the old "go run vos.Tools.IfcIngest" empty state. Falls back to a hint when
// no ingestion service is configured.
export function IfcUploadDropzone() {
  const { t } = useTranslation();
  const inputRef = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [replace, setReplace] = useState(false);

  const handleFile = async (file: File) => {
    setBusy(true);
    try {
      const name = file.name.replace(/\.ifc$/i, '');
      const result = await ingestApi.upload(file, name, replace ? 'new-model' : 'merge');
      if (result.success) {
        toast.success(t('ifcUpload.ingested', {
          file: file.name,
          created: result.thingsCreated,
          updated: result.thingsUpdated,
          rels: result.relationshipsCreated,
        }));
        reloadModelData();
      } else {
        toast.error(result.error || t('ifcUpload.ingestFailed'));
      }
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('ifcUpload.ingestFailed'));
    } finally {
      setBusy(false);
    }
  };

  if (!ingestApi.configured()) {
    return (
      <p className="text-sm text-zinc-500 dark:text-zinc-500 mt-2 max-w-md">
        <Trans
          i18nKey="ifcUpload.configureHint"
          components={[
            <code className="font-mono" />,
            <code className="font-mono" />,
            <code className="font-mono" />,
          ]}
        />
      </p>
    );
  }

  return (
    <div
      onDragOver={(e) => e.preventDefault()}
      onDrop={(e) => {
        e.preventDefault();
        const file = e.dataTransfer.files?.[0];
        if (file) handleFile(file);
      }}
      className="mt-4 flex flex-col items-center gap-3"
    >
      <input
        ref={inputRef}
        type="file"
        accept=".ifc"
        className="hidden"
        onChange={(e) => {
          const file = e.target.files?.[0];
          if (file) handleFile(file);
          e.target.value = '';
        }}
      />
      <button
        onClick={() => inputRef.current?.click()}
        disabled={busy}
        className="flex items-center gap-2 px-4 py-2 rounded bg-blue-600 text-white disabled:opacity-50"
      >
        <Upload size={16} /> {busy ? t('ifcUpload.ingesting') : t('ifcUpload.ingest')}
      </button>
      <label className="flex items-center gap-1.5 text-xs text-zinc-500 dark:text-zinc-400">
        <input type="checkbox" checked={replace} onChange={(e) => setReplace(e.target.checked)} disabled={busy} />
        {t('ifcUpload.replaceModel')}
      </label>
      <p className="text-xs text-zinc-400 dark:text-zinc-500">{t('ifcUpload.dragHint')}</p>
    </div>
  );
}
