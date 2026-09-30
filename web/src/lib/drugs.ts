/** How a patient describes a dose, and how we turn it into a quantity per fill. */

export const FREQUENCIES: { label: string; perDay: number }[] = [
  { label: "Once a day", perDay: 1 },
  { label: "Twice a day", perDay: 2 },
  { label: "Three times a day", perDay: 3 },
  { label: "Four times a day", perDay: 4 },
  { label: "Every other day", perDay: 0.5 },
  { label: "Once a week", perDay: 1 / 7 },
  { label: "Once a month", perDay: 1 / 30 },
];

export function frequencyLabel(perDay: number): string {
  return FREQUENCIES.find((f) => Math.abs(f.perDay - perDay) < 0.001)?.label ?? `${round(perDay)} times a day`;
}

/** The unit a dose is counted in, from the RxNorm name ("… Oral Tablet", "3 ML … Pen Injector"). */
export function doseUnit(name: string): { singular: string; plural: string } {
  const n = name.toLowerCase();
  if (n.includes("capsule")) return { singular: "capsule", plural: "capsules" };
  if (n.includes("tablet")) return { singular: "tablet", plural: "tablets" };
  if (n.includes("inject") || n.includes("pen") || n.includes("solution") || n.includes(" ml ")) return { singular: "mL", plural: "mL" };
  if (n.includes("inhal")) return { singular: "puff", plural: "puffs" };
  if (n.includes("patch")) return { singular: "patch", plural: "patches" };
  return { singular: "unit", plural: "units" };
}

export const round = (n: number) => Math.round(n * 100) / 100;

export function quantityPerFill(unitsPerDose: number, dosesPerDay: number, daysSupply: number): number {
  return round(unitsPerDose * dosesPerDay * daysSupply);
}

/** "atorvastatin 20 MG Oral Tablet [Lipitor]" → { title: "Lipitor", detail: "atorvastatin 20 MG Oral Tablet" }. */
export function drugTitle(name: string): { title: string; detail: string | null } {
  const m = name.match(/^(.*)\s\[(.+)\]$/);
  return m ? { title: m[2], detail: m[1] } : { title: name, detail: null };
}
