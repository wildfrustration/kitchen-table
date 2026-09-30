import { useMutation, useQuery } from "@tanstack/react-query";
import { CircleCheckIcon, MailIcon } from "lucide-react";
import { useEffect, useState } from "react";
import { useLocation, useNavigate, useParams } from "react-router";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { api, emptyForm, unwrap, type S } from "@/api/client";
import { IntakeWizard } from "./IntakeWizard";
import { PatientShell } from "./PatientShell";

type DoneState = { broker: S["PublicBroker"]; firstName: string; updated: boolean };

/** /start/:slug — a broker's public link. */
export function StartPage() {
  const { slug = "" } = useParams();
  const navigate = useNavigate();
  const [zip, setZip] = useState("");
  const [linkOpen, setLinkOpen] = useState(false);

  const broker = useQuery({
    queryKey: ["public-broker", slug],
    queryFn: () => unwrap(api.GET("/api/intake/{slug}", { params: { path: { slug } } })),
    retry: false,
  });
  const consents = useQuery({
    queryKey: ["consents", slug],
    queryFn: () => unwrap(api.GET("/api/intake/{slug}/consents", { params: { path: { slug } } })),
    enabled: broker.isSuccess,
  });

  if (broker.isError) {
    return (
      <PatientShell>
        <Card>
          <CardContent className="py-8 text-center">This link isn't valid. Please check it with your broker.</CardContent>
        </Card>
      </PatientShell>
    );
  }
  if (!broker.data || !consents.data) return <Loading />;

  return (
    <PatientShell broker={broker.data} zip={zip}>
      <IntakeWizard
        intro={
          <div className="mb-1 grid gap-2">
            <h1 className="text-2xl font-semibold">Find the right Medicare drug plan</h1>
            <p className="text-muted-foreground">
              Share your prescriptions and pharmacy with {broker.data.displayName}. They'll compare the plans in your area and contact you with the options
              that cost you least. It takes about 5 minutes.
            </p>
            <Button variant="link" className="h-auto justify-start p-0 underline" onClick={() => setLinkOpen(true)}>
              Already sent your list? Get a link to update it.
            </Button>
          </div>
        }
        broker={broker.data}
        consents={consents.data}
        initial={emptyForm()}
        submitLabel="Send to my broker"
        onZipChange={setZip}
        onSubmit={async (form, consentContact, consentShareHealthInfo) => {
          await unwrap(api.POST("/api/intake/{slug}", { params: { path: { slug } }, body: { form, consentContact, consentShareHealthInfo } }));
          navigate("/done", { state: { broker: broker.data, firstName: form.firstName, updated: false } satisfies DoneState });
        }}
      />
      <UpdateLinkDialog slug={slug} open={linkOpen} onOpenChange={setLinkOpen} />
    </PatientShell>
  );
}

