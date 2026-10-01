import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CopyIcon, PrinterIcon } from "lucide-react";
import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { api, emptyForm, unwrap } from "@/api/client";
import { drugTitle } from "@/lib/drugs";
import { formatDate, money, moneyWhole, MONTH_NAMES } from "@/lib/format";
import { ClientEditor } from "./ClientEditor";
import { publicLink } from "./QueuePage";
import { useMe } from "./Shell";

export function NewClientPage() {
  const navigate = useNavigate();
  const qc = useQueryClient();
  const create = useMutation({
    mutationFn: ({ form, level }: { form: ReturnType<typeof emptyForm>; level: number }) =>
      unwrap(api.POST("/api/clients", { body: { form, extraHelpLevel: level } })),
    onSuccess: (c) => {
      qc.invalidateQueries({ queryKey: ["clients"] });
      navigate(`/app/clients/${c.id}`);
    },
  });
  return (
    <Card className="mx-auto max-w-3xl">
      <CardHeader>
        <CardTitle>New client</CardTitle>
        <CardDescription>Enter a client you spoke with. Get their permission to keep their prescription list before you save it.</CardDescription>
      </CardHeader>
      <CardContent>
        <ClientEditor
          initial={emptyForm()}
          initialLevel={0}
          saving={create.isPending}
          error={create.error?.message}
          saveLabel="Save client"
          onCancel={() => navigate("/app")}
          onSave={(form, level) => create.mutate({ form, level })}
        />
      </CardContent>
    </Card>
  );
}

