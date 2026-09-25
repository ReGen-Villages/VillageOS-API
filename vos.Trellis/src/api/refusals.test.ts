import { describe, it, expect, afterEach } from 'vitest';
import i18n from '../i18n';
import { isTicketRefusal, refusalIn } from './refusals';

describe('refusalIn', () => {
  afterEach(async () => {
    await i18n.changeLanguage('en');
  });

  it('says a refusal the pages know in the reader’s language, with its values filled in', async () => {
    await i18n.changeLanguage('de');

    const refusal = refusalIn(
      { code: 'fileTooLarge', error: 'A file may be at most 25 MB.', values: { megabytes: 25 } }, 413);

    expect(refusal.message).toBe('Eine Datei darf höchstens 25 MB groß sein.');
    expect(refusal.code).toBe('fileTooLarge');
  });

  it('shows the service’s own words for a code the pages do not know', async () => {
    await i18n.changeLanguage('de');

    const refusal = refusalIn({ code: 'somethingNew', error: 'Something new went wrong.' }, 400);

    expect(refusal.message).toBe('Something new went wrong.');
    expect(refusal.code).toBeNull();
  });

  it('names the status when the service said nothing', () => {
    expect(refusalIn(null, 502).message).toBe('The submission was refused (502).');
  });

  it('tells a refused ticket apart by its code, in any language', async () => {
    await i18n.changeLanguage('fr');

    expect(isTicketRefusal(refusalIn({ code: 'ticketExpired', error: 'x' }, 403))).toBe(true);
    expect(isTicketRefusal(refusalIn({ code: 'ticketMissing', error: 'x' }, 403))).toBe(true);
    expect(isTicketRefusal(refusalIn({ code: 'codeNotAccepted', error: 'x' }, 403))).toBe(false);
    expect(isTicketRefusal(new Error('Verify the address again'))).toBe(false);
  });
});
