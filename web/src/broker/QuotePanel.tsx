import { useMutation, useQuery } from "@tanstack/react-query";
import { ChevronDownIcon, ChevronRightIcon, PrinterIcon, StarIcon } from "lucide-react";
import { Fragment, useMemo, useState } from "react";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { api, unwrap, type S } from "@/api/client";
import { NativeSelect } from "@/components/Field";
import { drugTitle } from "@/lib/drugs";
import { money, moneyWhole, MONTH_NAMES, MONTHS } from "@/lib/format";
import { cn } from "@/lib/utils";

type Plan = S["QuotePlan"];

const PHARMACY_LABEL: Record<S["PharmacyType"], string> = {
  PreferredRetail: "Preferred retail",
  StandardRetail: "Standard retail",
  PreferredMail: "Preferred mail",
  StandardMail: "Standard mail",
};

const BENEFIT_LABEL: Record<string, string> = {
  EnhancedAlternative: "Enhanced benefit",
  DefinedStandard: "Standard benefit",
  ActuariallyEquivalent: "Standard-equivalent benefit",
  BasicAlternative: "Basic alternative benefit",
};

const STATUS_LABEL: Record<S["CoverageStatus"], string> = {
  Covered: "Covered",
  CoveredSupplemental: "Covered (supplemental)",
  NotOnFormulary: "Not covered",
  DaysSupplyNotCovered: "Supply not covered",
};

