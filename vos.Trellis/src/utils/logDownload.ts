
/** Identifies the log source in a filename — a service key, or the broker when none is given. */
function sourceName(service: string | undefined): string {
  return service ?? 'broker';
}

/** The time is in the name, so repeated downloads of one log sit side by side rather than overwrite each other. */
export function snapshotFileName(service: string | undefined, at: Date): string {
  const stamp = at.toISOString().replace(/[:.]/g, '-').replace('T', '_').slice(0, 19);
  return `${sourceName(service)}-snapshot-${stamp}.log`;
}

/** Fallback name for a full-file download when the server sends no Content-Disposition. */
export function fullLogFallbackName(service: string | undefined): string {
  return `${sourceName(service)}.log`;
}

/** Prefers the RFC 5987 `filename*` form ASP.NET Core sends beside the plain one, and strips any directory
 *  part, so a hostile header cannot save outside the browser's downloads folder. */
export function fileNameFromContentDisposition(header: string | null, fallback: string): string {
  if (!header) return fallback;

  const extended = /filename\*\s*=\s*UTF-8''([^;]+)/i.exec(header);
  const plain = /filename\s*=\s*"?([^";]+)"?/i.exec(header);
  const raw = extended ? decodeURIComponentSafe(extended[1]) : plain?.[1];

  const name = raw?.trim().split(/[/\\]/).pop();
  return name ? name : fallback;
}

function decodeURIComponentSafe(value: string): string {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

export function snapshotBlob(lines: string[]): Blob {
  return new Blob([lines.join('\n') + (lines.length ? '\n' : '')], { type: 'text/plain;charset=utf-8' });
}

export function triggerDownload(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
