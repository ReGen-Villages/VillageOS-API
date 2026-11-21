import '@testing-library/jest-dom/vitest';
import { configure } from '@testing-library/react';
import { beforeEach } from 'vitest';
// Shims must run before any module that reads localStorage at import time.
import './testShims';
// Without it, `useTranslation` answers raw keys rather than the English sentences.
import './i18n';
import { waitLimitWithin } from './testWaitLimit';

beforeEach(({ task }) => {
  configure({ asyncUtilTimeout: waitLimitWithin(task.timeout) });
});
