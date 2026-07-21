import type { PartialResources } from './types';

/**
 * DRAFT — machine-drafted French. Not reviewed by a human translator.
 * Every string here needs sign-off by a fluent speaker before it ships as
 * final. Keys intentionally omitted (e.g. log.fullLogTitle) fall back to the
 * English base locale, which is the expected behaviour for an incomplete draft.
 */
export const fr: PartialResources = {
  nav: {
    appName: 'VILLAGEOS',
    subtitle: 'Interface de graphe temporel',
    collapseSidebar: 'Réduire la barre latérale',
    expandSidebar: 'Développer la barre latérale',
    dashboard: 'Tableau de bord',
    operations: 'Opérations',
    graph: 'Graphe',
    model: 'Modèle',
    pipelines: 'Pipelines',
    temporal: 'Temporel',
    things: 'Choses',
    properties: 'Propriétés',
    logs: 'Journaux',
  },
  theme: {
    switchTo: 'Passer en mode {{mode}}',
    light: 'clair',
    dark: 'sombre',
  },
  language: {
    label: 'Langue',
    select: 'Sélectionner la langue',
  },
  log: {
    brokerTitle: 'Journal du broker',
    serviceTitle: 'Journal de {{service}}',
    lineCount_one: '{{count}} ligne',
    lineCount_other: '{{count}} lignes',
    streaming: 'Diffusion',
    reconnecting: 'Reconnexion',
    pause: 'Pause',
    resume: 'Reprendre',
    pauseAutoScroll: 'Suspendre le défilement automatique',
    resumeAutoScroll: 'Reprendre le défilement automatique',
    snapshot: 'Instantané',
    snapshotTitle: 'Télécharger les lignes actuellement affichées',
    fullLog: 'Journal complet',
    downloadingFull: 'Téléchargement…',
    clear: 'Effacer',
    clearTitle: 'Effacer la vue',
    waiting: 'En attente de sortie du journal…',
    downloadFailed: 'Impossible de télécharger le journal complet.',
  },
};
