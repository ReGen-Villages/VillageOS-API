import type { PartialResources } from './types';

/**
 * DRAFT — machine-drafted Italian. Not reviewed by a human translator.
 * Every string here needs sign-off by a fluent speaker before it ships as
 * final. Keys intentionally omitted (e.g. log.fullLogTitle) fall back to the
 * English base locale, which is the expected behaviour for an incomplete draft.
 */
export const it: PartialResources = {
  nav: {
    appName: 'VILLAGEOS',
    subtitle: 'Interfaccia del grafo temporale',
    collapseSidebar: 'Comprimi barra laterale',
    expandSidebar: 'Espandi barra laterale',
    dashboard: 'Cruscotto',
    operations: 'Operazioni',
    graph: 'Grafo',
    model: 'Modello',
    pipelines: 'Pipeline',
    temporal: 'Temporale',
    things: 'Cose',
    properties: 'Proprietà',
    logs: 'Registri',
  },
  theme: {
    switchTo: 'Passa alla modalità {{mode}}',
    light: 'chiara',
    dark: 'scura',
  },
  language: {
    label: 'Lingua',
    select: 'Seleziona lingua',
  },
  log: {
    brokerTitle: 'Registro del broker',
    serviceTitle: 'Registro di {{service}}',
    lineCount_one: '{{count}} riga',
    lineCount_other: '{{count}} righe',
    streaming: 'Streaming',
    reconnecting: 'Riconnessione',
    pause: 'Pausa',
    resume: 'Riprendi',
    pauseAutoScroll: 'Sospendi scorrimento automatico',
    resumeAutoScroll: 'Riprendi scorrimento automatico',
    snapshot: 'Istantanea',
    snapshotTitle: 'Scarica le righe attualmente visibili',
    fullLog: 'Registro completo',
    downloadingFull: 'Download in corso…',
    clear: 'Cancella',
    clearTitle: 'Cancella la vista',
    waiting: 'In attesa dell’output del registro…',
    downloadFailed: 'Impossibile scaricare il registro completo.',
  },
};
