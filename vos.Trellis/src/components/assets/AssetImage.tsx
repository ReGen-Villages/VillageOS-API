import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { assetsApi } from '../../api/assetsApi';

interface Props {
  ticket: string;
  className?: string;
}

type Shown =
  | { kind: 'loading' }
  | { kind: 'image'; objectUrl: string }
  | { kind: 'missing' }
  | { kind: 'notImage' };

/**
 * A property value that is an asset ticket, shown as what it names. The bytes arrive through the
 * authenticated fetch and are handed to the <img> as an object address, released when the image
 * goes. A ticket the store cannot resolve — or one naming content that is not an image — degrades
 * to the ticket text with the reason in its tooltip, because a reference is still a value.
 */
export function AssetImage({ ticket, className }: Props) {
  const { t } = useTranslation();
  const [shown, setShown] = useState<Shown>({ kind: 'loading' });

  useEffect(() => {
    let outlived = false;
    let objectUrl: string | null = null;
    const load = async () => {
      let content: Awaited<ReturnType<typeof assetsApi.getContent>> = null;
      try {
        content = await assetsApi.getContent(ticket);
      } catch {
        content = null;
      }
      if (outlived) return;
      if (!content) {
        setShown({ kind: 'missing' });
        return;
      }
      if (!content.contentType.startsWith('image/')) {
        setShown({ kind: 'notImage' });
        return;
      }
      objectUrl = URL.createObjectURL(new Blob([content.bytes], { type: content.contentType }));
      setShown({ kind: 'image', objectUrl });
    };
    load();
    return () => {
      outlived = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [ticket]);

  if (shown.kind === 'image') {
    return (
      <img
        src={shown.objectUrl}
        alt={t('assets.alt')}
        className={className ?? 'max-h-24 rounded border border-zinc-200 dark:border-zinc-700'}
      />
    );
  }

  if (shown.kind === 'loading') {
    return (
      <div
        aria-label={t('assets.loading')}
        className="h-10 w-16 rounded border border-zinc-200 dark:border-zinc-700 bg-zinc-100 dark:bg-zinc-700/40 animate-pulse"
      />
    );
  }

  return (
    <span
      className="text-xs font-mono truncate max-w-[180px]"
      title={t(shown.kind === 'missing' ? 'assets.missing' : 'assets.notImage')}
    >
      {ticket}
    </span>
  );
}
