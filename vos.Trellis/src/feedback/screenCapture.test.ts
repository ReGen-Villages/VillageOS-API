import { describe, it, expect, vi, afterEach } from 'vitest';
import { domToCanvas } from 'modern-screenshot';
import { CaptureCancelledError, captureScreen, pictureFromFile, scaledToFit } from './screenCapture';

vi.mock('modern-screenshot', () => ({ domToCanvas: vi.fn() }));

const originalMediaDevices = Object.getOwnPropertyDescriptor(navigator, 'mediaDevices');

afterEach(() => {
  if (originalMediaDevices) Object.defineProperty(navigator, 'mediaDevices', originalMediaDevices);
  else delete (navigator as { mediaDevices?: unknown }).mediaDevices;
  vi.restoreAllMocks();
  vi.mocked(domToCanvas).mockReset();
});

function offerScreenCapture(getDisplayMedia: (constraints: unknown) => Promise<MediaStream>) {
  Object.defineProperty(navigator, 'mediaDevices', { value: { getDisplayMedia }, configurable: true });
}

function streamWithTrack() {
  const stop = vi.fn();
  return { stream: { getTracks: () => [{ stop }] } as unknown as MediaStream, stop };
}

describe('capturing the screen', () => {
  it('asks the browser for this tab where it can capture one, and lets go of the stream after one frame', async () => {
    const { stream, stop } = streamWithTrack();
    const getDisplayMedia = vi.fn().mockResolvedValue(stream);
    offerScreenCapture(getDisplayMedia);
    vi.spyOn(HTMLMediaElement.prototype, 'play').mockResolvedValue();
    vi.spyOn(HTMLVideoElement.prototype, 'videoWidth', 'get').mockReturnValue(1280);
    vi.spyOn(HTMLVideoElement.prototype, 'videoHeight', 'get').mockReturnValue(720);
    const drawImage = vi.fn();
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({ drawImage } as never);

    const picture = await captureScreen(document.createElement('div'));

    expect(getDisplayMedia).toHaveBeenCalledWith(expect.objectContaining({
      video: expect.objectContaining({ displaySurface: 'browser' }),
      audio: false,
      preferCurrentTab: true,
    }));
    expect(picture.width).toBe(1280);
    expect(picture.height).toBe(720);
    expect(drawImage).toHaveBeenCalled();
    expect(stop).toHaveBeenCalled();
    expect(domToCanvas).not.toHaveBeenCalled();
  });

  it('is cancelled, not failed, when the person turns the browser’s question down', async () => {
    offerScreenCapture(vi.fn().mockRejectedValue(new DOMException('denied', 'NotAllowedError')));

    await expect(captureScreen(document.createElement('div'))).rejects.toBeInstanceOf(CaptureCancelledError);
  });

  it('draws the page itself where the browser captures no screen, leaving the panel out', async () => {
    Object.defineProperty(navigator, 'mediaDevices', { value: undefined, configurable: true });
    const drawnPage = document.createElement('canvas');
    vi.mocked(domToCanvas).mockResolvedValue(drawnPage);
    const panelHost = document.createElement('div');

    const picture = await captureScreen(panelHost);

    expect(picture).toBe(drawnPage);
    const [, options] = vi.mocked(domToCanvas).mock.calls[0] as unknown as [
      Node, { width?: number; height?: number; filter?: (node: Node) => boolean },
    ];
    expect(options?.width).toBe(window.innerWidth);
    expect(options?.height).toBe(window.innerHeight);
    expect(options?.filter?.(panelHost)).toBe(false);
    expect(options?.filter?.(document.createElement('main'))).toBe(true);
  });
});

describe('a chosen picture', () => {
  it('is refused when the file is not a picture', async () => {
    await expect(pictureFromFile(new File(['notes'], 'notes.txt', { type: 'text/plain' }))).rejects.toThrow();
  });
});

describe('the size a screenshot is sent at', () => {
  it('stays as it is when it already fits', () => {
    expect(scaledToFit(1280, 720, 1920)).toEqual({ width: 1280, height: 720 });
  });

  it('shrinks to the widest allowed, keeping its shape', () => {
    expect(scaledToFit(3840, 2160, 1920)).toEqual({ width: 1920, height: 1080 });
  });
});
