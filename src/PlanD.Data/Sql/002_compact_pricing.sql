-- One row per plan and drug with the 30/60/90-day unit costs side by side (≈19M rows instead of 54M).
-- The long form didn't fit the dev database's disk and costs three index entries per drug.
drop table cms.spuf_pricing;

create table cms.spuf_pricing (
    release_id   int  not null references cms.release (id) on delete cascade,
    contract_id  text not null,
    plan_id      text not null,
    segment_id   text not null,
    ndc          text not null,
    unit_cost_30 numeric(14, 4),
    unit_cost_60 numeric(14, 4),
    unit_cost_90 numeric(14, 4),
    primary key (release_id, contract_id, plan_id, segment_id, ndc)
);
