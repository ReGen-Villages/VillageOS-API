import { useMemo } from 'react';
import { useModelStore } from '../stores/modelStore';
import { extractNumberDisplaySettings, type NumberDisplaySettings } from '../utils/guiSettings';

/** How many decimal places numbers show, as the loaded model states it (#6163). Derived rather
 *  than stored, so editing the GUI_Settings Thing changes the display without a rebuild. */
export function useNumberDisplaySettings(): NumberDisplaySettings {
  const things = useModelStore((state) => state.things);
  const relationships = useModelStore((state) => state.relationships);
  return useMemo(() => extractNumberDisplaySettings(things, relationships), [things, relationships]);
}
