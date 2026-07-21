import '@testing-library/jest-dom/vitest';
// Shims must run before any module that reads localStorage at import time.
import './testShims';
// Initialise i18next once so any component test that calls `useTranslation`
// resolves against the real English base locale instead of raw keys.
import './i18n';
