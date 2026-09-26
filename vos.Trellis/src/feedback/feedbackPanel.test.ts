import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { FeedbackRefusedError, mountFeedback, type FeedbackPanel, type FeedbackReport } from './feedbackPanel';
import { CaptureCancelledError, captureScreen, pictureFromFile } from './screenCapture';

vi.mock('./screenCapture', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./screenCapture')>()),
  captureScreen: vi.fn(),
  pictureFromFile: vi.fn(),
}));

const drawn: string[] = [];

function fakePicture(width = 800, height = 600): HTMLCanvasElement {
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  return canvas;
}

beforeEach(() => {
  drawn.length = 0;
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(() => ({
    drawImage: () => drawn.push('drawImage'),
    fillRect: (x: number, y: number, width: number, height: number) => drawn.push(`fillRect ${x} ${y} ${width} ${height}`),
    strokeRect: (x: number, y: number, width: number, height: number) => drawn.push(`strokeRect ${x} ${y} ${width} ${height}`),
    set fillStyle(_: string) {},
    set strokeStyle(_: string) {},
    set lineWidth(_: number) {},
  }) as never);
  vi.spyOn(HTMLCanvasElement.prototype, 'toDataURL').mockReturnValue('data:image/jpeg;base64,UElDVFVSRQ==');
});

let panel: FeedbackPanel | null = null;

afterEach(() => {
  panel?.unmount();
  panel = null;
  vi.restoreAllMocks();
  vi.mocked(captureScreen).mockReset();
  vi.mocked(pictureFromFile).mockReset();
});

function mount(overrides: Partial<Parameters<typeof mountFeedback>[0]> = {}) {
  const submit = vi.fn<(report: FeedbackReport) => Promise<{ reference: number }>>().mockResolvedValue({ reference: 7400 });
  panel = mountFeedback({ application: 'Trellis', language: 'en', submit, ...overrides });
  const host = document.querySelector('vos-feedback')!;
  const shadow = host.shadowRoot!;
  const find = <T extends Element>(selector: string) => shadow.querySelector<T>(selector)!;
  return { submit, host, shadow, find };
}

function type(field: HTMLInputElement | HTMLTextAreaElement, text: string) {
  field.value = text;
  field.dispatchEvent(new Event('input', { bubbles: true }));
}

const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

function openPanel() {
  document.querySelector('vos-feedback')!.shadowRoot!.querySelector<HTMLButtonElement>('[data-part="open"]')!.click();
}

