import type { PartialResources } from './types';

/**
 * DRAFT — machine-drafted Modern Standard Arabic. Not reviewed by a human
 * translator; every string needs sign-off by a fluent speaker before it ships
 * as final. Shared by both Arabic locales: `ar-SA` and `ar-AE` resolve here
 * through i18next's `ar-XX → ar` language fallback, and any key absent here
 * falls back further to the English base locale.
 *
 * Arabic pluralisation has six CLDR categories, so `log.lineCount` carries all
 * of them (English needs only one/other). The extra suffixes sit outside the
 * base `Resources` shape, hence the widened annotation below.
 */
type ArabicPlurals = {
  log?: {
    lineCount_zero?: string;
    lineCount_two?: string;
    lineCount_few?: string;
    lineCount_many?: string;
  };
};

export const ar: PartialResources & ArabicPlurals = {
  nav: {
    appName: 'VILLAGEOS',
    subtitle: 'واجهة الرسم البياني الزمني',
    collapseSidebar: 'طيّ الشريط الجانبي',
    expandSidebar: 'توسيع الشريط الجانبي',
    dashboard: 'لوحة المعلومات',
    operations: 'العمليات',
    graph: 'الرسم البياني',
    model: 'النموذج',
    pipelines: 'مسارات المعالجة',
    temporal: 'الزمني',
    things: 'الأشياء',
    properties: 'الخصائص',
    logs: 'السجلات',
  },
  theme: {
    switchTo: 'التبديل إلى الوضع {{mode}}',
    light: 'الفاتح',
    dark: 'الداكن',
  },
  language: {
    label: 'اللغة',
    select: 'اختر اللغة',
  },
  log: {
    brokerTitle: 'سجل الوسيط',
    serviceTitle: 'سجل {{service}}',
    lineCount_zero: 'لا أسطر',
    lineCount_one: 'سطر واحد',
    lineCount_two: 'سطران',
    lineCount_few: '{{count}} أسطر',
    lineCount_many: '{{count}} سطرًا',
    lineCount_other: '{{count}} سطر',
    streaming: 'البث',
    reconnecting: 'إعادة الاتصال',
    pause: 'إيقاف مؤقت',
    resume: 'استئناف',
    pauseAutoScroll: 'إيقاف التمرير التلقائي مؤقتًا',
    resumeAutoScroll: 'استئناف التمرير التلقائي',
    snapshot: 'لقطة',
    snapshotTitle: 'تنزيل الأسطر المعروضة حاليًا',
    fullLog: 'السجل الكامل',
    downloadingFull: 'جارٍ التنزيل…',
    fullLogTitle: 'تنزيل ملف السجل الكامل من الوسيط',
    clear: 'مسح',
    clearTitle: 'مسح العرض',
    waiting: 'في انتظار مخرجات السجل…',
    downloadFailed: 'تعذّر تنزيل السجل الكامل.',
  },
};
