import { useQuery } from "@tanstack/react-query";
import { Input } from "@/components/ui/input";
import { api, unwrap } from "@/api/client";
import { Field, NativeSelect } from "./Field";

/** ZIP code, plus a county choice when the ZIP crosses county lines (plans are sold by county/region). */
export function ZipCounty({
  zip,
  countyCode,
  onChange,
  error,
}: {
  zip: string;
  countyCode: string | null;
  onChange: (zip: string, countyCode: string | null) => void;
  error?: string;
}) {
  const valid = /^\d{5}$/.test(zip);
  const counties = useQuery({
    queryKey: ["zip", zip],
    queryFn: () => unwrap(api.GET("/api/reference/zip/{zip}", { params: { path: { zip } } })),
    enabled: valid,
    staleTime: Infinity,
  });
  const list = counties.data ?? [];

  return (
    <div className="grid gap-4 sm:grid-cols-2">
      <Field label="ZIP code" htmlFor="zip" error={error ?? (valid && counties.isSuccess && list.length === 0 ? "We don't recognize that ZIP code." : undefined)}>
        <Input
          id="zip"
          inputMode="numeric"
          autoComplete="postal-code"
          maxLength={5}
          className="h-10 text-base"
          value={zip}
          onChange={(e) => onChange(e.target.value.replace(/\D/g, ""), null)}
        />
      </Field>
      {list.length > 1 && (
        <Field label="County" htmlFor="county" hint="Your ZIP code is in more than one county.">
          <NativeSelect id="county" value={countyCode ?? list[0].countyCode} onChange={(e) => onChange(zip, e.target.value)}>
            {list.map((c) => (
              <option key={c.countyCode} value={c.countyCode}>
                {c.countyName}
              </option>
            ))}
          </NativeSelect>
        </Field>
      )}
    </div>
  );
}
