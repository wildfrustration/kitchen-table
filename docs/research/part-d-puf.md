# Part D formulary, pharmacy network and pricing files (SPUF)

Verified against `SPUF_2026_20260701.zip` (plan year 2026, Q2 quarterly) on 2026-09-29.
Items marked *inference* were derived from the data, not from CMS documentation.

## Distribution

- Monthly (no pricing table): https://data.cms.gov/provider-summary-by-type-of-service/medicare-part-d-prescribers/monthly-prescription-drug-plan-formulary-and-pharmacy-network-information
- Quarterly (with pricing): https://data.cms.gov/provider-summary-by-type-of-service/medicare-part-d-prescribers/quarterly-prescription-drug-plan-formulary-pharmacy-network-and-pricing-information
- Catalog: https://data.cms.gov/data.json — series ids: monthly `cb2a224f-4d52-4cae-aa55-8c00c671384f`, quarterly `a94b1015-1b93-476b-80ec-508b4169c8f5`.
- **Latest-release lookup:** `GET https://data.cms.gov/data-api/v1/dataset-resources/{seriesId}` returns JSON with the current zip and doc URLs. The regular `data-api/v1/dataset/{id}/data` API does NOT serve these tables (404).
- Q2 2026 quarterly: https://data.cms.gov/sites/default/files/2026-07/64c8d9e1-f350-45e5-88d8-5a2acce2b2d4/SPUF_2026_20260701.zip (2.49 GB)
- Sep 2026 monthly: https://data.cms.gov/sites/default/files/2026-09/903ba816-d276-4c17-9b5a-0224bbd4e949/2026_20260916.zip (2.29 GB)
- Docs: `SPUFRecordLayout-2026.pdf`, `Methodology-SPUF-2026.pdf` under https://data.cms.gov/sites/default/files/2025-10/ (local copies in `data/raw/puf/docs/`).
- New plan year first appears in the October monthly (last year: `2026_20251020.zip`, catalogued 2025-11-05) and the January quarterly (`SPUF_2026_20260107.zip`).
- File naming: `{contractYear}_{postingDate}.zip` (monthly), `SPUF_{contractYear}_{postingDate}.zip` (quarterly). Outer zip contains nested zips, one per table.

## Format rules

- Pipe `|` delimited, header row, CRLF, no quoting, no embedded pipes.
- Encoding: ASCII except **plan information** which is Windows-1252 (e.g. "Óptimo").
- Missing values: usually a single space `" "`; `.` (SAS missing) in insulin `TIER` and pharmacy `SELECTED_*` fees.
- Keep all codes as text (leading zeros): CONTRACT_ID, PLAN_ID ('007'), SEGMENT_ID ('000'), FORMULARY_ID ('00026408'), NDC (11), COUNTY_CODE, ZIPs, region codes ('07'), PHARMACY_NUMBER.
- Header quirks: pharmacy file uses `SELECTED_DISPENSING_FEE_30/60/90`; insulin headers are lowercase.
- File names have double spaces and mixed case — match with a case-insensitive regex on the table name. Monthly inner names use a date instead of `PPUF_2026Q2`.
- Pharmacy networks come in 6 part files, each with its own header.
- Y/N flags, except excluded drugs `QUANTITY_LIMIT_YN` which is 0/1.
- `CONTRACT_NAME` up to 60 chars (layout says 50).

## Tables

Plan key = CONTRACT_ID (H local MA, R regional MA, S PDP) + PLAN_ID + SEGMENT_ID.

### plan information — 112,294 rows
`CONTRACT_ID|PLAN_ID|SEGMENT_ID|CONTRACT_NAME|PLAN_NAME|FORMULARY_ID|PREMIUM|DEDUCTIBLE|MA_REGION_CODE|PDP_REGION_CODE|STATE|COUNTY_CODE|SNP|PLAN_SUPPRESSED_YN`
- H plans: one row per plan × county (STATE + COUNTY_CODE). S plans: one row per plan with PDP_REGION_CODE. R plans: one row with MA_REGION_CODE.
- 5,518 plans; 22 suppressed (`PLAN_SUPPRESSED_YN=Y`) and absent from every other table.
- PREMIUM monthly $ (whether drug-only for MA-PD: unverified); DEDUCTIBLE annual $ integer; SNP 0 none / 1 chronic / 2 dual / 3 institutional.

### geographic locator — 3,279 rows
`COUNTY_CODE|STATENAME|COUNTY|MA_REGION_CODE|MA_REGION|PDP_REGION_CODE|PDP_REGION`
- COUNTY_CODE is the **SSA** code (not FIPS), unique. Some counties have two SSA codes (Los Angeles 05200/05210).
- MA region blank for 118 territory counties. DC's STATENAME is "Washington D.C.".

### basic drugs formulary — 1,124,586 rows
`FORMULARY_ID|FORMULARY_VERSION|CONTRACT_YEAR|RXCUI|NDC|TIER_LEVEL_VALUE|QUANTITY_LIMIT_YN|QUANTITY_LIMIT_AMOUNT|QUANTITY_LIMIT_DAYS|PRIOR_AUTHORIZATION_YN|STEP_THERAPY_YN|SELECTED_DRUG_YN`
- Unique on (FORMULARY_ID, NDC). RXCUI ↔ NDC is 1:1 across the file: the NDC is a single proxy NDC per RxNorm concept. Map user drugs to RXCUI first.
- Tiers 1–7. `SELECTED_DRUG_YN=Y` = IRA negotiated drug.
- No brand/generic flag anywhere — use RxNorm term type (SBD vs SCD) or FDA NDC directory.

