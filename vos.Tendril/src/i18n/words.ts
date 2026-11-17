import i18next from 'i18next';

export const TENDRIL_NAMESPACE = 'tendril';

const en = {
  stateCountMissing: 'The number of things in “{{state}}” could not be read.',
  showDecided: 'Show decided',
  cancel: 'Cancel',
  cannotRelate: 'This page cannot relate a row along “{{predicate}}”.',
  rosterUnreadable: 'The list to choose from could not be read.',
  cannotPress: 'This page cannot send changes.',
};

export type TendrilWords = typeof en;

/** Arabic is held once, as `ar`, because i18next falls back to it from `ar-SA` and `ar-AE`. */
export const TENDRIL_WORDS: Readonly<Record<string, TendrilWords>> = {
  en,
  de: { stateCountMissing: 'Die Anzahl der Dinge in „{{state}}“ konnte nicht gelesen werden.', showDecided: 'Entschiedene zeigen', cancel: 'Abbrechen', cannotRelate: 'Diese Seite kann eine Zeile nicht über „{{predicate}}“ verknüpfen.', rosterUnreadable: 'Die Liste zur Auswahl konnte nicht gelesen werden.', cannotPress: 'Diese Seite kann keine Änderungen senden.' },
  es: { stateCountMissing: 'No se ha podido leer el número de cosas en «{{state}}».', showDecided: 'Mostrar decididos', cancel: 'Cancelar', cannotRelate: 'Esta página no puede relacionar una fila mediante «{{predicate}}».', rosterUnreadable: 'No se ha podido leer la lista para elegir.', cannotPress: 'Esta página no puede enviar cambios.' },
  fr: { stateCountMissing: 'Le nombre de choses dans « {{state}} » n’a pas pu être lu.', showDecided: 'Afficher les décidés', cancel: 'Annuler', cannotRelate: 'Cette page ne peut pas relier une ligne par « {{predicate}} ».', rosterUnreadable: 'La liste où choisir n’a pas pu être lue.', cannotPress: 'Cette page ne peut pas envoyer de modifications.' },
  it: { stateCountMissing: 'Impossibile leggere il numero di cose in «{{state}}».', showDecided: 'Mostra decisi', cancel: 'Annulla', cannotRelate: 'Questa pagina non può collegare una riga tramite «{{predicate}}».', rosterUnreadable: 'Impossibile leggere l’elenco da cui scegliere.', cannotPress: 'Questa pagina non può inviare modifiche.' },
  nl: { stateCountMissing: 'Het aantal dingen in ‘{{state}}’ kon niet worden gelezen.', showDecided: 'Besliste tonen', cancel: 'Annuleren', cannotRelate: 'Deze pagina kan een rij niet verbinden via ‘{{predicate}}’.', rosterUnreadable: 'De lijst om uit te kiezen kon niet worden gelezen.', cannotPress: 'Deze pagina kan geen wijzigingen verzenden.' },
  ar: { stateCountMissing: 'تعذّرت قراءة عدد الأشياء في «{{state}}».', showDecided: 'إظهار ما تقرّر', cancel: 'إلغاء', cannotRelate: 'لا تستطيع هذه الصفحة ربط صف عبر «{{predicate}}».', rosterUnreadable: 'تعذّرت قراءة القائمة للاختيار منها.', cannotPress: 'لا تستطيع هذه الصفحة إرسال تغييرات.' },
};

export function tendrilWord(key: keyof TendrilWords, values?: Record<string, unknown>): string {
  return i18next.t(key, { ...values, ns: TENDRIL_NAMESPACE });
}