describe('the report panel', () => {
  it('draws a button named for what it does, and opens on it with the title ready to type', () => {
    const { find, shadow } = mount();

    const button = find<HTMLButtonElement>('[data-part="open"]');
    expect(button.getAttribute('aria-label')).toBe('Report a problem or an idea');
    expect(find('[role="dialog"]').hasAttribute('hidden')).toBe(true);

    button.click();

    expect(find('[role="dialog"]').hasAttribute('hidden')).toBe(false);
    expect(shadow.activeElement).toBe(find('[name="title"]'));
  });

  it('keeps Send unavailable until there is a title', () => {
    const { find } = mount();
    openPanel();

    expect(find<HTMLButtonElement>('[data-part="send"]').disabled).toBe(true);
    type(find('[name="title"]'), '   ');
    expect(find<HTMLButtonElement>('[data-part="send"]').disabled).toBe(true);
    type(find('[name="title"]'), 'The map stays blank');
    expect(find<HTMLButtonElement>('[data-part="send"]').disabled).toBe(false);
  });

  it('sends what was written with where it was written, and shows the number it was filed as', async () => {
    const { find, submit, shadow } = mount({ language: 'en' });
    openPanel();
    type(find('[name="title"]'), '  The map stays blank  ');
    type(find('[name="description"]'), 'Switched model, nothing drew.');

    find<HTMLButtonElement>('[data-part="send"]').click();
    await settle();

    expect(submit).toHaveBeenCalledWith({
      application: 'Trellis',
      kind: 'bug',
      title: 'The map stays blank',
      description: 'Switched model, nothing drew.',
      screenshot: null,
      context: {
        pageAddress: `${window.location.origin}${window.location.pathname}`,
        browser: navigator.userAgent,
        screenSize: `${window.innerWidth} × ${window.innerHeight}`,
        language: 'en',
      },
    });
    expect(shadow.textContent).toContain('Your report was filed as number 7400.');
  });

  it('sends the page without its query or fragment, where a page may carry its own key', async () => {
    const { find, submit } = mount();
    window.history.pushState({}, '', '/console/index.html?key=secret-key&debug=1#step');
    try {
      openPanel();
      type(find('[name="title"]'), 'Anything');
      find<HTMLButtonElement>('[data-part="send"]').click();
      await settle();

      expect(submit.mock.calls[0][0].context.pageAddress).toBe(`${window.location.origin}/console/index.html`);
    } finally {
      window.history.pushState({}, '', '/');
    }
  });

  it('sends an idea as an idea, and asks for one in the words for an idea', async () => {
    const { find, submit } = mount();
    openPanel();

    find<HTMLInputElement>('[name="kind"][value="idea"]').click();
    expect(find<HTMLInputElement>('[name="title"]').placeholder).toBe('What would help, in a few words');
    type(find('[name="title"]'), 'Export the table');
    find<HTMLButtonElement>('[data-part="send"]').click();
    await settle();

    expect(submit.mock.calls[0][0].kind).toBe('idea');
  });

  it('says a refusal in the reader’s words and keeps what they wrote', async () => {
    const { find, submit, shadow } = mount({ language: 'de' });
    submit.mockRejectedValueOnce(new FeedbackRefusedError('tooManyRequests', { seconds: 40 }));
    openPanel();
    type(find('[name="title"]'), 'Karte bleibt leer');

    find<HTMLButtonElement>('[data-part="send"]').click();
    await settle();

    expect(shadow.textContent).toContain('Versuchen Sie es in 40 Sekunden erneut.');
    expect(find<HTMLInputElement>('[name="title"]').value).toBe('Karte bleibt leer');
    expect(find<HTMLButtonElement>('[data-part="send"]').disabled).toBe(false);
  });

  it('says the connection failed when the report never reached anyone', async () => {
    const { find, submit, shadow } = mount();
    submit.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    openPanel();
    type(find('[name="title"]'), 'Anything');

    find<HTMLButtonElement>('[data-part="send"]').click();
    await settle();

    expect(shadow.textContent).toContain('Check the connection and try again.');
  });

  it('says only that it was refused when the refusal names nothing the panel knows', async () => {
    const { find, submit, shadow } = mount();
    submit.mockRejectedValueOnce(new FeedbackRefusedError('somethingNew'));
    openPanel();
    type(find('[name="title"]'), 'Anything');

    find<HTMLButtonElement>('[data-part="send"]').click();
    await settle();

    expect(shadow.textContent).toContain('The report could not be sent.');
  });

  it('closes on Escape and hands focus back to what opened it', () => {
    const { find, shadow } = mount();
    const button = find<HTMLButtonElement>('[data-part="open"]');
    button.focus();
    button.click();

    find('[role="dialog"]').dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));

    expect(find('[role="dialog"]').hasAttribute('hidden')).toBe(true);
    expect(shadow.activeElement).toBe(button);
  });

  it('keeps Tab inside the open panel', () => {
    const { find, shadow } = mount();
    openPanel();
    find<HTMLButtonElement>('[data-part="close"]').focus();

    find('[role="dialog"]').dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true }));

    const focusable = [...shadow.querySelectorAll<HTMLElement>('[role="dialog"] button:not([disabled]), [role="dialog"] input, [role="dialog"] textarea')]
      .filter((element) => !element.closest('[hidden]'));
    expect(shadow.activeElement).toBe(focusable[focusable.length - 1]);
  });

  it('keeps a draft through Cancel, and starts empty again once a report has gone', async () => {
    const { find } = mount();
    openPanel();
    find<HTMLInputElement>('[name="kind"][value="idea"]').click();
    type(find('[name="title"]'), 'Export the table');

    find<HTMLButtonElement>('[data-part="cancel"]').click();
    openPanel();
    expect(find<HTMLInputElement>('[name="title"]').value).toBe('Export the table');

    find<HTMLButtonElement>('[data-part="send"]').click();
    await settle();
    find<HTMLButtonElement>('[data-part="finish"]').click();
    openPanel();

    expect(find<HTMLInputElement>('[name="title"]').value).toBe('');
    expect(find<HTMLInputElement>('[name="kind"][value="bug"]').checked).toBe(true);
    expect(find('[data-part="form"]').hasAttribute('hidden')).toBe(false);
    expect(find<HTMLButtonElement>('[data-part="send"]').disabled).toBe(true);
  });

  it('moves Tab from the last control back to the first', () => {
    const { find, shadow } = mount();
    openPanel();
    type(find('[name="title"]'), 'Anything');
    find<HTMLButtonElement>('[data-part="send"]').focus();

    find('[role="dialog"]').dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));

    expect(shadow.activeElement).toBe(find('[data-part="close"]'));
  });

  it('opens from a button the page already has, and then draws none of its own', () => {
    const trigger = document.createElement('button');
    document.body.append(trigger);
    try {
      const { find } = mount({ trigger });

      expect(find('[data-part="open"]')).toBeNull();
      trigger.click();
      expect(find('[role="dialog"]').hasAttribute('hidden')).toBe(false);
    } finally {
      trigger.remove();
    }
  });

  it('reads right to left in Arabic, and changes language when the page does', () => {
    const { find } = mount({ language: 'ar' });

    expect(find('[role="dialog"]').getAttribute('dir')).toBe('rtl');
    expect(find('[data-part="send"]').textContent).toBe('إرسال');

    panel!.setLanguage('fr');

    expect(find('[role="dialog"]').getAttribute('dir')).toBe('ltr');
    expect(find('[data-part="send"]').textContent).toBe('Envoyer');
  });

  it('is gone from the page once unmounted', () => {
    mount();
    panel!.unmount();
    panel = null;

    expect(document.querySelector('vos-feedback')).toBeNull();
  });
});

