-- Reference data loaded from public CMS / NLM / Census files.
-- Every table is keyed by release_id so a new release can be loaded and checked
-- while the previous one stays active.

create schema if not exists cms;

create table cms.release (
    id           serial primary key,
    source       text        not null,  -- spuf | landscape | geo | rxnorm
    plan_year    int,
    label        text        not null,  -- e.g. SPUF_2026_20260701
    source_path  text,
    status       text        not null default 'loading',  -- loading | ready | active | retired | failed
    row_counts   jsonb       not null default '{}'::jsonb,
    created_at   timestamptz not null default now(),
    activated_at timestamptz
);

create unique index release_one_active on cms.release (source, coalesce(plan_year, 0)) where status = 'active';

-- ---------------------------------------------------------------------------
-- Part D formulary, pharmacy network and pricing files (SPUF)
-- ---------------------------------------------------------------------------

-- One row per plan (the file repeats local MA plans once per county).
create table cms.spuf_plan (
    release_id      int      not null references cms.release (id) on delete cascade,
    contract_id     text     not null,
    plan_id         text     not null,
    segment_id      text     not null,
    contract_name   text     not null,
    plan_name       text     not null,
    formulary_id    text     not null,
    premium         numeric(10, 2),
    deductible      numeric(10, 2) not null,
    ma_region_code  text,
    pdp_region_code text,
    snp             smallint not null,
    primary key (release_id, contract_id, plan_id, segment_id)
);

-- Counties served by local MA plans (H contracts). PDPs and regional PPOs are
-- served by region, resolved through spuf_geo.
create table cms.spuf_plan_county (
    release_id  int  not null references cms.release (id) on delete cascade,
    contract_id text not null,
    plan_id     text not null,
    segment_id  text not null,
    county_code text not null,  -- SSA state+county code
    primary key (release_id, county_code, contract_id, plan_id, segment_id)
);

create table cms.spuf_geo (
    release_id      int  not null references cms.release (id) on delete cascade,
    county_code     text not null,  -- SSA
    state_name      text not null,
    county_name     text not null,
    ma_region_code  text,
    ma_region       text,
    pdp_region_code text,
    pdp_region      text,
    primary key (release_id, county_code)
);

create table cms.spuf_formulary (
    release_id        int      not null references cms.release (id) on delete cascade,
    formulary_id      text     not null,
    formulary_version int      not null,
    rxcui             text     not null,
    ndc               text     not null,  -- proxy NDC, 1:1 with rxcui
    tier              smallint not null,
    quantity_limit    boolean  not null,
    ql_amount         numeric(12, 3),
    ql_days           int,
    prior_auth        boolean  not null,
    step_therapy      boolean  not null,
    selected_drug     boolean  not null,
    primary key (release_id, formulary_id, rxcui)
);

create table cms.spuf_excluded_drug (
    release_id     int      not null references cms.release (id) on delete cascade,
    contract_id    text     not null,
    plan_id        text     not null,
    rxcui          text     not null,
    tier           smallint not null,
    quantity_limit boolean  not null,
    ql_amount      numeric(12, 3),
    ql_days        int,
    prior_auth     boolean  not null,
    step_therapy   boolean  not null,
    capped_benefit boolean  not null,
    primary key (release_id, contract_id, plan_id, rxcui)
);

create table cms.spuf_indication (
    release_id  int  not null references cms.release (id) on delete cascade,
    contract_id text not null,
    plan_id     text not null,
    rxcui       text not null,
    disease     text not null
);
create index on cms.spuf_indication (release_id, contract_id, plan_id, rxcui);

