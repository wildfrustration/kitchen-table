import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CopyIcon, MailIcon, PlusIcon, ShieldCheckIcon } from "lucide-react";
import { useState } from "react";
import { Navigate } from "react-router";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { api, ApiError, unwrap, type S } from "@/api/client";
import { Field, NativeSelect } from "@/components/Field";
import { formatDate, relativeTime } from "@/lib/format";
import { cn } from "@/lib/utils";
import { useMe } from "./Shell";

type Broker = S["BrokerSummary"];
type Role = S["BrokerRole"];
type SentLink = { email: string; url: string; expiresAt: string };

/** /app/brokers — agency admins only. */
export function BrokersPage() {
  const me = useMe();
  const qc = useQueryClient();
  const isAdmin = me.data?.role === "AgencyAdmin";
  const brokers = useQuery({ queryKey: ["agency", "brokers"], queryFn: () => unwrap(api.GET("/api/agency/brokers")), enabled: isAdmin });
  const invites = useQuery({ queryKey: ["agency", "invites"], queryFn: () => unwrap(api.GET("/api/agency/invites")), enabled: isAdmin });
  const [inviting, setInviting] = useState(false);
  const [sent, setSent] = useState<SentLink | null>(null);
  const [roleChange, setRoleChange] = useState<{ broker: Broker; role: Role } | null>(null);
  const [deactivating, setDeactivating] = useState<Broker | null>(null);

  const refresh = () => qc.invalidateQueries({ queryKey: ["agency"] });
  const resend = useMutation({
    mutationFn: (invite: S["PendingInvite"]) => unwrap(api.POST("/api/agency/invites/{id}/resend", { params: { path: { id: invite.id } } })),
    onSuccess: (link, invite) => {
      setSent({ email: invite.email, url: link.url, expiresAt: link.expiresAt });
      refresh();
    },
    onError: (e) => toast.error(e.message),
  });
  const revoke = useMutation({
    mutationFn: (invite: S["PendingInvite"]) => unwrap(api.DELETE("/api/agency/invites/{id}", { params: { path: { id: invite.id } } })),
    onSuccess: (_, invite) => {
      toast.success(`Invite to ${invite.displayName} cancelled. The link no longer works.`);
      refresh();
    },
    onError: (e) => toast.error(e.message),
  });
  const reactivate = useMutation({
    mutationFn: (b: Broker) => unwrap(api.POST("/api/agency/brokers/{id}/reactivate", { params: { path: { id: b.id } } })),
    onSuccess: (_, b) => {
      toast.success(`${b.displayName} can sign in again.`);
      refresh();
    },
    onError: (e) => toast.error(e.message),
  });

  if (me.data && !isAdmin) return <Navigate to="/app" replace />;

  const list = brokers.data ?? [];
  const active = list.filter((b) => !b.deactivatedAt);
  const pending = invites.data ?? [];

  return (
    <div className="grid gap-5">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">Brokers</h1>
          <p className="text-sm text-muted-foreground">
            {me.data?.agency.name} · {active.length} active {active.length === 1 ? "broker" : "brokers"}
            {pending.length > 0 && ` · ${pending.length} invited`}
          </p>
        </div>
        <Button onClick={() => setInviting(true)}>
          <PlusIcon /> Invite a broker
        </Button>
      </div>

      {pending.length > 0 && (
        <Card className="gap-0 py-0">
          <CardHeader className="border-b py-3">
            <CardTitle className="text-sm">Waiting to set up their login</CardTitle>
          </CardHeader>
          <CardContent className="px-0">
            <Table>
              <TableBody>
                {pending.map((i) => {
                  const expired = new Date(i.expiresAt) < new Date();
                  return (
                    <TableRow key={i.id}>
                      <TableCell className="pl-4">
                        <span className="font-medium">{i.displayName}</span>
                        <span className="block text-muted-foreground">{i.email}</span>
                      </TableCell>
                      <TableCell>
                        <RoleBadge role={i.role} />
                      </TableCell>
                      <TableCell className="text-muted-foreground">
                        Invited by {i.invitedBy} {relativeTime(i.createdAt)}
                      </TableCell>
                      <TableCell>
                        {expired ? (
                          <Badge variant="destructive">Link expired</Badge>
                        ) : (
                          <span className="text-muted-foreground">Link works until {formatDate(i.expiresAt)}</span>
                        )}
                      </TableCell>
                      <TableCell className="pr-4 text-right">
                        <Button variant="outline" size="sm" disabled={resend.isPending} onClick={() => resend.mutate(i)}>
                          <MailIcon /> Send again
                        </Button>
                        <Button variant="ghost" size="sm" className="ml-1" disabled={revoke.isPending} onClick={() => revoke.mutate(i)}>
                          Cancel invite
                        </Button>
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}

      <Card className="py-0">
        <CardContent className="px-0">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead className="pl-4">Broker</TableHead>
                <TableHead>Role</TableHead>
                <TableHead>Clients</TableHead>
                <TableHead>Carriers</TableHead>
                <TableHead>Last signed in</TableHead>
                <TableHead className="pr-4" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {list.map((b) => (
                <TableRow key={b.id} className={cn(b.deactivatedAt && "text-muted-foreground")}>
                  <TableCell className="pl-4">
                    <span className="flex items-center gap-2 font-medium">
                      {b.displayName}
                      {b.isYou && <Badge variant="secondary">You</Badge>}
                      {b.deactivatedAt && <Badge variant="outline">Deactivated {formatDate(b.deactivatedAt)}</Badge>}
                    </span>
                    <span className="block text-muted-foreground">{b.email}</span>
                  </TableCell>
                  <TableCell>
                    {b.isYou || b.deactivatedAt ? (
                      <RoleBadge role={b.role} />
                    ) : (
                      <NativeSelect
                        aria-label={`${b.displayName}'s role`}
                        className="h-8 w-28"
                        value={b.role}
                        onChange={(e) => setRoleChange({ broker: b, role: e.target.value as Role })}
                      >
                        <option value="Broker">Broker</option>
                        <option value="AgencyAdmin">Admin</option>
                      </NativeSelect>
                    )}
                  </TableCell>
                  <TableCell>
                    {b.clients === 0 ? (
                      <span className="text-muted-foreground">None</span>
                    ) : (
                      <>
                        {b.activeClients} active <span className="text-muted-foreground">of {b.clients}</span>
                      </>
                    )}
                  </TableCell>
                  <TableCell>
                    {b.carriers > 0 ? b.carriers : <span className={cn(!b.deactivatedAt && "text-amber-700 dark:text-amber-400")}>None picked yet</span>}
                  </TableCell>
                  <TableCell className="text-muted-foreground">{b.lastSignInAt ? relativeTime(b.lastSignInAt) : "Never"}</TableCell>
                  <TableCell className="pr-4 text-right">
                    {b.isYou ? null : b.deactivatedAt ? (
                      <Button variant="outline" size="sm" disabled={reactivate.isPending} onClick={() => reactivate.mutate(b)}>
                        Reactivate
                      </Button>
                    ) : (
                      <Button variant="ghost" size="sm" className="text-destructive hover:text-destructive" onClick={() => setDeactivating(b)}>
                        Deactivate
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>

      <p className="max-w-3xl text-sm text-muted-foreground">
        Brokers see only their own clients. Admins see every client in the agency, including prescriptions, and can invite, promote and deactivate
        brokers. Each broker picks their own carriers in Settings.
      </p>

      <InviteDialog
        open={inviting}
        onOpenChange={setInviting}
        onSent={(link) => {
          setInviting(false);
          setSent(link);
          refresh();
        }}
      />
      <SentDialog sent={sent} onClose={() => setSent(null)} />
      <RoleDialog change={roleChange} agency={me.data?.agency.name ?? "the agency"} onClose={() => setRoleChange(null)} onDone={refresh} />
      <DeactivateDialog broker={deactivating} brokers={active} onClose={() => setDeactivating(null)} onDone={refresh} />
    </div>
  );
}

function RoleBadge({ role }: { role: Role }) {
  return role === "AgencyAdmin" ? (
    <Badge className="border-amber-300 bg-amber-100 text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200">
      <ShieldCheckIcon /> Admin
    </Badge>
  ) : (
    <Badge variant="outline">Broker</Badge>
  );
}

function InviteDialog({ open, onOpenChange, onSent }: { open: boolean; onOpenChange: (open: boolean) => void; onSent: (link: SentLink) => void }) {
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<Role>("Broker");
  const invite = useMutation({
    mutationFn: () => unwrap(api.POST("/api/agency/invites", { body: { email, displayName: name, role } })),
    onSuccess: (link) => {
      onSent({ email: email.trim().toLowerCase(), url: link.url, expiresAt: link.expiresAt });
      setName("");
      setEmail("");
      setRole("Broker");
    },
  });
  const errors = invite.error instanceof ApiError ? invite.error.fieldErrors : {};

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Invite a broker</DialogTitle>
          <DialogDescription>We'll email them a link to set up their login. It works once, for 7 days.</DialogDescription>
        </DialogHeader>
        <form
          id="invite-form"
          className="grid gap-4"
          onSubmit={(e) => {
            e.preventDefault();
            invite.mutate();
          }}
        >
          <Field label="Name" htmlFor="invite-name" error={errors.DisplayName?.[0]}>
            <Input id="invite-name" value={name} onChange={(e) => setName(e.target.value)} autoComplete="off" />
          </Field>
          <Field label="Email" htmlFor="invite-email" error={errors.Email?.[0]}>
            <Input id="invite-email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoComplete="off" />
          </Field>
          <Field label="Role">
            <RadioGroup value={role} onValueChange={(v) => setRole(v as Role)} className="gap-3">
              <label className="flex items-start gap-2">
                <RadioGroupItem value="Broker" className="mt-0.5" />
                <span>
                  Broker <span className="block text-muted-foreground">Works their own clients.</span>
                </span>
              </label>
              <label className="flex items-start gap-2">
                <RadioGroupItem value="AgencyAdmin" className="mt-0.5" />
                <span>
                  Admin <span className="block text-muted-foreground">Also sees every client in the agency and manages brokers.</span>
                </span>
              </label>
            </RadioGroup>
          </Field>
          {invite.isError && Object.keys(errors).length === 0 && <p className="text-sm text-destructive">{invite.error.message}</p>}
        </form>
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button type="submit" form="invite-form" disabled={invite.isPending || !name.trim() || !email.trim()}>
            {invite.isPending ? "Sending…" : "Send invite"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function SentDialog({ sent, onClose }: { sent: SentLink | null; onClose: () => void }) {
  return (
    <Dialog open={!!sent} onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Invite sent</DialogTitle>
          <DialogDescription>
            We emailed a setup link to {sent?.email}. If it doesn't arrive, send them this link yourself. It works once, until{" "}
            {sent && new Date(sent.expiresAt).toLocaleString()}.
          </DialogDescription>
        </DialogHeader>
        <code className="rounded bg-muted p-2 text-xs break-all">{sent?.url}</code>
        <DialogFooter>
          <Button
            onClick={() => {
              navigator.clipboard.writeText(sent!.url);
              toast.success("Link copied.");
            }}
          >
            <CopyIcon /> Copy link
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function RoleDialog({
  change,
  agency,
  onClose,
  onDone,
}: {
  change: { broker: Broker; role: Role } | null;
  agency: string;
  onClose: () => void;
  onDone: () => void;
}) {
  const save = useMutation({
    mutationFn: ({ broker, role }: { broker: Broker; role: Role }) =>
      unwrap(api.PUT("/api/agency/brokers/{id}/role", { params: { path: { id: broker.id } }, body: { role } })),
    onSuccess: (_, { broker, role }) => {
      toast.success(role === "AgencyAdmin" ? `${broker.displayName} is now an admin.` : `${broker.displayName} is now a broker.`);
      onClose();
      onDone();
    },
    onError: (e) => toast.error(e.message),
  });
  const promoting = change?.role === "AgencyAdmin";
  const name = change?.broker.displayName;

  return (
    <Dialog open={!!change} onOpenChange={(o) => !o && onClose()}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{promoting ? `Make ${name} an admin?` : `Make ${name} a broker?`}</DialogTitle>
          <DialogDescription>
            {promoting
              ? `${name} will see every client in ${agency}, including their prescriptions, and can invite, promote and deactivate brokers.`
              : `${name} will see only their own clients and can no longer manage brokers.`}
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button disabled={save.isPending} onClick={() => change && save.mutate(change)}>
            {promoting ? "Make admin" : "Make broker"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function DeactivateDialog({ broker, brokers, onClose, onDone }: { broker: Broker | null; brokers: Broker[]; onClose: () => void; onDone: () => void }) {
  const others = brokers.filter((b) => b.id !== broker?.id);
  const [target, setTarget] = useState("");
  const moveTo = target || others.find((b) => b.isYou)?.id || others[0]?.id || "";
  const deactivate = useMutation({
    mutationFn: (b: Broker) =>
      unwrap(api.POST("/api/agency/brokers/{id}/deactivate", { params: { path: { id: b.id } }, body: { moveClientsTo: b.clients > 0 ? moveTo : null } })),
    onSuccess: (_, b) => {
      const to = others.find((o) => o.id === moveTo);
      toast.success(b.clients > 0 && to ? `${b.displayName} is deactivated. ${to.isYou ? "You have" : `${to.displayName} has`} their clients now.` : `${b.displayName} is deactivated.`);
      setTarget("");
      onClose();
      onDone();
    },
    onError: (e) => toast.error(e.message),
  });

  return (
    <Dialog
      open={!!broker}
      onOpenChange={(o) => {
        if (!o) {
          setTarget("");
          onClose();
        }
      }}
    >
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Deactivate {broker?.displayName}?</DialogTitle>
          <DialogDescription>
            They're signed out right away and can't sign back in. Their public intake link stops working. You can reactivate them later.
          </DialogDescription>
        </DialogHeader>
        {broker && broker.clients > 0 && (
          <Field
            label={`Move their ${broker.clients} ${broker.clients === 1 ? "client" : "clients"} to`}
            htmlFor="move-to"
            hint="Each client gets a note saying who they were moved from."
          >
            <NativeSelect id="move-to" value={moveTo} onChange={(e) => setTarget(e.target.value)}>
              {others.map((b) => (
                <option key={b.id} value={b.id}>
                  {b.displayName}
                  {b.isYou ? " (you)" : ""}
                </option>
              ))}
            </NativeSelect>
          </Field>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Cancel
          </Button>
          <Button variant="destructive" disabled={deactivate.isPending || (!!broker?.clients && !moveTo)} onClick={() => broker && deactivate.mutate(broker)}>
            Deactivate
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
