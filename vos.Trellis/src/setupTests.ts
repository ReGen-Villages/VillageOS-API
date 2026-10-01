import '@testing-library/jest-dom/vitest';
import { configure } from '@testing-library/react';
import { beforeEach } from 'vitest';
// Shims must run before any module that reads localStorage at import time.
import './testShims';
// Initialise i18next once so any component test that calls `useTranslation`
// resolves against the real English base locale instead of raw keys.
import './i18n';
import { waitLimitWithin } from './testWaitLimit';

beforeEach(({ task }) => {
  configure({ asyncUtilTimeout: waitLimitWithin(task.timeout) });
});
