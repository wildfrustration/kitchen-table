import { useQuery } from "@tanstack/react-query";
import { PhoneIcon } from "lucide-react";
import type { ReactNode } from "react";
import { api, unwrap, type S } from "@/api/client";
import { phone } from "@/lib/format";

/** Every patient page carries the broker (the organization of record) and the CMS disclaimer. */
export function PatientShell({ broker, zip, children }: { broker?: S["PublicBroker"]; zip?: string; children: ReactNode }) {
  const disclaimer = useQuery({
    queryKey: ["disclaimer", broker?.slug, zip],
    queryFn: () =>
      unwrap(
        api.GET("/api/intake/{slug}/disclaimer", {
          params: { path: { slug: broker!.slug }, query: { zip: zip && /^\d{5}$/.test(zip) ? zip : undefined } },
        }),
      ),
    enabled: !!broker,
    staleTime: 5 * 60_000,
  });

  return (
    <div className="min-h-svh bg-muted/40">
      <header className="border-b bg-background">
        <div className="mx-auto flex max-w-2xl items-center justify-between gap-4 px-4 py-4">
          {broker ? (
            <div className="flex items-center gap-3">
              <div className="grid size-11 place-items-center rounded-full bg-primary text-lg font-semibold text-primary-foreground">
                {broker.displayName.charAt(0)}
              </div>
              <div className="grid">
                <span className="font-semibold">{broker.displayName}</span>
                <span className="text-sm text-muted-foreground">{broker.agencyName}</span>
              </div>
            </div>
          ) : (
            <span className="font-semibold">Kitchen Table</span>
          )}
          {broker?.phone && (
            <a href={`tel:${broker.phone.replace(/\D/g, "")}`} className="flex items-center gap-2 text-sm font-medium text-primary">
              <PhoneIcon className="size-4" />
              <span className="hidden sm:inline">{phone(broker.phone)}</span>
              <span className="sm:hidden">Call</span>
            </a>
          )}
        </div>
      </header>

      <main className="mx-auto max-w-2xl px-4 py-6 text-base">{children}</main>

      <footer className="mx-auto max-w-2xl px-4 pb-10 text-xs leading-relaxed text-muted-foreground">
        {disclaimer.data && <p>{disclaimer.data.text}</p>}
        <p className="mt-3">Your information is shared only with {broker?.displayName ?? "your broker"}. Kitchen Table is not connected with or endorsed by the U.S. government or the federal Medicare program.</p>
      </footer>
    </div>
  );
}
