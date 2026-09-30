import { useState, type ReactNode } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { ApiError, type IntakeForm, type S } from "@/api/client";
import { AboutFields, EXTRA_HELP_OPTIONS } from "@/components/AboutFields";
import { DrugList, DrugPicker } from "@/components/Drugs";
import { PharmacyList, PharmacyPicker } from "@/components/Pharmacies";

const STEPS = ["About you", "Your prescriptions", "Your pharmacies", "Review and send"];

/**
 * The patient's four steps. Used for a new intake (public link) and for coming back to update one (magic link).
 * Patients never see plans here — only their own information and a confirmation.
 */
export function IntakeWizard({
  intro,
  broker,
  consents,
  initial,
  submitLabel,
  onZipChange,
  onSubmit,
}: {
  /** Shown above the steps on the first step only. */
  intro?: ReactNode;
  broker: S["PublicBroker"];
  consents: S["ConsentTexts"];
  initial: IntakeForm;
  submitLabel: string;
  onZipChange?: (zip: string) => void;
  onSubmit: (form: IntakeForm, consentContact: boolean, consentShare: boolean) => Promise<void>;
}) {
  const [step, setStep] = useState(0);
  const [form, setForm] = useState<IntakeForm>(initial);
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [consentContact, setConsentContact] = useState(false);
  const [consentShare, setConsentShare] = useState(false);
  const [sending, setSending] = useState(false);

  const update = (patch: Partial<IntakeForm>) => {
    setForm((f) => ({ ...f, ...patch }));
    if (patch.zip !== undefined) onZipChange?.(patch.zip);
  };

  const aboutErrors = (): Record<string, string[]> => {
    const e: Record<string, string[]> = {};
    if (!form.firstName.trim()) e.FirstName = ["Please enter your first name."];
    if (!form.lastName.trim()) e.LastName = ["Please enter your last name."];
    if (!form.email?.trim() && !form.phone?.trim()) e.Email = ["Enter an email or a phone number so we can reach you."];
    if (!/^\d{5}$/.test(form.zip)) e.Zip = ["Enter your 5-digit ZIP code."];
    return e;
  };

  const next = () => {
    if (step === 0) {
      const e = aboutErrors();
      setErrors(e);
      if (Object.keys(e).length > 0) return;
    }
    setStep((s) => Math.min(s + 1, STEPS.length - 1));
    window.scrollTo({ top: 0 });
  };

  const submit = async () => {
    setSending(true);
    try {
      await onSubmit(form, consentContact, consentShare);
    } catch (e) {
      if (e instanceof ApiError && Object.keys(e.fieldErrors).length > 0) {
        setErrors(e.fieldErrors);
        if (["FirstName", "LastName", "Email", "Zip", "BirthMonth", "BirthYear"].some((k) => e.fieldErrors[k])) setStep(0);
      }
      toast.error(e instanceof Error ? e.message : "Something went wrong. Please try again.");
    } finally {
      setSending(false);
    }
  };

  return (
    <div className="grid gap-5">
      {step === 0 && intro}
      <ol className="flex gap-1.5" aria-label="Progress">
        {STEPS.map((s, i) => (
          <li key={s} className={`h-1.5 flex-1 rounded-full ${i <= step ? "bg-primary" : "bg-border"}`} aria-current={i === step ? "step" : undefined}>
            <span className="sr-only">{s}</span>
          </li>
        ))}
      </ol>
      <p className="text-sm text-muted-foreground">
        Step {step + 1} of {STEPS.length}
      </p>

      <Card>
        <CardHeader>
          <CardTitle className="text-xl">{STEPS[step]}</CardTitle>
          <CardDescription className="text-base">
            {step === 0 && `${broker.displayName} will use this to find the Medicare drug plans available where you live.`}
            {step === 1 && "Add each prescription you take. Your pill bottle label has the name and strength."}
            {step === 2 && "Where do you fill your prescriptions? Plans charge less at some pharmacies than others."}
            {step === 3 && "Check your information, then send it to your broker."}
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-5">
          {step === 0 && <AboutFields form={form} onChange={update} errors={errors} large />}

          {step === 1 && (
            <>
              <DrugList drugs={form.drugs} onRemove={(i) => update({ drugs: form.drugs.filter((_, j) => j !== i) })} />
              <DrugPicker large onAdd={(d) => update({ drugs: [...form.drugs, d] })} />
            </>
          )}

          {step === 2 && (
            <>
              <PharmacyList pharmacies={form.pharmacies} onRemove={(i) => update({ pharmacies: form.pharmacies.filter((_, j) => j !== i) })} />
              <PharmacyPicker large zip={form.zip} chosen={form.pharmacies} onAdd={(p) => update({ pharmacies: [...form.pharmacies, p] })} />
              <label className="flex items-start gap-3 rounded-lg border p-3">
                <Checkbox checked={form.usesMailOrder} onCheckedChange={(c) => update({ usesMailOrder: c })} className="mt-0.5" />
                <span>I get my prescriptions by mail, or I'd like to.</span>
              </label>
            </>
          )}

          {step === 3 && (
            <Review
              form={form}
              consents={consents}
              consentContact={consentContact}
              consentShare={consentShare}
              errors={errors}
              onConsentContact={setConsentContact}
              onConsentShare={setConsentShare}
              onEdit={setStep}
            />
          )}
        </CardContent>
      </Card>

      <div className="flex justify-between gap-3">
        <Button variant="outline" size="lg" className="h-12 px-5 text-base" disabled={step === 0} onClick={() => setStep((s) => s - 1)}>
          Back
        </Button>
        {step < STEPS.length - 1 ? (
          <Button size="lg" className="h-12 px-6 text-base" onClick={next}>
            {step === 1 && form.drugs.length === 0 ? "I don't take any" : step === 2 && form.pharmacies.length === 0 ? "Skip" : "Continue"}
          </Button>
        ) : (
          <Button size="lg" className="h-12 px-6 text-base" disabled={!consentContact || !consentShare || sending} onClick={submit}>
            {sending ? "Sending…" : submitLabel}
          </Button>
        )}
      </div>
    </div>
  );
}

