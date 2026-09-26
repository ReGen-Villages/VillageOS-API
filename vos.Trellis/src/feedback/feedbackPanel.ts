import type { FeedbackWords } from './locales/feedbackWords';
import { drawMarks, markBetween, type Mark, type MarkKind } from './markup';
import { CaptureCancelledError, captureScreen, pictureFromFile, scaledToFit } from './screenCapture';
import { directionFor, fill, wordsFor } from './words';

/**
 * The report panel: a person picks a problem or an idea, writes a title and details, and may add a
 * screenshot they can mark up. It is written without a framework and draws itself inside a shadow
 * root, so a page with no build step can carry it and no page's styles can reach into it. It sends
 * nothing itself: the page hands it `submit`, because each page reaches its relay differently.
 */

export type FeedbackKind = 'bug' | 'idea';

export interface FeedbackContext {
  pageAddress: string;
  browser: string;
  screenSize: string;
  language: string;
}

export interface FeedbackReport {
  application: string;
  kind: FeedbackKind;
  title: string;
  description: string;
  /** A JPEG data address, or null when no screenshot was added. */
  screenshot: string | null;
  context: FeedbackContext;
}

/** What a page throws from `submit` when the relay refused: the relay's code, and the values its
 *  words need. Anything else thrown reads as the report not having arrived. */
export class FeedbackRefusedError extends Error {
  readonly code: string;
  readonly values: Record<string, unknown>;

  constructor(code: string, values: Record<string, unknown> = {}) {
    super(code);
    this.name = 'FeedbackRefusedError';
    this.code = code;
    this.values = values;
  }
}

export interface FeedbackOptions {
  /** The name the relay's destinations file knows this page by. */
  application: string;
  submit: (report: FeedbackReport) => Promise<{ reference: string | number }>;
  language?: string;
  /** A button the page already draws. Given one, the panel opens from it and draws none of its own. */
  trigger?: HTMLElement;
  container?: HTMLElement;
}

export interface FeedbackPanel {
  open(): void;
  setLanguage(language: string): void;
  unmount(): void;
}

const ELEMENT_NAME = 'vos-feedback';
const WIDEST_SCREENSHOT = 1920;
const SCREENSHOT_QUALITY = 0.9;

const REFUSALS_WITH_WORDS = new Set<keyof FeedbackWords>([
  'signInRequired', 'tooManyRequests', 'serviceUnavailable', 'filingFailed', 'reportTooLarge', 'fieldTooLong',
  'applicationUnknown',
]);

