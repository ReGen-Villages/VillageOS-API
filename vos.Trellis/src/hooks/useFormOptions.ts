import { useEffect, useState } from 'react';
import { intakeApi, type FormOptions } from '../api/intakeApi';

/** `onAnswered` must keep its identity between renders, since a new one reads the options again. */
export function useFormOptions(onAnswered?: (options: FormOptions) => void): { options: FormOptions | null; unreachable: boolean } {
  const [options, setOptions] = useState<FormOptions | null>(null);
  const [unreachable, setUnreachable] = useState(false);

  useEffect(() => {
    let abandoned = false;
    intakeApi
      .formOptions()
      .then((answered) => {
        if (abandoned) return;
        setOptions(answered);
        onAnswered?.(answered);
      })
      .catch(() => {
        if (!abandoned) setUnreachable(true);
      });
    return () => {
      abandoned = true;
    };
  }, [onAnswered]);

  return { options, unreachable };
}
