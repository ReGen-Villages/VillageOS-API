import type { PartialResources } from './types';

/**
 * DRAFT — machine-drafted Dutch. Not reviewed by a human translator.
 * Every string here needs sign-off by a fluent speaker before it ships as
 * final. Keys intentionally omitted (e.g. log.fullLogTitle) fall back to the
 * English base locale, which is the expected behaviour for an incomplete draft.
 */
export const nl: PartialResources = {
  nav: {
    appName: 'VILLAGEOS',
    subtitle: 'Temporele grafiek-interface',
    collapseSidebar: 'Zijbalk inklappen',
    expandSidebar: 'Zijbalk uitklappen',
    dashboard: 'Dashboard',
    operations: 'Operaties',
    graph: 'Grafiek',
    model: 'Model',
    pipelines: 'Pijplijnen',
    temporal: 'Temporeel',
    things: 'Dingen',
    properties: 'Eigenschappen',
    logs: 'Logboeken',
  },
  theme: {
    switchTo: 'Schakel naar {{mode}} modus',
    light: 'lichte',
    dark: 'donkere',
  },
  language: {
    label: 'Taal',
    select: 'Taal selecteren',
  },
  log: {
    brokerTitle: 'Broker-logboek',
    serviceTitle: '{{service}}-logboek',
    lineCount_one: '{{count}} regel',
    lineCount_other: '{{count}} regels',
    streaming: 'Streamen',
    reconnecting: 'Opnieuw verbinden',
    pause: 'Pauzeren',
    resume: 'Hervatten',
    pauseAutoScroll: 'Automatisch scrollen pauzeren',
    resumeAutoScroll: 'Automatisch scrollen hervatten',
    snapshot: 'Momentopname',
    snapshotTitle: 'Download de regels die nu zichtbaar zijn',
    fullLog: 'Volledig logboek',
    downloadingFull: 'Downloaden…',
    clear: 'Wissen',
    clearTitle: 'De weergave wissen',
    waiting: 'Wachten op logboekuitvoer…',
    downloadFailed: 'Kon het volledige logboek niet downloaden.',
  },
};
