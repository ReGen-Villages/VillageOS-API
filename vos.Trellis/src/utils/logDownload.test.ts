import { describe, it, expect, vi, afterEach } from 'vitest';
import {
  snapshotFileName,
  fullLogFallbackName,
  fileNameFromContentDisposition,
  snapshotBlob,
  triggerDownload,
} from './logDownload';

describe('snapshotFileName', () => {
  it('names the service and stamps the time so repeat downloads do not collide', () => {
    const name = snapshotFileName('irrigator', new Date('2026-07-19T19:53:10.123Z'));
    expect(name).toBe('irrigator-snapshot-2026-07-19_19-53-10.log');
  });

  it('falls back to the broker when no service is given', () => {
    expect(snapshotFileName(undefined, new Date('2026-07-19T00:00:00Z'))).toBe(
      'broker-snapshot-2026-07-19_00-00-00.log',
    );
  });
});

describe('fullLogFallbackName', () => {
  it('uses the service key, or the broker when absent', () => {
    expect(fullLogFallbackName('irrigator')).toBe('irrigator.log');
    expect(fullLogFallbackName(undefined)).toBe('broker.log');
  });
});

describe('fileNameFromContentDisposition', () => {
  it('falls back when the header is missing', () => {
    expect(fileNameFromContentDisposition(null, 'broker.log')).toBe('broker.log');
  });

  it('reads the plain filename form', () => {
    expect(fileNameFromContentDisposition('attachment; filename=mycelium-20260719.log', 'broker.log')).toBe(
      'mycelium-20260719.log',
    );
  });

  it('strips surrounding quotes', () => {
    expect(fileNameFromContentDisposition('attachment; filename="watch-irrigator.log"', 'broker.log')).toBe(
      'watch-irrigator.log',
    );
  });

  // ASP.NET Core emits both forms; the encoded one is authoritative.
  it('prefers the RFC 5987 extended form and decodes it', () => {
    const header = "attachment; filename=fallback.log; filename*=UTF-8''mycelium%2D20260719.log";
    expect(fileNameFromContentDisposition(header, 'broker.log')).toBe('mycelium-20260719.log');
  });

  // A header is server-controlled but must never steer the save out of the downloads folder.
  it('strips any directory part from the name', () => {
    expect(fileNameFromContentDisposition('attachment; filename="../../etc/passwd"', 'broker.log')).toBe('passwd');
    expect(fileNameFromContentDisposition('attachment; filename="C:\\Windows\\evil.log"', 'broker.log')).toBe(
      'evil.log',
    );
  });

  it('falls back when the header carries no filename', () => {
    expect(fileNameFromContentDisposition('attachment', 'broker.log')).toBe('broker.log');
  });
});

describe('snapshotBlob', () => {
  it('joins lines with newlines and ends with one', async () => {
    expect(await snapshotBlob(['a', 'b']).text()).toBe('a\nb\n');
  });

  it('produces an empty blob for an empty buffer', async () => {
    expect(await snapshotBlob([]).text()).toBe('');
  });
});

describe('fileNameFromContentDisposition, when the extended form is damaged', () => {
  // A stray percent is not valid escaping, so decoding throws. The raw text is still a better
  // answer than dropping the server's name and falling back.
  it('keeps the undecodable name rather than discarding it', () => {
    expect(fileNameFromContentDisposition("attachment; filename*=UTF-8''broker%.log", 'fallback.log'))
      .toBe('broker%.log');
  });
});

describe('triggerDownload', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('saves the blob under the given name and cleans up after itself', () => {
    const createObjectURL = vi.fn(() => 'blob:fake-url');
    const revokeObjectURL = vi.fn();
    vi.stubGlobal('URL', { ...URL, createObjectURL, revokeObjectURL });

    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});

    triggerDownload(new Blob(['a line']), 'broker.log');

    expect(createObjectURL).toHaveBeenCalledOnce();
    expect(click).toHaveBeenCalledOnce();

    // The object URL is released and the link removed, so repeated downloads do not leak either.
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:fake-url');
    expect(document.querySelectorAll('a[download]')).toHaveLength(0);

    vi.unstubAllGlobals();
  });

  it('names the saved file', () => {
    vi.stubGlobal('URL', { ...URL, createObjectURL: () => 'blob:fake-url', revokeObjectURL: () => {} });

    let downloadName: string | undefined;
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      downloadName = this.download;
    });

    triggerDownload(new Blob(['x']), 'broker-snapshot-2026-08-05_12-00-00.log');

    expect(downloadName).toBe('broker-snapshot-2026-08-05_12-00-00.log');

    vi.unstubAllGlobals();
  });
});
