import i18next from 'i18next';

export const TENDRIL_NAMESPACE = 'tendril';

const en = {
  stateCountMissing: 'The number of things in “{{state}}” could not be read.',
  showDecided: 'Show decided',
  cancel: 'Cancel',
  cannotRelate: 'This page cannot relate a row along “{{predicate}}”.',
};

export type TendrilWords = typeof en;

/** Arabic is held once, as `ar`, because i18next falls back to it from `ar-SA` and `ar-AE`. */
export const TENDRIL_WORDS: Readonly<Record<string, TendrilWords>> = {
  en,
  de: { stateCountMissing: 'Die Anzahl der Dinge in „{{state}}“ konnte nicht gelesen werden.', showDecided: 'Entschiedene zeigen', cancel: 'Abbrechen', cannotRelate: 'Diese Seite kann eine Zeile nicht über „{{predicate}}“ verknüpfen.' },
  es: { stateCountMissing: 'No se ha podido leer el número de cosas en «{{state}}».', showDecided: 'Mostrar decididos', cancel: 'Cancelar', cannotRelate: 'Esta página no puede relacionar una fila mediante «{{predicate}}».' },
  fr: { stateCountMissing: 'Le nombre de choses dans « {{state}} » n’a pas pu être lu.', showDecided: 'Afficher les décidés', cancel: 'Annuler', cannotRelate: 'Cette page ne peut pas relier une ligne par « {{predicate}} ».' },
  it: { stateCountMissing: 'Impossibile leggere il numero di cose in «{{state}}».', showDecided: 'Mostra decisi', cancel: 'Annulla', cannotRelate: 'Questa pagina non può collegare una riga tramite «{{predicate}}».' },
  nl: { stateCountMissing: 'Het aantal dingen in ‘{{state}}’ kon niet worden gelezen.', showDecided: 'Besliste tonen', cancel: 'Annuleren', cannotRelate: 'Deze pagina kan een rij niet verbinden via ‘{{predicate}}’.' },
  ar: { stateCountMissing: 'تعذّرت قراءة عدد الأشياء في «{{state}}».', showDecided: 'إظهار ما تقرّر', cancel: 'إلغاء', cannotRelate: 'لا تستطيع هذه الصفحة ربط صف عبر «{{predicate}}».' },
};

export function tendrilWord(key: keyof TendrilWords, values?: Record<string, unknown>): string {
  return i18next.t(key, { ...values, ns: TENDRIL_NAMESPACE });
}
