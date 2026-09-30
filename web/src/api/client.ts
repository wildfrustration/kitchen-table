import createClient from "openapi-fetch";
import type { components, paths } from "./schema";

/** Same-origin API: cookies carry the broker or patient session. */
export const api = createClient<paths>({ baseUrl: "", credentials: "include" });

export type S = components["schemas"];
export type IntakeForm = S["IntakeForm"];
export type DrugEntry = S["DrugEntry"];
export type PharmacyEntry = S["PharmacyEntry"];

/** A failed API call. `fieldErrors` holds ASP.NET validation problems keyed by field name. */
export class ApiError extends Error {
  status: number;
  fieldErrors: Record<string, string[]>;

  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.status = status;
    this.fieldErrors = fieldErrors;
  }
}

type Result<T> = { data?: T; error?: unknown; response: Response };

/** Returns the data or throws an ApiError with a readable message. */
export async function unwrap<T>(call: Promise<Result<T>>): Promise<T> {
  const { data, error, response } = await call;
  if (response.ok) return data as T;
  const problem = (error ?? {}) as { title?: string; detail?: string; errors?: Record<string, string[]> };
  const message =
    typeof error === "string"
      ? error
      : problem.errors
        ? Object.values(problem.errors).flat().join(" ")
        : (problem.detail ?? problem.title ?? `Request failed (${response.status})`);
  throw new ApiError(response.status, message, problem.errors ?? {});
}

export const emptyForm = (): IntakeForm => ({
  firstName: "",
  lastName: "",
  email: "",
  phone: "",
  zip: "",
  countyCode: null,
  birthMonth: null,
  birthYear: null,
  medicareStatus: null,
  currentPlan: "",
  extraHelp: "No",
  usesMailOrder: false,
  drugs: [],
  pharmacies: [],
});
