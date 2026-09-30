import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Separator } from "@/components/ui/separator";
import type { IntakeForm } from "@/api/client";
import { AboutFields } from "@/components/AboutFields";
import { DrugList, DrugPicker } from "@/components/Drugs";
import { Field, NativeSelect } from "@/components/Field";
import { PharmacyList, PharmacyPicker } from "@/components/Pharmacies";

/** The Extra Help category the quote uses. The patient's answer suggests one; the broker confirms it. */
export const EXTRA_HELP_LEVELS = [
  { value: 0, label: "No Extra Help" },
  { value: 1, label: "Extra Help — full subsidy (higher copays)" },
  { value: 2, label: "Extra Help — full dual ≤100% FPL (lower copays)" },
  { value: 3, label: "Extra Help — institutional ($0 copays)" },
];

/** Everything about a client, editable by the broker (used for new clients and edits). */
export function ClientEditor({
  initial,
  initialLevel,
  saving,
  error,
  saveLabel = "Save",
  onSave,
  onCancel,
}: {
  initial: IntakeForm;
  initialLevel: number;
  saving: boolean;
  error?: string;
  saveLabel?: string;
  onSave: (form: IntakeForm, extraHelpLevel: number) => void;
  onCancel: () => void;
}) {
  const [form, setForm] = useState<IntakeForm>(initial);
  const [level, setLevel] = useState(initialLevel);
  const update = (patch: Partial<IntakeForm>) => setForm((f) => ({ ...f, ...patch }));

  return (
    <div className="grid gap-6">
      <AboutFields form={form} onChange={update} />
      <Field label="Quote with" htmlFor="level" hint="Confirm the client's Extra Help category; it changes their copays and premium.">
        <NativeSelect id="level" value={level} onChange={(e) => setLevel(Number(e.target.value))}>
          {EXTRA_HELP_LEVELS.map((l) => (
            <option key={l.value} value={l.value}>
              {l.label}
            </option>
          ))}
        </NativeSelect>
      </Field>

      <Separator />
      <div className="grid gap-3">
        <h3 className="font-semibold">Prescriptions</h3>
        <DrugList drugs={form.drugs} onRemove={(i) => update({ drugs: form.drugs.filter((_, j) => j !== i) })} />
        <DrugPicker onAdd={(d) => update({ drugs: [...form.drugs, d] })} />
      </div>

      <Separator />
      <div className="grid gap-3">
        <h3 className="font-semibold">Pharmacies</h3>
        <PharmacyList pharmacies={form.pharmacies} onRemove={(i) => update({ pharmacies: form.pharmacies.filter((_, j) => j !== i) })} />
        <PharmacyPicker zip={form.zip} chosen={form.pharmacies} onAdd={(p) => update({ pharmacies: [...form.pharmacies, p] })} />
        <label className="flex items-center gap-2 text-sm">
          <Checkbox checked={form.usesMailOrder} onCheckedChange={(c) => update({ usesMailOrder: c })} /> Quote with mail order
        </label>
      </div>

      {error && <p className="text-sm text-destructive">{error}</p>}
      <div className="flex justify-end gap-2">
        <Button variant="ghost" onClick={onCancel}>
          Cancel
        </Button>
        <Button disabled={saving} onClick={() => onSave(form, level)}>
          {saving ? "Saving…" : saveLabel}
        </Button>
      </div>
    </div>
  );
}
