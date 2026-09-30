# Landscape, PBP benefits, Star Ratings, geography

> Scope note (2026-09-30): plan-d quotes stand-alone PDPs only. The Medicare Advantage parts below (PBP benefits,
> MA landscape columns, OOPC) are kept for reference and aren't loaded.

Verified by download on 2026-09-29. Local copies under `data/raw/`.

## Landscape (one combined MA + PDP file since CY2025)

- Page: https://www.cms.gov/medicare/coverage/prescription-drug-coverage ("Downloads")
- 2027: https://www.cms.gov/files/zip/cy2027-landscape-202609.zip → `CY2027_Landscape_202609.csv` (130,306 rows, 5,884 plans)
- 2026: https://www.cms.gov/files/zip/cy2026-landscape-202609.zip (137,259 rows)
- CSV, UTF-8 **with BOM**, comma, CRLF, double-quoted.

2027 header (53 columns): Contract Year, Contract Category Type, US Territory, State Territory Abbreviation, State Territory Name, County Name, Contract ID, Plan ID, Segment ID, Contract Plan ID, Contract Plan Segment ID, Sanctioned Plan, Parent Organization Name, Contract Name, Organization Marketing Name, Organization Type, Plan Name, Plan Type, Enrollment Capacity Limit Accepted, Enrollment Capacity Limit Amount, Special Needs Plan (SNP) Indicator, SNP Type, SNP Institutional Type, Dual Eligible SNP (D-SNP) Integration Status, D-SNP Applicable Integrated Plan (AIP) Identifier, Chronic or Disabling Condition SNP (C-SNP) Condition Type, Medicare Zero-Dollar Cost Sharing D-SNP, Part D Coverage Indicator, National PDP, Drug Benefit Category, Drug Benefit Type, Voluntary De Minimis Program Participant, Part D Basic Premium At or Below Regional Benchmark, Low Income Subsidy (LIS) Auto Enrollment, Offers Drug Tier with No Part D Deductible, Annual Part D Deductible Amount, Part D Basic Premium, Part D Supplemental Premium, Part D Total Premium, Low Income Premium Subsidy (LIPS) Amount, Part D LIPS (CMS Pays), Part D Low Income Beneficiary Premium Amount, Part D Out-of-Pocket (OOP) Threshold, Part C Premium, Monthly Consolidated Premium (Part C + D), In-Network Maximum Out-of-Pocket (MOOP) Amount, Part C Summary Star Rating, Part D Summary Star Rating, Overall Star Rating, MA Region Code, MA Region, PDP Region Code, PDP Region

- **Map by column name.** 2026 differs: `ContractPlanID`, `ContractPlanSegmentID` (no spaces), extra `SNP Institutional Category`, no Enrollment Capacity columns.
- Money is text: `$1,234.00 ` (trailing space), negatives `($60.80)`. "Not Applicable" appears in numeric columns. Part D Basic Premium can be negative.
- Total = Basic + Supplemental; Consolidated = Part C + Part D Total; LIS premium = max(Total − LIPS (CMS Pays), 0).
- Drug Benefit Type: Enhanced Alternative, Defined Standard, Actuarially Equivalent Standard, Basic Alternative.
- Stars: 2027 blank (not released); values "2.0"–"5.0" or text.
- Keys: `Contract Plan Segment ID` (`H0432_009_0`) = PBP `bid_id`. Plan ID 3-digit padded; **Segment ID unpadded**. Region codes 2-digit text.
- Counties by **name only** (ASCII-folded, no "County" suffix). CT uses the old 8 counties. PDP rows use "All Counties" (one row per state).
- Excludes employer plans (plan ID ≥ 800), National PACE, Part B-only.

## PBP Benefits

- 2026: https://www.cms.gov/files/zip/pbp-benefits-2026.zip → `data/raw/pbp/pbp_2026/`. 2027 not yet published (404 on 2026-09-29).
- Tab-delimited `.txt`, CRLF, no quoting. **Windows-1252** in several tables. Duplicate column names in Section_A, PlanArea, PlanRegionArea.
- Dictionary: `PBP_Benefits_2026_dictionary.xlsx` (FILE, NAME, TYPE, …, CODES, CODE_VALUES).
- Keys in every table: `pbp_a_hnumber`, `pbp_a_plan_identifier`, `segment_id` (unpadded), `bid_id`.
- Individual MA universe: Section_A with `pbp_a_eghp_yn=2` and plan type not in {20, 29, 30}.
- Conventions: `*_yn` 1 yes / 2 no; amounts decimal strings; percents integers; bitmask fields position N = "1".
- `mc_*_cstshr_yn = 1` → Original Medicare cost sharing applies (2026: Part A ded $1,736, days 61–90 $434/day, LRD $868/day, SNF 21–100 $217, Part B ded $283).
- Key fields: Section_D MOOP `pbp_d_out_pocket_amt`, combined `pbp_d_comb_max_enr_amt`, OON `pbp_d_oon_max_enr_oopc_amt`; deductibles `pbp_d_inn_deduct_*` (HMO etc.) / `pbp_d_ann_deduct_*` (PPO); PCP `pbp_b7a_*`; specialist `pbp_b7d_*`; inpatient `pbp_b1a_copay_mcs_*` intervals; SNF `pbp_b2_*`; ER `pbp_b4a_*`; urgent `pbp_b4b_*`; outpatient `pbp_b9a_*` / ASC `pbp_b9b_*`; lab/diagnostics `pbp_b8a_*`; imaging `pbp_b8b_*`; ambulance `pbp_b10a_*`; Part B drugs `mrx_b_*`; dental b16, vision b17, hearing b18.
- Service areas: PlanArea.txt (local MA; SSA `county_code`, `partial_flag`), PlanRegionArea.txt (regional PPO/PDP).

## Star Ratings

- 2026: https://www.cms.gov/files/zip/2026-star-ratings-data-tables.zip → Summary Ratings workbook, sheet `Summary_Rating`, header on row 2. 2027 not yet published.
- Trailing spaces in all text values; trim before joining on Contract Number.

## ZIP → county

- HUD USPS crosswalk requires login — not used.
- **Geocorr 2022** `data/raw/geo/geocorr2022_zcta_to_county_pop20.csv`: zcta, county (FIPS), CountyName, ZIPName, pop20, afact2, afact (ZCTA→county share). Latin-1, **two header rows**. CT uses planning regions → use `geocorr2022_zcta_to_ctcounty_pre2023_pop20.csv` for CT.
- ZIP → ZCTA: `data/raw/geo/uds_zip_to_zcta_2022.csv` (zip, po_name, state, zip_type, zcta, zip_join_type).
- Census alternative: `tab20_zcta520_county20_natl.txt` (land-area weights only).

## SSA ↔ FIPS

- Base: NBER `data/raw/geo/ssa_fips_state_county_2026.csv` (fipscounty, countyname_fips, state, …, ssa_code, state_name, countyname_rate).
- Patch CT (07000–07070 → 09001–09015) and VI (48010→78010, 48020→78020) from the CMS GPCI file `geo/ffs_2024/CSV/Geographic indices 2020-2026 - Physician GPCI.csv`. Do not use GPCI for anything else (it has errors).
- 06037 (Los Angeles) ↔ SSA 05200 and 05210.

## OOPC (MA medical cost model)

- https://www.cms.gov/medicare/coverage/prescription-drug-coverage/out-of-pocket-costs — SAS-only model over PBP JSON, MCBS utilization, average beneficiary, no health-status split, no precomputed plan values. Health-status version last shipped for CY2019.
