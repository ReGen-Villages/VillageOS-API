interface RegenLogoProps {
  className?: string;
}

export function RegenLogo({ className }: RegenLogoProps) {
  const c = '#37c2aa';

  return (
    <svg viewBox="36 34 128 128" className={className} xmlns="http://www.w3.org/2000/svg">
      <defs>
        <clipPath id="regenClip">
          <circle cx="100" cy="97" r="54" />
        </clipPath>
        <style>{`
          .rd {
            stroke-dasharray: 1;
            stroke-dashoffset: 1;
            animation: rdraw 0.6s ease-out forwards;
          }
          @keyframes rdraw { to { stroke-dashoffset: 0; } }
          .rfi {
            opacity: 0;
            animation: rfade 0.3s ease-out forwards;
          }
          @keyframes rfade { to { opacity: 1; } }
          .rspin {
            transform-origin: 126px 74px;
            animation: rspinKf 7s linear infinite;
            animation-delay: 1.1s;
          }
          @keyframes rspinKf { to { transform: rotate(360deg); } }
          .rfloat {
            animation: rfloatKf 4s ease-in-out infinite;
            animation-delay: 1.2s;
          }
          @keyframes rfloatKf {
            0%, 100% { transform: translateX(0); }
            50% { transform: translateX(2px); }
          }
          .rsway {
            transform-origin: 120px 140px;
            animation: rswayKf 3.5s ease-in-out infinite;
            animation-delay: 1.3s;
          }
          @keyframes rswayKf {
            0%, 100% { transform: rotate(0deg); }
            33% { transform: rotate(2deg); }
            66% { transform: rotate(-2deg); }
          }
        `}</style>
      </defs>

      <g fill="none" stroke={c} strokeLinecap="round" strokeLinejoin="round">

        {/* === STRUCTURE === */}
        <circle cx="100" cy="97" r="55" strokeWidth="2.7" pathLength={1}
          className="rd" style={{ animationDelay: '0.05s', animationDuration: '0.9s' }} />
        <line x1="40" y1="97" x2="160" y2="97" strokeWidth="2.7" pathLength={1}
          className="rd" style={{ animationDelay: '0s' }} />
        <line x1="100" y1="37" x2="100" y2="157" strokeWidth="2.7" pathLength={1}
          className="rd" style={{ animationDelay: '0s' }} />

        {/* === ICONS (clipped to circle) === */}
        <g clipPath="url(#regenClip)">

          {/* ─── TOP-LEFT: Pseudo-3D House ─── */}
          {/* Front face (gable): left wall, roof peak, right slope */}
          <path d="M 66,97 L 66,74 L 73,64 L 97,77" strokeWidth="2.2" pathLength={1}
            className="rd" style={{ animationDelay: '0.2s' }} />
          {/* Back face visible through 3D depth */}
          <path d="M 73,87 L 73,78 L 80,71 L 88,80" strokeWidth="2.2" pathLength={1}
            className="rd" style={{ animationDelay: '0.3s' }} />
          {/* 3D connecting lines: ridge, side walls, eave */}
          <line x1="73" y1="64" x2="80" y2="71" strokeWidth="2.2" pathLength={1}
            className="rd" style={{ animationDelay: '0.35s' }} />
          <line x1="66" y1="74" x2="73" y2="78" strokeWidth="2.2" pathLength={1}
            className="rd" style={{ animationDelay: '0.35s' }} />
          <line x1="66" y1="97" x2="73" y2="87" strokeWidth="2.2" pathLength={1}
            className="rd" style={{ animationDelay: '0.35s' }} />
          <line x1="97" y1="77" x2="88" y2="80" strokeWidth="2.2" pathLength={1}
            className="rd" style={{ animationDelay: '0.35s' }} />

          {/* ─── TOP-RIGHT: Windmill ─── */}
          {/* Stem */}
          <line x1="126" y1="74" x2="126" y2="97" strokeWidth="2.0" pathLength={1}
            className="rd" style={{ animationDelay: '0.1s' }} />

          {/* Hub */}
          <circle cx="126" cy="74" r="2.5" fill={c} stroke="none"
            className="rfi" style={{ animationDelay: '0.2s' }} />

          {/* 3 leaf-shaped blades (rotating) */}
          <g className="rspin" strokeWidth="2.0">
            {/* Blade upper-left */}
            <path d="M 126,74 Q 112,72 114,62 Q 128,63 126,74 Z" pathLength={1}
              className="rd" style={{ animationDelay: '0.15s' }} />
            {/* Blade right */}
            <path d="M 126,74 Q 134,66 140,76 Q 134,83 126,74 Z" pathLength={1}
              className="rd" style={{ animationDelay: '0.2s' }} />
            {/* Blade lower-left */}
            <path d="M 126,74 Q 127,87 116,87 Q 114,76 126,74 Z" pathLength={1}
              className="rd" style={{ animationDelay: '0.25s' }} />
          </g>

          {/* Clouds (floating) */}
          <g className="rfloat">
            {/* Large cloud — 3 bumps */}
            <path d="M 105,62 Q 105,50 109,48 Q 113,44 116,50 Q 120,44 122,53 Q 125,62 122,62"
              strokeWidth="2.0" pathLength={1}
              className="rd" style={{ animationDelay: '0.3s' }} />
            {/* Small cloud */}
            <path d="M 113,67 Q 113,62 117,61 Q 121,58 124,62 Q 126,67 124,67"
              strokeWidth="1.8" pathLength={1}
              className="rd" style={{ animationDelay: '0.35s' }} />
            {/* Right-edge cloud — partially clipped by circle, left tip only */}
            <path d="M 136,69 Q 136,64 140,63 Q 144,58 147,64 Q 150,69 147,69"
              strokeWidth="1.8" pathLength={1}
              className="rd" style={{ animationDelay: '0.4s' }} />
          </g>

          {/* ─── BOTTOM-LEFT: Circuit / Network ─── */}
          <g strokeWidth="2.2">
            <path d="M 98,103 L 87,103 L 83,108" pathLength={1}
              className="rd" style={{ animationDelay: '0.15s' }} />
            <path d="M 83,108 L 72,108" pathLength={1}
              className="rd" style={{ animationDelay: '0.25s' }} />
            <path d="M 83,108 L 76,119" pathLength={1}
              className="rd" style={{ animationDelay: '0.25s' }} />
            <path d="M 76,119 L 65,119" pathLength={1}
              className="rd" style={{ animationDelay: '0.35s' }} />
            <path d="M 65,119 L 59,133" pathLength={1}
              className="rd" style={{ animationDelay: '0.4s' }} />
            <path d="M 76,119 L 70,136" pathLength={1}
              className="rd" style={{ animationDelay: '0.4s' }} />
          </g>

          {/* Circuit nodes */}
          <g fill={c} stroke="none">
            <circle cx="72" cy="108" r="3.5"
              className="rfi" style={{ animationDelay: '0.3s' }} />
            <circle cx="65" cy="119" r="3.5"
              className="rfi" style={{ animationDelay: '0.4s' }} />
            <circle cx="59" cy="133" r="3.5"
              className="rfi" style={{ animationDelay: '0.5s' }} />
            <circle cx="70" cy="136" r="3.5"
              className="rfi" style={{ animationDelay: '0.5s' }} />
          </g>

          {/* ─── BOTTOM-RIGHT: Plant / Agriculture ─── */}
          <g className="rsway">
            {/* Wave / hill */}
            <path d="M 103,140 C 110,148 118,126 128,136 C 136,126 148,126 155,140"
              strokeWidth="2.2" pathLength={1}
              className="rd" style={{ animationDelay: '0.25s' }} />
            {/* Stem */}
            <path d="M 120,136 C 119,128 118,120 119,108"
              strokeWidth="2.0" pathLength={1}
              className="rd" style={{ animationDelay: '0.35s' }} />
            {/* Leaf right (low) */}
            <path d="M 120,130 Q 128,124 133,130 Q 128,135 120,130 Z"
              strokeWidth="1.8" pathLength={1}
              className="rd" style={{ animationDelay: '0.45s' }} />
            {/* Leaf left (mid) */}
            <path d="M 119,121 Q 110,115 106,121 Q 110,126 119,121 Z"
              strokeWidth="1.8" pathLength={1}
              className="rd" style={{ animationDelay: '0.5s' }} />
            {/* Leaf right (high) */}
            <path d="M 119,113 Q 127,107 131,113 Q 127,118 119,113 Z"
              strokeWidth="1.8" pathLength={1}
              className="rd" style={{ animationDelay: '0.55s' }} />
            {/* Bud / droplet at top */}
            <path d="M 119,109 Q 117,105 119,102 Q 121,105 119,109 Z"
              strokeWidth="1.5" pathLength={1}
              className="rd" style={{ animationDelay: '0.6s' }} />
          </g>

        </g>
      </g>
    </svg>
  );
}
