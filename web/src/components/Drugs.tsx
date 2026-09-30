import { useQuery } from "@tanstack/react-query";
import { PillIcon, SearchIcon, Trash2Icon } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { api, unwrap, type DrugEntry, type S } from "@/api/client";
import { doseUnit, drugTitle, FREQUENCIES, frequencyLabel, quantityPerFill } from "@/lib/drugs";
import { useDebounced } from "@/lib/hooks";
import { Field, NativeSelect } from "./Field";

type Found = S["DrugSearchResult"];

/** Search a drug, pick generic or brand, say how it's taken, and add it to the list. */
export function DrugPicker({ onAdd, large = false }: { onAdd: (drug: DrugEntry) => void; large?: boolean }) {
  const [query, setQuery] = useState("");
  const [picked, setPicked] = useState<Found | null>(null);
  const q = useDebounced(query.trim());
  const results = useQuery({
    queryKey: ["drugs", q],
    queryFn: () => unwrap(api.GET("/api/reference/drugs", { params: { query: { q } } })),
    enabled: q.length >= 2 && !picked,
    staleTime: 60_000,
  });

  if (picked) {
    return (
      <DoseForm
        drug={picked}
        large={large}
        onCancel={() => setPicked(null)}
        onAdd={(d) => {
          onAdd(d);
          setPicked(null);
          setQuery("");
        }}
      />
    );
  }

  return (
    <div className="grid gap-2">
      <div className="relative">
        <SearchIcon className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          aria-label="Search for a drug"
          placeholder="Type a drug name, like atorvastatin or Eliquis"
          className={large ? "h-12 pl-9 text-base" : "pl-9"}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
      </div>
      {q.length >= 2 && (
        <div className="max-h-80 overflow-y-auto rounded-lg border">
          {results.isLoading && <p className="p-3 text-sm text-muted-foreground">Searching…</p>}
          {results.data?.length === 0 && <p className="p-3 text-sm text-muted-foreground">No drugs match "{q}". Check the spelling or try the generic name.</p>}
          {results.data?.map((r) => {
            const { title, detail } = drugTitle(r.name);
            return (
              <button
                key={r.rxcui}
                type="button"
                onClick={() => setPicked(r)}
                className="flex w-full items-start gap-3 border-b px-3 py-2.5 text-left last:border-b-0 hover:bg-muted focus-visible:bg-muted focus-visible:outline-none"
              >
                <PillIcon className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
                <span className="grid">
                  <span className="font-medium">{title}</span>
                  {detail && <span className="text-sm text-muted-foreground">{detail}</span>}
                  {!r.onAnyFormulary && !r.genericRxcui && (
                    <span className="text-xs text-amber-700 dark:text-amber-400">Not on any drug plan's list</span>
                  )}
                </span>
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

function DoseForm({ drug, large, onAdd, onCancel }: { drug: Found; large: boolean; onAdd: (d: DrugEntry) => void; onCancel: () => void }) {
  const hasGeneric = drug.isBrand && drug.genericRxcui && drug.genericName;
  const [version, setVersion] = useState<"generic" | "brand">(hasGeneric ? "generic" : "brand");
  const [units, setUnits] = useState("1");
  const [perDay, setPerDay] = useState(1);
  const [days, setDays] = useState(30);

  const chosen = version === "generic" && hasGeneric ? { rxcui: drug.genericRxcui!, name: drug.genericName! } : { rxcui: drug.rxcui, name: drug.name };
  const unit = doseUnit(chosen.name);
  const unitsNumber = Number(units);
  const valid = unitsNumber > 0;

  return (
    <div className="grid gap-4 rounded-lg border bg-muted/30 p-4">
      <p className="font-medium">{drugTitle(drug.name).title}</p>

      {hasGeneric && (
        <Field label="Generic or brand?" hint="Generics usually cost much less. Choose brand only if you need it.">
          <RadioGroup value={version} onValueChange={(v) => setVersion(v as "generic" | "brand")} className="gap-2">
            <label className="flex items-start gap-2">
              <RadioGroupItem value="generic" className="mt-1" />
              <span>
                Generic <span className="text-muted-foreground">— {drug.genericName}</span>
              </span>
            </label>
            <label className="flex items-start gap-2">
              <RadioGroupItem value="brand" className="mt-1" />
              <span>
                Brand <span className="text-muted-foreground">— {drug.name}</span>
              </span>
            </label>
          </RadioGroup>
        </Field>
      )}

      <div className="grid gap-4 sm:grid-cols-3">
        <Field label={`How many ${unit.plural} each time?`} htmlFor="units">
          <Input
            id="units"
            inputMode="decimal"
            className={large ? "h-11 text-base" : undefined}
            value={units}
            onChange={(e) => setUnits(e.target.value.replace(/[^\d.]/g, ""))}
          />
        </Field>
        <Field label="How often?" htmlFor="often">
          <NativeSelect id="often" value={perDay} onChange={(e) => setPerDay(Number(e.target.value))}>
            {FREQUENCIES.map((f) => (
              <option key={f.label} value={f.perDay}>
                {f.label}
              </option>
            ))}
          </NativeSelect>
        </Field>
        <Field label="Supply per refill" htmlFor="days">
          <NativeSelect id="days" value={days} onChange={(e) => setDays(Number(e.target.value))}>
            <option value={30}>30 days</option>
            <option value={60}>60 days</option>
            <option value={90}>90 days</option>
          </NativeSelect>
        </Field>
      </div>
      {valid && (
        <p className="text-sm text-muted-foreground">
          That's {quantityPerFill(unitsNumber, perDay, days)} {unit.plural} every {days} days.
        </p>
      )}

      <div className="flex gap-2">
        <Button
          size={large ? "lg" : "default"}
          disabled={!valid}
          onClick={() => onAdd({ rxcui: chosen.rxcui, name: chosen.name, unitsPerDose: unitsNumber, dosesPerDay: perDay, daysSupply: days })}
        >
          Add this drug
        </Button>
        <Button size={large ? "lg" : "default"} variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </div>
  );
}

/** The drugs entered so far. */
export function DrugList({ drugs, onRemove }: { drugs: DrugEntry[]; onRemove?: (index: number) => void }) {
  if (drugs.length === 0) return <p className="text-sm text-muted-foreground">No drugs added yet.</p>;
  return (
    <ul className="grid gap-2">
      {drugs.map((d, i) => {
        const { title, detail } = drugTitle(d.name);
        const unit = doseUnit(d.name);
        return (
          <li key={`${d.rxcui}-${i}`} className="flex items-start justify-between gap-3 rounded-lg border px-3 py-2.5">
            <div className="grid">
              <span className="font-medium">{title}</span>
              {detail && <span className="text-sm text-muted-foreground">{detail}</span>}
              <span className="text-sm text-muted-foreground">
                {d.unitsPerDose} {d.unitsPerDose === 1 ? unit.singular : unit.plural}, {frequencyLabel(d.dosesPerDay).toLowerCase()} ·{" "}
                {quantityPerFill(d.unitsPerDose, d.dosesPerDay, d.daysSupply)} per {d.daysSupply} days
              </span>
            </div>
            {onRemove && (
              <Button variant="ghost" size="icon-sm" aria-label={`Remove ${title}`} onClick={() => onRemove(i)}>
                <Trash2Icon />
              </Button>
            )}
          </li>
        );
      })}
    </ul>
  );
}
