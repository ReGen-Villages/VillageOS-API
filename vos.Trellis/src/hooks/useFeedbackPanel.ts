import { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { mountFeedback, type FeedbackPanel } from '../feedback/feedbackPanel';
import { feedbackApi } from '../api/feedbackApi';

/** The name the feedback relay's destinations file knows this console by. */
const APPLICATION = 'Trellis';

/** Puts the report panel on every page while somebody is signed in, in the console's language. */
export function useFeedbackPanel(signedIn: boolean): void {
  const { i18n } = useTranslation();
  const panel = useRef<FeedbackPanel | null>(null);

  useEffect(() => {
    if (!signedIn || !feedbackApi.offered()) return;
    panel.current = mountFeedback({ application: APPLICATION, language: i18n.language, submit: feedbackApi.send });
    return () => {
      panel.current?.unmount();
      panel.current = null;
    };
    // The language is followed by the effect below; remounting on it would throw away a draft.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [signedIn]);

  useEffect(() => {
    panel.current?.setLanguage(i18n.language);
  }, [i18n.language]);
}
