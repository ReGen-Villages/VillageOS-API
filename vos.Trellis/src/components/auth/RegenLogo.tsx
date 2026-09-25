import animatedLogo from './regenLogoAnimated.svg';
import stillLogo from './regenLogoStill.svg';

interface RegenLogoProps {
  className?: string;
}

/** Both files are rebuilt from the frames of the brand's animated GIF; the animation lives in the file itself. */
export function RegenLogo({ className }: RegenLogoProps) {
  return (
    <picture>
      <source media="(prefers-reduced-motion: reduce)" srcSet={stillLogo} />
      <img src={animatedLogo} alt="ReGen Villages" className={className} />
    </picture>
  );
}
