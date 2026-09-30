import { useQuery } from "@tanstack/react-query";
import { MapPinIcon, SearchIcon, StoreIcon, Trash2Icon } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api, unwrap, type PharmacyEntry } from "@/api/client";
import { titleCase, titleCaseAddress } from "@/lib/format";
import { useDebounced } from "@/lib/hooks";

export const MAX_PHARMACIES = 5;

/** Find a pharmacy by name near a ZIP code. */
export function PharmacyPicker({
  zip,
  chosen,
  onAdd,
  large = false,
}: {
  zip: string;
  chosen: PharmacyEntry[];
  onAdd: (p: PharmacyEntry) => void;
  large?: boolean;
}) {
  const [query, setQuery] = useState("");
  const [radius, setRadius] = useState(15);
  const q = useDebounced(query.trim());
  const full = chosen.length >= MAX_PHARMACIES;
  const results = useQuery({
    queryKey: ["pharmacies", zip, q, radius],
    queryFn: () => unwrap(api.GET("/api/reference/pharmacies", { params: { query: { zip, q, radius } } })),
    enabled: /^\d{5}$/.test(zip) && q.length >= 2 && !full,
    staleTime: 60_000,
  });

  if (full) return <p className="text-sm text-muted-foreground">You've added the most pharmacies we can compare ({MAX_PHARMACIES}).</p>;

  return (
    <div className="grid gap-2">
      <div className="relative">
        <SearchIcon className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          aria-label="Search for a pharmacy"
          placeholder="Pharmacy name or street, like Walgreens or Coral Way"
          className={large ? "h-12 pl-9 text-base" : "pl-9"}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
      </div>
      {q.length >= 2 && (
        <div className="max-h-80 overflow-y-auto rounded-lg border">
          {results.isLoading && <p className="p-3 text-sm text-muted-foreground">Searching…</p>}
          {results.data?.length === 0 && (
            <div className="p-3 text-sm text-muted-foreground">
              No pharmacies match "{q}" within {radius} miles.{" "}
              {radius < 50 && (
                <Button variant="link" className="h-auto p-0" onClick={() => setRadius(50)}>
                  Search within 50 miles
                </Button>
              )}
            </div>
          )}
          {results.data?.map((p) => {
            const already = chosen.some((c) => c.npi === p.npi);
            const address = [p.address1, p.city].filter(Boolean).map((s) => titleCaseAddress(s!)).join(", ");
            return (
              <button
                key={p.npi}
                type="button"
                disabled={already}
                onClick={() => {
                  onAdd({ npi: p.npi, name: titleCase(p.name), address, zip: p.zip });
                  setQuery("");
                }}
                className="flex w-full items-start gap-3 border-b px-3 py-2.5 text-left last:border-b-0 hover:bg-muted focus-visible:bg-muted focus-visible:outline-none disabled:opacity-50"
              >
                <StoreIcon className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
                <span className="grid flex-1">
                  <span className="font-medium">{titleCase(p.name)}</span>
                  <span className="text-sm text-muted-foreground">{address}</span>
                  {!p.inNetwork && <span className="text-xs text-amber-700 dark:text-amber-400">Not in any Medicare drug plan's network</span>}
                </span>
                {p.miles != null && <span className="shrink-0 text-sm text-muted-foreground">{p.miles < 0.5 ? "< 1" : Math.round(p.miles)} mi</span>}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

export function PharmacyList({ pharmacies, onRemove }: { pharmacies: PharmacyEntry[]; onRemove?: (index: number) => void }) {
  if (pharmacies.length === 0) return <p className="text-sm text-muted-foreground">No pharmacies added yet.</p>;
  return (
    <ul className="grid gap-2">
      {pharmacies.map((p, i) => (
        <li key={p.npi} className="flex items-start justify-between gap-3 rounded-lg border px-3 py-2.5">
          <div className="flex items-start gap-3">
            <MapPinIcon className="mt-0.5 size-4 shrink-0 text-muted-foreground" />
            <div className="grid">
              <span className="font-medium">{p.name}</span>
              {p.address && <span className="text-sm text-muted-foreground">{p.address}</span>}
            </div>
          </div>
          {onRemove && (
            <Button variant="ghost" size="icon-sm" aria-label={`Remove ${p.name}`} onClick={() => onRemove(i)}>
              <Trash2Icon />
            </Button>
          )}
        </li>
      ))}
    </ul>
  );
}
