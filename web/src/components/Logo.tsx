import { cn } from "@/lib/utils";

/** The Kitchen Table mark: a table whose top is a two-tone pill. Same drawing as public/favicon.svg. */
export function LogoMark({ className }: { className?: string }) {
  return (
    <svg viewBox="-52 -47 104 104" aria-hidden="true" className={cn("size-7 shrink-0", className)}>
      <g className="fill-[#0E7C86] dark:fill-[#3BB8C3]">
        <path d="M-39-30h37v22h-37a11 11 0 0 1 0-22z" />
        <rect x="-32" y="-6" width="11" height="46" rx="3" />
        <rect x="21" y="-6" width="11" height="46" rx="3" />
      </g>
      <path d="M2-30h37a11 11 0 0 1 0 22H2z" className="fill-[#6BC4CB] dark:fill-[#9BDDE2]" />
    </svg>
  );
}

/** Mark plus wordmark. The wordmark takes the surrounding text colour. */
export function Logo({ className }: { className?: string }) {
  return (
    <span className={cn("inline-flex items-center gap-2 font-semibold tracking-tight", className)}>
      <LogoMark />
      Kitchen Table
    </span>
  );
}