/** The ranked plan list for one client, with the broker's filters. */
export function QuotePanel({ clientId, hasPharmacies }: { clientId: string; hasPharmacies: boolean }) {
  const [startMonth, setStartMonth] = useState<number | null>(null);
  const [allCovered, setAllCovered] = useState(false);
  const [keepPharmacy, setKeepPharmacy] = useState(false);
  const [appointedOnly, setAppointedOnly] = useState(false);
  const [minStars, setMinStars] = useState(0);
  const [open, setOpen] = useState<string | null>(null);

  const quote = useQuery({
    queryKey: ["quote", clientId, startMonth],
    queryFn: () =>
      unwrap(api.POST("/api/clients/{id}/quote", { params: { path: { id: clientId } }, body: { planYear: null, startMonth } })),
  });
  const present = useMutation({
    mutationFn: () =>
      unwrap(api.POST("/api/clients/{id}/snapshots", { params: { path: { id: clientId } }, body: { planYear: null, startMonth } })),
    onSuccess: (s) => window.open(`/app/snapshots/${s.id}`, "_blank"),
  });

  const plans = useMemo(
    () =>
      (quote.data?.plans ?? []).filter(
        (p) =>
          (!allCovered || p.allDrugsCovered) &&
          (!keepPharmacy || p.pharmacyInNetwork !== false) &&
          (!appointedOnly || p.appointed) &&
          (minStars === 0 || Number(p.starRating) >= minStars),
      ),
    [quote.data, allCovered, keepPharmacy, appointedOnly, minStars],
  );

  const q = quote.data;
  return (
    <Card>
      <CardHeader className="grid gap-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <CardTitle>Part D plans</CardTitle>
            {q && (
              <p className="text-sm text-muted-foreground">
                {q.planYear} plans in {q.countyName} · {MONTH_NAMES[q.startMonth - 1]}–December · estimates from CMS data {q.dataRelease}
              </p>
            )}
          </div>
          <div className="flex items-center gap-2">
            <label className="flex items-center gap-2 text-sm">
              Coverage starts
              <NativeSelect className="h-8 w-32" value={startMonth ?? q?.startMonth ?? 1} onChange={(e) => setStartMonth(Number(e.target.value))}>
                {MONTH_NAMES.map((m, i) => (
                  <option key={m} value={i + 1}>
                    {m}
                  </option>
                ))}
              </NativeSelect>
            </label>
            <Button variant="outline" disabled={!q || present.isPending} onClick={() => present.mutate()}>
              <PrinterIcon /> Present / print
            </Button>
          </div>
        </div>
        <div className="flex flex-wrap gap-x-5 gap-y-2 text-sm">
          <Toggle label="Covers every drug" checked={allCovered} onChange={setAllCovered} />
          <Toggle label="Client's pharmacy in network" checked={keepPharmacy} onChange={setKeepPharmacy} disabled={!hasPharmacies} />
          <Toggle label="My carriers only" checked={appointedOnly} onChange={setAppointedOnly} />
          <label className="flex items-center gap-2">
            Stars
            <NativeSelect className="h-7 w-24" value={minStars} onChange={(e) => setMinStars(Number(e.target.value))}>
              <option value={0}>Any</option>
              <option value={3}>3+</option>
              <option value={3.5}>3.5+</option>
              <option value={4}>4+</option>
            </NativeSelect>
          </label>
        </div>
        {q?.warnings.map((w) => (
          <Alert key={w}>
            <AlertDescription>{w}</AlertDescription>
          </Alert>
        ))}
      </CardHeader>
      <CardContent className="px-0">
        {quote.isLoading && <Skeleton className="mx-4 h-64" />}
        {quote.isError && <p className="px-4 text-sm text-destructive">{quote.error.message}</p>}
        {q && (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="w-8 pl-4" />
                <TableHead>Plan</TableHead>
                <TableHead className="text-right">Premium / mo</TableHead>
                <TableHead className="text-right">Drug costs</TableHead>
                <TableHead className="text-right">Estimated total</TableHead>
                <TableHead className="text-right">Deductible</TableHead>
                <TableHead className="pr-4">Pharmacy</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {plans.length === 0 && (
                <TableRow>
                  <TableCell colSpan={7} className="py-8 text-center text-muted-foreground">
                    No plans match these filters.
                  </TableCell>
                </TableRow>
              )}
              {plans.map((p) => (
                <Fragment key={p.key}>
                  <TableRow className={cn("cursor-pointer", !p.appointed && "opacity-55")} onClick={() => setOpen(open === p.key ? null : p.key)}>
                    <TableCell className="pl-4">{open === p.key ? <ChevronDownIcon className="size-4" /> : <ChevronRightIcon className="size-4" />}</TableCell>
                    <TableCell className="whitespace-normal">
                      <div className="grid gap-1">
                        <span className="font-medium">{p.name}</span>
                        <span className="flex items-center gap-2 text-xs text-muted-foreground">
                          {p.organization}
                          {p.starRating && (
                            <span className="flex items-center gap-0.5">
                              <StarIcon className="size-3 fill-current" /> {p.starRating}
                            </span>
                          )}
                          {!p.appointed && (
                            <Tooltip>
                              <TooltipTrigger render={<span className="underline decoration-dotted" />}>not appointed</TooltipTrigger>
                              <TooltipContent>You haven't marked {p.parentOrganization} as a carrier you're appointed with.</TooltipContent>
                            </Tooltip>
                          )}
                        </span>
                        <CoverageBadges plan={p} />
                      </div>
                    </TableCell>
                    <TableCell className="text-right tabular-nums">{money(p.monthlyPremium)}</TableCell>
                    <TableCell className="text-right tabular-nums">{moneyWhole(p.drugCost)}</TableCell>
                    <TableCell className="text-right font-semibold tabular-nums">{moneyWhole(p.estimatedAnnualCost)}</TableCell>
                    <TableCell className="text-right tabular-nums">{moneyWhole(p.deductible)}</TableCell>
                    <TableCell className="pr-4">
                      <PharmacyBadge plan={p} />
                    </TableCell>
                  </TableRow>
                  {open === p.key && (
                    <TableRow className="hover:bg-transparent">
                      <TableCell colSpan={7} className="bg-muted/30 px-4 py-4 whitespace-normal">
                        <PlanDetail plan={p} startMonth={q.startMonth} />
                      </TableCell>
                    </TableRow>
                  )}
                </Fragment>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}

function Toggle({ label, checked, onChange, disabled }: { label: string; checked: boolean; onChange: (v: boolean) => void; disabled?: boolean }) {
  return (
    <label className={cn("flex items-center gap-2", disabled && "opacity-50")}>
      <Switch checked={checked} onCheckedChange={onChange} disabled={disabled} /> {label}
    </label>
  );
}

function PharmacyBadge({ plan }: { plan: Plan }) {
  if (plan.pharmacyInNetwork === false) return <Badge variant="destructive">Out of network</Badge>;
  const chosen = plan.pharmacies.find((p) => p.npi === plan.pricedAtNpi);
  if (chosen) return <Badge variant={chosen.preferred ? "default" : "secondary"}>{chosen.preferred ? "Preferred" : "Standard"}</Badge>;
  return <span className="text-xs text-muted-foreground">{PHARMACY_LABEL[plan.pricedAt]}</span>;
}

function CoverageBadges({ plan }: { plan: Plan }) {
  const notCovered = plan.drugs.filter((d) => d.status === "NotOnFormulary" || d.status === "DaysSupplyNotCovered").length;
  const hurdles = plan.drugs.filter((d) => d.priorAuth || d.stepTherapy || d.exceedsQuantityLimit).length;
  return (
    <div className="flex flex-wrap gap-1">
      {notCovered === 0 ? <Badge variant="outline">Covers every drug</Badge> : <Badge variant="destructive">{notCovered} not covered</Badge>}
      {hurdles > 0 && <Badge variant="secondary">{hurdles} with restrictions</Badge>}
    </div>
  );
}

function PlanDetail({ plan, startMonth }: { plan: Plan; startMonth: number }) {
  const max = Math.max(...plan.byMonth, 1);
  return (
    <div className="grid gap-5 xl:grid-cols-[1fr_240px]">
      <div className="grid gap-3">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Drug</TableHead>
              <TableHead>Tier</TableHead>
              <TableHead>Status</TableHead>
              <TableHead className="text-right">Full price / fill</TableHead>
              <TableHead className="text-right">Fills</TableHead>
              <TableHead className="text-right">Client pays</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {plan.drugs.map((d) => (
              <TableRow key={d.rxcui}>
                <TableCell className="whitespace-normal">
                  <div className="grid">
                    <span>{drugTitle(d.name).title}</span>
                    <span className="text-xs text-muted-foreground">
                      {[d.priorAuth && "prior authorization", d.stepTherapy && "step therapy", d.exceedsQuantityLimit && "over quantity limit", d.priceEstimated && !d.priceUnavailable && "price estimated", d.priceUnavailable && "not in the total"]
                        .filter(Boolean)
                        .join(" · ")}
                    </span>
                  </div>
                </TableCell>
                <TableCell>{d.tier ?? "—"}</TableCell>
                <TableCell>
                  <Badge variant={d.status === "Covered" ? "outline" : "destructive"}>{STATUS_LABEL[d.status]}</Badge>
                </TableCell>
                <TableCell className="text-right tabular-nums">
                  {d.priceUnavailable ? <span className="text-muted-foreground">No price data</span> : money(d.fullCostPerFill)}
                </TableCell>
                <TableCell className="text-right tabular-nums">{d.fills}</TableCell>
                <TableCell className="text-right tabular-nums">
                  {d.priceUnavailable ? <span className="text-muted-foreground">Cash price</span> : money(d.memberCost)}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        <ul className="list-disc pl-5 text-sm text-muted-foreground">
          {plan.notes.map((n) => (
            <li key={n}>{n}</li>
          ))}
        </ul>
      </div>
      <div className="grid content-start gap-3 text-sm">
        <div>
          <p className="mb-2 font-medium">Drug costs by month</p>
          <div className="flex h-24 items-end gap-1">
            {plan.byMonth.map((m, i) => (
              <Tooltip key={i}>
                <TooltipTrigger
                  render={
                    <div
                      className={cn("flex-1 rounded-t", i + 1 < startMonth ? "bg-transparent" : "bg-primary/70")}
                      style={{ height: `${Math.max((m / max) * 100, i + 1 < startMonth ? 0 : 2)}%` }}
                    />
                  }
                />
                <TooltipContent>
                  {MONTHS[i]}: {money(m)}
                </TooltipContent>
              </Tooltip>
            ))}
          </div>
          <div className="mt-1 flex gap-1 text-[10px] text-muted-foreground">
            {MONTHS.map((m) => (
              <span key={m} className="flex-1 text-center">
                {m[0]}
              </span>
            ))}
          </div>
        </div>
        <p className="text-muted-foreground">
          {plan.deductibleMetMonth ? `Deductible met in ${MONTH_NAMES[plan.deductibleMetMonth - 1]}. ` : ""}
          {plan.catastrophicMonth ? `Out-of-pocket cap reached in ${MONTH_NAMES[plan.catastrophicMonth - 1]}; $0 after that.` : ""}
        </p>
        {plan.pharmacies.length > 0 && (
          <div className="grid gap-1">
            <p className="font-medium">Client's pharmacies</p>
            {plan.pharmacies.map((p) => (
              <p key={p.npi} className="flex justify-between gap-2">
                <span>{p.name}</span>
                <span className="text-muted-foreground">{p.inNetwork ? (p.preferred ? "Preferred" : "Standard") : "Out of network"}</span>
              </p>
            ))}
          </div>
        )}
        <p className="text-muted-foreground">
          {BENEFIT_LABEL[plan.benefitType ?? ""] ?? "Benefit type unknown"} · premium {money(plan.annualPremium)} for the period
        </p>
      </div>
    </div>
  );
}