describe('a screenshot in the report panel', () => {
  it('is captured with the panel out of the way, and then shown for marking up', async () => {
    const { find, host } = mount();
    let hiddenWhileCapturing = false;
    vi.mocked(captureScreen).mockImplementation(async () => {
      hiddenWhileCapturing = (host as HTMLElement).style.visibility === 'hidden';
      return fakePicture();
    });
    openPanel();

    find<HTMLButtonElement>('[data-part="capture"]').click();
    await settle();

    expect(hiddenWhileCapturing).toBe(true);
    expect((host as HTMLElement).style.visibility).toBe('');
    expect(find('[data-part="preview"]').closest('[hidden]')).toBeNull();
  });

  it('says so when the capture was turned down, and keeps the panel open', async () => {
    const { find, shadow } = mount();
    vi.mocked(captureScreen).mockRejectedValue(new CaptureCancelledError());
    openPanel();

    find<HTMLButtonElement>('[data-part="capture"]').click();
    await settle();

    expect(shadow.textContent).toContain('The screen was not captured.');
    expect(find('[role="dialog"]').hasAttribute('hidden')).toBe(false);
  });

  it('can be a chosen picture, and a file that is not one is refused', async () => {
    const { find, shadow } = mount();
    openPanel();
    const chooser = find<HTMLInputElement>('[data-part="file"]');

    vi.mocked(pictureFromFile).mockRejectedValueOnce(new Error('not a picture'));
    Object.defineProperty(chooser, 'files', { value: [new File(['x'], 'notes.txt')], configurable: true });
    chooser.dispatchEvent(new Event('change'));
    await settle();
    expect(shadow.textContent).toContain('That file is not a picture.');

    vi.mocked(pictureFromFile).mockResolvedValueOnce(fakePicture());
    Object.defineProperty(chooser, 'files', { value: [new File(['x'], 'screen.png', { type: 'image/png' })], configurable: true });
    chooser.dispatchEvent(new Event('change'));
    await settle();
    expect(find('[data-part="preview"]').closest('[hidden]')).toBeNull();
  });

  it('opens the file chooser from Choose a picture', () => {
    const { find } = mount();
    openPanel();
    const opened = vi.spyOn(find<HTMLInputElement>('[data-part="file"]'), 'click');

    find<HTMLButtonElement>('[data-part="choose"]').click();

    expect(opened).toHaveBeenCalled();
  });

  it('can be pasted into the panel', async () => {
    const { find } = mount();
    vi.mocked(pictureFromFile).mockResolvedValueOnce(fakePicture());
    openPanel();
    const pasted = new Event('paste', { bubbles: true, cancelable: true }) as ClipboardEvent;
    const picture = new File(['x'], 'pasted.png', { type: 'image/png' });
    Object.defineProperty(pasted, 'clipboardData', {
      value: { items: [{ type: 'text/plain', getAsFile: () => null }, { type: 'image/png', getAsFile: () => picture }] },
    });

    find('[role="dialog"]').dispatchEvent(pasted);
    await settle();

    expect(pictureFromFile).toHaveBeenCalledWith(picture);
    expect(pasted.defaultPrevented).toBe(true);
    expect(find('[data-part="preview"]').closest('[hidden]')).toBeNull();
  });

  it('is shrunk to the widest the relay is sent before it goes', async () => {
    const { find } = mount();
    const sentWidths: number[] = [];
    vi.mocked(HTMLCanvasElement.prototype.toDataURL).mockImplementation(function (this: HTMLCanvasElement) {
      sentWidths.push(this.width);
      return 'data:image/jpeg;base64,UElDVFVSRQ==';
    });
    vi.mocked(captureScreen).mockResolvedValue(fakePicture(3840, 2160));
    openPanel();
    find<HTMLButtonElement>('[data-part="capture"]').click();
    await settle();

    type(find('[name="title"]'), 'A wide screen');
    find<HTMLButtonElement>('[data-part="send"]').click();
    await settle();

    expect(sentWidths).toEqual([1920]);
  });

  it('is sent with a hidden box painted over, and can be taken off again', async () => {
    const { find, submit } = mount();
    vi.mocked(captureScreen).mockResolvedValue(fakePicture(800, 600));
    openPanel();
    find<HTMLButtonElement>('[data-part="capture"]').click();
    await settle();

    const preview = find<HTMLCanvasElement>('[data-part="preview"]');
    vi.spyOn(preview, 'getBoundingClientRect').mockReturnValue({ left: 0, top: 0, width: 400, height: 300 } as DOMRect);
    find<HTMLButtonElement>('[data-part="mark-hide"]').click();
    preview.dispatchEvent(new PointerEvent('pointerdown', { clientX: 10, clientY: 10, bubbles: true }));
    preview.dispatchEvent(new PointerEvent('pointermove', { clientX: 60, clientY: 40, bubbles: true }));
    preview.dispatchEvent(new PointerEvent('pointerup', { clientX: 60, clientY: 40, bubbles: true }));
    expect(find<HTMLButtonElement>('[data-part="undo"]').disabled).toBe(false);

    type(find('[name="title"]'), 'A name is showing');
    drawn.length = 0;
    find<HTMLButtonElement>('[data-part="send"]').click();
    await settle();

    expect(drawn).toContain('fillRect 20 20 100 60');
    expect(submit.mock.calls[0][0].screenshot).toBe('data:image/jpeg;base64,UElDVFVSRQ==');
  });

  it('draws no box from a drag the system cancelled', async () => {
    const { find } = mount();
    vi.mocked(captureScreen).mockResolvedValue(fakePicture());
    openPanel();
    find<HTMLButtonElement>('[data-part="capture"]').click();
    await settle();
    const preview = find<HTMLCanvasElement>('[data-part="preview"]');
    vi.spyOn(preview, 'getBoundingClientRect').mockReturnValue({ left: 0, top: 0, width: 800, height: 600 } as DOMRect);

    preview.dispatchEvent(new PointerEvent('pointerdown', { clientX: 10, clientY: 10 }));
    preview.dispatchEvent(new PointerEvent('pointercancel', {}));
    drawn.length = 0;
    preview.dispatchEvent(new PointerEvent('pointermove', { clientX: 90, clientY: 90 }));
    preview.dispatchEvent(new PointerEvent('pointerup', { clientX: 90, clientY: 90 }));

    expect(drawn.some((call) => call.startsWith('strokeRect'))).toBe(false);
    expect(find<HTMLButtonElement>('[data-part="undo"]').disabled).toBe(true);
  });

  it('loses its last mark on Undo, and goes entirely on Remove', async () => {
    const { find, submit } = mount();
    vi.mocked(captureScreen).mockResolvedValue(fakePicture());
    openPanel();
    find<HTMLButtonElement>('[data-part="capture"]').click();
    await settle();
    const preview = find<HTMLCanvasElement>('[data-part="preview"]');
    vi.spyOn(preview, 'getBoundingClientRect').mockReturnValue({ left: 0, top: 0, width: 800, height: 600 } as DOMRect);
    preview.dispatchEvent(new PointerEvent('pointerdown', { clientX: 10, clientY: 10 }));
    preview.dispatchEvent(new PointerEvent('pointerup', { clientX: 90, clientY: 90 }));

    find<HTMLButtonElement>('[data-part="undo"]').click();
    expect(find<HTMLButtonElement>('[data-part="undo"]').disabled).toBe(true);

    find<HTMLButtonElement>('[data-part="remove"]').click();
    expect(find('[data-part="preview"]').closest('[hidden]')).not.toBeNull();
    type(find('[name="title"]'), 'Anything');
    find<HTMLButtonElement>('[data-part="send"]').click();
    await settle();
    expect(submit.mock.calls[0][0].screenshot).toBeNull();
  });
});
