import type { PartialResources } from './types';

/**
 * DRAFT — machine-drafted Spanish. Not reviewed by a human translator.
 * Every string here needs sign-off by a fluent speaker before it ships as
 * final. Keys intentionally omitted (e.g. log.fullLogTitle) fall back to the
 * English base locale, which is the expected behaviour for an incomplete draft.
 */
export const es: PartialResources = {
  nav: {
    appName: 'VILLAGEOS',
    subtitle: 'Interfaz de grafo temporal',
    collapseSidebar: 'Contraer barra lateral',
    expandSidebar: 'Expandir barra lateral',
    dashboard: 'Panel',
    operations: 'Operaciones',
    graph: 'Grafo',
    model: 'Modelo',
    pipelines: 'Flujos',
    temporal: 'Temporal',
    things: 'Cosas',
    properties: 'Propiedades',
    logs: 'Registros',
  },
  theme: {
    switchTo: 'Cambiar al modo {{mode}}',
    light: 'claro',
    dark: 'oscuro',
  },
  language: {
    label: 'Idioma',
    select: 'Seleccionar idioma',
  },
  log: {
    brokerTitle: 'Registro del broker',
    serviceTitle: 'Registro de {{service}}',
    lineCount_one: '{{count}} línea',
    lineCount_other: '{{count}} líneas',
    streaming: 'Transmitiendo',
    reconnecting: 'Reconectando',
    pause: 'Pausar',
    resume: 'Reanudar',
    pauseAutoScroll: 'Pausar desplazamiento automático',
    resumeAutoScroll: 'Reanudar desplazamiento automático',
    snapshot: 'Instantánea',
    snapshotTitle: 'Descargar las líneas visibles actualmente',
    fullLog: 'Registro completo',
    downloadingFull: 'Descargando…',
    clear: 'Limpiar',
    clearTitle: 'Limpiar la vista',
    waiting: 'Esperando salida del registro…',
    downloadFailed: 'No se pudo descargar el registro completo.',
  },
};
