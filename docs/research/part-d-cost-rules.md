# Part D cost rules and Plan Finder behaviour

Checked 2026-09-29. Items marked UNVERIFIED were not confirmed in a primary source or in Plan Finder.

## Parameters

| Parameter | 2026 | 2027 | Source |
|---|---|---|---|
| Standard deductible | $615 | $700 | 2027 Rate Announcement Table V-2, V-6 |
| Out-of-pocket threshold | $2,100 | $2,400 | 2027 RA Table V-2; 2027 Landscape |
| Initial coverage, defined standard | 25% | 25% | 2027 RA Table V-6 |
| Catastrophic, member pays | $0 | $0 | same |
| Covered insulin | lesser of $35, 25% MFP, 25% negotiated price per month's supply; $70/2 mo, $105/3 mo; no deductible; $0 catastrophic | same | 42 USC 1395w-102(b)(9); SPUF methodology |
| ACIP vaccines | $0, no deductible | same | 1395w-102(b)(8) |
| Extra Help cat. 2 (full-dual ≤100% FPL) generic / other | $1.60 / $4.90 | $1.65 / $5.00 | 2027 RA Table V-2 |
| Extra Help cat. 1 (other full subsidy ≤150% FPL) generic / other | $5.10 / $12.65 | $5.80 / $14.40 | same |
| Extra Help cat. 3 (institutional / HCBS) | $0 | $0 | same |
| Extra Help deductible | $0 | $0 | 42 CFR 423.782 |
| Base beneficiary premium | $38.99 | $41.33 | July-28 memos |
| De minimis | $2 | $2 | same |
| Specialty-tier threshold | $950 | $1,080 | CY2027 bid instructions |

Sources: https://www.cms.gov/files/document/2027-announcement.pdf, https://www.cms.gov/files/document/2026-announcement.pdf, https://www.cms.gov/files/document/final-cy-2025-part-d-redesign-program-instructions.pdf, https://www.ecfr.gov/current/title-42/section-423.100, https://www.ecfr.gov/current/title-42/section-423.782, https://data.cms.gov/sites/default/files/2025-10/cf6b8476-ca42-48c6-9fc4-281b08c6ea87/Methodology-SPUF-2026.pdf

## Rules

- **Lesser-of:** member pays min(cost share, negotiated price) in every phase.
- **Negotiated price** includes the dispensing fee; coinsurance applies to it.
- **Crossing the cap:** member pays min(normal cost share, cap − TrOOP).
- **Crossing the deductible:** pay remaining deductible, then cost share on the rest (coinsurance × (price − remaining)). Copay straddle handling UNVERIFIED.
- **TrOOP counts:** member cost share, Extra Help subsidy, SPAP/IHS/ADAP, and (from 2025) EA supplemental benefits. For Enhanced Alternative plans: TrOOP increment = max(actual cost share, defined-standard cost share). Not counted: non-covered / excluded drugs.
- **Deductible deemed satisfied** once TrOOP reaches the standard deductible (CY2025 PI §40, §150) — Plan Finder implementation UNVERIFIED.
- **Brand discount cap** in initial coverage (price − 10% manufacturer discount) — Plan Finder UNVERIFIED.
- **Extra Help:** no deductible; below cap pay min(LIS copay, plan cost share); $0 above cap. Subsidy counts toward TrOOP, so phases follow the non-LIS cost path. Generic copay covers generics, biosimilars, preferred multi-source drugs.
- **No coverage gap** since 2025.
- **Defined-standard share (for EA TrOOP):** 100% until TrOOP reaches standard deductible, then 25%; insulin min($35/month, 25%); vaccines $0.

## Plan Finder pricing (from CMS docs)

- Full cost = round2(unit cost × quantity + dispensing fee); floor price applied if higher. Ceiling prices applied (not in public files).
- Only 30/60/90-day supplies. Default (cash) price for non-formulary and out-of-network — current formula UNVERIFIED.
- No generic substitution; user picks brand or generic.
- Plan card total uses the lowest-cost retail pharmacy; mail order separate.
- Non-covered drugs: full cash price, not counted toward the cap.
- If plan doesn't cover the refill interval (e.g. specialty 30-day only): full price.
- Live test: 30-day mail-order request returned as 90-day fills (qty × 3) in Jan, Apr, Jul, Oct.
- Open-enrollment view = full 12 months of next year; otherwise rest of year.

## Plan Finder internal API (undocumented)

- Base `https://www.medicare.gov/api/v1/data/plan-compare`; `POST /drugs/cost` with `{npis, prescriptions:[{ndc, frequency:"FREQUENCY_30_DAYS", quantity}], lis, plans:[{contract_year, contract_id, plan_id, segment_id}], full_year:true, retailOnly:false}`.
- Returns per-pharmacy `drug_costs`, `estimated_monthly_costs`, `phase_information`, payment-plan schedule; plan-level `lowest_retail_total`, `lowest_mail_total`.
- `lis` enum: LIS_NO_HELP, LIS_LEVEL_1A, LIS_LEVEL_1A_DN, LIS_LEVEL_1B, LIS_LEVEL_1C, LIS_LEVEL_2, LIS_LEVEL_3.
- Use only for a small pinned regression set, spaced out, stop on 403/429, no bot-evasion.

## Validate first (all UNVERIFIED in Plan Finder)

1. EA TrOOP credit. 2. Deductible deemed satisfied. 3. Copay straddling the deductible; in-month fill order. 4. Brand discount cap. 5. Rest-of-year start month. 6. 180/360-day and 60-day mail pricing. 7. D-SNP copay min/max rows. 8. Vaccine and generic identification. 9. Cash price formula.