-- Cost sharing per plan, coverage level, tier and days supply.
-- Pharmacy-type groups: pref = preferred retail, std = standard retail,
-- mail_pref = preferred mail, mail_std = standard mail.
-- cost_type: 0 not offered, 1 copay ($), 2 coinsurance (fraction).
create table cms.spuf_beneficiary_cost (
    release_id      int      not null references cms.release (id) on delete cascade,
    contract_id     text     not null,
    plan_id         text     not null,
    segment_id      text     not null,
    coverage_level  smallint not null,  -- 0 before deductible, 1 initial, 3 catastrophic
    tier            smallint not null,
    days_supply     smallint not null,  -- 30 | 60 | 90 (converted from the file's 1/4/2 codes)
    pref_type       smallint not null, pref_amt      numeric(12, 4), pref_min      numeric(12, 2), pref_max      numeric(12, 2),
    std_type        smallint not null, std_amt       numeric(12, 4), std_min       numeric(12, 2), std_max       numeric(12, 2),
    mail_pref_type  smallint not null, mail_pref_amt numeric(12, 4), mail_pref_min numeric(12, 2), mail_pref_max numeric(12, 2),
    mail_std_type   smallint not null, mail_std_amt  numeric(12, 4), mail_std_min  numeric(12, 2), mail_std_max  numeric(12, 2),
    tier_specialty  boolean  not null,
    ded_applies     boolean  not null,
    primary key (release_id, contract_id, plan_id, segment_id, coverage_level, tier, days_supply)
);

-- Insulin cost sharing. tier is null when the row applies to every tier.
-- A null amount means not offered at that pharmacy type.
create table cms.spuf_insulin_cost (
    release_id      int      not null references cms.release (id) on delete cascade,
    contract_id     text     not null,
    plan_id         text     not null,
    segment_id      text     not null,
    tier            smallint,
    days_supply     smallint not null,  -- 30 | 60 | 90
    copay_pref      numeric(10, 2), copay_std      numeric(10, 2), copay_mail_pref numeric(10, 2), copay_mail_std numeric(10, 2),
    coins_pref      numeric(8, 4),  coins_std      numeric(8, 4),  coins_mail_pref numeric(8, 4),  coins_mail_std numeric(8, 4)
);
create index on cms.spuf_insulin_cost (release_id, contract_id, plan_id, segment_id);

-- Plan-level average unit cost at in-area retail pharmacies (quarterly files only).
create table cms.spuf_pricing (
    release_id  int      not null references cms.release (id) on delete cascade,
    contract_id text     not null,
    plan_id     text     not null,
    segment_id  text     not null,
    ndc         text     not null,
    days_supply smallint not null,  -- 30 | 60 | 90
    unit_cost   numeric(14, 4) not null,
    primary key (release_id, contract_id, plan_id, segment_id, ndc, days_supply)
);

-- Median unit cost of each drug across all plans. Stands in for the cash price of a drug
-- a plan doesn't cover (the public files have no cash-price data). Built after pricing loads.
create table cms.spuf_drug_price (
    release_id  int      not null references cms.release (id) on delete cascade,
    rxcui       text     not null,
    ndc         text     not null,
    days_supply smallint not null,
    unit_cost   numeric(14, 4) not null,
    primary key (release_id, rxcui, days_supply)
);

-- Summary of each plan's pharmacy network by pharmacy type, built while streaming
-- the 300M-row pharmacy file. Fees are the most common fee schedule for that type.
-- pharmacy_type: pref | std | mail_pref | mail_std
create table cms.spuf_network_summary (
    release_id           int      not null references cms.release (id) on delete cascade,
    contract_id          text     not null,
    plan_id              text     not null,
    segment_id           text     not null,
    pharmacy_type        text     not null,
    pharmacy_count       int      not null,
    in_area_count        int      not null,
    brand_fee_30         numeric(8, 2), brand_fee_60    numeric(8, 2), brand_fee_90    numeric(8, 2),
    generic_fee_30       numeric(8, 2), generic_fee_60  numeric(8, 2), generic_fee_90  numeric(8, 2),
    selected_fee_30      numeric(8, 2), selected_fee_60 numeric(8, 2), selected_fee_90 numeric(8, 2),
    floor_price          numeric(10, 2),
    primary key (release_id, contract_id, plan_id, segment_id, pharmacy_type)
);

-- ---------------------------------------------------------------------------
-- Landscape (premiums, benefit type, MOOP, stars) — one row per plan segment
-- ---------------------------------------------------------------------------

create table cms.landscape_plan (
    release_id                 int     not null references cms.release (id) on delete cascade,
    contract_id                text    not null,
    plan_id                    text    not null,
    segment_id                 text    not null,  -- zero-padded to 3 to match SPUF
    contract_category          text    not null,  -- MA-PD | SNP | MA | Cost | PDP
    organization_name          text,
    parent_organization        text,
    plan_name                  text    not null,
    plan_type                  text    not null,
    snp_type                   text,
    has_part_d                 boolean not null,
    drug_benefit_type          text,              -- Enhanced Alternative | Defined Standard | ...
    part_d_deductible          numeric(10, 2),
    part_d_basic_premium       numeric(10, 2),
    part_d_supplemental_premium numeric(10, 2),
    part_d_total_premium       numeric(10, 2),
    part_d_lis_premium         numeric(10, 2),    -- premium paid with full Extra Help
    part_c_premium             numeric(10, 2),
    consolidated_premium       numeric(10, 2),
    oop_threshold              numeric(10, 2),
    moop_in_network            numeric(10, 2),
    star_part_c                text,
    star_part_d                text,
    star_overall               text,
    lis_benchmark              boolean,
    ma_region_code             text,
    pdp_region_code            text,
    primary key (release_id, contract_id, plan_id, segment_id)
);

-- ---------------------------------------------------------------------------
-- Geography: ZIP -> ZCTA -> county FIPS -> SSA county code
-- ---------------------------------------------------------------------------

create table cms.zip_zcta (
    release_id int  not null references cms.release (id) on delete cascade,
    zip        text not null,
    zcta       text not null,
    po_name    text,
    state      text,
    primary key (release_id, zip)
);

create table cms.zcta_county (
    release_id  int           not null references cms.release (id) on delete cascade,
    zcta        text          not null,
    county_fips text          not null,
    county_name text          not null,
    share       numeric(6, 4) not null,  -- population share of the ZCTA in this county
    primary key (release_id, zcta, county_fips)
);

create table cms.fips_ssa (
    release_id  int  not null references cms.release (id) on delete cascade,
    county_fips text not null,
    ssa_code    text not null,
    state       text not null,
    county_name text not null,
    primary key (release_id, county_fips, ssa_code)
);
create index on cms.fips_ssa (release_id, ssa_code);

-- ---------------------------------------------------------------------------
-- RxNorm prescribable subset
-- ---------------------------------------------------------------------------

create table cms.rx_concept (
    release_id int  not null references cms.release (id) on delete cascade,
    rxcui      text not null,
    tty        text not null,  -- SCD, SBD, GPCK, BPCK, IN, BN, ...
    name       text not null,
    primary key (release_id, rxcui)
);
create index rx_concept_name on cms.rx_concept (release_id, lower(name) text_pattern_ops);

create table cms.rx_ndc (
    release_id int  not null references cms.release (id) on delete cascade,
    ndc        text not null,
    rxcui      text not null,
    primary key (release_id, ndc, rxcui)
);
