const usd = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });
const usdWhole = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD", maximumFractionDigits: 0 });

export const money = (n: number | null | undefined) => (n == null ? "—" : usd.format(n));
export const moneyWhole = (n: number | null | undefined) => (n == null ? "—" : usdWhole.format(n));

export const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
export const MONTH_NAMES = [
  "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December",
];

export function relativeTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const minutes = Math.round((Date.now() - new Date(iso).getTime()) / 60000);
  if (minutes < 1) return "just now";
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} h ago`;
  const days = Math.round(hours / 24);
  return days < 30 ? `${days} d ago` : new Date(iso).toLocaleDateString();
}

export const formatDate = (iso: string) =>
  new Date(iso).toLocaleDateString("en-US", { year: "numeric", month: "short", day: "numeric" });

export function phone(p: string | null | undefined): string {
  if (!p) return "";
  const d = p.replace(/\D/g, "");
  return d.length === 10 ? `(${d.slice(0, 3)}) ${d.slice(3, 6)}-${d.slice(6)}` : p;
}

/** "WALGREENS #03316" → "Walgreens #03316"; keeps short all-caps words like CVS. */
export function titleCase(name: string): string {
  return name
    .split(" ")
    .map((w) => (w.length <= 3 && /^[A-Z&]+$/.test(w) ? w : w.charAt(0) + w.slice(1).toLowerCase()))
    .join(" ");
}

const DIRECTIONS = new Set(["N", "S", "E", "W", "NE", "NW", "SE", "SW", "US", "PO"]);

/** "2700 W FLAGLER ST STE 5" → "2700 W Flagler St Ste 5" (directions stay upper case). */
export function titleCaseAddress(address: string): string {
  return address
    .split(" ")
    .map((w) => (DIRECTIONS.has(w.toUpperCase()) ? w.toUpperCase() : /^\d/.test(w) ? w.toLowerCase() : w.charAt(0).toUpperCase() + w.slice(1).toLowerCase()))
    .join(" ");
}
