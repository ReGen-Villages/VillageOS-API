import type { PartialResources } from './types';

/**
 * DRAFT — machine-drafted German. Not reviewed by a human translator.
 * Every string here needs sign-off by a fluent speaker before it ships as
 * final. Keys intentionally omitted (e.g. log.fullLogTitle) fall back to the
 * English base locale, which is the expected behaviour for an incomplete draft.
 */
export const de: PartialResources = {
  nav: {
    appName: 'VILLAGEOS',
    subtitle: 'Oberfläche für temporale Graphen',
    collapseSidebar: 'Seitenleiste einklappen',
    expandSidebar: 'Seitenleiste ausklappen',
    dashboard: 'Dashboard',
    operations: 'Betrieb',
    graph: 'Graph',
    model: 'Modell',
    pipelines: 'Pipelines',
    temporal: 'Temporal',
    things: 'Dinge',
    properties: 'Eigenschaften',
    logs: 'Protokolle',
  },
  theme: {
    switchTo: 'In den {{mode}} Modus wechseln',
    light: 'hellen',
    dark: 'dunklen',
  },
  language: {
    label: 'Sprache',
    select: 'Sprache auswählen',
  },
  log: {
    brokerTitle: 'Broker-Protokoll',
    serviceTitle: '{{service}}-Protokoll',
    lineCount_one: '{{count}} Zeile',
    lineCount_other: '{{count}} Zeilen',
    streaming: 'Streaming',
    reconnecting: 'Neuverbindung',
    pause: 'Pausieren',
    resume: 'Fortsetzen',
    pauseAutoScroll: 'Automatisches Scrollen pausieren',
    resumeAutoScroll: 'Automatisches Scrollen fortsetzen',
    snapshot: 'Momentaufnahme',
    snapshotTitle: 'Die aktuell sichtbaren Zeilen herunterladen',
    fullLog: 'Vollständiges Protokoll',
    downloadingFull: 'Wird heruntergeladen…',
    clear: 'Leeren',
    clearTitle: 'Die Ansicht leeren',
    waiting: 'Warten auf Protokollausgabe…',
    downloadFailed: 'Das vollständige Protokoll konnte nicht heruntergeladen werden.',
  },
};
