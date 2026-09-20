import { useState } from 'react';

/** The held text of a field that is written when the field is left — on blur or Enter — so a
 *  half-typed name never reaches the specification. A new value from outside replaces what is held. */
export function useWrittenWhenLeft(value: string, onCommit: (value: string) => void) {
  const [held, setHeld] = useState(value);
  const [seen, setSeen] = useState(value);
  if (seen !== value) {
    setSeen(value);
    setHeld(value);
  }
  return {
    value: held,
    onChange: (event: React.ChangeEvent<HTMLInputElement>) => setHeld(event.target.value),
    onBlur: () => { if (held !== value) onCommit(held); },
    onKeyDown: (event: React.KeyboardEvent<HTMLInputElement>) => {
      if (event.key === 'Enter') (event.target as HTMLInputElement).blur();
    },
  };
}
