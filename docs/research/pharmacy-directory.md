# Pharmacy directory (NPPES) and ZIP coordinates

Checked 2026-09-30 against live files, range requests and the SPUF Q2-2026 pharmacy network files.

## NPPES (https://download.cms.gov/nppes/NPI_Files.html)

- Only V2 is published (V1 dropped 2026-03-03). No login; byte ranges supported.
- Monthly full file: `NPPES_Data_Dissemination_{MonthName}_{YYYY}_V2.zip` — Sept 2026 is 1.16 GB zipped, ~12 GB unzipped.
  Members (find by prefix): `npidata_pfile_…csv` (11.7 GB), `othername_pfile_…csv` (50 MB), `pl_pfile_…`, `endpoint_pfile_…`, headers, Readme.
- Weekly incremental and monthly deactivation list also exist (not used yet).
- CSV: every value double-quoted, header row, LF, UTF-8, dates MM/DD/YYYY.

npidata columns used (0-based): `NPI` [0], `Entity Type Code` [1], `Provider Organization Name (Legal Business Name)` [4],
`Provider Last Name (Legal Name)` [5], `Provider First Name` [6], practice location `…First Line…` [28], `…Second Line…` [29],
`…City Name` [30], `…State Name` [31], `…Postal Code` [32] (9 digits for ~89%; take left 5), `…Telephone Number` [34],
`Last Update Date` [37], `NPI Deactivation Date` [39], `Healthcare Provider Taxonomy Code_1..15` with primary switches,
`Is Organization Subpart` [308], `Parent Organization LBN` [309]. Match columns by name.

Gotchas:
- **DBA names are not in npidata** in V2 (`Provider Other Organization Name` = `<UNAVAIL>`, type 6). Join `othername_pfile`
  (`NPI`, `Provider Other Organization Name`, `…Type Code` 3 = DBA, 4 = former legal, 5 = other, `Created Date`); dedupe on (NPI, name, type).
- Legal names are meaningless to patients ("HOLIDAY CVS LLC", "WAL-MART STORES EAST LP") — display the DBA, fall back to legal.
- Store numbers vary in format (`WALGREENS #01173`, `CVS PHARMACY 02464`, `WALMART PHARMACY 10-5609`).
- Deactivated NPIs stay in the monthly file with only NPI + deactivation date; empty entity type = deactivated.
- Chains (Walgreens, CVS) use generic `333600000X` as primary — filter on any taxonomy slot. 4.5% of network pharmacies have
  no pharmacy taxonomy (hospitals, clinics), so NPIs in the SPUF network are kept regardless of taxonomy.
- ~41% of NPPES pharmacy orgs in sampled ZIPs are in no Part D network (many stale). Several NPIs can share an address.

Pharmacy taxonomy codes: 333600000X Pharmacy, 3336C0002X Clinic, 3336C0003X Community/Retail, 3336C0004X Compounding,
3336H0001X Home Infusion, 3336I0012X Institutional, 3336L0003X Long Term Care, 3336M0002X Mail Order, 3336M0003X Managed Care Org,
3336N0007X Nuclear, 3336S0011X Specialty; also 332000000X Military, 332100000X VA, 332800000X I/T/U, 332900000X Non-Pharmacy Dispensing Site.

Counts: 69,603 distinct NPIs in any SPUF network; 67,615 in PDP networks (S contracts).

## NPI Registry API

`https://npiregistry.cms.hhs.gov/api/?version=2.1&…` — max 200 per call, skip ≤ 1000, trailing-wildcard name search only,
no radius search. Not suitable for typeahead; use only to refresh a single NPI.

## ZIP → lat/long

Census Gazetteer 2026 ZCTA: `https://www2.census.gov/geo/docs/maps-data/data/gazetteer/2026_Gazetteer/2026_Gaz_zcta_national.zip`
(pipe-delimited `GEOID|GEOIDFQ|ALAND|AWATER|ALAND_SQMI|AWATER_SQMI|INTPTLAT|INTPTLONG`, 33,791 rows; older years are
tab-delimited). Local copy: `data/raw/geo/2026_Gaz_zcta_national.txt`. PO-box ZIPs resolve through the UDS ZIP→ZCTA file.
SPUF masks 286 pharmacy ZIPs as `XXXXX`; use the NPPES practice ZIP for display and distance, join on NPI only.

## Loading approach

Stream the monthly zip (no 12 GB extract), keep entity 1/2 rows with a pharmacy taxonomy or an NPI in the PDP network set,
join DBA names, derive display name + normalized search text + store number + ZIP centroid. ~70–115k rows, < 100 MB with a
pg_trgm index. Distance is ZIP-centroid level.