function Review({
  form,
  consents,
  consentContact,
  consentShare,
  errors,
  onConsentContact,
  onConsentShare,
  onEdit,
}: {
  form: IntakeForm;
  consents: S["ConsentTexts"];
  consentContact: boolean;
  consentShare: boolean;
  errors: Record<string, string[]>;
  onConsentContact: (v: boolean) => void;
  onConsentShare: (v: boolean) => void;
  onEdit: (step: number) => void;
}) {
  const help = EXTRA_HELP_OPTIONS.find((o) => o.value === form.extraHelp)?.label;
  return (
    <div className="grid gap-5">
      <Section title="About you" onEdit={() => onEdit(0)}>
        <p>
          {form.firstName} {form.lastName} · ZIP {form.zip}
        </p>
        <p className="text-muted-foreground">{[form.email, form.phone].filter(Boolean).join(" · ")}</p>
        {help && help !== "No" && <p className="text-muted-foreground">Help paying: {help}</p>}
      </Section>
      <Section title={`Prescriptions (${form.drugs.length})`} onEdit={() => onEdit(1)}>
        <DrugList drugs={form.drugs} />
      </Section>
      <Section title="Pharmacies" onEdit={() => onEdit(2)}>
        <PharmacyList pharmacies={form.pharmacies} />
        {form.usesMailOrder && <p className="text-muted-foreground">Mail order: yes</p>}
      </Section>

      <div className="grid gap-3 rounded-lg border bg-muted/30 p-4">
        <label className="flex items-start gap-3">
          <Checkbox checked={consentContact} onCheckedChange={onConsentContact} className="mt-1" />
          <span>{consents.contact}</span>
        </label>
        <label className="flex items-start gap-3">
          <Checkbox checked={consentShare} onCheckedChange={onConsentShare} className="mt-1" />
          <span>{consents.shareHealthInfo}</span>
        </label>
        {(errors.ConsentContact || errors.ConsentShareHealthInfo) && (
          <p className="text-sm text-destructive">{errors.ConsentContact?.[0] ?? errors.ConsentShareHealthInfo?.[0]}</p>
        )}
      </div>
    </div>
  );
}

function Section({ title, onEdit, children }: { title: string; onEdit: () => void; children: React.ReactNode }) {
  return (
    <section className="grid gap-2">
      <div className="flex items-center justify-between">
        <h3 className="font-semibold">{title}</h3>
        <Button variant="link" className="h-auto p-0" onClick={onEdit}>
          Change
        </Button>
      </div>
      {children}
    </section>
  );
}
