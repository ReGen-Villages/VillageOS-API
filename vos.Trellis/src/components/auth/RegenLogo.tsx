import { regenMark } from './regenMark';

/** The mark's own colour, as the brand draws it. */
const REGEN_TEAL = '#37c2aa';

interface RegenLogoProps {
  className?: string;
}

/**
 * The ReGen mark.
 *
 * The outlines come from `regenMark`, a trace of the brand's own artwork. Nothing here draws the
 * mark, which is the point: what stood here before was a transcription written from a description,
 * and it had a malformed house, foliage where the brand draws a windmill, and both rules running
 * past the circle.
 *
 * It blooms rather than drawing itself on. A trace gives filled outlines, not the strokes a
 * draw-on animation needs, so the mark grows into place instead — and stays still for a reader who
 * has asked for less motion.
 */
export function RegenLogo({ className }: RegenLogoProps) {
  return (
    <svg
      viewBox={regenMark.viewBox}
      className={className}
      xmlns="http://www.w3.org/2000/svg"
      role="img"
      aria-label="ReGen Villages"
    >
      <style>{`
        .regen-mark {
          animation: regen-bloom 0.7s cubic-bezier(0.2, 0.8, 0.25, 1) both;
          transform-box: fill-box;
          transform-origin: 50% 50%;
        }
        @keyframes regen-bloom {
          from { opacity: 0; transform: scale(0.86); }
          to { opacity: 1; transform: scale(1); }
        }
        @media (prefers-reduced-motion: reduce) {
          .regen-mark { animation: none; }
        }
      `}</style>
      <g className="regen-mark" transform={regenMark.transform} fill={REGEN_TEAL} stroke="none">
        {regenMark.paths.map((d, index) => (
          <path key={index} d={d} />
        ))}
      </g>
    </svg>
  );
}
