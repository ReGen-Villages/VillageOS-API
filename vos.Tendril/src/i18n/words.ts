import i18next from 'i18next';

export const TENDRIL_NAMESPACE = 'tendril';

const en = {
  stateCountMissing: 'The number of things in “{{state}}” could not be read.',
};

export type TendrilWords = typeof en;

/** Arabic is held once, as `ar`, because i18next falls back to it from `ar-SA` and `ar-AE`. */
export const TENDRIL_WORDS: Readonly<Record<string, TendrilWords>> = {
  en,
  de: { stateCountMissing: 'Die Anzahl der Dinge in „{{state}}“ konnte nicht gelesen werden.' },
  es: { stateCountMissing: 'No se ha podido leer el número de cosas en «{{state}}».' },
  fr: { stateCountMissing: 'Le nombre de choses dans « {{state}} » n’a pas pu être lu.' },
  it: { stateCountMissing: 'Impossibile leggere il numero di cose in «{{state}}».' },
  nl: { stateCountMissing: 'Het aantal dingen in ‘{{state}}’ kon niet worden gelezen.' },
  ar: { stateCountMissing: 'تعذّرت قراءة عدد الأشياء في «{{state}}».' },
};

export function tendrilWord(key: keyof TendrilWords, values?: Record<string, unknown>): string {
  return i18next.t(key, { ...values, ns: TENDRIL_NAMESPACE });
}