export function SettingsPage() {
  const me = useMe();
  const qc = useQueryClient();
  const carriers = useQuery({ queryKey: ["carriers"], queryFn: () => unwrap(api.GET("/api/carriers")) });
  const [selected, setSelected] = useState<Set<string>>(new Set());
  useEffect(() => {
    if (carriers.data) setSelected(new Set(carriers.data.filter((c) => c.appointed).map((c) => c.parentOrganization)));
  }, [carriers.data]);
  const save = useMutation({
    mutationFn: () => unwrap(api.PUT("/api/carriers", { body: { parentOrganizations: [...selected] } })),
    onSuccess: () => {
      toast.success("Carriers saved.");
      qc.invalidateQueries({ queryKey: ["carriers"] });
      qc.invalidateQueries({ queryKey: ["quote"] });
    },
  });

  return (
    <div className="grid max-w-3xl gap-5">
      <h1 className="text-2xl font-semibold">Settings</h1>
      {me.data && (
        <Card>
          <CardHeader>
            <CardTitle>Your public intake link</CardTitle>
            <CardDescription>Put it on your website, email signature or social media. Anyone who fills it in lands in your client list.</CardDescription>
          </CardHeader>
          <CardContent className="flex gap-2">
            <Input readOnly value={publicLink(me.data.publicSlug)} />
            <Button
              variant="outline"
              onClick={() => {
                navigator.clipboard.writeText(publicLink(me.data!.publicSlug));
                toast.success("Link copied.");
              }}
            >
              <CopyIcon /> Copy
            </Button>
          </CardContent>
        </Card>
      )}
      <Card>
        <CardHeader>
          <CardTitle>Carriers you're appointed with</CardTitle>
          <CardDescription>
            Plans from other carriers show greyed out in quotes. These also set the numbers in the disclaimer your clients see.
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-2">
          {carriers.data?.map((c) => (
            <label key={c.parentOrganization} className="flex items-center justify-between gap-3 rounded-md border px-3 py-2 text-sm">
              <span className="flex items-center gap-3">
                <Checkbox
                  checked={selected.has(c.parentOrganization)}
                  onCheckedChange={(on) =>
                    setSelected((s) => {
                      const next = new Set(s);
                      if (on) next.add(c.parentOrganization);
                      else next.delete(c.parentOrganization);
                      return next;
                    })
                  }
                />
                {c.parentOrganization}
              </span>
              <span className="text-muted-foreground">{c.plans} {c.plans === 1 ? "plan" : "plans"}</span>
            </label>
          ))}
          <Button className="mt-2 justify-self-start" disabled={save.isPending} onClick={() => save.mutate()}>
            Save carriers
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}

/** /app/snapshots/:id — the printable comparison the broker hands the client. */
export function SnapshotPage() {
  const { id = "" } = useParams();
  const snapshot = useQuery({ queryKey: ["snapshot", id], queryFn: () => unwrap(api.GET("/api/snapshots/{id}", { params: { path: { id } } })) });
  if (!snapshot.data) return <Skeleton className="m-8 h-96" />;
  const s = snapshot.data;
  const q = s.quote;
  const top = q.plans.filter((p) => p.appointed).slice(0, 5);

  return (
    <div className="mx-auto max-w-4xl bg-white p-8 text-sm text-neutral-900 print:p-0">
      <div className="mb-6 flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">Medicare drug plan comparison</h1>
          <p>
            Prepared for {s.clientName} by {s.brokerName}, {s.agencyName}
            {s.brokerPhone ? ` · ${s.brokerPhone}` : ""}
          </p>
          <p className="text-neutral-500">
            {formatDate(s.createdAt)} · {q.planYear} plans in {q.countyName} · {MONTH_NAMES[q.startMonth - 1]}–December
          </p>
        </div>
        <Button variant="outline" className="print:hidden" onClick={() => window.print()}>
          <PrinterIcon /> Print
        </Button>
      </div>

      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Plan</TableHead>
            <TableHead className="text-right">Premium / mo</TableHead>
            <TableHead className="text-right">Drug costs</TableHead>
            <TableHead className="text-right">Estimated total</TableHead>
            <TableHead className="text-right">Deductible</TableHead>
            <TableHead>Stars</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {top.map((p) => (
            <TableRow key={p.key}>
              <TableCell>
                <span className="font-medium">{p.name}</span>
                <span className="block text-xs text-neutral-500">
                  {p.organization} · {p.key}
                </span>
              </TableCell>
              <TableCell className="text-right">{money(p.monthlyPremium)}</TableCell>
              <TableCell className="text-right">{moneyWhole(p.drugCost)}</TableCell>
              <TableCell className="text-right font-semibold">{moneyWhole(p.estimatedAnnualCost)}</TableCell>
              <TableCell className="text-right">{moneyWhole(p.deductible)}</TableCell>
              <TableCell>{p.starRating ?? "—"}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

      {top.slice(0, 3).map((p) => (
        <section key={p.key} className="mt-6 break-inside-avoid">
          <h2 className="font-semibold">{p.name}</h2>
          <ul className="mt-1 grid gap-0.5">
            {p.drugs.map((d) => (
              <li key={d.rxcui} className="flex justify-between gap-4">
                <span>
                  {drugTitle(d.name).title}
                  {d.status !== "Covered" && <span className="text-red-700"> — not covered</span>}
                  {(d.priorAuth || d.stepTherapy) && <span className="text-neutral-500"> — needs {d.priorAuth ? "prior authorization" : "step therapy"}</span>}
                </span>
                <span>{d.priceUnavailable ? "cash price" : money(d.memberCost)}</span>
              </li>
            ))}
          </ul>
          {p.pharmacies.length > 0 && (
            <p className="mt-1 text-neutral-500">
              Pharmacy: {p.pharmacies.map((ph) => `${ph.name} (${ph.inNetwork ? (ph.preferred ? "preferred" : "standard") : "out of network"})`).join(", ")}
            </p>
          )}
        </section>
      ))}

      {q.warnings.length > 0 && (
        <div className="mt-6 grid gap-1 rounded border border-amber-300 bg-amber-50 p-3 text-xs">
          {q.warnings.map((w) => (
            <p key={w}>{w}</p>
          ))}
        </div>
      )}

      <footer className="mt-8 grid gap-2 border-t pt-4 text-xs text-neutral-500">
        <p>
          Costs are estimates based on CMS public data ({q.dataRelease}) and the prescriptions, quantities and pharmacies listed. Actual costs depend on the
          plan, pharmacy and prices at the time you fill each prescription. Plan names and benefits are from CMS; confirm details with the plan before
          enrolling.
        </p>
        <p>{s.disclaimer.text}</p>
        <p>Reference {s.id}</p>
      </footer>
    </div>
  );
}