const STYLE = `
:host { all: initial; }
* { box-sizing: border-box; font-family: inherit; }
[hidden] { display: none !important; }
.open, .panel {
  --surface: var(--vos-feedback-surface, #ffffff);
  --text: var(--vos-feedback-text, #18181b);
  --muted: var(--vos-feedback-muted, #52525b);
  --border: var(--vos-feedback-border, #d4d4d8);
  --accent: var(--vos-feedback-accent, #2563eb);
  --accent-text: var(--vos-feedback-accent-text, #ffffff);
  font-family: var(--vos-feedback-font, system-ui, sans-serif);
  color: var(--text);
  z-index: 2147483000;
}
.open {
  position: fixed; inset-block-end: 16px; inset-inline-end: 16px;
  width: 48px; height: 48px; border-radius: 24px; border: none; cursor: pointer;
  background: var(--accent); color: var(--accent-text);
  display: grid; place-items: center; box-shadow: 0 4px 12px rgb(0 0 0 / 0.25);
}
.open svg { width: 24px; height: 24px; }
.panel {
  position: fixed; inset-block-end: 76px; inset-inline-end: 16px;
  width: min(440px, calc(100vw - 32px)); max-height: calc(100vh - 96px); overflow: auto;
  background: var(--surface); border: 1px solid var(--border); border-radius: 12px;
  box-shadow: 0 12px 32px rgb(0 0 0 / 0.25); padding: 16px; font-size: 14px; line-height: 1.4;
}
header { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin-block-end: 12px; }
h2 { font-size: 16px; font-weight: 600; margin: 0; }
fieldset { border: none; padding: 0; margin: 0 0 12px; display: flex; flex-wrap: wrap; gap: 8px 16px; }
legend { padding: 0; margin-block-end: 6px; color: var(--muted); width: 100%; }
fieldset label { display: flex; align-items: center; gap: 6px; cursor: pointer; }
input[type="radio"] { accent-color: var(--accent); }
.field { display: grid; gap: 4px; margin-block-end: 12px; color: var(--muted); }
input:not([type]), textarea {
  font-size: 16px; color: var(--text); background: var(--surface);
  border: 1px solid var(--border); border-radius: 8px; padding: 8px 10px; width: 100%;
}
textarea { resize: vertical; }
input:focus-visible, textarea:focus-visible, button:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
.label { color: var(--muted); }
.row { display: flex; flex-wrap: wrap; gap: 8px; margin-block: 6px; }
button {
  font-size: 14px; padding: 6px 12px; border-radius: 8px; cursor: pointer;
  border: 1px solid var(--border); background: var(--surface); color: var(--text);
}
button[aria-pressed="true"] { border-color: var(--accent); color: var(--accent); }
button:disabled { opacity: 0.5; cursor: default; }
button.primary { background: var(--accent); border-color: var(--accent); color: var(--accent-text); }
button.icon { border: none; font-size: 20px; line-height: 1; padding: 4px 8px; }
canvas { display: block; width: 100%; height: auto; border: 1px solid var(--border); border-radius: 6px; cursor: crosshair; touch-action: none; }
.hint { color: var(--muted); font-size: 12px; margin: 4px 0 0; }
.status { min-height: 1.4em; margin: 8px 0; }
footer { display: flex; justify-content: flex-end; gap: 8px; }
@media (max-width: 639px) {
  .panel { inset: 0; width: 100%; max-height: 100%; border-radius: 0; border: none; }
}
`;

const SPEECH_BUBBLE = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/></svg>`;

function template(drawsItsOwnButton: boolean): string {
  return `
<style>${STYLE}</style>
${drawsItsOwnButton ? `<button type="button" class="open" data-part="open" data-word-label="openButton">${SPEECH_BUBBLE}</button>` : ''}
<section class="panel" role="dialog" aria-modal="true" aria-labelledby="feedback-heading" hidden>
  <header>
    <h2 id="feedback-heading" data-word="heading"></h2>
    <button type="button" class="icon" data-part="close" data-word-label="close">&times;</button>
  </header>
  <div data-part="form">
    <fieldset>
      <legend data-word="kindLegend"></legend>
      <label><input type="radio" name="kind" value="bug" checked><span data-word="bug"></span></label>
      <label><input type="radio" name="kind" value="idea"><span data-word="idea"></span></label>
    </fieldset>
    <label class="field"><span data-word="title"></span><input name="title" maxlength="255" autocomplete="off"></label>
    <label class="field"><span data-word="description"></span><textarea name="description" rows="5" maxlength="20000"></textarea></label>
    <div>
      <span class="label" data-word="screenshot"></span>
      <div data-part="choices">
        <div class="row">
          <button type="button" data-part="capture" data-word="capture"></button>
          <button type="button" data-part="choose" data-word="choose"></button>
        </div>
        <input type="file" accept="image/*" data-part="file" hidden>
        <p class="hint" data-word="pasteHint"></p>
      </div>
      <div data-part="markup" hidden>
        <div class="row" role="toolbar">
          <button type="button" data-part="mark-highlight" aria-pressed="true" data-word="highlight"></button>
          <button type="button" data-part="mark-hide" aria-pressed="false" data-word="hide"></button>
          <button type="button" data-part="undo" data-word="undo" disabled></button>
          <button type="button" data-part="remove" data-word="remove"></button>
        </div>
        <canvas data-part="preview"></canvas>
        <p class="hint" data-word="markupHint"></p>
      </div>
    </div>
  </div>
  <p class="status" data-part="status" role="status" aria-live="polite"></p>
  <footer data-part="actions">
    <button type="button" data-part="cancel" data-word="cancel"></button>
    <button type="button" class="primary" data-part="send" data-word="send" disabled></button>
  </footer>
  <footer data-part="done" hidden>
    <button type="button" class="primary" data-part="finish" data-word="close"></button>
  </footer>