### excluded drugs formulary — 13,717 rows
`CONTRACT_ID|PLAN_ID|RXCUI|TIER|QUANTITY_LIMIT_YN|QUANTITY_LIMIT_AMOUNT|QUANTITY_LIMIT_DAYS|PRIOR_AUTH_YN|STEP_THERAPY_YN|CAPPED_BENEFIT_YN`
- Supplemental coverage of Part D-excluded drugs. No segment (applies to all segments), no NDC, no pricing rows.

### indication based coverage — 397 rows
`CONTRACT_ID|PLAN_ID|RXCUI|DISEASE`

### beneficiary cost — 172,642 rows
`CONTRACT_ID|PLAN_ID|SEGMENT_ID|COVERAGE_LEVEL|TIER|DAYS_SUPPLY|COST_TYPE_PREF|COST_AMT_PREF|COST_MIN_AMT_PREF|COST_MAX_AMT_PREF|COST_TYPE_NONPREF|COST_AMT_NONPREF|COST_MIN_AMT_NONPREF|COST_MAX_AMT_NONPREF|COST_TYPE_MAIL_PREF|COST_AMT_MAIL_PREF|COST_MIN_AMT_MAIL_PREF|COST_MAX_AMT_MAIL_PREF|COST_TYPE_MAIL_NONPREF|COST_AMT_MAIL_NONPREF|COST_MIN_AMT_MAIL_NONPREF|COST_MAX_AMT_MAIL_NONPREF|TIER_SPECIALTY_YN|DED_APPLIES_YN`
- Unique on (plan, COVERAGE_LEVEL, TIER, DAYS_SUPPLY).
- Suffixes: `_PREF` preferred retail, `_NONPREF` standard retail, `_MAIL_PREF`, `_MAIL_NONPREF`.
- COST_TYPE: 0 not offered, 1 copay ($), 2 coinsurance (fraction, 0.25 = 25%).
- COVERAGE_LEVEL: 0 pre-deductible, 1 initial coverage, 3 catastrophic (level 2 gap never present).
- Level-0 rows exist only when plan DEDUCTIBLE > 0 and only for tiers with `DED_APPLIES_YN=N`. Deductible tiers have no level-0 row → member pays 100% until deductible met.
- DAYS_SUPPLY codes: 1 = 30 days, 4 = 60 days, 2 = 90 days (3 = other, never occurs).
- Min/max: mostly 0; where set, clamp coinsurance. Some D-SNP copay rows carry LIS-style ranges (1.60/5.10) — handling unverified.

### insulin beneficiary cost — 43,057 rows
`CONTRACT_ID|PLAN_ID|SEGMENT_ID|TIER|DAYS_SUPPLY|copay_amt_pref_insln|copay_amt_nonpref_insln|copay_amt_mail_pref_insln|copay_amt_mail_nonpref_insln|coin_amt_pref_insln|coin_amt_nonpref_insln|coin_amt_mail_pref_insln|coin_amt_mail_nonpref_insln`
- `TIER='.'` = applies to all tiers (defined-standard plans). `" "` = not offered at that pharmacy type.
- Copays already scaled by days supply (35/70/105). Member pays the lesser of copay, coinsurance × price, 25% of MFP (selected drugs).

### pharmacy networks — 6 parts, 307,272,341 rows, 23 GB
`CONTRACT_ID|PLAN_ID|SEGMENT_ID|PHARMACY_NUMBER|PHARMACY_ZIPCODE|PREFERRED_STATUS_RETAIL|PREFERRED_STATUS_MAIL|PHARMACY_RETAIL|PHARMACY_MAIL|IN_AREA_FLAG|FLOOR_PRICE|BRAND_DISPENSING_FEE_30|BRAND_DISPENSING_FEE_60|BRAND_DISPENSING_FEE_90|GENERIC_DISPENSING_FEE_30|GENERIC_DISPENSING_FEE_60|GENERIC_DISPENSING_FEE_90|SELECTED_DISPENSING_FEE_30|SELECTED_DISPENSING_FEE_60|SELECTED_DISPENSING_FEE_90`
- Unique on (plan, PHARMACY_NUMBER). PHARMACY_NUMBER = "10" + 10-digit NPI. No names/addresses (use NPPES).
- IN_AREA_FLAG=1 on ~1.3% of rows; network is nationwide.
- Selected-drug fee `.` → use brand fee (methodology).
- Monthly file lacks FLOOR_PRICE.

### pricing — 53,762,064 rows, 2 GB (quarterly only)
`CONTRACT_ID|PLAN_ID|SEGMENT_ID|NDC|DAYS_SUPPLY|UNIT_COST`
- Unique on (plan, NDC, DAYS_SUPPLY). DAYS_SUPPLY in literal days 30/60/90 (map cost codes 1→30, 4→60, 2→90).
- UNIT_COST = plan-level average per billing unit across in-area retail pharmacies (*inference* on the unit).

## Joins

1. Service area: H → COUNTY_CODE; S → PDP_REGION_CODE → counties; R → MA_REGION_CODE → counties.
2. Plan → formulary: one row per plan, then FORMULARY_ID. Segments share a formulary; premiums can differ.
3. Formulary → drug: RXCUI (or proxy NDC).
4. Cost: TIER_LEVEL_VALUE → beneficiary cost TIER on (plan, COVERAGE_LEVEL, TIER, DAYS_SUPPLY code); column group by pharmacy type; insulin file overrides for insulin.
5. Excluded / indication-based: (CONTRACT_ID, PLAN_ID, RXCUI), fan out to segments.
6. Pharmacy: plan key → PHARMACY_NUMBER; flags pick column group; fees by brand/generic/selected × 30/60/90.
7. Price: (plan, proxy NDC, days) → UNIT_COST × quantity + dispensing fee.
