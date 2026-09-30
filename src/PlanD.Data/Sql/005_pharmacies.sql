-- Named pharmacies: a searchable directory (NPPES), ZIP coordinates, and each PDP's network.

-- ZCTA internal points from the Census Gazetteer (geo release).
create table cms.zcta_centroid (
    release_id int              not null references cms.release (id) on delete cascade,
    zcta       text             not null,
    lat        double precision not null,
    lon        double precision not null,
    primary key (release_id, zcta)
);

-- Pharmacies from the NPPES monthly file (source 'nppes'): every NPI in a PDP network plus other pharmacy NPIs.
create table cms.pharmacy (
    release_id       int     not null references cms.release (id) on delete cascade,
    npi              text    not null,
    display_name     text    not null,  -- DBA name when there is one ("CVS PHARMACY #05961"), else the legal name
    legal_name       text,
    store_number     text,
    address1         text,
    address2         text,
    city             text,
    state            text,
    zip5             text,
    phone            text,
    primary_taxonomy text,
    mail_order       boolean not null,
    lat              double precision,
    lon              double precision,
    search_text      text    not null,  -- lower-cased names, store number, address and city
    primary key (release_id, npi)
);
create index pharmacy_search_trgm on cms.pharmacy using gin (search_text gin_trgm_ops);
create index on cms.pharmacy (release_id, zip5);

-- PDP networks, stored once per distinct network: plans whose pharmacy rows are identical share a network id.
create table cms.spuf_plan_network (
    release_id  int  not null references cms.release (id) on delete cascade,
    contract_id text not null,
    plan_id     text not null,
    segment_id  text not null,
    network_id  int  not null,
    primary key (release_id, contract_id, plan_id, segment_id)
);
create index on cms.spuf_plan_network (release_id, network_id);

-- Distinct dispensing-fee schedules (30/60/90 days × brand, generic, selected drug) and floor price.
create table cms.spuf_fee_schedule (
    release_id      int not null references cms.release (id) on delete cascade,
    fee_schedule_id int not null,
    floor_price     numeric(10, 2),
    brand_fee_30    numeric(8, 2), brand_fee_60    numeric(8, 2), brand_fee_90    numeric(8, 2),
    generic_fee_30  numeric(8, 2), generic_fee_60  numeric(8, 2), generic_fee_90  numeric(8, 2),
    selected_fee_30 numeric(8, 2), selected_fee_60 numeric(8, 2), selected_fee_90 numeric(8, 2),
    primary key (release_id, fee_schedule_id)
);

-- flags: 1 retail, 2 mail, 4 preferred retail, 8 preferred mail. (The file's in-area flag depends on each plan's
-- region, so it isn't kept: plans in different regions can then share one network.)
create table cms.spuf_network_pharmacy (
    release_id      int      not null references cms.release (id) on delete cascade,
    npi             text     not null,
    network_id      int      not null,
    flags           smallint not null,
    fee_schedule_id int      not null,
    zip             text,
    primary key (release_id, npi, network_id)
);