</section>`;
}

type PanelState = 'editing' | 'sending' | 'sent';

class PanelController {
  private words: FeedbackWords;
  private language: string;
  private state: PanelState = 'editing';
  private picture: HTMLCanvasElement | null = null;
  private marks: Mark[] = [];
  private markKind: MarkKind = 'highlight';
  private dragStart: { x: number; y: number } | null = null;
  private focusBeforeOpening: HTMLElement | null = null;

  private readonly options: FeedbackOptions;
  private readonly host: HTMLElement;
  private readonly shadow: ShadowRoot;
  private readonly dialog: HTMLElement;
  private readonly title: HTMLInputElement;
  private readonly description: HTMLTextAreaElement;
  private readonly preview: HTMLCanvasElement;
  private readonly status: HTMLElement;
  private readonly openFromTrigger = () => this.open();

  constructor(host: HTMLElement, shadow: ShadowRoot, options: FeedbackOptions) {
    this.host = host;
    this.shadow = shadow;
    this.options = options;
    this.language = options.language ?? (document.documentElement.lang || navigator.language);
    this.words = wordsFor(this.language);
    this.dialog = this.part('dialog');
    this.title = this.find('[name="title"]');
    this.description = this.find('[name="description"]');
    this.preview = this.part('preview');
    this.status = this.part('status');
    this.listen();
    this.applyWords();
  }

  open(): void {
    const focused = this.shadow.activeElement ?? document.activeElement;
    this.focusBeforeOpening = focused instanceof HTMLElement ? focused : null;
    this.dialog.hidden = false;
    this.title.focus();
  }

  close(): void {
    this.dialog.hidden = true;
    if (this.state === 'sent') this.reset();
    this.focusBeforeOpening?.focus();
  }

  setLanguage(language: string): void {
    this.language = language;
    this.words = wordsFor(language);
    this.applyWords();
  }

  unmount(): void {
    this.options.trigger?.removeEventListener('click', this.openFromTrigger);
    this.host.remove();
  }

  private find<T extends Element>(selector: string): T {
    return this.shadow.querySelector<T>(selector)!;
  }

  private part<T extends HTMLElement = HTMLElement>(name: string): T {
    return name === 'dialog' ? this.find<T>('[role="dialog"]') : this.find<T>(`[data-part="${name}"]`);
  }

  private listen(): void {
    this.options.trigger?.addEventListener('click', this.openFromTrigger);
    this.shadow.querySelector('[data-part="open"]')?.addEventListener('click', () => this.open());
    this.part('close').addEventListener('click', () => this.close());
    this.part('cancel').addEventListener('click', () => this.close());
    this.part('finish').addEventListener('click', () => this.close());
    this.part('send').addEventListener('click', () => void this.send());
    this.title.addEventListener('input', () => this.refreshSend());
    for (const kind of this.shadow.querySelectorAll('[name="kind"]'))
      kind.addEventListener('change', () => this.applyWords());

    this.part('capture').addEventListener('click', () => void this.capture());
    this.part('choose').addEventListener('click', () => this.part<HTMLInputElement>('file').click());
    this.part<HTMLInputElement>('file').addEventListener('change', (event) => {
      const chooser = event.target as HTMLInputElement;
      const file = chooser.files?.[0];
      if (file) void this.usePictureFrom(file);
      chooser.value = '';
    });
    this.dialog.addEventListener('paste', (event) => {
      const file = [...(event.clipboardData?.items ?? [])].find((item) => item.type.startsWith('image/'))?.getAsFile();
      if (file) {
        event.preventDefault();
        void this.usePictureFrom(file);
      }
    });

    this.part('mark-highlight').addEventListener('click', () => this.chooseMark('highlight'));
    this.part('mark-hide').addEventListener('click', () => this.chooseMark('hide'));
    this.part('undo').addEventListener('click', () => {
      this.marks.pop();
      this.redrawPreview();
    });
    this.part('remove').addEventListener('click', () => this.showPicture(null));
    this.preview.addEventListener('pointerdown', (event) => {
      this.dragStart = this.pointInPicture(event);
      this.preview.setPointerCapture?.(event.pointerId);
    });
    this.preview.addEventListener('pointermove', (event) => {
      if (this.dragStart) this.redrawPreview(markBetween(this.dragStart, this.pointInPicture(event), this.markKind));
    });
    this.preview.addEventListener('pointerup', (event) => {
      if (!this.dragStart) return;
      const mark = markBetween(this.dragStart, this.pointInPicture(event), this.markKind);
      this.dragStart = null;
      if (mark) this.marks.push(mark);
      this.redrawPreview();
    });

    this.dialog.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        this.close();
      } else if (event.key === 'Tab') {
        this.keepFocusInside(event);
      }
    });
  }

  private keepFocusInside(event: KeyboardEvent): void {
    const focusable = [...this.dialog.querySelectorAll<HTMLElement>('button:not([disabled]), input, textarea')]
      .filter((element) => !element.closest('[hidden]'));
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = this.shadow.activeElement;
    if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  private kind(): FeedbackKind {
    return this.find<HTMLInputElement>('[name="kind"][value="idea"]').checked ? 'idea' : 'bug';
  }

  private applyWords(): void {
    const words = this.words;
    this.dialog.setAttribute('dir', directionFor(this.language));
    for (const element of this.shadow.querySelectorAll<HTMLElement>('[data-word]'))
      element.textContent = words[element.dataset.word as keyof FeedbackWords];
    for (const element of this.shadow.querySelectorAll<HTMLElement>('[data-word-label]')) {
      const label = words[element.dataset.wordLabel as keyof FeedbackWords];
      element.setAttribute('aria-label', label);
      element.title = label;
    }
    const idea = this.kind() === 'idea';
    this.title.placeholder = idea ? words.titlePlaceholderIdea : words.titlePlaceholderBug;
    this.description.placeholder = idea ? words.descriptionPlaceholderIdea : words.descriptionPlaceholderBug;
  }

  private refreshSend(): void {
    this.part<HTMLButtonElement>('send').disabled = this.state !== 'editing' || this.title.value.trim() === '';
  }

  private say(text: string): void {
    this.status.textContent = text;
  }

  private async capture(): Promise<void> {
    this.say(this.words.capturing);
    this.host.style.visibility = 'hidden';
    try {
      this.showPicture(await captureScreen(this.host));
      this.say('');
    } catch (error) {
      this.say(error instanceof CaptureCancelledError ? this.words.captureCancelled : this.words.captureFailed);
    } finally {
      this.host.style.visibility = '';
    }
  }

  private async usePictureFrom(file: File): Promise<void> {
    try {
      this.showPicture(await pictureFromFile(file));
      this.say('');
    } catch {
      this.say(this.words.notAPicture);
    }
  }

  private showPicture(picture: HTMLCanvasElement | null): void {
    this.picture = picture;
    this.marks = [];
    this.part('choices').hidden = picture !== null;
    this.part('markup').hidden = picture === null;
    if (picture) {
      this.preview.width = picture.width;
      this.preview.height = picture.height;
      this.redrawPreview();
    } else {
      this.refreshUndo();
    }
  }

  private chooseMark(kind: MarkKind): void {
    this.markKind = kind;
    this.part('mark-highlight').setAttribute('aria-pressed', String(kind === 'highlight'));
    this.part('mark-hide').setAttribute('aria-pressed', String(kind === 'hide'));
  }

  private picturePixelsPerScreenPixel(): number {
    const shown = this.preview.getBoundingClientRect().width;
    return shown > 0 ? this.preview.width / shown : 1;
  }

  private pointInPicture(event: PointerEvent): { x: number; y: number } {
    const box = this.preview.getBoundingClientRect();
    const scale = this.picturePixelsPerScreenPixel();
    return { x: Math.round((event.clientX - box.left) * scale), y: Math.round((event.clientY - box.top) * scale) };
  }

  private redrawPreview(provisional: Mark | null = null): void {
    this.refreshUndo();
    if (!this.picture) return;
    const context = this.preview.getContext('2d');
    if (!context) return;
    context.drawImage(this.picture, 0, 0);
    drawMarks(context, provisional ? [...this.marks, provisional] : this.marks, this.picturePixelsPerScreenPixel());
  }

  private refreshUndo(): void {
    this.part<HTMLButtonElement>('undo').disabled = this.marks.length === 0;
  }

  /** The picture as sent: marks painted in at full size, so a hidden box covers every pixel it
   *  was drawn over, and only then shrunk to the widest the relay is sent. */
  private screenshotToSend(): string | null {
    if (!this.picture) return null;
    const marked = document.createElement('canvas');
    marked.width = this.picture.width;
    marked.height = this.picture.height;
    const context = marked.getContext('2d');
    if (!context) return null;
    context.drawImage(this.picture, 0, 0);
    drawMarks(context, this.marks, this.picturePixelsPerScreenPixel());

    const size = scaledToFit(marked.width, marked.height, WIDEST_SCREENSHOT);
    if (size.width === marked.width) return marked.toDataURL('image/jpeg', SCREENSHOT_QUALITY);
    const sent = document.createElement('canvas');
    sent.width = size.width;
    sent.height = size.height;
    sent.getContext('2d')?.drawImage(marked, 0, 0, size.width, size.height);
    return sent.toDataURL('image/jpeg', SCREENSHOT_QUALITY);
  }

  private report(): FeedbackReport {
    return {
      application: this.options.application,
      kind: this.kind(),
      title: this.title.value.trim(),
      description: this.description.value.trim(),
      screenshot: this.screenshotToSend(),
      context: {
        pageAddress: window.location.href,
        browser: navigator.userAgent,
        screenSize: `${window.innerWidth} × ${window.innerHeight}`,
        language: this.language,
      },
    };
  }

  private async send(): Promise<void> {
    this.state = 'sending';
    this.refreshSend();
    this.say(this.words.sending);
    try {
      const { reference } = await this.options.submit(this.report());
      this.state = 'sent';
      this.part('form').hidden = true;
      this.part('actions').hidden = true;
      this.part('done').hidden = false;
      this.say(fill(this.words.sent, { reference }));
      this.part<HTMLButtonElement>('finish').focus();
    } catch (error) {
      this.state = 'editing';
      this.refreshSend();
      this.say(this.refusalWords(error));
    }
  }

  private refusalWords(error: unknown): string {
    if (!(error instanceof FeedbackRefusedError)) return this.words.unreachable;
    const code = error.code as keyof FeedbackWords;
    return REFUSALS_WITH_WORDS.has(code) ? fill(this.words[code], error.values) : this.words.refused;
  }

  private reset(): void {
    this.state = 'editing';
    this.title.value = '';
    this.description.value = '';
    this.find<HTMLInputElement>('[name="kind"][value="bug"]').checked = true;
    this.showPicture(null);
    this.part('form').hidden = false;
    this.part('actions').hidden = false;
    this.part('done').hidden = true;
    this.say('');
    this.applyWords();
    this.refreshSend();
  }
}

export function mountFeedback(options: FeedbackOptions): FeedbackPanel {
  const host = document.createElement(ELEMENT_NAME);
  const shadow = host.attachShadow({ mode: 'open' });
  shadow.innerHTML = template(options.trigger === undefined);
  (options.container ?? document.body).append(host);

  const controller = new PanelController(host, shadow, options);
  return {
    open: () => controller.open(),
    setLanguage: (language) => controller.setLanguage(language),
    unmount: () => controller.unmount(),
  };
}
