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
  common: {
    start: 'تشغيل',
    stop: 'إيقاف',
    delete: 'حذف',
    cancel: 'إلغاء',
    confirm: 'تأكيد',
    apply: 'تطبيق',
    saving: 'جارٍ الحفظ…',
    loading: 'جارٍ التحميل…',
    notAvailable: 'غير متاح',
    switchModel: 'تبديل النموذج',
    logout: 'تسجيل الخروج',
    search: 'بحث',
    clear: 'مسح',
  },
  dashboard: {
    title: 'لوحة تحكم Mycelium',
    status: { mycelium: 'Mycelium', live: 'مباشر' },
    actions: {
      swagger: 'وثائق واجهة برمجة تطبيقات Swagger',
      reloadSeeds: 'إعادة تحميل الـ seeds من القرص',
      shutdown: 'إيقاف تشغيل Mycelium',
      showActivityFeed: 'عرض موجز النشاط',
    },
    toast: {
      logDownloadFailed: 'فشل تنزيل السجل',
      serviceStarted: 'تم تشغيل الخدمة',
      startFailed: 'فشل التشغيل',
      stopRequested: 'تم طلب الإيقاف',
      stopFailed: 'فشل الإيقاف',
      serviceRetracted: 'تم سحب الخدمة من النموذج',
      deleteFailed: 'فشل الحذف',
      seedsReloaded: 'تمت إعادة تحميل الـ seeds من القرص',
      reloadFailed: 'فشلت إعادة التحميل',
      shutdownInitiated: 'بدأ إيقاف تشغيل Mycelium',
      shutdownFailed: 'فشل إيقاف التشغيل',
    },
    shutdownDialog: {
      title: 'إيقاف تشغيل Mycelium',
      message:
        'هل أنت متأكد من رغبتك في إيقاف تشغيل Mycelium؟ سيتم إيقاف جميع الخدمات والـ daemons. وسيفقد Trellis اتصاله.',
      confirm: 'إيقاف التشغيل',
    },
    deleteDialog: {
      title: 'حذف الخدمة',
      message:
        'سحب «{{name}}» من النموذج؟ يؤدي هذا إلى إزالة الاتصال بحيث لا يوجّه Mycelium إليه بعد الآن. يبقى في الـ seed، لذا فإن إعادة تحميل الـ seed تستعيده. لإيقاف العملية فقط، استخدم إيقاف بدلاً من ذلك.',
      confirm: 'حذف',
    },
    stats: {
      title: 'إحصاءات النموذج',
      things: 'الأشياء',
      relationships: 'العلاقات',
      predicates: 'المسندات',
      properties: 'الخصائص',
      handlers: 'المعالِجات',
      topPredicates: 'أكثر المسندات استخدامًا',
    },
    services: {
      title: 'الخدمات',
      none: 'لا توجد خدمات مسجّلة',
      running: 'قيد التشغيل',
      stopped: 'متوقّف',
      external: 'خارجي',
      viewLog: 'عرض السجل',
      downloadLog: 'تنزيل السجل',
      deleteRetract: 'حذف (سحب من النموذج)',
      requests: 'الطلبات',
      avgTime: 'متوسط الوقت',
      errors: 'الأخطاء',
      lastReq: 'آخر طلب',
      lastContact: 'آخر اتصال',
      pid: 'المعرّف',
      failures: 'حالات الفشل: {{count}}',
      health: {
        Healthy: 'سليم',
        Unhealthy: 'غير سليم',
        Unreachable: 'غير قابل للوصول',
        Unknown: 'غير معروف',
      },
    },
    propertyMode: {
      title: 'وضع تخزين الخصائص',
      defaultMode: 'الوضع الافتراضي',
      ringBufferSize: 'حجم المخزن المؤقت الحلقي',
      sampleRate: 'معدل أخذ العينات',
      ringExample: 'مثال: 100',
      sampleExample: 'مثال: 10',
      updated: 'تم تحديث وضع الخاصية',
      updateFailed: 'تعذّر تحديث وضع الخاصية',
    },
    feed: {
      title: 'موجز النشاط',
      pause: 'إيقاف مؤقت',
      resume: 'استئناف',
      collapse: 'طيّ اللوحة',
      pausedBuffered_one: 'متوقّف مؤقتًا — حدث جديد واحد ({{count}}) في المخزن المؤقت',
      pausedBuffered_other: 'متوقّف مؤقتًا — {{count}} أحداث جديدة في المخزن المؤقت',
      filterPlaceholder: 'تصفية الأحداث…',
      none: 'لا يوجد نشاط بعد',
      category: { model: 'النموذج', things: 'الأشياء', rels: 'العلاقات', props: 'الخصائص', services: 'الخدمات' },
    },
  },
  graph: {
    placeholder: { loadingGraph: 'جارٍ تحميل الرسم البياني…' },
    actions: { importFragment: 'استيراد جزء' },
    toast: {
      deleted: 'تم الحذف: {{name}}',
      deleteFailed: 'فشل الحذف',
      relationshipDeleted: 'تم حذف العلاقة',
      propertyDeleted: 'تم حذف الخاصية: {{name}}',
      thingCreated: 'تم إنشاء الشيء: {{name}}',
      createThingFailed: 'تعذّر إنشاء الشيء',
      fragmentApplied: 'تم تطبيق الجزء: {{created}} مُنشأة · {{updated}} مُحدَّثة · {{rels}} علاقات',
      fragmentFailed: 'تعذّر تطبيق الجزء',
      idCopied: 'تم نسخ معرّف العقدة',
      copyFailed: 'فشل النسخ',
    },
    deleteDialog: {
      title: 'حذف {{entity}}',
      message: 'هل أنت متأكد من رغبتك في حذف «{{name}}»؟ لا يمكن التراجع عن هذا الإجراء.',
    },
    entity: { thing: 'الشيء', relationship: 'العلاقة' },
    search: {
      placeholder: 'البحث عن الأشياء (فاصلة = قائمة)…',
      regexPlaceholder: 'نمط تعبير منتظم…',
      matchCase: 'مطابقة حالة الأحرف',
      exactMatch: 'مطابقة تامة',
      regex: 'تعبير منتظم',
      clear: 'مسح البحث',
      found: 'تم العثور على {{count}}',
      createThing: 'إنشاء شيء',
      newThingPlaceholder: 'اسم الشيء الجديد…',
      create: 'إنشاء',
    },
    toolbar: {
      zoomIn: 'تكبير',
      zoomOut: 'تصغير',
      fit: 'ملاءمة العرض',
      relayout: 'إعادة الترتيب',
      spread: 'مباعدة العقد',
      spreadOff: 'تعطيل المباعدة',
      freeze: 'تجميد التخطيط',
      resume: 'استئناف التخطيط',
      semanticOn: 'تفعيل التكبير الدلالي',
      semanticOff: 'تعطيل التكبير الدلالي',
      collapseLogical: 'طيّ جميع العقد المنطقية',
    },
    contextMenu: {
      viewDetails: 'عرض التفاصيل',
      expandRelationships: 'توسيع العلاقات',
      toggleLogical: 'تبديل العقد المنطقية',
      view3D: 'عرض ثلاثي الأبعاد',
      copyId: 'نسخ المعرّف',
      delete: 'حذف',
    },
    radial: { clearAll: 'مسح جميع المسندات', slice: '{{name}} ({{edges}} حواف)' },
    filter: { all: 'الكل', none: 'لا شيء', hidden: '{{count}} مخفية', clearSearch: 'مسح البحث' },
    typeFilter: {
      title: 'التصفية حسب النوع',
      search: 'البحث عن الأنواع…',
      sort: 'الترتيب',
      sortAria: 'ترتيب الأنواع',
      sortCountDesc: 'العدد (من الأعلى إلى الأدنى)',
      sortCountAsc: 'العدد (من الأدنى إلى الأعلى)',
      sortNameAsc: 'الاسم (أ → ي)',
      sortNameDesc: 'الاسم (ي → أ)',
      noMatch: 'لا توجد أنواع مطابقة.',
    },
    predicateFilter: {
      title: 'التصفية حسب المسند',
      search: 'البحث عن المسندات…',
      noMatch: 'لا توجد مسندات مطابقة.',
    },
  },
};