/** /i/:token (invite) and /r/:token (return): a single-use link opens the patient's own intake. */
export function LinkPage({ purpose }: { purpose: "invite" | "return" }) {
  const { token = "" } = useParams();
  const navigate = useNavigate();
  const [ready, setReady] = useState(false);
  const [failed, setFailed] = useState(false);
  const [zip, setZip] = useState("");

  useEffect(() => {
    // Redeem once; if the link was already used but the session cookie is still valid, carry on.
    api.POST("/api/patient/redeem", { body: { token } }).then(async ({ response }) => {
      if (response.ok) return setReady(true);
      const probe = await api.GET("/api/patient/intake");
      if (probe.response.ok) setReady(true);
      else setFailed(true);
    });
  }, [token]);

  const intake = useQuery({ queryKey: ["patient-intake"], queryFn: () => unwrap(api.GET("/api/patient/intake")), enabled: ready });
  const consents = useQuery({ queryKey: ["patient-consents"], queryFn: () => unwrap(api.GET("/api/patient/consents")), enabled: ready });

  if (failed) {
    return (
      <PatientShell>
        <Card>
          <CardContent className="grid gap-2 py-8 text-center">
            <p className="font-medium">This link has expired or was already used.</p>
            <p className="text-muted-foreground">Links work once, for 24 hours. Ask your broker for a new one, or use the "update" option on their page.</p>
          </CardContent>
        </Card>
      </PatientShell>
    );
  }
  if (!intake.data || !consents.data) return <Loading />;

  const { broker, form, submittedAt } = intake.data;
  const updating = submittedAt != null;
  return (
    <PatientShell broker={broker} zip={zip || form.zip}>
      <IntakeWizard
        intro={
          <div className="mb-1 grid gap-2">
            <h1 className="text-2xl font-semibold">{updating ? "Update your prescription list" : "Share your prescriptions"}</h1>
            <p className="text-muted-foreground">
              {updating
                ? `Change anything that's different, then send it to ${broker.displayName} again.`
                : `${broker.displayName} asked you to list your prescriptions and pharmacy so they can compare Medicare drug plans for you.`}
            </p>
          </div>
        }
        broker={broker}
        consents={consents.data}
        initial={form}
        submitLabel={updating ? "Send my update" : "Send to my broker"}
        onZipChange={setZip}
        onSubmit={async (next, consentContact, consentShareHealthInfo) => {
          await unwrap(api.PUT("/api/patient/intake", { body: { form: next, consentContact, consentShareHealthInfo } }));
          navigate("/done", { state: { broker, firstName: next.firstName, updated: updating || purpose === "return" } satisfies DoneState });
        }}
      />
    </PatientShell>
  );
}

/** /done — the only thing a patient sees after submitting: a confirmation, never plans. */
export function DonePage() {
  const state = useLocation().state as DoneState | null;
  return (
    <PatientShell broker={state?.broker}>
      <Card>
        <CardContent className="grid justify-items-center gap-3 py-10 text-center">
          <CircleCheckIcon className="size-12 text-green-600" />
          <h1 className="text-2xl font-semibold">{state ? `Thank you, ${state.firstName}!` : "Thank you!"}</h1>
          <p className="max-w-md text-muted-foreground">
            {state?.updated ? "Your update was sent" : "Your information was sent"} to {state?.broker.displayName ?? "your broker"}. They'll review the drug
            plans in your area and contact you with your options.
          </p>
          {state?.broker.phone && <p className="text-muted-foreground">Questions? Call {state.broker.phone}.</p>}
          <p className="text-sm text-muted-foreground">We've emailed you a link in case you need to change anything later.</p>
        </CardContent>
      </Card>
    </PatientShell>
  );
}

function UpdateLinkDialog({ slug, open, onOpenChange }: { slug: string; open: boolean; onOpenChange: (o: boolean) => void }) {
  const [email, setEmail] = useState("");
  const send = useMutation({
    mutationFn: () => unwrap(api.POST("/api/intake/{slug}/link", { params: { path: { slug } }, body: { email } })),
    onSuccess: () => {
      toast.success("If we have your list, a link is on its way to your inbox.");
      onOpenChange(false);
    },
  });
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Update your list</DialogTitle>
          <DialogDescription>Enter the email you used, and we'll send you a link to update your prescriptions.</DialogDescription>
        </DialogHeader>
        <Input type="email" autoComplete="email" placeholder="you@example.com" value={email} onChange={(e) => setEmail(e.target.value)} className="h-11 text-base" />
        <Button size="lg" disabled={!email.includes("@") || send.isPending} onClick={() => send.mutate()}>
          <MailIcon /> Email me a link
        </Button>
      </DialogContent>
    </Dialog>
  );
}

function Loading() {
  return (
    <PatientShell>
      <div className="grid gap-4">
        <Skeleton className="h-8 w-2/3" />
        <Skeleton className="h-64 w-full" />
      </div>
    </PatientShell>
  );
}
