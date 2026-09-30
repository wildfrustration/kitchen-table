import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeftIcon, CopyIcon, MailIcon, PencilIcon } from "lucide-react";
import { useState } from "react";
import { Link, useParams } from "react-router";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import { api, unwrap, type IntakeForm, type S } from "@/api/client";
import { EXTRA_HELP_OPTIONS, MEDICARE_OPTIONS } from "@/components/AboutFields";
import { DrugList } from "@/components/Drugs";
import { NativeSelect } from "@/components/Field";
import { PharmacyList } from "@/components/Pharmacies";
import { formatDate, MONTH_NAMES, relativeTime } from "@/lib/format";
import { ClientEditor, EXTRA_HELP_LEVELS } from "./ClientEditor";
import { QuotePanel } from "./QuotePanel";
import { STATUSES } from "./QueuePage";

export function ClientPage() {
  const { id = "" } = useParams();
  const qc = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [invite, setInvite] = useState<S["InviteLink"] | null>(null);

  const client = useQuery({
    queryKey: ["client", id],
    queryFn: () => unwrap(api.GET("/api/clients/{id}", { params: { path: { id } } })),
  });
  const refresh = () => {
    qc.invalidateQueries({ queryKey: ["client", id] });
    qc.invalidateQueries({ queryKey: ["quote", id] });
    qc.invalidateQueries({ queryKey: ["clients"] });
  };

  const setStatus = useMutation({
    mutationFn: (status: S["ClientStatus"]) => unwrap(api.PUT("/api/clients/{id}/status", { params: { path: { id } }, body: { status } })),
    onSuccess: refresh,
  });
  const sendInvite = useMutation({
    mutationFn: () => unwrap(api.POST("/api/clients/{id}/invite", { params: { path: { id } } })),
    onSuccess: setInvite,
  });
  const save = useMutation({
    mutationFn: ({ form, level }: { form: IntakeForm; level: number }) =>
      unwrap(api.PUT("/api/clients/{id}", { params: { path: { id } }, body: { form, extraHelpLevel: level } })),
    onSuccess: () => {
      setEditing(false);
      refresh();
      toast.success("Saved.");
    },
  });

  if (!client.data) return <Skeleton className="h-96 w-full" />;
  const c = client.data;
  const f = c.form;

  return (
    <div className="grid gap-5">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="grid gap-1">
          <Link to="/app" className="flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
            <ArrowLeftIcon className="size-4" /> Clients
          </Link>
          <h1 className="text-2xl font-semibold">
            {f.firstName} {f.lastName}
          </h1>
          <p className="text-sm text-muted-foreground">
            {c.submittedAt ? `Submitted ${relativeTime(c.submittedAt)}` : `Added ${formatDate(c.createdAt)}`} · {c.brokerName}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <NativeSelect aria-label="Status" className="h-8 w-36" value={c.status} onChange={(e) => setStatus.mutate(e.target.value as S["ClientStatus"])}>
            {STATUSES.map((s) => (
              <option key={s}>{s}</option>
            ))}
          </NativeSelect>
          <Button variant="outline" onClick={() => sendInvite.mutate()} disabled={sendInvite.isPending}>
            <MailIcon /> Invite to update
          </Button>
        </div>
      </div>

      <div className="grid items-start gap-5 lg:grid-cols-[360px_1fr]">
        <div className="grid gap-5">
          <Card>
            <CardHeader className="flex flex-row items-center justify-between">
              <CardTitle>Client</CardTitle>
              <Button variant="ghost" size="sm" onClick={() => setEditing(true)}>
                <PencilIcon /> Edit
              </Button>
            </CardHeader>
            <CardContent className="grid gap-4 text-sm">
              <Info label="Contact">{[f.email, f.phone].filter(Boolean).join(" · ") || "—"}</Info>
              <Info label="ZIP">{f.zip}</Info>
              <Info label="Born">{f.birthMonth && f.birthYear ? `${MONTH_NAMES[f.birthMonth - 1]} ${f.birthYear}` : "—"}</Info>
              <Info label="Medicare">{MEDICARE_OPTIONS.find((o) => o.value === f.medicareStatus)?.label ?? "—"}</Info>
              <Info label="Current plan">{f.currentPlan || "—"}</Info>
              <Info label="Help paying">
                {EXTRA_HELP_OPTIONS.find((o) => o.value === f.extraHelp)?.label}
                <span className="block text-muted-foreground">Quoted as: {EXTRA_HELP_LEVELS.find((l) => l.value === c.extraHelpLevel)?.label}</span>
              </Info>
              <div className="grid gap-2">
                <span className="text-muted-foreground">Prescriptions</span>
                <DrugList drugs={f.drugs} />
              </div>
              <div className="grid gap-2">
                <span className="text-muted-foreground">Pharmacies</span>
                <PharmacyList pharmacies={f.pharmacies} />
                {f.usesMailOrder && <Badge variant="secondary">Uses mail order</Badge>}
              </div>
            </CardContent>
          </Card>
          <Notes clientId={id} notes={c.notes} onAdded={refresh} />
          <Card>
            <CardHeader>
              <CardTitle>Consent</CardTitle>
            </CardHeader>
            <CardContent className="grid gap-1 text-sm text-muted-foreground">
              {c.consents.length === 0 && <p>No consent recorded. Get the client's permission before you contact them.</p>}
              {c.consents.slice(0, 4).map((x, i) => (
                <p key={i}>
                  {x.kind === "Contact" ? "Contact" : "Share health information"} — {formatDate(x.grantedAt)} (wording {x.textVersion})
                </p>
              ))}
            </CardContent>
          </Card>
        </div>

        <QuotePanel clientId={id} hasPharmacies={f.pharmacies.length > 0} />
      </div>

      <Dialog open={editing} onOpenChange={setEditing}>
        <DialogContent className="max-h-[90svh] overflow-y-auto sm:max-w-3xl">
          <DialogHeader>
            <DialogTitle>Edit client</DialogTitle>
          </DialogHeader>
          {editing && (
            <ClientEditor
              initial={f}
              initialLevel={c.extraHelpLevel}
              saving={save.isPending}
              error={save.error?.message}
              onCancel={() => setEditing(false)}
              onSave={(form, level) => save.mutate({ form, level })}
            />
          )}
        </DialogContent>
      </Dialog>

      <Dialog open={!!invite} onOpenChange={(o) => !o && setInvite(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Invite link</DialogTitle>
            <DialogDescription>
              {invite?.emailed ? "We emailed this link to the client. " : "The client has no email on file — send them this link yourself. "}
              It works once, until {invite && new Date(invite.expiresAt).toLocaleString()}.
            </DialogDescription>
          </DialogHeader>
          <code className="rounded bg-muted p-2 text-xs break-all">{invite?.url}</code>
          <DialogFooter>
            <Button
              onClick={() => {
                navigator.clipboard.writeText(invite!.url);
                toast.success("Link copied.");
              }}
            >
              <CopyIcon /> Copy link
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function Info({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="grid grid-cols-[110px_1fr] gap-2">
      <span className="text-muted-foreground">{label}</span>
      <span>{children}</span>
    </div>
  );
}

function Notes({ clientId, notes, onAdded }: { clientId: string; notes: S["NoteView"][]; onAdded: () => void }) {
  const [body, setBody] = useState("");
  const add = useMutation({
    mutationFn: () => unwrap(api.POST("/api/clients/{id}/notes", { params: { path: { id: clientId } }, body: { body } })),
    onSuccess: () => {
      setBody("");
      onAdded();
    },
  });
  return (
    <Card>
      <CardHeader>
        <CardTitle>Notes</CardTitle>
      </CardHeader>
      <CardContent className="grid gap-3 text-sm">
        <Textarea placeholder="Add a note…" value={body} onChange={(e) => setBody(e.target.value)} />
        <Button size="sm" className="justify-self-end" disabled={!body.trim() || add.isPending} onClick={() => add.mutate()}>
          Add note
        </Button>
        {notes.map((n) => (
          <div key={n.id} className="grid gap-0.5 border-t pt-2">
            <p className="whitespace-pre-wrap">{n.body}</p>
            <p className="text-xs text-muted-foreground">
              {n.author} · {relativeTime(n.createdAt)}
            </p>
          </div>
        ))}
      </CardContent>
    </Card>
  );
}
