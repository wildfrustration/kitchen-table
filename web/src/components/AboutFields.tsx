import { Input } from "@/components/ui/input";
import type { IntakeForm, S } from "@/api/client";
import { MONTH_NAMES } from "@/lib/format";
import { Field, NativeSelect } from "./Field";
import { ZipCounty } from "./ZipCounty";

type Errors = Record<string, string[]>;
const first = (errors: Errors, key: string) => errors[key]?.[0];

export const EXTRA_HELP_OPTIONS: { value: S["ExtraHelpAnswer"]; label: string }[] = [
  { value: "No", label: "No" },
  { value: "ExtraHelp", label: "Yes, I get Extra Help (Low Income Subsidy)" },
  { value: "Medicaid", label: "I have Medicaid" },
  { value: "Ssi", label: "I get Supplemental Security Income (SSI)" },
  { value: "MedicareSavingsProgram", label: "I'm in a Medicare Savings Program" },
  { value: "NotSure", label: "I'm not sure" },
];

export const MEDICARE_OPTIONS: { value: S["MedicareStatus"]; label: string }[] = [
  { value: "OnMedicare", label: "I have Medicare now" },
  { value: "TurningSixtyFive", label: "I'm new to Medicare (turning 65 or retiring)" },
  { value: "NotSure", label: "I'm not sure" },
];

/** Name, contact, ZIP and the few questions that change which plans fit. */
export function AboutFields({
  form,
  onChange,
  errors = {},
  large = false,
}: {
  form: IntakeForm;
  onChange: (patch: Partial<IntakeForm>) => void;
  errors?: Errors;
  large?: boolean;
}) {
  const inputClass = large ? "h-11 text-base" : undefined;
  const thisYear = new Date().getFullYear();

  return (
    <div className="grid gap-5">
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="First name" htmlFor="first" error={first(errors, "FirstName")}>
          <Input id="first" autoComplete="given-name" className={inputClass} value={form.firstName} onChange={(e) => onChange({ firstName: e.target.value })} />
        </Field>
        <Field label="Last name" htmlFor="last" error={first(errors, "LastName")}>
          <Input id="last" autoComplete="family-name" className={inputClass} value={form.lastName} onChange={(e) => onChange({ lastName: e.target.value })} />
        </Field>
        <Field label="Email" htmlFor="email" error={first(errors, "Email")} hint="We'll send you a link to update your list later.">
          <Input id="email" type="email" autoComplete="email" className={inputClass} value={form.email ?? ""} onChange={(e) => onChange({ email: e.target.value })} />
        </Field>
        <Field label="Phone" htmlFor="phone">
          <Input id="phone" type="tel" autoComplete="tel" className={inputClass} value={form.phone ?? ""} onChange={(e) => onChange({ phone: e.target.value })} />
        </Field>
      </div>

      <ZipCounty zip={form.zip} countyCode={form.countyCode} error={first(errors, "Zip")} onChange={(zip, countyCode) => onChange({ zip, countyCode })} />

      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="Birth month" htmlFor="bmonth" error={first(errors, "BirthMonth")}>
          <NativeSelect id="bmonth" value={form.birthMonth ?? ""} onChange={(e) => onChange({ birthMonth: e.target.value ? Number(e.target.value) : null })}>
            <option value="">Choose…</option>
            {MONTH_NAMES.map((m, i) => (
              <option key={m} value={i + 1}>
                {m}
              </option>
            ))}
          </NativeSelect>
        </Field>
        <Field label="Birth year" htmlFor="byear" error={first(errors, "BirthYear")}>
          <NativeSelect id="byear" value={form.birthYear ?? ""} onChange={(e) => onChange({ birthYear: e.target.value ? Number(e.target.value) : null })}>
            <option value="">Choose…</option>
            {Array.from({ length: 50 }, (_, i) => thisYear - 55 - i).map((y) => (
              <option key={y} value={y}>
                {y}
              </option>
            ))}
          </NativeSelect>
        </Field>
      </div>

      <Field label="Medicare" htmlFor="medicare">
        <NativeSelect
          id="medicare"
          value={form.medicareStatus ?? ""}
          onChange={(e) => onChange({ medicareStatus: (e.target.value || null) as S["MedicareStatus"] | null })}
        >
          <option value="">Choose…</option>
          {MEDICARE_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </NativeSelect>
      </Field>

      <Field label="Current drug plan, if you have one" htmlFor="plan" hint="The name on your plan card is fine, e.g. “SilverScript Choice”.">
        <Input id="plan" className={inputClass} value={form.currentPlan ?? ""} onChange={(e) => onChange({ currentPlan: e.target.value })} />
      </Field>

      <Field label="Do you get help paying for prescriptions?" htmlFor="help">
        <NativeSelect id="help" value={form.extraHelp} onChange={(e) => onChange({ extraHelp: e.target.value as S["ExtraHelpAnswer"] })}>
          {EXTRA_HELP_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </NativeSelect>
      </Field>
    </div>
  );
}
