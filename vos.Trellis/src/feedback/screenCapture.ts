/** The person said no to the browser's question about sharing the screen, which is a choice rather
 *  than a failure and is answered differently. */
export class CaptureCancelledError extends Error {
  constructor() {
    super();
    this.name = 'CaptureCancelledError';
  }
}

/** Long enough for the browser to deliver a frame once the stream has started, where it cannot say
 *  when a frame is ready. */
const FIRST_FRAME_WAIT_MILLISECONDS = 150;

/**
 * A picture of what the person is looking at. Where the browser can capture a tab (desktop browsers)
 * the picture is exact, maps and 3D views included, at the cost of one question from the browser.
 * Phones and tablets offer no such capture, so there the page draws itself, leaving `panelHost`
 * out; what the graphics card draws may come out blank that way.
 */
export async function captureScreen(panelHost: Element): Promise<HTMLCanvasElement> {
  if (typeof navigator.mediaDevices?.getDisplayMedia === 'function') return frameOfThisTab();

  const { domToCanvas } = await import('modern-screenshot');
  return domToCanvas(document.documentElement, {
    width: window.innerWidth,
    height: window.innerHeight,
    filter: (node) => node !== panelHost,
  });
}

async function frameOfThisTab(): Promise<HTMLCanvasElement> {
  let stream: MediaStream;
  try {
    stream = await navigator.mediaDevices.getDisplayMedia({
      video: { displaySurface: 'browser' },
      audio: false,
      preferCurrentTab: true,
      selfBrowserSurface: 'include',
    } as DisplayMediaStreamOptions);
  } catch (error) {
    if (error instanceof DOMException && error.name === 'NotAllowedError') throw new CaptureCancelledError();
    throw error;
  }

  const video = document.createElement('video');
  try {
    video.muted = true;
    video.srcObject = stream;
    await video.play();
    await new Promise((resolve) => setTimeout(resolve, FIRST_FRAME_WAIT_MILLISECONDS));

    const canvas = document.createElement('canvas');
    canvas.width = video.videoWidth;
    canvas.height = video.videoHeight;
    canvas.getContext('2d')?.drawImage(video, 0, 0);
    return canvas;
  } finally {
    for (const track of stream.getTracks()) track.stop();
    video.srcObject = null;
  }
}

export async function pictureFromFile(file: File): Promise<HTMLCanvasElement> {
  if (!file.type.startsWith('image/')) throw new TypeError(file.type);

  const address = URL.createObjectURL(file);
  try {
    const image = new Image();
    image.src = address;
    await image.decode();
    const canvas = document.createElement('canvas');
    canvas.width = image.naturalWidth;
    canvas.height = image.naturalHeight;
    canvas.getContext('2d')?.drawImage(image, 0, 0);
    return canvas;
  } finally {
    URL.revokeObjectURL(address);
  }
}

export function scaledToFit(width: number, height: number, widest: number): { width: number; height: number } {
  if (width <= widest) return { width, height };
  return { width: widest, height: Math.round((height * widest) / width) };
}
