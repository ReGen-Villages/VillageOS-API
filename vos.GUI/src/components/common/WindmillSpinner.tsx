export function WindmillSpinner({ size = 20 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      className="animate-spin"
      style={{ animationDuration: '1.5s' }}
    >
      {/* Four blades radiating from center */}
      <path d="M12 2 C12 2 14 7 14 10 C14 11.1 13.1 12 12 12 C12 12 12 6 12 2Z" fill="#a1a1aa" opacity="0.9" />
      <path d="M22 12 C22 12 17 14 14 14 C12.9 14 12 13.1 12 12 C12 12 18 12 22 12Z" fill="#a1a1aa" opacity="0.7" />
      <path d="M12 22 C12 22 10 17 10 14 C10 12.9 10.9 12 12 12 C12 12 12 18 12 22Z" fill="#a1a1aa" opacity="0.5" />
      <path d="M2 12 C2 12 7 10 10 10 C11.1 10 12 10.9 12 12 C12 12 6 12 2 12Z" fill="#a1a1aa" opacity="0.3" />
      {/* Center hub */}
      <circle cx="12" cy="12" r="1.5" fill="#71717a" />
    </svg>
  );
}
