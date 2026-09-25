import animatedLogo from './regenLogoAnimated.svg';
import stillLogo from './regenLogoStill.svg';

interface RegenLogoProps {
  className?: string;
}

/**
 * The ReGen logo, drawing itself the way the brand's original animation did, then keeping the windmill turning,
 * the clouds drifting and the sprout swaying. A reader who has asked for less motion gets the finished logo
 * standing still.
 *
 * Both files are rebuilt from the frames of the brand's animated GIF. The animation lives in the files themselves,
 * so it plays the same wherever an image can be shown.
 */
export function RegenLogo({ className }: RegenLogoProps) {
  return (
    <picture>
      <source media="(prefers-reduced-motion: reduce)" srcSet={stillLogo} />
      <img src={animatedLogo} alt="ReGen Villages" className={className} />
    </picture>
  );
}
